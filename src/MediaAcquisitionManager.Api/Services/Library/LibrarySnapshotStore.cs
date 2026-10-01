using System.Linq.Expressions;
using MediaAcquisitionManager.Core.Abstractions.Library;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Library;
using MediaAcquisitionManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Services.Library;

/// <inheritdoc cref="ILibrarySnapshotStore"/>
/// <remarks>
/// Split by facet: this file replaces and purges a snapshot; <c>LibrarySnapshotStore.WorkAssignment.cs</c>
/// decides which work each group of items lands on, and <c>LibrarySnapshotStore.Upserts.cs</c> writes the
/// rows over the ones already stored.
/// </remarks>
public sealed partial class LibrarySnapshotStore(ApplicationDbContext db, TimeProvider time) : ILibrarySnapshotStore
{
    public async Task<LibrarySnapshotResult> ApplyAsync(
        Guid runId,
        Guid integrationId,
        IReadOnlyList<LibraryLocation> locations,
        IReadOnlyList<LibraryItem> items,
        CancellationToken cancellationToken = default)
    {
        // ADR 0008 — one transaction for the whole replacement and the run's outcome. Anything that
        // fails below (a cancelled shutdown included) leaves it uncommitted, and disposing it rolls every
        // row back: the snapshot from before the sync stays exactly as it was.
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            // Read again inside the transaction rather than trusted from when the sync began: the owner
            // may have removed the integration since — its snapshot was purged with it and must not come
            // back — or renamed it, and every fact carries the current name. Not tracked: nothing here
            // changes it.
            var sourceName = await db.IntegrationConnections
                .AsNoTracking()
                .Where(connection => connection.Id == integrationId)
                .Select(connection => connection.Name)
                .SingleOrDefaultAsync(cancellationToken);
            if (sourceName is null)
                return LibrarySnapshotResult.SourceRemoved;

            var now = time.GetUtcNow().UtcDateTime;
            var existing = await LoadExistingAsync(integrationId, cancellationToken);

            // Each media source goes to one item before the items are grouped, so merged versions that
            // several items all list are stored once (ADR 0008). A group none of whose items says
            // anything — no title, identifier or version — gets no work: an empty work would only be
            // deleted again below.
            var groups = LibraryWorkGrouping.Group(LibraryVersionOwnership.Resolve(items))
                .Where(group => group.Items.Any(HasFacts))
                .ToList();
            var assignment = await AssignWorksAsync(integrationId, groups, existing, now, cancellationToken);

            var upserts = new SnapshotUpserts(db, existing, integrationId, sourceName, now);
            upserts.UpsertLocations(locations);
            for (var index = 0; index < groups.Count; index++)
            {
                foreach (var item in groups[index].Items)
                    upserts.UpsertItem(item, assignment.WorkIds[index]);
            }

            upserts.RemoveUntouched();
            await db.SaveChangesAsync(cancellationToken);

            // Every work this connection's facts sat on, and every work made just now: the ones left with
            // nothing to say go.
            await DeleteEmptyWorksAsync([.. existing.WorkIds, .. assignment.CreatedWorkIds], cancellationToken);

            if (!await MarkSucceededAsync(runId, items.Count, groups.Count, upserts.VersionsCount, upserts.FilesCount))
                return LibrarySnapshotResult.RunNoLongerRunning;

            // Not cancelled either: the whole library is read and written, and a shutdown arriving now
            // would throw that away at the last step, leaving the run for the reconciler to blame on a
            // restart.
            await transaction.CommitAsync(CancellationToken.None);
            return LibrarySnapshotResult.Applied(groups.Count, upserts.VersionsCount, upserts.FilesCount);
        }
        finally
        {
            // Nothing tracked here outlives the call: after a rollback the tracker would hold rows the
            // database doesn't, and the next use of this context would trust them.
            db.ChangeTracker.Clear();
        }
    }

    public async Task PurgeAsync(Guid integrationId, CancellationToken cancellationToken = default)
    {
        // The works its facts sit on, read before the facts go: afterwards nothing says which they were.
        var workIds = await WorksReportedBy(integrationId).ToListAsync(cancellationToken);

        // Set-based deletes inside the caller's transaction. Only this library's own rows: a title,
        // identifier or version another source (or a source of another kind) reported stays, and so does
        // a work that still holds one. A version's files go with it by the database's cascade, whatever
        // source reported them (an open point for MAM-9's FileInspection rows, ILibrarySnapshotStore);
        // files reported under another source's version are removed explicitly; removing the libraries
        // sets any remaining file's library null.
        await db.MediaVersions.Where(ReportedBy<MediaVersionEntity>(integrationId)).ExecuteDeleteAsync(cancellationToken);
        await db.PhysicalFiles.Where(ReportedBy<PhysicalFileEntity>(integrationId)).ExecuteDeleteAsync(cancellationToken);
        await db.MediaWorkTitles.Where(ReportedBy<MediaWorkTitleEntity>(integrationId)).ExecuteDeleteAsync(cancellationToken);
        await db.MediaWorkIdentifiers.Where(ReportedBy<MediaWorkIdentifierEntity>(integrationId)).ExecuteDeleteAsync(cancellationToken);
        await db.LibraryLocations.Where(ReportedBy<LibraryLocationEntity>(integrationId)).ExecuteDeleteAsync(cancellationToken);
        await DeleteEmptyWorksAsync(workIds, cancellationToken);
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The rows of <paramref name="integrationId"/>'s snapshot: what it reported as a media library. The
    /// source kind too, not only the connection: a row of the same connection under another kind (none
    /// exists today) is not this snapshot's to replace or purge.
    /// </summary>
    private static Expression<Func<TFact, bool>> ReportedBy<TFact>(Guid integrationId) where TFact : ProvenancedEntity =>
        fact => fact.SourceConnectionId == integrationId && fact.SourceKind == ProvenanceSourceKind.MediaLibrary;

    private IQueryable<Guid> WorksReportedBy(Guid integrationId) =>
        db.MediaWorkTitles.Where(ReportedBy<MediaWorkTitleEntity>(integrationId)).Select(title => title.MediaWorkId)
            .Union(db.MediaWorkIdentifiers.Where(ReportedBy<MediaWorkIdentifierEntity>(integrationId)).Select(identifier => identifier.MediaWorkId))
            .Union(db.MediaVersions.Where(ReportedBy<MediaVersionEntity>(integrationId)).Select(version => version.MediaWorkId));

    /// <summary>
    /// Every row of the snapshot, one query per table. No N+1: the rows are indexed in memory
    /// (<see cref="SnapshotUpserts"/>) instead of looked up per item, which would be thousands of round
    /// trips inside the write lock for a library of 870 movies.
    /// </summary>
    /// <remarks>
    /// Tracked, as the rows found again are updated in place, keeping their ids and first-seen times.
    /// Ordered by first-seen time, then id, so "the first of two rows with one key" is the same row on
    /// every sync.
    /// </remarks>
    private async Task<ExistingSnapshot> LoadExistingAsync(Guid integrationId, CancellationToken cancellationToken) =>
        new(
            await Ordered(db.LibraryLocations.AsTracking().Where(ReportedBy<LibraryLocationEntity>(integrationId))).ToListAsync(cancellationToken),
            await Ordered(db.MediaWorkTitles.AsTracking().Where(ReportedBy<MediaWorkTitleEntity>(integrationId))).ToListAsync(cancellationToken),
            await Ordered(db.MediaWorkIdentifiers.AsTracking().Where(ReportedBy<MediaWorkIdentifierEntity>(integrationId))).ToListAsync(cancellationToken),
            await Ordered(db.MediaVersions.AsTracking().Where(ReportedBy<MediaVersionEntity>(integrationId))).ToListAsync(cancellationToken),
            await Ordered(db.PhysicalFiles.AsTracking().Where(ReportedBy<PhysicalFileEntity>(integrationId))).ToListAsync(cancellationToken));

    private static IQueryable<TFact> Ordered<TFact>(IQueryable<TFact> facts) where TFact : ProvenancedEntity =>
        facts.OrderBy(fact => fact.CreatedUtc).ThenBy(fact => fact.Id);

    /// <summary>Deletes those of <paramref name="workIds"/> that no longer hold a title, an identifier or
    /// a version of any source.</summary>
    private Task<int> DeleteEmptyWorksAsync(List<Guid> workIds, CancellationToken cancellationToken) =>
        db.MediaWorks
            // One JSON parameter (EF.Parameter) rather than EF Core 10's default of one parameter per id:
            // a large library's works would run into SQLite's limit on the number of parameters.
            .Where(work => EF.Parameter(workIds).Contains(work.Id)
                && !db.MediaWorkTitles.Any(title => title.MediaWorkId == work.Id)
                && !db.MediaWorkIdentifiers.Any(identifier => identifier.MediaWorkId == work.Id)
                && !db.MediaVersions.Any(version => version.MediaWorkId == work.Id))
            .ExecuteDeleteAsync(cancellationToken);

    /// <summary>
    /// Marks the run succeeded with its counts, only while it is still running (the conditional write of
    /// <see cref="LibrarySyncRunUpdates"/>, never cancelled for the same reason); <c>false</c> when
    /// something else finished it first.
    /// </summary>
    private async Task<bool> MarkSucceededAsync(Guid runId, int itemsCount, int worksCount, int versionsCount, int filesCount)
    {
        var finishedUtc = time.GetUtcNow().UtcDateTime;
        return await db.LibrarySyncRuns
            .Where(run => run.Id == runId && run.Status == LibrarySyncStatus.Running)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(run => run.Status, LibrarySyncStatus.Succeeded)
                    .SetProperty(run => run.FinishedUtc, finishedUtc)
                    .SetProperty(run => run.ItemsRead, itemsCount)
                    .SetProperty(run => run.ItemsTotal, itemsCount)
                    .SetProperty(run => run.WorksCount, worksCount)
                    .SetProperty(run => run.VersionsCount, versionsCount)
                    .SetProperty(run => run.FilesCount, filesCount)
                    .SetProperty(run => run.Error, (string?)null),
                CancellationToken.None) == 1;
    }

    private static bool HasFacts(LibraryItem item) => item.Titles.Count > 0 || item.Identifiers.Count > 0 || item.Versions.Count > 0;

    /// <summary>The rows of one integration's snapshot as they were before the sync.</summary>
    private sealed record ExistingSnapshot(
        List<LibraryLocationEntity> Locations,
        List<MediaWorkTitleEntity> Titles,
        List<MediaWorkIdentifierEntity> Identifiers,
        List<MediaVersionEntity> Versions,
        List<PhysicalFileEntity> Files)
    {
        /// <summary>Every work one of the snapshot's titles, identifiers or versions sat on.</summary>
        public HashSet<Guid> WorkIds { get; } =
        [
            .. Titles.Select(title => title.MediaWorkId),
            .. Identifiers.Select(identifier => identifier.MediaWorkId),
            .. Versions.Select(version => version.MediaWorkId),
        ];
    }
}
