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
/// MAM-26 — but a torrent file whose mapped path is a name's own path holds it whatever size it lists (ADR 0031: "an old
/// copy any torrent lists, in any state"): there the path alone is the claim, as a recheck would write that path back.
/// And the read before a move asks only the clients whose mappings reach the allowed roots (ADR 0031's "each download
/// client whose paths reach the allowed folders"); the Download dialog's read still answers for every client.
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
    public Task<ReplacementHoldings> ReadAsync(
        IReadOnlyList<ReplacementCandidateCopy> copies, IReadOnlyList<string> allowedRoots, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(copies);
        // MAM-26 — the dialog's copies as the names to look for: only a copy with a path and its identity read fresh can be
        // confirmed held.
        List<ReplacementHoldingTarget> targets =
        [
            .. copies
                .Where(copy => copy is { AppPath: not null, Fresh: { DeviceId: not null, Inode: not null } })
                .Select(copy => new ReplacementHoldingTarget(copy.AppPath!, copy.Fresh!)),
        ];
        // The dialog answers for every client, one whose mappings reach no allowed root included: it only marks, and says
        // what it couldn't tell.
        return ReadHoldsAsync(targets, allowedRoots, reachingOnly: false, cancellationToken);
    }

    public Task<ReplacementHoldings> ReadAsync(
        IReadOnlyList<ReplacementHoldingTarget> targets, IReadOnlyList<string> allowedRoots, CancellationToken cancellationToken = default) =>
        ReadHoldsAsync(targets, allowedRoots, reachingOnly: true, cancellationToken);

    /// <summary>
    /// The read itself: every download client's holds of <paramref name="targets"/> inside <paramref name="allowedRoots"/>;
    /// with <paramref name="reachingOnly"/>, only the clients whose mappings reach those roots
    /// (<see cref="ClientFilePlaces.Reaches"/>).
    /// </summary>
    private async Task<ReplacementHoldings> ReadHoldsAsync(
        IReadOnlyList<ReplacementHoldingTarget> targets, IReadOnlyList<string> allowedRoots, bool reachingOnly, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentNullException.ThrowIfNull(allowedRoots);

        // Every download client, switched off or not: one the app may not ask can still hold the copies (R2). Read before
        // the clients are asked, as the context is not safe to share between the calls below.
        var stored = await db.IntegrationConnections
            .AsNoTracking()
            .Include(client => client.PathMappings)
            .Where(client => client.Kind == IntegrationKind.DownloadClient)
            // The normalised name is unique, so the order is total.
            .OrderBy(client => client.NormalizedName)
            .ToListAsync(cancellationToken);
        var roots = allowedRoots.Select(root => NormalizedPath.TryParse(root, out var path, out _) ? path : null).OfType<NormalizedPath>().ToList();
        // MAM-26 — before a move, a client whose every mapping leads outside the allowed roots can name no file inside them
        // (ADR 0031): left out, unasked, rather than counted uncertain for torrents it saves elsewhere.
        var clients = reachingOnly ? stored.Where(client => Reaches(client, roots)).ToList() : stored;
        // Once per path: one file several works list is one copy.
        List<ReplacementHoldingTarget> names =
        [
            .. targets
                .Where(target => target.Fresh is { DeviceId: not null, Inode: not null })
                .GroupBy(target => target.AppPath)
                .Select(path => path.First()),
        ];

        // Each client once and all of them at once, the DestinationChecker way: the slowest sets the wait, and one that
        // fails can't hold the others back.
        var answers = await Task.WhenAll(clients.Select(client => AskAsync(client, cancellationToken)));
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
                if (!await PlaceAsync(client, job, names, roots, allowedRoots, described, holds, cancellationToken))
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

    /// <summary>Whether <paramref name="client"/>'s mappings reach <paramref name="roots"/>, and a line in the log when they
    /// don't: ids only.</summary>
    private bool Reaches(IntegrationConnectionEntity client, IReadOnlyList<NormalizedPath> roots)
    {
        if (ClientFilePlaces.Reaches(ClientFilePlaces.PairsOf(client), roots))
            return true;

        logger.LogInformation(
            "Holdings listing of integration {IntegrationId} ({AdapterType}) did not ask it: its path mappings reach no allowed folder.",
            client.Id,
            client.AdapterType);
        return false;
    }

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
    /// its client's answer uncertain. A file outside the allowed roots, or of a size no copy has, holds none of them; a
    /// file at a copy's own path holds it whatever its size (MAM-26), and is placed.
    /// </summary>
    private async Task<bool> PlaceAsync(
        IntegrationConnectionEntity client,
        ClientJobWithFiles job,
        IReadOnlyList<ReplacementHoldingTarget> targets,
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

        var pairs = ClientFilePlaces.PairsOf(client);
        var placed = true;
        foreach (var file in jobFiles)
        {
            var mapped = ClientFilePlaces.Map(savePath, file.RelativePath, pairs);
            if (mapped is not { HasAppPath: true, AppPath: { } appText } || !NormalizedPath.TryParse(appText, out var appPath, out _))
            {
                placed = false;
                continue;
            }

            if (!roots.Any(appPath.IsUnder))
                continue;

            // MAM-26 — the name's own path is a hold whatever size the torrent lists (ADR 0031: "an old copy any torrent
            // lists, in any state"): an errored torrent, one missing its files or a companion edited since lists another
            // size than the disk's, and a recheck would still write that path back. Its place is known, so nothing below
            // leaves the torrent unplaced for it; the disk is still read for another name of the file.
            var atPath = targets.FirstOrDefault(target => appPath.IsSameAs(target.AppPath));
            if (atPath is not null)
                Hold(holds, atPath, new ReplacementHold(client.Name, job.Job.ClientJobId, job.Job.Name, SecondName: false));
            if (file.SizeBytes is not { } size)
            {
                placed &= atPath is not null;
                continue;
            }

            var sameSize = targets.Where(target => target.Fresh.SizeBytes == size).ToList();
            if (sameSize.Count == 0)
                continue;

            if (!described.TryGetValue(appPath, out var answer))
                described[appPath] = answer = await files.DescribeAsync(appPath.ToString(), allowedRoots, cancellationToken);
            if (answer is not { Status: FileInventoryStatus.Found, Value: { DeviceId: not null, Inode: not null } facts })
            {
                placed &= atPath is not null;
                continue;
            }

            foreach (var target in sameSize.Where(target => target.Fresh.DeviceId == facts.DeviceId && target.Fresh.Inode == facts.Inode))
            {
                // The torrent's own path, or the same file reached through a link (one name): the torrent holds the copy.
                // Another name of a file with several: the torrent keeps its own, and moving the copy frees nothing.
                Hold(holds, target, new ReplacementHold(
                    client.Name, job.Job.ClientJobId, job.Job.Name, SecondName: !(appPath.IsSameAs(target.AppPath) || target.Fresh.LinkCount == 1)));
            }
        }

        return placed;
    }

    /// <summary>Adds <paramref name="hold"/> to <paramref name="target"/>'s holds, once.</summary>
    private static void Hold(Dictionary<NormalizedPath, List<ReplacementHold>> holds, ReplacementHoldingTarget target, ReplacementHold hold)
    {
        if (!holds.TryGetValue(target.AppPath, out var held))
            holds[target.AppPath] = held = [];
        // Told apart by the torrent's id, never its name: two of one torrent's files reaching the copy the same way are one
        // hold, and two torrents of one name (a cross-seed) are two.
        if (!held.Contains(hold))
            held.Add(hold);
    }

    /// <summary>What one client's listing gave: its jobs, or the sentence why there are none. Its <c>ToString</c> prints
    /// the list as its type name; the error is masked.</summary>
    private sealed record ClientAnswer(IReadOnlyList<ClientJobWithFiles>? Jobs, string? Error);
}
