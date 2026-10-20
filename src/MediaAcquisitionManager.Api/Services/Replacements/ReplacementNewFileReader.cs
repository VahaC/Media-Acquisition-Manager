using MediaAcquisitionManager.Api.Services.Files;
using MediaAcquisitionManager.Api.Services.Integrations;
using MediaAcquisitionManager.Core.Abstractions;
using MediaAcquisitionManager.Core.Abstractions.Downloads;
using MediaAcquisitionManager.Core.Abstractions.Files;
using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Downloads;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Files;
using MediaAcquisitionManager.Core.Paths;
using MediaAcquisitionManager.Core.Replacements;
using MediaAcquisitionManager.Core.Texts;
using MediaAcquisitionManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Services.Replacements;

/// <inheritdoc cref="IReplacementNewFileReader"/>
/// <remarks>
/// <para>
/// In this order, each a step of its own: the client must still be there, switched on, of a type this build reads and
/// with a secret the app can open (the poller's order); the download is found again by its id and hashes and must be
/// finished (<see cref="NewFileCompleteness.OfJob"/>); its files are listed and each new one must be complete
/// (<see cref="NewFileCompleteness.OfFiles"/>); each is placed in the app through the client's mappings
/// (<see cref="ClientFilePlaces"/>) and in the one library whose folder holds it (<see cref="ReplacementLibraryPlaces"/>),
/// then read by the media inspector (<see cref="NewFileReadability"/>); last, the client's whole listing must show no
/// other download writing one of them, by path or by identity on the disk, and none the app can't place.
/// </para>
/// <para>
/// A bad file fails the job whatever else is wrong; then a problem waiting can't cure stops it for review; then
/// anything not settled yet waits — the first file by the client's order giving the reason. The whole listing is read
/// only once every file passed: one client-wide request, not one per pass of a job that stops anyway.
/// </para>
/// <para>
/// Information: an integration's id, its type and a call's status, never a path, a name or a sentence.
/// </para>
/// </remarks>
public sealed class ReplacementNewFileReader(
    ApplicationDbContext db,
    IIntegrationAdapterRegistry adapters,
    IIntegrationSettingsFactory settingsFactory,
    IFileInventory files,
    ILogger<ReplacementNewFileReader> logger) : IReplacementNewFileReader
{
    public async Task<ReplacementFilesCheck> CheckAsync(
        Guid replacementJobId,
        DownloadJobEntity download,
        IReadOnlyList<ReplacementJobFileEntity> tickedCopies,
        DateTime checkedUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(download);
        ArgumentNullException.ThrowIfNull(tickedCopies);

        // ── the client ──
        // With its mappings: what places the files (the poller loads it without them, and needs none).
        var client = download.DownloadClientConnectionId is { } clientId
            ? await db.IntegrationConnections
                .AsNoTracking()
                .Include(connection => connection.PathMappings)
                .SingleOrDefaultAsync(connection => connection.Id == clientId, cancellationToken)
            : null;
        // SetNull: the client was removed, and its word on the files with it.
        if (client is null)
            return Stopped(ReplacementCheckStatus.Review, ClientRemovedReason(download.DownloadClientName));
        if (!client.IsEnabled)
            return Stopped(ReplacementCheckStatus.Wait, SwitchedOffReason(client.Name));
        if (adapters.Find<IDownloadClient>(client.AdapterType) is not { } downloadClient)
            return Stopped(ReplacementCheckStatus.Wait, NoAdapterReason(client.AdapterType));
        var built = settingsFactory.Create(client);
        if (built.Settings is not { } settings)
            return Stopped(ReplacementCheckStatus.Wait, built.Error!);
        var context = new ClientContext(client, downloadClient, settings);

        // ── check 1: the download, then its files ──
        var hashes = new TorrentInfoHashes(download.InfoHashV1, download.InfoHashV2);
        var found = await CallAsync(
            context,
            "job lookup",
            () => downloadClient.FindJobsAsync(settings, ClientJobCorrelation.FollowIds(download.ClientJobId, hashes), cancellationToken),
            cancellationToken);
        if (found is not { IsSuccess: true })
            return Stopped(ReplacementCheckStatus.Wait, UnaskedReason(context, found));

        var match = ClientJobCorrelation.Follow(found.Value!, download.ClientJobId, hashes);
        if (match.Status == ClientJobMatchStatus.Ambiguous)
            return Stopped(ReplacementCheckStatus.Review, TwoDownloadsReason(client.Name));
        var job = match.Status == ClientJobMatchStatus.Found ? match.Job : null;
        if (Unsettled(NewFileCompleteness.OfJob(job, client.Name), []) is { } unfinished)
            return unfinished;

        var listed = await CallAsync(
            context, "files read", () => downloadClient.ListJobFilesAsync(settings, job!.ClientJobId, cancellationToken), cancellationToken);
        // Gone between the two reads: as gone before them.
        if (listed is { Status: IntegrationCallStatus.NotFound })
            return Unsettled(NewFileCompleteness.OfJob(null, client.Name), [])!;
        if (listed is not { IsSuccess: true })
            return Stopped(ReplacementCheckStatus.Wait, UnaskedReason(context, listed));

        var rows = EvidenceOf(replacementJobId, listed.Value!);
        var complete = NewFileCompleteness.OfFiles(listed.Value!, client.Name);
        if (Unsettled(complete, rows) is { } incomplete)
        {
            // MAM-25 — the file it stopped on says why, as a file check 2 stops on does: the first, in the client's order,
            // the same rule stops on alone. A download with no file to blame (none listed, none a video) has no row to mark.
            var stopping = rows.FirstOrDefault(row =>
                NewFileCompleteness.OfFiles([listed.Value![row.Position]], client.Name).Status != NewFileCompletenessStatus.Complete);
            if (stopping is not null)
                stopping.Problem = complete.Problem;
            return incomplete;
        }

        // ── where each new file is, and check 2 ──
        var roots = await db.AllowedRoots.AsNoTracking().OrderBy(root => root.Position).Select(root => root.Path).ToListAsync(cancellationToken);
        var folders = (await LibraryFolderReader.LoadAsync(db, cancellationToken)).Mapped;
        var copies = tickedCopies.Select(copy => new TickedCopyIdentity(copy.AppPath, copy.DeviceId, copy.Inode)).ToList();
        var savePath = NormalizedPath.TryParse(job!.ServiceSavePath, out var parsedSavePath, out _) ? parsedSavePath : null;
        var pairs = ClientFilePlaces.PairsOf(client);
        var verdicts = new List<FileVerdict>();
        foreach (var newFile in complete.NewFiles)
        {
            var row = rows.Single(evidence => evidence.Position == newFile.Position);
            // One at a time: the media inspector shares the disk with the library's own reads.
            var verdict = await CheckFileAsync(row, newFile.File, savePath, pairs, folders, roots, copies, client.Name, checkedUtc, cancellationToken);
            row.Problem = verdict?.Reason;
            if (verdict is not null)
                verdicts.Add(verdict);
        }

        // The worst first — the statuses are numbered by how far they stop the job —, then the client's order (a stable
        // sort keeps it).
        if (verdicts.OrderByDescending(verdict => verdict.Status).FirstOrDefault() is { } worst)
            return new ReplacementFilesCheck(worst.Status, worst.Reason, rows);

        // ── ADR 0034's gap: another download writing a new file ──
        return await OtherDownloadAsync(context, job, rows, roots, cancellationToken) is { } other
            ? new ReplacementFilesCheck(other.Status, other.Reason, rows)
            : new ReplacementFilesCheck(ReplacementCheckStatus.Pass, null, rows);
    }

    // ── one new file ────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Places <paramref name="file"/> in the app and in its library and reads it, writing what was found on
    /// <paramref name="row"/>; why it stops or waits, or <c>null</c> when it passed. A bad file is read even outside every
    /// library folder: it fails the job whichever library would have read it.
    /// </summary>
    private async Task<FileVerdict?> CheckFileAsync(
        ReplacementJobNewFileEntity row,
        ClientJobFile file,
        NormalizedPath? savePath,
        IReadOnlyList<PathMappingPair> pairs,
        IReadOnlyList<LibraryFolder> folders,
        IReadOnlyList<string> roots,
        IReadOnlyList<TickedCopyIdentity> copies,
        string clientName,
        DateTime checkedUtc,
        CancellationToken cancellationToken)
    {
        // An unknown new path (ADR 0003): no save folder to start from, or no mapping that places it.
        if (savePath is null)
            return new FileVerdict(ReplacementCheckStatus.Review, NoSavePathReason(clientName, file.RelativePath));
        var mapped = ClientFilePlaces.Map(savePath, file.RelativePath, pairs);
        if (mapped is not { HasAppPath: true, AppPath: { } appText } || !NormalizedPath.TryParse(appText, out var appPath, out _))
            return new FileVerdict(ReplacementCheckStatus.Review, UnknownPathReason(file.RelativePath, mapped.Reason));
        row.AppPath = appPath.ToString();

        FileVerdict? placeProblem = null;
        var places = ReplacementLibraryPlaces.Of(appPath, folders);
        switch (places)
        {
            case [var place]:
                row.LibraryIntegrationId = place.IntegrationId;
                row.LibraryPath = place.LibraryPath;
                break;
            case []:
                placeProblem = new FileVerdict(ReplacementCheckStatus.Review, NoLibraryReason(row.AppPath));
                break;
            default:
                placeProblem = new FileVerdict(ReplacementCheckStatus.Review, TwoLibrariesReason(row.AppPath, places));
                break;
        }

        var inspection = await files.InspectMediaAsync(row.AppPath, roots, cancellationToken);
        if (inspection is { Status: FileInventoryStatus.Found, Value: { } found })
        {
            var video = found.Media.Tracks.FirstOrDefault(track => track.Kind == MediaTrackKind.Video);
            row.DeviceId = found.File.DeviceId;
            row.Inode = found.File.Inode;
            row.SizeBytes = found.File.SizeBytes;
            row.ModifiedUtc = found.File.ModifiedUtc;
            // Rounded as an inspection run stores it (FileInspectionRunner), so the two compare alike.
            row.DurationMilliseconds = found.Media.Duration is { } duration ? (long)Math.Round(duration.TotalMilliseconds) : null;
            row.VideoCodec = video?.Codec;
            row.Width = video?.Width;
            row.Height = video?.Height;
        }

        row.CheckedUtc = checkedUtc;
        var readability = NewFileReadability.Check(row.AppPath, inspection, file.SizeBytes, copies, clientName);
        return readability.Status switch
        {
            NewFileReadabilityStatus.Failed => new FileVerdict(ReplacementCheckStatus.Failed, readability.Problem!),
            NewFileReadabilityStatus.Review => new FileVerdict(ReplacementCheckStatus.Review, readability.Problem!),
            // A place no library holds stops it at once; waiting for the media inspector won't change that.
            _ when placeProblem is not null => placeProblem,
            NewFileReadabilityStatus.Wait => new FileVerdict(ReplacementCheckStatus.Wait, readability.Problem!),
            _ => null,
        };
    }

    /// <summary>
    /// Every video file of the download the client downloads, or doesn't say it doesn't, as an unsaved evidence row of
    /// <paramref name="replacementJobId"/> with the client's word on it: what check 1 counts, so a file it stops on is
    /// shown with it. A skipped file is no new copy; subtitles and the like never replace anything.
    /// </summary>
    private static List<ReplacementJobNewFileEntity> EvidenceOf(Guid replacementJobId, IReadOnlyList<ClientJobFile> clientFiles)
    {
        var rows = new List<ReplacementJobNewFileEntity>();
        for (var position = 0; position < clientFiles.Count; position++)
        {
            var file = clientFiles[position];
            if (file.IsWanted == false || !MediaFileNames.IsMediaFile(ReplacementText.FileNameOf(file.RelativePath)))
                continue;

            rows.Add(new ReplacementJobNewFileEntity
            {
                ReplacementJobId = replacementJobId,
                Position = position,
                RelativePath = file.RelativePath,
                ClientSizeBytes = file.SizeBytes,
                ClientProgress = file.Progress,
            });
        }

        return rows;
    }

    // ── another download ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Whether another download of the client writes one of the new <paramref name="rows"/>, all of which passed: by path
    /// once placed as the holdings are (<see cref="ClientFilePlaces"/>), or by identity on the disk for a file of the same
    /// size inside the allowed roots — a link reaches one file by two paths. <c>null</c> when none does, and every other
    /// download could be placed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// MAM-24's check before the send refused a download that would write over a file, but a download of another
    /// category may still write where this one did (ADR 0034's accepted gap, MAM-25's to close): the new file may then not
    /// be this download's own at all. The listing unreachable is a wait; refused, too old to list the files, or
    /// unreadable, it is the owner's review: it won't come right by waiting.
    /// </para>
    /// <para>
    /// A download the app can't place — no metadata yet, a save path it can't read, a file no mapping places, a file
    /// inside the allowed roots of a size the client doesn't give, or one of a new file's size the disk can't show — may
    /// still write one of them, so it is a wait, never a pass (<see cref="ClientJobWithFiles"/>' contract, the holdings'
    /// rule): a magnet gets its metadata, a mapping is added, and the next pass reads it again; the first such download, in
    /// the client's order, names the reason once the files' wait ran out. One that writes a new file stops the job first,
    /// whatever the others. A file outside the allowed roots is only compared by path: no new file is there.
    /// </para>
    /// </remarks>
    private async Task<FileVerdict?> OtherDownloadAsync(
        ClientContext context, ClientJob job, IReadOnlyList<ReplacementJobNewFileEntity> rows, IReadOnlyList<string> roots, CancellationToken cancellationToken)
    {
        var listed = await CallAsync(
            context, "holdings listing", () => context.DownloadClient.ListJobsWithFilesAsync(context.Settings, cancellationToken), cancellationToken);
        if (listed is null or { Status: IntegrationCallStatus.Unreachable or IntegrationCallStatus.TimedOut })
            return new FileVerdict(ReplacementCheckStatus.Wait, UnaskedReason(context, listed));
        // Its own sentence: the client's speaks of telling which downloads hold a library file (MAM-24).
        if (listed is { Status: IntegrationCallStatus.UnsupportedVersion })
            return new FileVerdict(ReplacementCheckStatus.Review, TooOldReason(context.Client.Name));
        if (!listed.IsSuccess)
            return new FileVerdict(ReplacementCheckStatus.Review, OthersUnlistedReason(context.Client.Name, Masked(context, listed.Error!)));

        var newFiles = rows
            .Select(row => (Row: row, Path: NormalizedPath.TryParse(row.AppPath, out var path, out _) ? path : null))
            .Where(file => file.Path is not null)
            .Select(file => (file.Row, Path: file.Path!))
            .ToList();
        var parsedRoots = roots.Select(root => NormalizedPath.TryParse(root, out var path, out _) ? path : null).OfType<NormalizedPath>().ToList();
        var pairs = ClientFilePlaces.PairsOf(context.Client);
        var described = new Dictionary<NormalizedPath, FileInventoryResult<FileFacts>>();
        string? unplaced = null;
        foreach (var other in listed.Value!)
        {
            // Told apart by the client's id, never the name: a cross-seed of the same content is another download.
            if (string.Equals(other.Job.ClientJobId, job.ClientJobId, StringComparison.OrdinalIgnoreCase))
                continue;

            var (written, placed) = await WritesAsync(other, newFiles, pairs, parsedRoots, roots, described, cancellationToken);
            if (written is not null)
            {
                var reason = OtherDownloadReason(context.Client.Name, other.Job.Name, written.AppPath!);
                written.Problem = reason;
                return new FileVerdict(ReplacementCheckStatus.Review, reason);
            }

            if (!placed)
                unplaced ??= other.Job.Name;
        }

        return unplaced is null ? null : new FileVerdict(ReplacementCheckStatus.Wait, UnplacedReason(context.Client.Name, unplaced));
    }

    /// <summary>
    /// The new file the other download <paramref name="other"/> writes — the first by its files' order —, by path or by
    /// identity on the disk, each path looked at once; and whether every file of it could be placed and, inside the allowed
    /// roots, compared (the holdings' rule, <c>ReplacementHoldingReader</c>).
    /// </summary>
    private async Task<(ReplacementJobNewFileEntity? Written, bool Placed)> WritesAsync(
        ClientJobWithFiles other,
        IReadOnlyList<(ReplacementJobNewFileEntity Row, NormalizedPath Path)> newFiles,
        IReadOnlyList<PathMappingPair> pairs,
        IReadOnlyList<NormalizedPath> parsedRoots,
        IReadOnlyList<string> roots,
        Dictionary<NormalizedPath, FileInventoryResult<FileFacts>> described,
        CancellationToken cancellationToken)
    {
        if (!NormalizedPath.TryParse(other.Job.ServiceSavePath, out var savePath, out _) || other.Files is not { } otherFiles)
            return (null, false);

        var placed = true;
        foreach (var file in otherFiles)
        {
            if (ClientFilePlaces.Map(savePath, file.RelativePath, pairs) is not { HasAppPath: true, AppPath: { } appText }
                || !NormalizedPath.TryParse(appText, out var appPath, out _))
            {
                placed = false;
                continue;
            }

            if (newFiles.FirstOrDefault(newFile => appPath.IsSameAs(newFile.Path)).Row is { } samePath)
                return (samePath, placed);
            // Only where the app may look, and only a file of a new file's size.
            if (!parsedRoots.Any(appPath.IsUnder))
                continue;
            if (file.SizeBytes is not { } size)
            {
                placed = false;
                continue;
            }

            var sameSize = newFiles.Where(newFile => newFile.Row.SizeBytes == size).Select(newFile => newFile.Row).ToList();
            if (sameSize.Count == 0)
                continue;
            if (!described.TryGetValue(appPath, out var answer))
                described[appPath] = answer = await files.DescribeAsync(appPath.ToString(), roots, cancellationToken);
            // Nothing there: no second name of a new file. Anything else the disk can't show is not "not the same".
            if (answer.Status == FileInventoryStatus.NotFound)
                continue;
            if (answer is not { Status: FileInventoryStatus.Found, Value: { DeviceId: { } deviceId, Inode: { } inode } })
            {
                placed = false;
                continue;
            }

            if (sameSize.FirstOrDefault(row => row.DeviceId == deviceId && row.Inode == inode) is { } linked)
                return (linked, placed);
        }

        return (null, placed);
    }

    // ── calls ───────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// One call to the client that never throws but for the caller's cancellation: <c>null</c> when the adapter threw,
    /// which is a bug nobody foresaw. Error, with the type and the stack, never the message — a parser's message can quote
    /// what the client sent (the poller's rule). A failure is logged at Information with its status only.
    /// </summary>
    private async Task<IntegrationCallResult<T>?> CallAsync<T>(
        ClientContext context, string call, Func<Task<IntegrationCallResult<T>>> send, CancellationToken cancellationToken)
        where T : class
    {
        try
        {
            var result = await send();
            if (!result.IsSuccess)
            {
                logger.LogInformation(
                    "The {Call} for a replacement's check of integration {IntegrationId} ({AdapterType}) failed: {Status}.",
                    call,
                    context.Client.Id,
                    context.Client.AdapterType,
                    result.Status);
            }

            return result;
        }
        catch (Exception exception) when (!(exception is OperationCanceledException && cancellationToken.IsCancellationRequested))
        {
            logger.LogError(
                "The {Call} for a replacement's check of integration {IntegrationId} ({AdapterType}) threw {ExceptionType} {StackTrace}",
                call,
                context.Client.Id,
                context.Client.AdapterType,
                exception.GetType().FullName,
                exception.StackTrace);
            return null;
        }
    }

    /// <summary>The client's own sentence, masked again after the adapter (ADR 0007's second pass).</summary>
    private static Message Masked(ClientContext context, Message error) => error.Redacted(context.Settings.Secrets);

    /// <summary>Why a call the client didn't answer leaves the files unchecked, once the files' wait ran out: the client's
    /// own sentence, or the app's for an adapter that threw.</summary>
    private static Message UnaskedReason<T>(ClientContext context, IntegrationCallResult<T>? failed)
        where T : class =>
        failed is null
            ? ThrewReason(context.Client.Name)
            : Message.Of(
                "replacements.new-files.client-unasked", ("client", MessageText.Clean(context.Client.Name)), ("error", Masked(context, failed.Error!)));

    /// <summary>A check that stopped before any new file was read.</summary>
    private static ReplacementFilesCheck Stopped(ReplacementCheckStatus status, Message reason) => new(status, reason, []);

    /// <summary>What check 1's <paramref name="result"/> makes of the check when it is not complete, with the evidence
    /// read so far; <c>null</c> when it is.</summary>
    private static ReplacementFilesCheck? Unsettled(NewFileCompletenessResult result, IReadOnlyList<ReplacementJobNewFileEntity> rows) =>
        result.Status switch
        {
            NewFileCompletenessStatus.Complete => null,
            NewFileCompletenessStatus.Wait => new ReplacementFilesCheck(ReplacementCheckStatus.Wait, result.Problem, rows),
            _ => new ReplacementFilesCheck(ReplacementCheckStatus.Review, result.Problem, rows),
        };

    // The reasons. Each quotes the client by its name now — what Integrations lists — and a file by its path: in the app
    // once placed, in the client's own terms before. MAM-47 (ADR 0042) — as messages of the replacements.new-files keys:
    // names and paths cleaned and cut, the quotes the template's.

    private static Message ClientRemovedReason(string clientName) =>
        Message.Of("replacements.new-files.client-removed", ("client", MessageText.Clean(clientName)));

    private static Message SwitchedOffReason(string clientName) =>
        Message.Of("replacements.new-files.client-switched-off", ("client", MessageText.Clean(clientName)));

    private static Message NoAdapterReason(string adapterType) =>
        Message.Of("replacements.new-files.no-adapter", ("type", MessageText.Clean(adapterType)));

    private static Message ThrewReason(string clientName) =>
        Message.Of("replacements.new-files.client-threw", ("client", MessageText.Clean(clientName)));

    private static Message TwoDownloadsReason(string clientName) =>
        Message.Of("replacements.new-files.two-downloads", ("client", MessageText.Clean(clientName)));

    private static Message NoSavePathReason(string clientName, string relativePath) =>
        Message.Of("replacements.new-files.no-save-path", ("client", MessageText.Clean(clientName)), ("path", MessageText.Clean(relativePath)));

    // The path mapper's own sentence after the colon, when it gave one.
    private static Message UnknownPathReason(string relativePath, Message? mapperReason) =>
        mapperReason is null
            ? Message.Of("replacements.new-files.unknown-path", ("path", MessageText.Clean(relativePath)))
            : Message.Of("replacements.new-files.unknown-path-why", ("path", MessageText.Clean(relativePath)), ("reason", mapperReason));

    private static Message NoLibraryReason(string appPath) =>
        Message.Of("replacements.new-files.no-library", ("path", MessageText.Clean(appPath)));

    private static Message TwoLibrariesReason(string appPath, IReadOnlyList<ReplacementLibraryPlace> places) =>
        Message.Of(
            "replacements.new-files.two-libraries",
            ("path", MessageText.Clean(appPath)),
            ("libraries", new ListValue(places.Select(place => (MessageValue)MessageText.Clean(place.LibraryName)))));

    private static Message OthersUnlistedReason(string clientName, Message error) =>
        Message.Of("replacements.new-files.others-unlisted", ("client", MessageText.Clean(clientName)), ("error", error));

    private static Message TooOldReason(string clientName) =>
        Message.Of("replacements.new-files.too-old", ("client", MessageText.Clean(clientName)));

    private static Message UnplacedReason(string clientName, string downloadName) =>
        Message.Of("replacements.new-files.unplaced", ("client", MessageText.Clean(clientName)), ("download", MessageText.Clean(downloadName)));

    private static Message OtherDownloadReason(string clientName, string downloadName, string appPath) =>
        Message.Of(
            "replacements.new-files.other-download",
            ("client", MessageText.Clean(clientName)),
            ("download", MessageText.Clean(downloadName)),
            ("path", MessageText.Clean(appPath)));

    /// <summary>A client that can be asked: its row, its adapter and the settings to ask it with, for one check.</summary>
    private sealed record ClientContext(IntegrationConnectionEntity Client, IDownloadClient DownloadClient, IntegrationConnectionSettings Settings);

    /// <summary>What one step found against the files: whether it stops or waits, and why.</summary>
    private sealed record FileVerdict(ReplacementCheckStatus Status, Message Reason);
}
