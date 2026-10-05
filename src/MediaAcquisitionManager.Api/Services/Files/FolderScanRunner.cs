using MediaAcquisitionManager.Core.Abstractions.Files;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Paths;
using MediaAcquisitionManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Services.Files;

/// <inheritdoc cref="IFolderScanRunner"/>
/// <remarks>
/// <para>
/// MAM-18, ADR 0021 — one walk of every allowed folder, then one transaction. A file the walk reached is
/// reported when any name it was reached by is where the app finds a name a library reported
/// (<see cref="LibraryAppPaths"/>); a file no library reported keeps one stored name, with no version; and
/// a stored name the walk looked for and didn't find goes. The scan only finds names — nothing is opened
/// for reading — and then queues the usual file inspection, which reads them through MAM-9's cache, as a
/// sync does.
/// </para>
/// <para>
/// Split by facet: this file claims the run and walks; <c>FolderScanRunner.Store.cs</c> replaces the names
/// the scans stored with what this walk found.
/// </para>
/// </remarks>
public sealed partial class FolderScanRunner(
    ApplicationDbContext db,
    IFileInventory inventory,
    IFileInspectionService inspections,
    TimeProvider time,
    ILogger<FolderScanRunner> logger) : IFolderScanRunner
{
    /// <summary>
    /// How many new files the walk reaches between two writes of its progress: the page polls every two
    /// seconds, and a write per file would put a database write behind every name of a large disk, while
    /// one per fifty still moves the count on every poll.
    /// </summary>
    internal const int ProgressInterval = 50;

    internal const string NoAllowedRootsError =
        "No media folders are allowed any more, so there was nothing to scan. Add one under Media folders in the user menu.";

    internal const string LimitReachedError = "The scan reached its limit before it had walked every media folder, so it stopped.";

    internal const string UnscannedPlaceError = "The app didn't scan a place in the media folders.";

    public async Task RunAsync(Guid runId, CancellationToken cancellationToken = default)
    {
        // One worker runs one scan at a time, so a running scan other than this one is dead: its worker
        // couldn't mark it failed, the database refusing that too (a full disk). Left running, it would
        // hold IX_FolderScanRuns_OneRunPerActiveStatus's one running slot, and every claim from here to
        // the next start would break the index and fail as an app error.
        await db.FolderScanRuns
            .Where(run => run.Id != runId && run.Status == FolderScanStatus.Running)
            .MarkFailedAsync(FolderScanWorker.AppErrorMessage, time.GetUtcNow().UtcDateTime);

        // The atomic claim: only a queued run turns running, and only once, whoever else holds its id (the
        // id queued twice, the reconciler queueing it again). Nothing else is done for a run not claimed.
        var startedUtc = time.GetUtcNow().UtcDateTime;
        var claimed = await db.FolderScanRuns
            .Where(run => run.Id == runId && run.Status == FolderScanStatus.Queued)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(run => run.Status, FolderScanStatus.Running)
                    .SetProperty(run => run.StartedUtc, startedUtc),
                cancellationToken) == 1;
        if (!claimed)
            return;

        // Read once, here: a scan walks the roots as they were when it started, and what it keeps or
        // forgets is decided against those same roots (FolderScanRunner.Store.cs).
        var roots = await db.AllowedRoots
            .AsNoTracking()
            .OrderBy(root => root.Position)
            .Select(root => root.Path)
            .ToListAsync(cancellationToken);
        if (roots.Count == 0)
        {
            await db.FolderScanRuns
                .Where(run => run.Id == runId && run.Status == FolderScanStatus.Running)
                .MarkFailedAsync(NoAllowedRootsError, time.GetUtcNow().UtcDateTime);
            logger.LogWarning("Folder scan {RunId} failed: no media folder is allowed any more.", runId);
            return;
        }

        var walk = await WalkAsync(runId, roots, cancellationToken);
        if (walk.LimitReached is { } limit)
        {
            // An incomplete walk must not replace anything: every name it didn't reach would be forgotten.
            // Nothing is stored, so nothing new waits for an inspection either.
            await db.FolderScanRuns
                .Where(run => run.Id == runId && run.Status == FolderScanStatus.Running)
                .MarkFailedAsync(limit, time.GetUtcNow().UtcDateTime);
            logger.LogWarning("Folder scan {RunId} failed: the walk reached its limit after {Found} files; nothing was stored.", runId, walk.Files.Count);
            return;
        }

        await ReportAsync(runId, walk.Files.Count, cancellationToken);
        if (await StoreAsync(runId, roots, walk, cancellationToken) is not { } unreported)
        {
            // Something else finished the run first; its outcome stands and nothing was stored.
            logger.LogWarning("Folder scan {RunId} was finished elsewhere before what it found was stored; nothing was stored.", runId);
            return;
        }

        // Information: counts only — never a path.
        logger.LogInformation(
            "Folder scan {RunId} finished: {Found} files found, {Unreported} in no library, {Problems} places not scanned.",
            runId, walk.Files.Count, unreported, walk.Problems.Count);
        await QueueInspectionAsync(runId, cancellationToken);
    }

    // ── the walk ────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Everything the walk of <paramref name="roots"/> reached: each file once, with every name it was
    /// reached by, and the places it didn't go into or through, all in walk order. Nothing is written
    /// but the progress.
    /// </summary>
    private async Task<Walk> WalkAsync(Guid runId, IReadOnlyList<string> roots, CancellationToken cancellationToken)
    {
        var files = new List<ScannedFile>();
        // A file is where its names lead with every link followed: two names reaching it through a folder
        // link are one file, counted and stored once (ADR 0021).
        var filesByResolvedPath = new Dictionary<NormalizedPath, ScannedFile>();
        var problems = new List<FileListingEntry>();
        await foreach (var entry in inventory.ListMediaFilesAsync(roots, cancellationToken).WithCancellation(cancellationToken))
        {
            switch (entry.Status)
            {
                case FileListingStatus.MediaFile:
                    // An inventory gives canonical paths; one that doesn't parse names nothing the app could
                    // store or look at.
                    if (!NormalizedPath.TryParse(entry.AppPath, out var name, out _)
                        || !NormalizedPath.TryParse(entry.ResolvedPath ?? entry.AppPath, out var resolved, out _))
                        break;
                    if (filesByResolvedPath.TryGetValue(resolved, out var known))
                    {
                        if (!known.Names.Contains(name))
                            known.Names.Add(name);
                        break;
                    }

                    var file = new ScannedFile([name]);
                    filesByResolvedPath[resolved] = file;
                    files.Add(file);
                    if (files.Count % ProgressInterval == 0)
                        await ReportAsync(runId, files.Count, cancellationToken);
                    break;
                case FileListingStatus.Unlisted or FileListingStatus.Skipped:
                    problems.Add(entry);
                    break;
                case FileListingStatus.LimitReached:
                    // The walk ends here; what it gave is incomplete.
                    return new Walk(files, problems, entry.Error ?? LimitReachedError);
            }
        }

        return new Walk(files, problems, LimitReached: null);
    }

    // How many distinct files the walk has reached, for the page; conditional on Running, so a run finished
    // elsewhere is never written on again.
    private Task ReportAsync(Guid runId, int filesFound, CancellationToken cancellationToken) =>
        db.FolderScanRuns
            .Where(run => run.Id == runId && run.Status == FolderScanStatus.Running)
            .ExecuteUpdateAsync(setters => setters.SetProperty(run => run.FilesFound, filesFound), cancellationToken);

    /// <summary>
    /// MAM-18 — once the names are stored, the usual file inspection reads them (or the one waiting is
    /// joined), the <c>LibrarySyncRunner</c> rule. The scan has succeeded by now and stays so: a failure
    /// here is logged, and the next sync, the app's start or the owner's Inspect files queues one again.
    /// </summary>
    private async Task QueueInspectionAsync(Guid runId, CancellationToken cancellationToken)
    {
        try
        {
            await inspections.EnsureQueuedAsync(FileInspectionTrigger.FolderScanned, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Folder scan {RunId} succeeded, but no file inspection could be queued after it.", runId);
        }
    }
}
