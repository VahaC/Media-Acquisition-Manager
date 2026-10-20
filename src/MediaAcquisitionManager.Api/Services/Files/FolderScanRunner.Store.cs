using MediaAcquisitionManager.Core.Abstractions.Files;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Library;
using MediaAcquisitionManager.Core.Paths;
using MediaAcquisitionManager.Core.Texts;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Services.Files;

// What a scan stores once its walk is over — one name per file no library reported, in place of the names
// the scans stored before — and the small records a walk carries.
public sealed partial class FolderScanRunner
{
    /// <summary>
    /// Replaces the names the scans stored with what <paramref name="walk"/> found, and marks the run
    /// succeeded, in one transaction; how many of the files no library reported, or <c>null</c> when the
    /// run was no longer running, and nothing was stored.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ADR 0021 — a file no library reported keeps the row one of its names already has, so its id, when it
    /// was first seen and what the inspection read from it stay across scans; else its first name in walk
    /// order is stored. A stored name the walk reached under a library's path, or as another name of a file
    /// it found, goes.
    /// </para>
    /// <para>
    /// Any other stored name is kept only where the walk didn't look: outside every root this scan walked
    /// (the owner may have narrowed them by mistake, and the inspection shows such a name outside), or at
    /// or under a place it couldn't list now (a mount that is away costs no names). Everywhere else the
    /// walk looked and found nothing there, so the name goes — a place it refused included, as nothing
    /// under a link leading out or a loop is a name of the media folders.
    /// </para>
    /// </remarks>
    private async Task<int?> StoreAsync(Guid runId, IReadOnlyList<string> roots, Walk walk, CancellationToken cancellationToken)
    {
        // One transaction for the whole replacement and the run's outcome, the snapshot store's rule (ADR
        // 0008): anything that fails below, a shutdown included, leaves it uncommitted, and disposing it
        // rolls every row back — the names stored before the scan stay exactly as they were.
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            // Everything read first, inside the transaction and after the walk: a sync that finished while
            // the folders were walked counts.
            var libraryPaths = await LibraryAppPaths.LoadAsync(db, cancellationToken);
            var stored = await db.PhysicalFiles
                .AsTracking()
                .Where(file => file.SourceKind == ProvenanceSourceKind.FileInspection && file.MediaVersionId == null)
                // The oldest first, so a name stored twice keeps its older row.
                .OrderBy(file => file.CreatedUtc)
                .ThenBy(file => file.Id)
                .ToListAsync(cancellationToken);
            var storedByName = stored
                .Select(row => (Row: row, Name: PathOf(row.ServicePath)))
                .Where(row => row.Name is not null)
                .GroupBy(row => row.Name!)
                .ToDictionary(group => group.Key, group => group.Select(row => row.Row).ToList());

            var now = time.GetUtcNow().UtcDateTime;
            var kept = new HashSet<Guid>();
            var reportedNames = new HashSet<NormalizedPath>();
            var unreportedNames = new HashSet<NormalizedPath>();
            var unreported = 0;
            foreach (var file in walk.Files)
            {
                if (file.Names.Any(libraryPaths.Contains))
                {
                    reportedNames.UnionWith(file.Names);
                    continue;
                }

                unreported++;
                unreportedNames.UnionWith(file.Names);
                if (file.Names.SelectMany(name => storedByName.GetValueOrDefault(name) ?? []).FirstOrDefault() is { } row)
                {
                    // Only when its source last reported it moves; the identity and the inspection's columns
                    // stay for the inspection, which reads again only what changed.
                    row.ObservedUtc = now;
                    kept.Add(row.Id);
                    continue;
                }

                // The app is the reporting source: no connection, no item, no library, no size as reported,
                // and no version — which work it is, if any, is not the app's to guess (ADR 0017). The path
                // is the app's own, which no mapping applies to (ADR 0006).
                var appPath = file.Names[0].ToString();
                db.PhysicalFiles.Add(new PhysicalFileEntity
                {
                    SourceKind = ProvenanceSourceKind.FileInspection,
                    ServicePath = appPath,
                    NormalizedServicePath = LibrarySearchText.Normalize(appPath),
                    CreatedUtc = now,
                    ObservedUtc = now,
                });
            }

            // The roots this walk used, not the ones now: a name under a root added meanwhile was never looked
            // for.
            var walkedRoots = roots.Select(PathOf).OfType<NormalizedPath>().ToList();
            var unlisted = walk.Problems
                .Where(problem => problem.Status == FileListingStatus.Unlisted)
                .Select(problem => PathOf(problem.AppPath))
                .OfType<NormalizedPath>()
                .ToList();
            foreach (var row in stored.Where(row => !kept.Contains(row.Id)))
            {
                var name = PathOf(row.ServicePath);
                var forget = name is not null
                    && (libraryPaths.Contains(name)
                        || reportedNames.Contains(name)
                        || unreportedNames.Contains(name)
                        || (walkedRoots.Any(name.IsUnder) && !unlisted.Any(name.IsUnder)));
                if (forget)
                    db.PhysicalFiles.Remove(row);
            }

            await db.SaveChangesAsync(cancellationToken);

            var finishedUtc = time.GetUtcNow().UtcDateTime;
            var problem = walk.Problems.Count == 0
                ? null
                : walk.Problems[0].Error ?? UnscannedPlaceError;
            // Never cancelled, the LibrarySyncRunUpdates rule: once the outcome is decided it is written.
            var succeeded = await db.FolderScanRuns
                .Where(run => run.Id == runId && run.Status == FolderScanStatus.Running)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(run => run.Status, FolderScanStatus.Succeeded)
                        .SetProperty(run => run.FinishedUtc, finishedUtc)
                        .SetProperty(run => run.FilesFound, walk.Files.Count)
                        .SetProperty(run => run.FilesUnreported, unreported)
                        .SetProperty(run => run.ProblemCount, walk.Problems.Count)
                        .SetProperty(run => run.Problem, problem),
                    CancellationToken.None) == 1;
            if (!succeeded)
                return null;

            // Not cancelled either: the folders are walked and the names written, and a shutdown arriving
            // now would throw that away at the last step.
            await transaction.CommitAsync(CancellationToken.None);
            return unreported;
        }
        finally
        {
            // Nothing tracked here outlives the call: after a rollback the tracker would hold rows the
            // database doesn't, and the next use of this context would trust them.
            db.ChangeTracker.Clear();
        }
    }

    // A stored or listed path in the form paths are compared in; null for one that isn't a full path, which
    // no walk reaches and no root covers.
    private static NormalizedPath? PathOf(string path) => NormalizedPath.TryParse(path, out var parsed, out _) ? parsed : null;

    // ── what a scan carries ─────────────────────────────────────────────────────────────────────

    /// <summary>What a walk found, in walk order: each file once, the places it didn't go into or through,
    /// and why it stopped short, when it did, as a message the Api renders (ADR 0042).</summary>
    private sealed record Walk(List<ScannedFile> Files, List<FileListingEntry> Problems, Message? LimitReached);

    /// <summary>One file the walk reached, with every name it was reached by, in walk order.</summary>
    private sealed record ScannedFile(List<NormalizedPath> Names);
}
