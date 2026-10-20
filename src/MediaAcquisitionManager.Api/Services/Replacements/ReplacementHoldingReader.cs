using MediaAcquisitionManager.Api.Services.Integrations;
using MediaAcquisitionManager.Core.Abstractions;
using MediaAcquisitionManager.Core.Abstractions.Downloads;
using MediaAcquisitionManager.Core.Abstractions.Files;
using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Paths;
using MediaAcquisitionManager.Core.Texts;
using MediaAcquisitionManager.Infrastructure.Integrations;
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
/// MAM-28 — but before a move every library client — one the app sent a replacement to — is asked whatever its mappings
/// say: a mapping deleted since takes the client's paths as the app's own, one pointed elsewhere leads outside the roots,
/// and either way an old copy it lists would lie outside them, where the app never looks. So a library client's torrent
/// file outside the roots of a name's file name (in any letter case), of its size or of no size counts its torrent
/// outside (<see cref="ReplacementClientReading.OutsideTorrents"/>) and the client read in part; a file like none of them
/// still counts for nothing, so a broken mapping stops a move only when it matters. Nor does a file only of the size of a
/// name whose file has several names (its link count above 1): that is the torrent's own name of it, a hardlink twin
/// outside the roots, which moving the library's name leaves seeding.
/// </para>
/// <para>
/// MAM-30, D11 — a client that writes an unfinished file under its name and a suffix
/// (<see cref="DownloadClientCapabilities.PartialFileSuffix"/>, Transmission's <c>.part</c>) holds that name too: a copy's
/// companion of that name is the torrent's own file, still downloading, and moving it would lose the torrent its data. The
/// disk is read at the suffixed name when the file isn't at its own yet, and outside the roots a file's name counts with
/// or without the suffix.
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
        // what it couldn't tell. No library clients: what lies outside the roots counts only right before a move (MAM-28).
        return ReadHoldsAsync(targets, allowedRoots, libraryClients: [], reachingOnly: false, cancellationToken);
    }

    public Task<ReplacementHoldings> ReadAsync(
        IReadOnlyList<ReplacementHoldingTarget> targets,
        IReadOnlyList<string> allowedRoots,
        IReadOnlyCollection<Guid> libraryClients,
        CancellationToken cancellationToken = default) =>
        ReadHoldsAsync(targets, allowedRoots, libraryClients, reachingOnly: true, cancellationToken);

    /// <summary>
    /// The read itself: every download client's holds of <paramref name="targets"/> inside <paramref name="allowedRoots"/>;
    /// with <paramref name="reachingOnly"/>, only the clients whose mappings reach those roots
    /// (<see cref="ClientFilePlaces.Reaches"/>) and, whatever their mappings, the <paramref name="libraryClients"/>, whose
    /// torrents' files outside the roots that may be a name are counted (MAM-28).
    /// </summary>
    private async Task<ReplacementHoldings> ReadHoldsAsync(
        IReadOnlyList<ReplacementHoldingTarget> targets,
        IReadOnlyList<string> allowedRoots,
        IReadOnlyCollection<Guid> libraryClients,
        bool reachingOnly,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentNullException.ThrowIfNull(allowedRoots);
        ArgumentNullException.ThrowIfNull(libraryClients);

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
        // (ADR 0031): left out, unasked, rather than counted uncertain for torrents it saves elsewhere. MAM-28 — but never a
        // client the app sent a replacement to: its mapping may have been pointed elsewhere since, and it is asked whatever
        // its mappings say, with no "did not ask it" line.
        var clients = reachingOnly ? stored.Where(client => libraryClients.Contains(client.Id) || Reaches(client, roots)).ToList() : stored;
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

            var libraryClient = libraryClients.Contains(client.Id);
            var (unplaced, outside) = (0, 0);
            foreach (var job in jobs)
            {
                var (placed, outsideRoots) = await PlaceAsync(
                    client, libraryClient, answer.PartialFileSuffix, job, names, roots, allowedRoots, described, holds, cancellationToken);
                if (!placed)
                    unplaced++;
                if (outsideRoots)
                    outside++;
            }

            // Information: ids and counts only, never a torrent's name or a path.
            logger.LogInformation(
                "Holdings listing of integration {IntegrationId} ({AdapterType}) read {TorrentCount} torrents; {UnplacedCount} could not be placed.",
                client.Id,
                client.AdapterType,
                jobs.Count,
                unplaced);
            // MAM-28 — a line of its own, and only where the count was taken: a client the app sent a replacement to, right
            // before a move. The dialog's read and every other client count nothing outside the roots, so say nothing of it.
            if (libraryClient)
            {
                logger.LogInformation(
                    "Holdings listing of integration {IntegrationId} ({AdapterType}), a client the app sent a replacement to, found {OutsideCount} torrents listing a file outside the allowed folders that may be an old copy.",
                    client.Id,
                    client.AdapterType,
                    outside);
            }

            readings.Add(new ReplacementClientReading(
                client.Name, unplaced > 0 || outside > 0 ? ReplacementClientCheck.Partial : ReplacementClientCheck.Read, null, unplaced, outside));
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
            return NotAsked(client, "switched off", Message.Of("replacements.holdings.switched-off", ("client", client.Name)));
        if (adapters.Find<IDownloadClient>(client.AdapterType) is not { } downloadClient)
            return NotAsked(client, "type unavailable", Message.Of("replacements.holdings.type-unavailable", ("type", client.AdapterType)));

        var settingsResult = settingsFactory.Create(client);
        if (settingsResult.Settings is not { } settings)
            return NotAsked(client, "secret unreadable", settingsResult.Error!);

        try
        {
            var listed = await downloadClient.ListJobsWithFilesAsync(settings, cancellationToken);
            if (listed.IsSuccess)
                return new ClientAnswer(listed.Value, null, downloadClient.Capabilities.PartialFileSuffix);

            // Information: the status only — never the address or the sentence (the connection test's rule).
            logger.LogInformation(
                "Holdings listing of integration {IntegrationId} ({AdapterType}) failed: {Status}.", client.Id, client.AdapterType, listed.Status);
            // Masked again after the adapter (ADR 0007's second pass), value by value (MAM-47).
            return new ClientAnswer(null, listed.Error!.Redacted(settings.Secrets), null);
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
            return new ClientAnswer(null, IntegrationAdapterMessages.UnreadableAnswer(client.Name), null);
        }
    }

    private ClientAnswer NotAsked(IntegrationConnectionEntity client, string why, Message error)
    {
        logger.LogInformation(
            "Holdings listing of integration {IntegrationId} ({AdapterType}) did not ask it: {Reason}.", client.Id, client.AdapterType, why);
        return new ClientAnswer(null, error, null);
    }

    /// <summary>
    /// Places one torrent's files in the app's folders and marks the copies it holds. <c>Placed</c> is <c>false</c> when
    /// any of its files couldn't be placed — no save path, no metadata, no mapping, an unknown size, nothing the disk shows
    /// —, which makes its client's answer uncertain. A file outside the allowed roots, or of a size no copy has, holds none
    /// of them; a file at a copy's own path holds it whatever its size (MAM-26), and is placed. MAM-28 — <c>Outside</c> is
    /// <c>true</c> when <paramref name="libraryClient"/>, a client the app sent a replacement to, lists a file outside the
    /// allowed roots that may be one of <paramref name="targets"/> — of its file name, of no size, or of its size unless its
    /// file has several names —, which makes its answer uncertain too. MAM-30 — with <paramref name="partialFileSuffix"/>,
    /// each file's name with the suffix is its too (D11).
    /// </summary>
    private async Task<(bool Placed, bool Outside)> PlaceAsync(
        IntegrationConnectionEntity client,
        bool libraryClient,
        string? partialFileSuffix,
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
            return (false, false);

        var pairs = ClientFilePlaces.PairsOf(client);
        var (placed, outside) = (true, false);
        foreach (var file in jobFiles)
        {
            var mapped = ClientFilePlaces.Map(savePath, file.RelativePath, pairs);
            if (mapped is not { HasAppPath: true, AppPath: { } appText } || !NormalizedPath.TryParse(appText, out var appPath, out _))
            {
                placed = false;
                continue;
            }

            // MAM-30, D11 — where the file lies while it is unfinished, beside its own place.
            var partial = PartialOf(appPath, partialFileSuffix);
            if (!roots.Any(appPath.IsUnder))
            {
                // MAM-28 — never looked at there (ADR 0009), but a library client's file of a name's file name or size, or of
                // no size, may be that name behind a mapping deleted or pointed elsewhere since: the torrent can't be ruled
                // out. A prefix mapping never changes a file's name; the size catches the name reached through a link. But
                // of a name whose file has several names, a file only of its size is the torrent's own name of it — a
                // hardlink twin outside the roots —, which moving the library's leaves seeding; a count the disk doesn't
                // tell may be one name.
                if (libraryClient && targets.Any(target => file.SizeBytes is not { } outsideSize
                        || (outsideSize == target.Fresh.SizeBytes && target.Fresh.LinkCount is not > 1)
                        || SameFileName(appPath, target.AppPath)
                        || (partial is not null && SameFileName(partial, target.AppPath))))
                    outside = true;
                continue;
            }

            // MAM-26 — the name's own path is a hold whatever size the torrent lists (ADR 0031: "an old copy any torrent
            // lists, in any state"): an errored torrent, one missing its files or a companion edited since lists another
            // size than the disk's, and a recheck would still write that path back. Its place is known, so nothing below
            // leaves the torrent unplaced for it; the disk is still read for another name of the file.
            // MAM-30 — and its unfinished name's, which is the same file's place (D11).
            List<ReplacementHoldingTarget> atPath =
                [.. targets.Where(target => appPath.IsSameAs(target.AppPath) || (partial is not null && partial.IsSameAs(target.AppPath)))];
            foreach (var target in atPath)
                Hold(holds, target, new ReplacementHold(client.Name, job.Job.ClientJobId, job.Job.Name, SecondName: false));
            if (file.SizeBytes is not { } size)
            {
                placed &= atPath.Count > 0;
                continue;
            }

            var sameSize = targets.Where(target => target.Fresh.SizeBytes == size).ToList();
            if (sameSize.Count == 0)
                continue;

            // MAM-30 — at its unfinished name when it isn't at its own yet (D11).
            var (onDisk, answer) = (appPath, await DescribeOnceAsync(appPath, allowedRoots, described, cancellationToken));
            if (answer.Status == FileInventoryStatus.NotFound && partial is not null)
                (onDisk, answer) = (partial, await DescribeOnceAsync(partial, allowedRoots, described, cancellationToken));
            if (answer is not { Status: FileInventoryStatus.Found, Value: { DeviceId: not null, Inode: not null } facts })
            {
                placed &= atPath.Count > 0;
                continue;
            }

            foreach (var target in sameSize.Where(target => target.Fresh.DeviceId == facts.DeviceId && target.Fresh.Inode == facts.Inode))
            {
                // The torrent's own path, or the same file reached through a link (one name): the torrent holds the copy.
                // Another name of a file with several: the torrent keeps its own, and moving the copy frees nothing.
                Hold(holds, target, new ReplacementHold(
                    client.Name, job.Job.ClientJobId, job.Job.Name, SecondName: !(onDisk.IsSameAs(target.AppPath) || target.Fresh.LinkCount == 1)));
            }
        }

        return (placed, outside);
    }

    /// <summary>What the disk says is at <paramref name="path"/>, asked once per path however many torrents list it.</summary>
    private async Task<FileInventoryResult<FileFacts>> DescribeOnceAsync(
        NormalizedPath path,
        IReadOnlyList<string> allowedRoots,
        Dictionary<NormalizedPath, FileInventoryResult<FileFacts>> described,
        CancellationToken cancellationToken)
    {
        if (!described.TryGetValue(path, out var answer))
            described[path] = answer = await files.DescribeAsync(path.ToString(), allowedRoots, cancellationToken);
        return answer;
    }

    /// <summary>MAM-30 — <paramref name="path"/> with <paramref name="partialFileSuffix"/> on its file's name: where a client
    /// that names an unfinished file so keeps it; <c>null</c> for a client that doesn't, or a path that names no file.</summary>
    private static NormalizedPath? PartialOf(NormalizedPath path, string? partialFileSuffix) =>
        string.IsNullOrEmpty(partialFileSuffix) || path.Segments.Count == 0
            ? null
            : path with { Segments = [.. path.Segments.SkipLast(1), path.Segments[^1] + partialFileSuffix] };

    /// <summary>MAM-28 — whether <paramref name="path"/> and <paramref name="other"/> end in the same file name, in any
    /// letter case. A root names no file, so it ends in none: a client's file a mapping puts at a bare root is no name's by
    /// its name, and reading its name never throws.</summary>
    private static bool SameFileName(NormalizedPath path, NormalizedPath other) =>
        path.Segments is [.., var name] && other.Segments is [.., var otherName] && string.Equals(name, otherName, StringComparison.OrdinalIgnoreCase);

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

    /// <summary>What one client's listing gave: its jobs, or the sentence why there are none — MAM-30, and the suffix its
    /// unfinished files carry, if any. Its <c>ToString</c> prints the list as its type name; the error is masked.</summary>
    private sealed record ClientAnswer(IReadOnlyList<ClientJobWithFiles>? Jobs, Message? Error, string? PartialFileSuffix);
}
