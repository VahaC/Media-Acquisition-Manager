using MediaAcquisitionManager.Api.Services.LibraryLinks;
using MediaAcquisitionManager.Core.Abstractions.Files;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Paths;
using MediaAcquisitionManager.Core.Texts;
using MediaAcquisitionManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Services.Files;

/// <inheritdoc cref="IFileInspectionRunner"/>
/// <remarks>
/// <para>
/// MAM-9, ADR 0009 — one pass over every stored file name, in four steps: map each name to the
/// app's path through its integration's current mappings (ADR 0003); look at each distinct path through
/// the file inventory, inside the allowed roots only; answer from the cache what it holds for the file's
/// identity, size and modification time (specification §3.3); and read with the media inspector, once per
/// key, what it doesn't. Readings no name points at any more are then forgotten, which is how a changed
/// file's old reading goes.
/// </para>
/// <para>
/// MAM-18, ADR 0021 — every stored name, not only the libraries': the names a scan of the media folders found
/// that no library reported have no version and no source connection, so they map as they are, and are read
/// like any other (FolderScanInspectionHttpTests). Narrowing the names loaded here to the libraries' would leave
/// them unread.
/// </para>
/// <para>
/// MAM-19, ADR 0022 — and a last step: once every name carries what this run's look read, the files a
/// byte-identical copy could hide among — a size a file of another identity shares, media facts that don't
/// tell them apart — are read whole for their SHA-256, one at a time and through a cache keyed like the
/// readings; a hash goes once neither a name nor a reading stands for its bytes.
/// </para>
/// <para>
/// What each file showed is written as soon as it is known, so the Library page's counts move while the
/// run goes and a run that stops keeps what it had read. The writes share this scope's one context, which
/// serves one operation at a time, so they queue on <see cref="_writes"/>; the looks and reads around them
/// run <see cref="MaximumConcurrentReads"/> at a time.
/// </para>
/// <para>
/// MAM-34 — and once it has succeeded, the owner's TMDb links are brought up to date with the identities it read
/// (<c>FileInspectionRunner.Links.cs</c>).
/// </para>
/// </remarks>
public sealed partial class FileInspectionRunner(
    ApplicationDbContext db,
    IFileInventory inventory,
    ILibraryLinkReattacher linkReattacher,
    TimeProvider time,
    ILogger<FileInspectionRunner> logger) : IFileInspectionRunner
{
    /// <summary>
    /// MAM-9 — how many files are looked at and read at a time. The media inspector reads the start of a
    /// file from a spinning disk (the reference installation's 14 TB drive): two overlap one file's seek
    /// with the other's parse, while more only add seeks and slow every read down. Bounded rather than one
    /// at a time, so a first inspection of a library of hundreds of films takes minutes, not an hour.
    /// </summary>
    public const int MaximumConcurrentReads = 2;

    internal static readonly Message NoAllowedRootsError = Message.Of("files.inspection.no-allowed-roots-any-more");

    internal static readonly Message IdentityUnknownError = Message.Of("files.inspection.identity-unknown");

    // The four below stand in where the file inventory gave no reason of its own; the inventory's, when it gives one,
    // says more (it names the path).
    internal static readonly Message InspectorUnavailableError = Message.Of("files.inspection.inspector-unavailable");

    internal static readonly Message ChangedWhileReadingError = Message.Of("files.inspection.changed-while-reading");

    internal static readonly Message MediaUnreadableError = Message.Of("files.inspection.media-unreadable");

    internal static readonly Message UnreadableError = Message.Of("files.inspection.unreadable");

    private readonly SemaphoreSlim _writes = new(1, 1);

    // The reading each name pointed at when the run loaded it, and the readings this run moved a name
    // away from (taken under _writes): step 5 may forget those, never a reading a sync orphaned meanwhile.
    private Dictionary<Guid, Guid> _linkedAtStart = [];
    private readonly HashSet<Guid> _replacedReadings = [];

    // The first "can't start ffprobe" reason of this run: every later read would fail the same way, so
    // the files still waiting get it without starting the inspector hundreds of times.
    private Message? _inspectorUnavailable;

    public async Task RunAsync(Guid runId, CancellationToken cancellationToken = default)
    {
        // One worker runs one run at a time, so a running run other than this one is dead: its worker
        // couldn't mark it failed, the database refusing that too (a full disk). Left running, it would
        // hold IX_FileInspectionRuns_OneRunPerActiveStatus's one running slot, and every claim from here
        // to the next start would break the index and fail as an app error.
        await db.FileInspectionRuns
            .Where(run => run.Id != runId && run.Status == FileInspectionRunStatus.Running)
            .MarkFailedAsync(FileInspectionWorker.AppErrorMessage, time.GetUtcNow().UtcDateTime);

        // The atomic claim: only a queued run turns running, and only once, whoever else holds its id (the
        // id queued twice, the reconciler queueing it again). Nothing else is done for a run not claimed.
        var startedUtc = time.GetUtcNow().UtcDateTime;
        var claimed = await db.FileInspectionRuns
            .Where(run => run.Id == runId && run.Status == FileInspectionRunStatus.Queued)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(run => run.Status, FileInspectionRunStatus.Running)
                    .SetProperty(run => run.StartedUtc, startedUtc),
                cancellationToken) == 1;
        if (!claimed)
            return;

        // Read once, here: a run uses the roots as they were when it started, and the inventory checks
        // every path against them.
        var roots = await db.AllowedRoots
            .AsNoTracking()
            .OrderBy(root => root.Position)
            .Select(root => root.Path)
            .ToListAsync(cancellationToken);
        if (roots.Count == 0)
        {
            await db.FileInspectionRuns
                .Where(run => run.Id == runId && run.Status == FileInspectionRunStatus.Running)
                .MarkFailedAsync(NoAllowedRootsError, time.GetUtcNow().UtcDateTime);
            logger.LogWarning("File inspection {RunId} failed: no media folder is allowed any more.", runId);
            return;
        }

        // Taken before the names are loaded, so a reading a sync orphans from here on is never among them.
        var unlinkedAtStart = await db.MediaInspections
            .Where(reading => !db.PhysicalFiles.Any(file => file.MediaInspectionId == reading.Id))
            .Select(reading => reading.Id)
            .ToListAsync(cancellationToken);
        // MAM-19 — the hashes stored before this run: only those are its to forget (step 6).
        var hashesAtStart = await db.FileHashes.Select(hash => hash.Id).ToListAsync(cancellationToken);
        var files = await LoadFilesAsync(cancellationToken);
        _linkedAtStart = files
            .Where(file => file.ReadingId is not null)
            .ToDictionary(file => file.Id, file => file.ReadingId!.Value);
        var progress = new Progress(runId);
        await db.FileInspectionRuns
            .Where(run => run.Id == runId && run.Status == FileInspectionRunStatus.Running)
            .ExecuteUpdateAsync(setters => setters.SetProperty(run => run.FilesTotal, files.Count), cancellationToken);

        // 1. A name the mappings give no app path can't be looked at; the mapper says why (ADR 0003).
        foreach (var file in files.Where(file => !file.Path.HasAppPath))
            await WriteAsync(progress, [file.Id], Outcome.Problem(FileInspectionStatus.NoAppPath, file.Path.Reason), cancellationToken);

        // 2. Each distinct app path is looked at once, whatever number of names lead to it.
        var paths = files
            .Where(file => file.Path.HasAppPath)
            .GroupBy(file => file.Path.AppPath!, StringComparer.Ordinal)
            .Select(group => new PathToInspect(group.Key, [.. group.Select(file => file.Id)]))
            .ToList();
        var described = new FileInventoryResult<FileFacts>[paths.Count];
        await Parallel.ForEachAsync(Enumerable.Range(0, paths.Count), Concurrently(cancellationToken), async (index, token) =>
            described[index] = await inventory.DescribeAsync(paths[index].AppPath, roots, token));

        // 3. The cache answers what it holds; the rest is grouped by key, so two names of one inode
        // (hardlinks) are read once.
        var cache = await LoadCacheAsync(cancellationToken);
        var toRead = new Dictionary<CacheKey, List<DescribedPath>>();
        for (var index = 0; index < paths.Count; index++)
        {
            var path = paths[index];
            switch (described[index])
            {
                case { Status: FileInventoryStatus.Found, Value: { DeviceId: { } deviceId, Inode: { } inode } facts }:
                    var key = new CacheKey(deviceId, inode, facts.SizeBytes, facts.ModifiedUtc);
                    if (cache.TryGetValue(key, out var reading))
                        await WriteAsync(progress, path.FileIds, Outcome.Read(reading.Id, reading.Error, facts), cancellationToken);
                    else if (toRead.TryGetValue(key, out var names))
                        names.Add(new DescribedPath(path, facts));
                    else
                        toRead[key] = [new DescribedPath(path, facts)];
                    break;
                case { Status: FileInventoryStatus.Found, Value: { } facts }:
                    // An unknown identity beats a guessed one: nothing is cached under a key that can't
                    // tell two files apart.
                    await WriteAsync(progress, path.FileIds, Outcome.Problem(FileInspectionStatus.IdentityUnknown, IdentityUnknownError, facts), cancellationToken);
                    break;
                case var result:
                    await WriteAsync(progress, path.FileIds, Outcome.NotRead(result), cancellationToken);
                    break;
            }
        }

        // 4. Read what the cache doesn't hold, once per key.
        await Parallel.ForEachAsync(toRead, Concurrently(cancellationToken), async (group, token) =>
            await ReadAsync(progress, roots, group.Key, group.Value, token));

        // 5. Forget the readings no name points at that were this run's to forget (ADR 0009): those no
        // name pointed at when it started (the bytes of a name a sync dropped), and the old key of a
        // changed file, which this run moved its names away from. Only a run links a name to a reading,
        // one run at a time, and this one has finished linking, so nothing in use goes; a name the run
        // didn't find keeps its link (WriteAsync), so a mount that was away costs no reading. A reading a
        // sync orphaned while the run went waits for the next run, which that sync queued: its new names
        // haven't been looked at yet, and a moved file finds its reading there.
        var forgettable = unlinkedAtStart.Concat(_replacedReadings).ToList();
        await db.MediaInspections
            .Where(reading => forgettable.Contains(reading.Id) && !db.PhysicalFiles.Any(file => file.MediaInspectionId == reading.Id))
            .ExecuteDeleteAsync(cancellationToken);

        // 6. MAM-19, ADR 0022 — hash the files a byte-identical copy could hide among, once every name carries
        // what this run's look read, and forget the hashes of bytes neither a name nor a reading stands for now.
        await HashAsync(progress, roots, files, hashesAtStart, cancellationToken);

        var finishedUtc = time.GetUtcNow().UtcDateTime;
        var (hashCandidates, hashChecked, hashRead, hashProblems, hashProblem) =
            (progress.HashCandidates, progress.HashChecked, progress.HashRead, progress.HashProblems, progress.HashProblem);
        // Never cancelled, the LibrarySyncRunUpdates rule: once the outcome is decided it is written.
        var succeeded = await db.FileInspectionRuns
            .Where(run => run.Id == runId && run.Status == FileInspectionRunStatus.Running)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(run => run.Status, FileInspectionRunStatus.Succeeded)
                    .SetProperty(run => run.FinishedUtc, finishedUtc)
                    .SetProperty(run => run.FilesChecked, progress.Checked)
                    .SetProperty(run => run.FilesProbed, progress.Probed)
                    .SetProperty(run => run.HashCandidates, hashCandidates)
                    .SetProperty(run => run.HashCandidatesChecked, hashChecked)
                    .SetProperty(run => run.HashCandidatesRead, hashRead)
                    .SetProperty(run => run.HashProblemCount, hashProblems)
                    .SetProperty(run => run.HashProblem, hashProblem),
                CancellationToken.None) == 1;
        // Information: counts only — never a path or a title.
        logger.LogInformation(
            "File inspection {RunId} finished: {Checked} of {Total} files checked, {Probed} read by the media inspector; {HashCandidates} files could be copies, {HashRead} read whole for their hash, {HashProblems} not hashed.",
            runId, progress.Checked, files.Count, progress.Probed, hashCandidates, hashRead, hashProblems);

        // 7. MAM-34 — the owner's TMDb links, with the identities this run read.
        if (succeeded)
            await ReattachLinksAsync(runId, cancellationToken);
    }

    // ── reading what the cache doesn't hold ─────────────────────────────────────────────────────

    private async Task ReadAsync(
        Progress progress, IReadOnlyList<string> roots, CacheKey key, List<DescribedPath> names, CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _inspectorUnavailable) is { } unavailable)
        {
            foreach (var name in names)
                await WriteAsync(progress, name.Path.FileIds, Outcome.Problem(FileInspectionStatus.InspectorUnavailable, unavailable, name.Facts), cancellationToken);
            return;
        }

        // One name is read for the key; the others are names of the same bytes.
        var first = names[0];
        var read = await inventory.InspectMediaAsync(first.Path.AppPath, roots, cancellationToken);
        switch (read.Status)
        {
            case FileInventoryStatus.Found when read.Value is { File: { DeviceId: { } deviceId, Inode: { } inode } file } inspection:
                progress.AddProbed();
                var readKey = new CacheKey(deviceId, inode, file.SizeBytes, file.ModifiedUtc);
                var readingId = await StoreAsync(readKey, inspection.Media, error: null, cancellationToken);
                if (readKey == key)
                {
                    foreach (var name in names)
                        await WriteAsync(progress, name.Path.FileIds, Outcome.Read(readingId, null, name.Facts), cancellationToken);
                }
                else
                {
                    // The file changed between the look and the read, and stayed put during the read: the
                    // reading is the new bytes', and only the name read leads to them for sure. The other
                    // names keep what they showed until the next inspection looks again.
                    await WriteAsync(progress, first.Path.FileIds, Outcome.Read(readingId, null, file), cancellationToken);
                    await SkipAsync(progress, names.Skip(1).Sum(name => name.Path.FileIds.Count), cancellationToken);
                }

                break;
            case FileInventoryStatus.MediaUnreadable:
                progress.AddProbed();
                // Cached like a success under the key that was looked at, so a damaged file isn't read
                // again on every inspection — only once it changes.
                var error = read.Error ?? MediaUnreadableError;
                var failedId = await StoreAsync(key, media: null, error, cancellationToken);
                foreach (var name in names)
                    await WriteAsync(progress, name.Path.FileIds, Outcome.Read(failedId, error, name.Facts), cancellationToken);
                break;
            case FileInventoryStatus.InspectorUnavailable:
                // Never cached: it is the app's host that lacks the inspector, not the file that is wrong.
                var reason = read.Error ?? InspectorUnavailableError;
                Interlocked.CompareExchange(ref _inspectorUnavailable, reason, null);
                foreach (var name in names)
                    await WriteAsync(progress, name.Path.FileIds, Outcome.Problem(FileInspectionStatus.InspectorUnavailable, reason, name.Facts), cancellationToken);
                break;
            case FileInventoryStatus.ChangedWhileReading:
                // Not cached: the reading belongs to neither version. Shown as unreadable for now, so the
                // page never offers the facts of bytes that are gone.
                await WriteAsync(progress, first.Path.FileIds, Outcome.Problem(FileInspectionStatus.MediaUnreadable, read.Error ?? ChangedWhileReadingError, first.Facts), cancellationToken);
                await SkipAsync(progress, names.Skip(1).Sum(name => name.Path.FileIds.Count), cancellationToken);
                break;
            default:
                // Gone, moved out of the roots or locked since the look a moment ago.
                await WriteAsync(progress, first.Path.FileIds, Outcome.NotRead(read), cancellationToken);
                await SkipAsync(progress, names.Skip(1).Sum(name => name.Path.FileIds.Count), cancellationToken);
                break;
        }
    }

    private static ParallelOptions Concurrently(CancellationToken cancellationToken) =>
        new() { MaxDegreeOfParallelism = MaximumConcurrentReads, CancellationToken = cancellationToken };

    // ── what the run reads before it starts ─────────────────────────────────────────────────────

    // Every file name with where the app finds it, through its integration's mappings as they are now
    // (ADR 0003); a name no integration reported maps as it is. In path order, so a run's writes come in
    // the order the page lists files.
    private async Task<List<FileToInspect>> LoadFilesAsync(CancellationToken cancellationToken)
    {
        var files = await db.PhysicalFiles
            .AsNoTracking()
            .Select(file => new { file.Id, file.ServicePath, file.SourceConnectionId, file.MediaInspectionId })
            .ToListAsync(cancellationToken);
        var pathMappings = (await db.PathMappings.AsNoTracking().ToListAsync(cancellationToken))
            .GroupBy(mapping => mapping.IntegrationConnectionId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<PathMappingPair>)[.. group
                    .OrderBy(mapping => mapping.Position)
                    .Select(mapping => new PathMappingPair(mapping.ServicePrefix, mapping.AppPrefix))]);
        return
        [
            .. files
                .OrderBy(file => file.ServicePath, StringComparer.Ordinal)
                .ThenBy(file => file.Id)
                .Select(file => new FileToInspect(
                    file.Id,
                    PathMapper.Map(
                        file.ServicePath,
                        file.SourceConnectionId is { } connectionId ? pathMappings.GetValueOrDefault(connectionId) ?? [] : []),
                    file.MediaInspectionId)),
        ];
    }

    private async Task<Dictionary<CacheKey, CachedReading>> LoadCacheAsync(CancellationToken cancellationToken) =>
        (await db.MediaInspections
            .AsNoTracking()
            .Select(reading => new { reading.Id, reading.DeviceId, reading.Inode, reading.SizeBytes, reading.ModifiedUtc, reading.Error })
            .ToListAsync(cancellationToken))
        // DateTime equality is by ticks, whatever the Kind SQLite reads back, so a stored key matches the
        // file system's UTC instant exactly.
        .ToDictionary(
            reading => new CacheKey(reading.DeviceId, reading.Inode, reading.SizeBytes, reading.ModifiedUtc),
            reading => new CachedReading(reading.Id, reading.Error));
}
