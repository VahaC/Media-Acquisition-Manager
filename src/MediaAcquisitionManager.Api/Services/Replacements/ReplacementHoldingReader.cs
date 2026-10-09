using MediaAcquisitionManager.Api.Services.Integrations;
using MediaAcquisitionManager.Core.Abstractions;
using MediaAcquisitionManager.Core.Abstractions.Downloads;
using MediaAcquisitionManager.Core.Abstractions.Files;
using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Paths;
using MediaAcquisitionManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Services.Replacements;

/// <inheritdoc cref="IReplacementHoldingReader"/>
/// <remarks>
/// <para>
/// ADR 0031's "read by path, confirmed by identity": each torrent file's path is its save path joined with its path in
/// the torrent, mapped through the client's own pairs as a whole (<see cref="PathMapper"/>, which also refuses a
/// <c>..</c> in a name) — never the save path's app path with the name appended, which a pair deeper than the save path
/// wouldn't map. A mapped path is where to look, not proof (ADR 0003): only a torrent file of a ticked copy's size is
/// looked at on the disk, once per path, and the device and inode found there decide.
/// </para>
/// <para>
/// Whether a type is a download client is asked of the adapter registry, and the stored secrets are opened by
/// <see cref="IIntegrationSettingsFactory"/>, as <c>DestinationChecker</c> does.
/// </para>
/// </remarks>
public sealed class ReplacementHoldingReader(
    ApplicationDbContext db,
    IIntegrationAdapterRegistry adapters,
    IIntegrationSettingsFactory settingsFactory,
    IFileInventory files,
    ILogger<ReplacementHoldingReader> logger) : IReplacementHoldingReader
{
    public async Task<ReplacementHoldings> ReadAsync(
        IReadOnlyList<ReplacementCandidateCopy> copies, IReadOnlyList<string> allowedRoots, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(copies);
        ArgumentNullException.ThrowIfNull(allowedRoots);

        // Every download client, switched off or not: one the app may not ask can still hold the copies (R2). Read before
        // the clients are asked, as the context is not safe to share between the calls below.
        var clients = await db.IntegrationConnections
            .AsNoTracking()
            .Include(client => client.PathMappings)
            .Where(client => client.Kind == IntegrationKind.DownloadClient)
            // The normalised name is unique, so the order is total.
            .OrderBy(client => client.NormalizedName)
            .ToListAsync(cancellationToken);
        // Once per path: one file several works list is one copy.
        List<ReplacementCandidateCopy> targets =
        [
            .. copies
                .Where(copy => copy is { AppPath: not null, Fresh: { DeviceId: not null, Inode: not null } })
                .GroupBy(copy => copy.AppPath!)
                .Select(path => path.First()),
        ];

        // Each client once and all of them at once, the DestinationChecker way: the slowest sets the wait, and one that
        // fails can't hold the others back.
        var answers = await Task.WhenAll(clients.Select(client => AskAsync(client, cancellationToken)));
        var roots = allowedRoots.Select(root => NormalizedPath.TryParse(root, out var path, out _) ? path : null).OfType<NormalizedPath>().ToList();
        var holds = new Dictionary<NormalizedPath, List<ReplacementHold>>();
        var described = new Dictionary<NormalizedPath, FileInventoryResult<FileFacts>>();
        var readings = new List<ReplacementClientReading>(clients.Count);
        for (var index = 0; index < clients.Count; index++)
        {
            var (client, answer) = (clients[index], answers[index]);
            if (answer.Jobs is not { } jobs)
            {
                readings.Add(new ReplacementClientReading(client.Name, ReplacementClientCheck.NotRead, answer.Error, 0));
                continue;
            }

            var unplaced = 0;
            foreach (var job in jobs)
            {
                if (!await PlaceAsync(client, job, targets, roots, allowedRoots, described, holds, cancellationToken))
                    unplaced++;
            }

            // Information: ids and counts only, never a torrent's name or a path.
            logger.LogInformation(
                "Holdings listing of integration {IntegrationId} ({AdapterType}) read {TorrentCount} torrents; {UnplacedCount} could not be placed.",
                client.Id,
                client.AdapterType,
                jobs.Count,
                unplaced);
            readings.Add(new ReplacementClientReading(
                client.Name, unplaced > 0 ? ReplacementClientCheck.Partial : ReplacementClientCheck.Read, null, unplaced));
        }

        return new ReplacementHoldings(holds.ToDictionary(held => held.Key, held => (IReadOnlyList<ReplacementHold>)held.Value), readings);
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// One client's whole listing, or the sentence why it wasn't read: switched off, a type this version can't use, a
    /// secret it can't open, or the client's own failure. Never throws but for the caller's cancellation, and never
    /// touches the database, so several run at once.
    /// </summary>
    private async Task<ClientAnswer> AskAsync(IntegrationConnectionEntity client, CancellationToken cancellationToken)
    {
        // Every client starts on its own (ReleaseSources): one whose call runs synchronously up to its first wait would
        // otherwise hold the others back.
        await Task.Yield();

        // The order a destination's listing is refused in (DestinationChecker).
        if (!client.IsEnabled)
            return NotAsked(client, "switched off", $"“{client.Name}” is switched off.");
        if (adapters.Find<IDownloadClient>(client.AdapterType) is not { } downloadClient)
            return NotAsked(client, "type unavailable", $"This version of the app can't read “{client.AdapterType}”.");

        var settingsResult = settingsFactory.Create(client);
        if (settingsResult.Settings is not { } settings)
            return NotAsked(client, "secret unreadable", settingsResult.Error!);

        try
        {
            var listed = await downloadClient.ListJobsWithFilesAsync(settings, cancellationToken);
            if (listed.IsSuccess)
                return new ClientAnswer(listed.Value, null);

            // Information: the status only — never the address or the sentence (the connection test's rule).
            logger.LogInformation(
                "Holdings listing of integration {IntegrationId} ({AdapterType}) failed: {Status}.", client.Id, client.AdapterType, listed.Status);
            // Masked again after the adapter (ADR 0007's second pass).
            return new ClientAnswer(null, SecretRedaction.Redact(listed.Error!, settings.Secrets));
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // An adapter never throws on an expected failure; this is a bug meeting an answer nobody foresaw. It costs
            // this client its answer, never the others' or the dialog's. Error, with the type and the stack, never the
            // message: a parser's message can quote what the client sent.
            logger.LogError(
                "Holdings listing of integration {IntegrationId} ({AdapterType}) threw {ExceptionType} {StackTrace}",
                client.Id,
                client.AdapterType,
                exception.GetType().FullName,
                exception.StackTrace);
            return new ClientAnswer(null, $"The app couldn't read what “{client.Name}” answered.");
        }
    }

    private ClientAnswer NotAsked(IntegrationConnectionEntity client, string why, string error)
    {
        logger.LogInformation(
            "Holdings listing of integration {IntegrationId} ({AdapterType}) did not ask it: {Reason}.", client.Id, client.AdapterType, why);
        return new ClientAnswer(null, error);
    }

    /// <summary>
    /// Places one torrent's files in the app's folders and marks the copies it holds; <c>false</c> when any of its files
    /// couldn't be placed — no save path, no metadata, no mapping, an unknown size, nothing the disk shows —, which makes
    /// its client's answer uncertain. A file outside the allowed roots, or of a size no copy has, holds none of them.
    /// </summary>
    private async Task<bool> PlaceAsync(
        IntegrationConnectionEntity client,
        ClientJobWithFiles job,
        IReadOnlyList<ReplacementCandidateCopy> targets,
        IReadOnlyList<NormalizedPath> roots,
        IReadOnlyList<string> allowedRoots,
        Dictionary<NormalizedPath, FileInventoryResult<FileFacts>> described,
        Dictionary<NormalizedPath, List<ReplacementHold>> holds,
        CancellationToken cancellationToken)
    {
        if (job.Job.ServiceSavePath is not { } servicePath
            || !NormalizedPath.TryParse(servicePath, out var savePath, out _)
            || job.Files is not { } jobFiles)
            return false;

        var pairs = client.PathMappings
            .OrderBy(mapping => mapping.Position)
            .Select(mapping => new PathMappingPair(mapping.ServicePrefix, mapping.AppPrefix))
            .ToList();
        var placed = true;
        foreach (var file in jobFiles)
        {
            // Joined as the client names it and mapped whole: a ".." in a name maps to nothing rather than somewhere else.
            var mapped = PathMapper.Map(savePath.Append(file.RelativePath.Split('/', StringSplitOptions.RemoveEmptyEntries)).ToString(), pairs);
            if (mapped is not { HasAppPath: true, AppPath: { } appText } || !NormalizedPath.TryParse(appText, out var appPath, out _))
            {
                placed = false;
                continue;
            }

            if (!roots.Any(appPath.IsUnder))
                continue;
            if (file.SizeBytes is not { } size)
            {
                placed = false;
                continue;
            }

            var sameSize = targets.Where(target => target.Fresh!.SizeBytes == size).ToList();
            if (sameSize.Count == 0)
                continue;

            if (!described.TryGetValue(appPath, out var answer))
                described[appPath] = answer = await files.DescribeAsync(appPath.ToString(), allowedRoots, cancellationToken);
            if (answer is not { Status: FileInventoryStatus.Found, Value: { DeviceId: not null, Inode: not null } facts })
            {
                placed = false;
                continue;
            }

            foreach (var target in sameSize.Where(target => target.Fresh!.DeviceId == facts.DeviceId && target.Fresh.Inode == facts.Inode))
            {
                // The torrent's own path, or the same file reached through a link (one name): the torrent holds the copy.
                // Another name of a file with several: the torrent keeps its own, and moving the copy frees nothing.
                var hold = new ReplacementHold(
                    client.Name, job.Job.ClientJobId, job.Job.Name, SecondName: !(appPath.IsSameAs(target.AppPath!) || target.Fresh!.LinkCount == 1));
                if (!holds.TryGetValue(target.AppPath!, out var held))
                    holds[target.AppPath!] = held = [];
                // Told apart by the torrent's id, never its name: two of one torrent's files reaching the copy the same
                // way are one hold, and two torrents of one name (a cross-seed) are two.
                if (!held.Contains(hold))
                    held.Add(hold);
            }
        }

        return placed;
    }

    /// <summary>What one client's listing gave: its jobs, or the sentence why there are none. Its <c>ToString</c> prints
    /// the list as its type name; the error is masked.</summary>
    private sealed record ClientAnswer(IReadOnlyList<ClientJobWithFiles>? Jobs, string? Error);
}
