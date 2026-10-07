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
/// rows over the ones already stored; MAM-34 — <c>LibrarySnapshotStore.Links.cs</c> re-applies the owner's TMDb
/// links of the library's items.
/// </remarks>
public sealed partial class LibrarySnapshotStore(ApplicationDbContext db, TimeProvider time, ILogger<LibrarySnapshotStore> logger)
    : ILibrarySnapshotStore
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
            var links = await LoadLinksAsync(runId, integrationId, now, cancellationToken);

            // ADR 0017 — a missing episode is known to the library but holds no file: it was read, so the
            // pages of its list added up, and it is stored as nothing. Each media source goes to one item
            // before the items are grouped, so merged versions that several items all list are stored
            // once (ADR 0008).
            var resolved = LibraryVersionOwnership.Resolve([.. items.Where(item => !item.IsMissing)]);

            // MAM-34 — the owner's links first: a link whose item came back under a new id finds it by its files,
            // each link learns from this read whether the library holds it, and the items are grouped by the ids
            // they count as holding with their links applied (copies: only what the library reported is stored).
            links.Reattach(resolved, existing.Files);
            links.JudgeWrites(resolved);
            var effective = links.Effective(resolved);

            // MAM-32 — each kind is grouped on its own, so a movie and a series sharing a TMDb number are
            // never one work (ADR 0017); series first, as an episode is known by its series' ids with its
            // numbers too, and lands in the series work its series item landed on.
            var seriesItems = effective
                .Where(item => item.Kind == MediaWorkKind.Series)
                .ToDictionary(item => item.SourceItemId, StringComparer.Ordinal);
            var seriesGroups = GroupsOf(effective, MediaWorkKind.Series, _ => []);
            var movieGroups = GroupsOf(effective, MediaWorkKind.Movie, links.ShowEpisodeKeys);
            var episodeGroups = GroupsOf(
                effective, MediaWorkKind.Episode, item => SeriesPositionKeys(item, seriesItems).Concat(links.ShowEpisodeKeys(item)));

            var previous = await LoadPreviousWorksAsync(integrationId, existing, cancellationToken);
            var seriesWorkIds = AssignWorks(seriesGroups, MediaWorkKind.Series, existing, previous, _ => [], now);
            var seriesWorkOfItem = seriesGroups
                .SelectMany((group, index) => group.Items.Select(item => (item.SourceItemId, WorkId: seriesWorkIds[index])))
                .ToDictionary(item => item.SourceItemId, item => item.WorkId, StringComparer.Ordinal);
            var movieWorkIds = AssignWorks(movieGroups, MediaWorkKind.Movie, existing, previous, _ => [], now);
            var episodeWorkIds = AssignWorks(
                episodeGroups, MediaWorkKind.Episode, existing, previous, item => PositionKeys(item, seriesWorkOfItem), now);

            var upserts = new SnapshotUpserts(db, existing, integrationId, sourceName, now);
            upserts.UpsertLocations(locations);
            // MAM-34 — the work of every item, of every kind: where each item's link now points.
            var workOfItem = new Dictionary<string, Guid>(StringComparer.Ordinal);
            foreach (var (groups, workIds) in new[] { (seriesGroups, seriesWorkIds), (movieGroups, movieWorkIds), (episodeGroups, episodeWorkIds) })
            {
                for (var index = 0; index < groups.Count; index++)
                {
                    foreach (var item in groups[index].Items)
                    {
                        upserts.UpsertItem(links.Original(item), workIds[index], SeriesWorkOf(item, seriesWorkOfItem));
                        workOfItem.TryAdd(item.SourceItemId, workIds[index]);
                    }
                }
            }

            links.Attach(workOfItem);
            var identitiesAdded = links.RefreshIdentities(resolved, existing.Files);
            upserts.RemoveUntouched();
            await db.SaveChangesAsync(cancellationToken);

            // Every work this connection's facts sat on, and every work made just now: the ones left with
            // nothing to say go.
            await DeleteEmptyWorksAsync([.. existing.WorkIds, .. previous.Created], cancellationToken);

            var worksCount = movieGroups.Count + episodeGroups.Count;
            if (!await MarkSucceededAsync(runId, items.Count, worksCount, seriesGroups.Count, upserts.VersionsCount, upserts.FilesCount))
                return LibrarySnapshotResult.RunNoLongerRunning;

            // Not cancelled either: the whole library is read and written, and a shutdown arriving now
            // would throw that away at the last step, leaving the run for the reconciler to blame on a
            // restart.
            await transaction.CommitAsync(CancellationToken.None);
            // Information: counts only — never a title or an id; and only for a library with links, so a library
            // without any logs nothing new.
            if (links.Count > 0)
            {
                logger.LogInformation(
                    "Library sync {RunId} re-applied {Links} TMDb links: {Reattached} re-attached by their files, {Ambiguous} left unattached as their files lead to no single unlinked item, {Identities} file identities added.",
                    runId, links.Count, links.ReattachedCount, links.AmbiguousCount, identitiesAdded);
            }

            return LibrarySnapshotResult.Applied(worksCount, seriesGroups.Count, upserts.VersionsCount, upserts.FilesCount);
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
        // source reported them (what the app read from a file outlives its name, ADR 0009);
        // files reported under another source's version are removed explicitly; removing the libraries
        // sets any remaining file's library null and takes their root folders with them, by the database's
        // cascade too (MAM-11).
        await db.MediaVersions.Where(ReportedBy<MediaVersionEntity>(integrationId)).ExecuteDeleteAsync(cancellationToken);
        await db.PhysicalFiles.Where(ReportedBy<PhysicalFileEntity>(integrationId)).ExecuteDeleteAsync(cancellationToken);
        await db.MediaWorkTitles.Where(ReportedBy<MediaWorkTitleEntity>(integrationId)).ExecuteDeleteAsync(cancellationToken);
        await db.MediaWorkIdentifiers.Where(ReportedBy<MediaWorkIdentifierEntity>(integrationId)).ExecuteDeleteAsync(cancellationToken);
        await db.MediaEpisodePositions.Where(ReportedBy<MediaEpisodePositionEntity>(integrationId)).ExecuteDeleteAsync(cancellationToken);
        // MAM-35 — its posters too: a work another source keeps would otherwise show a picture of a library that is gone.
        await db.MediaWorkPosters.Where(ReportedBy<MediaWorkPosterEntity>(integrationId)).ExecuteDeleteAsync(cancellationToken);
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

    /// <summary>
    /// The root folders of the snapshot's libraries (MAM-11): part of their library, so found through it
    /// rather than by a provenance of their own.
    /// </summary>
    private IQueryable<LibraryLocationFolderEntity> FoldersReportedBy(Guid integrationId)
    {
        var locations = db.LibraryLocations.Where(ReportedBy<LibraryLocationEntity>(integrationId));
        return db.LibraryLocationFolders.Where(folder => locations.Any(location => location.Id == folder.LibraryLocationId));
    }

    // MAM-35 — not the posters: a poster row is only ever written beside its item's other facts, so it adds no work.
    private IQueryable<Guid> WorksReportedBy(Guid integrationId) =>
        db.MediaWorkTitles.Where(ReportedBy<MediaWorkTitleEntity>(integrationId)).Select(title => title.MediaWorkId)
            .Union(db.MediaWorkIdentifiers.Where(ReportedBy<MediaWorkIdentifierEntity>(integrationId)).Select(identifier => identifier.MediaWorkId))
            .Union(db.MediaVersions.Where(ReportedBy<MediaVersionEntity>(integrationId)).Select(version => version.MediaWorkId))
            .Union(db.MediaEpisodePositions.Where(ReportedBy<MediaEpisodePositionEntity>(integrationId)).Select(position => position.MediaWorkId));

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
            await FoldersReportedBy(integrationId).AsTracking().ToListAsync(cancellationToken),
            await Ordered(db.MediaWorkTitles.AsTracking().Where(ReportedBy<MediaWorkTitleEntity>(integrationId))).ToListAsync(cancellationToken),
            await Ordered(db.MediaWorkIdentifiers.AsTracking().Where(ReportedBy<MediaWorkIdentifierEntity>(integrationId))).ToListAsync(cancellationToken),
            await Ordered(db.MediaVersions.AsTracking().Where(ReportedBy<MediaVersionEntity>(integrationId))).ToListAsync(cancellationToken),
            await Ordered(db.PhysicalFiles.AsTracking().Where(ReportedBy<PhysicalFileEntity>(integrationId))).ToListAsync(cancellationToken),
            await Ordered(db.MediaEpisodePositions.AsTracking().Where(ReportedBy<MediaEpisodePositionEntity>(integrationId))).ToListAsync(cancellationToken),
            await Ordered(db.MediaWorkPosters.AsTracking().Where(ReportedBy<MediaWorkPosterEntity>(integrationId))).ToListAsync(cancellationToken));

    private static IQueryable<TFact> Ordered<TFact>(IQueryable<TFact> facts) where TFact : ProvenancedEntity =>
        facts.OrderBy(fact => fact.CreatedUtc).ThenBy(fact => fact.Id);

    /// <summary>Deletes those of <paramref name="workIds"/> that no longer hold a title, an identifier, a
    /// version or an episode position of any source. A series that goes leaves its episodes' positions
    /// pointing at no series (the database's SetNull, MAM-32).</summary>
    /// <remarks>MAM-35 — a poster doesn't count: a picture alone says nothing of a work, and goes with it by cascade.</remarks>
    private Task<int> DeleteEmptyWorksAsync(List<Guid> workIds, CancellationToken cancellationToken) =>
        db.MediaWorks
            // One JSON parameter (EF.Parameter) rather than EF Core 10's default of one parameter per id:
            // a large library's works would run into SQLite's limit on the number of parameters.
            .Where(work => EF.Parameter(workIds).Contains(work.Id)
                && !db.MediaWorkTitles.Any(title => title.MediaWorkId == work.Id)
                && !db.MediaWorkIdentifiers.Any(identifier => identifier.MediaWorkId == work.Id)
                && !db.MediaVersions.Any(version => version.MediaWorkId == work.Id)
                && !db.MediaEpisodePositions.Any(position => position.MediaWorkId == work.Id))
            .ExecuteDeleteAsync(cancellationToken);

    /// <summary>
    /// Marks the run succeeded with its counts, only while it is still running (the conditional write of
    /// <see cref="LibrarySyncRunUpdates"/>, never cancelled for the same reason); <c>false</c> when
    /// something else finished it first.
    /// </summary>
    private async Task<bool> MarkSucceededAsync(Guid runId, int itemsCount, int worksCount, int seriesCount, int versionsCount, int filesCount)
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
                    .SetProperty(run => run.SeriesCount, seriesCount)
                    .SetProperty(run => run.VersionsCount, versionsCount)
                    .SetProperty(run => run.FilesCount, filesCount)
                    .SetProperty(run => run.Error, (string?)null),
                CancellationToken.None) == 1;
    }

    // MAM-35 — not the poster tag: an item with nothing but a picture still makes no work.
    private static bool HasFacts(LibraryItem item) =>
        item.Titles.Count > 0
        || item.Identifiers.Count > 0
        || item.Versions.Count > 0
        || item.Episode is { SeriesSourceItemId: not null } or { SeasonNumber: not null } or { EpisodeNumber: not null };

    /// <summary>
    /// The works the items of <paramref name="kind"/> make (<see cref="LibraryWorkGrouping"/>), with
    /// <paramref name="moreKeys"/> as further identifiers. A group none of whose items says anything — no
    /// title, identifier, version or position — gets no work: an empty work would only be deleted again.
    /// </summary>
    private static List<LibraryWorkGroup> GroupsOf(
        IReadOnlyList<LibraryItem> items, MediaWorkKind kind, Func<LibraryItem, IEnumerable<(string Key, string Value)>> moreKeys) =>
        [.. LibraryWorkGrouping.Group([.. items.Where(item => item.Kind == kind)], moreKeys).Where(group => group.Items.Any(HasFacts))];

    /// <summary>
    /// MAM-32 — an episode's position as identifiers for the grouping (ADR 0017): each registry id of its
    /// series item with its season and episode numbers, under a key of the id's scheme. None when it has
    /// no series item, no numbers, or a series item the library didn't list: the library's own series id
    /// is never one of them, as one show can be several series items.
    /// </summary>
    private static IEnumerable<(string Key, string Value)> SeriesPositionKeys(LibraryItem item, Dictionary<string, LibraryItem> seriesItems) =>
        item.Episode is { SeriesSourceItemId: { } seriesItemId, SeasonNumber: { } season, EpisodeNumber: { } episode }
        && seriesItems.TryGetValue(seriesItemId, out var series)
            ? series.Identifiers.Select(identifier => ($"Series{identifier.Scheme}", $"{identifier.Value.Trim()}/{season}/{episode}"))
            : [];

    /// <summary>MAM-32 — an episode's position as a key for the work assignment: the series work its
    /// series item landed on, with its numbers.</summary>
    private static IEnumerable<(string Scheme, string Value)> PositionKeys(LibraryItem item, Dictionary<string, Guid> seriesWorkOfItem) =>
        item.Episode is { SeasonNumber: { } season, EpisodeNumber: { } episode } && SeriesWorkOf(item, seriesWorkOfItem) is { } seriesWorkId
            ? [(PositionKey, PositionValue(seriesWorkId, season, episode))]
            : [];

    /// <summary>The series work the episode's series item landed on; <c>null</c> for anything else.</summary>
    private static Guid? SeriesWorkOf(LibraryItem item, Dictionary<string, Guid> seriesWorkOfItem) =>
        item.Episode is { SeriesSourceItemId: { } seriesItemId } && seriesWorkOfItem.TryGetValue(seriesItemId, out var workId) ? workId : null;

    /// <summary>The rows of one integration's snapshot as they were before the sync.</summary>
    private sealed record ExistingSnapshot(
        List<LibraryLocationEntity> Locations,
        List<LibraryLocationFolderEntity> Folders,
        List<MediaWorkTitleEntity> Titles,
        List<MediaWorkIdentifierEntity> Identifiers,
        List<MediaVersionEntity> Versions,
        List<PhysicalFileEntity> Files,
        List<MediaEpisodePositionEntity> Positions,
        List<MediaWorkPosterEntity> Posters)
    {
        /// <summary>Every work one of the snapshot's titles, identifiers, versions or episode positions sat
        /// on. MAM-35 — not the posters': each sits on a work its item's other facts sit on.</summary>
        public HashSet<Guid> WorkIds { get; } =
        [
            .. Titles.Select(title => title.MediaWorkId),
            .. Identifiers.Select(identifier => identifier.MediaWorkId),
            .. Versions.Select(version => version.MediaWorkId),
            .. Positions.Select(position => position.MediaWorkId),
        ];
    }
}
