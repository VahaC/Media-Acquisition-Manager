using MediaAcquisitionManager.Core.Abstractions.Library;
using MediaAcquisitionManager.Core.Abstractions.Media;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Library;
using MediaAcquisitionManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Services.Library;

// Writing one sync's facts over the rows already stored (ADR 0008).
public sealed partial class LibrarySnapshotStore
{
    /// <summary>
    /// Upserts one sync's facts into the snapshot by their provenance keys: a row found again is updated
    /// in place (its id and first-seen time kept), a new one is added, and every stored row no key
    /// reached is deleted by <see cref="RemoveUntouched"/> — a dropped identifier, a cleared original
    /// title, a removed media source and a moved file disappear with their item's other facts kept.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The keys, all within this connection's media library rows: a library by its own id; a title by
    /// (item, kind) and an identifier by (item, scheme), the first of each in the item winning; a version
    /// by its media source id, which is unique per file on the server — or, without one, by (item,
    /// position among the item's versions without one); a file by (version row, path), compared byte for
    /// byte; MAM-32 — an episode's position by its item, one per item; MAM-35 — a poster by its item, one per item.
    /// </para>
    /// <para>
    /// The lookups over stored rows are built by grouping and keeping the first of each key, never with
    /// a dictionary that throws on a duplicate: two stored rows with one key (bad data, a hand edit) then
    /// leave one row untouched, which is deleted, instead of stopping every later sync.
    /// </para>
    /// </remarks>
    private sealed class SnapshotUpserts(
        ApplicationDbContext db,
        ExistingSnapshot existing,
        Guid integrationId,
        string sourceName,
        DateTime now)
    {
        private readonly Dictionary<string, LibraryLocationEntity> _locations = FirstByKey(
            existing.Locations.Where(location => location.SourceItemId is not null), location => location.SourceItemId!);

        private readonly Dictionary<(string?, MediaTitleKind), MediaWorkTitleEntity> _titles = FirstByKey(
            existing.Titles, title => (title.SourceItemId, title.Kind));

        private readonly Dictionary<(string?, MediaIdentifierScheme), MediaWorkIdentifierEntity> _identifiers = FirstByKey(
            existing.Identifiers, identifier => (identifier.SourceItemId, identifier.Scheme));

        private readonly Dictionary<string, MediaVersionEntity> _versionsBySourceId = FirstByKey(
            existing.Versions.Where(version => version.SourceVersionId is not null), version => version.SourceVersionId!);

        // By item, in first-seen order: the n-th version without a source id is found by its position.
        private readonly Dictionary<string, List<MediaVersionEntity>> _versionsWithoutSourceId = existing.Versions
            .Where(version => version.SourceVersionId is null && version.SourceItemId is not null)
            .GroupBy(version => version.SourceItemId!, StringComparer.Ordinal)
            .ToDictionary(item => item.Key, item => item.ToList(), StringComparer.Ordinal);

        // MAM-18 — a library's name always has its version; only the scan's own names have none.
        private readonly Dictionary<(Guid?, string), PhysicalFileEntity> _files = FirstByKey(
            existing.Files, file => (file.MediaVersionId, file.ServicePath));

        private readonly ILookup<Guid, LibraryLocationFolderEntity> _folders = existing.Folders.ToLookup(folder => folder.LibraryLocationId);

        private readonly Dictionary<string, MediaEpisodePositionEntity> _positions = FirstByKey(
            existing.Positions.Where(position => position.SourceItemId is not null), position => position.SourceItemId!);

        private readonly Dictionary<string, MediaWorkPosterEntity> _posters = FirstByKey(
            existing.Posters.Where(poster => poster.SourceItemId is not null), poster => poster.SourceItemId!);

        // Every stored or added row this sync reached, across the tables (ids are unique across them).
        private readonly HashSet<Guid> _touched = [];

        private List<LibraryLocation> _reportedLocations = [];

        public int VersionsCount { get; private set; }

        public int FilesCount { get; private set; }

        /// <summary>Upserts the libraries, the first of two with one id winning, each with the root
        /// folders just read (MAM-11); the files' libraries are then matched against them.</summary>
        public void UpsertLocations(IReadOnlyList<LibraryLocation> locations)
        {
            _reportedLocations = [.. locations.DistinctBy(location => location.SourceLocationId, StringComparer.Ordinal)];
            foreach (var location in _reportedLocations)
            {
                if (Find(_locations, location.SourceLocationId, () => new LibraryLocationEntity { CreatedUtc = now }, db.LibraryLocations) is not { } row)
                    continue;
                row.Name = location.Name;
                Observe(row, location.SourceLocationId);
                ReplaceFolders(row, location.ServicePaths);
            }
        }

        /// <summary>Upserts every fact of <paramref name="item"/> onto the work <paramref name="workId"/>; an
        /// episode's position points at <paramref name="seriesWorkId"/>, the series work its series item
        /// landed on (MAM-32).</summary>
        public void UpsertItem(LibraryItem item, Guid workId, Guid? seriesWorkId)
        {
            foreach (var title in item.Titles)
                UpsertTitle(item, title, workId);
            foreach (var identifier in item.Identifiers)
                UpsertIdentifier(item, identifier, workId);
            if (item.Episode is { } episode)
                UpsertPosition(item, episode, workId, seriesWorkId);
            if (item.PosterTag is { } posterTag)
                UpsertPoster(item, posterTag, workId);

            var withoutSourceId = 0;
            foreach (var version in item.Versions)
            {
                var row = version.SourceVersionId is { } sourceVersionId
                    ? Find(_versionsBySourceId, sourceVersionId, () => new MediaVersionEntity { SourceVersionId = sourceVersionId, CreatedUtc = now }, db.MediaVersions)
                    : FindWithoutSourceId(item.SourceItemId, withoutSourceId++);
                // A source id met twice in one sync: the first listing wins (LibraryVersionOwnership has
                // already dropped every other, so this only guards the store).
                if (row is null)
                    continue;

                row.MediaWorkId = workId;
                row.EditionLabel = version.EditionLabel;
                // The item that kept the source (LibraryVersionOwnership), which may change between syncs.
                Observe(row, item.SourceItemId);
                VersionsCount++;

                foreach (var file in version.Files)
                    UpsertFile(item, row, file);
            }
        }

        /// <summary>Deletes every stored row of the snapshot this sync didn't reach. A version's files go
        /// with it by cascade, whatever source reported them (<see cref="ILibrarySnapshotStore"/>; what the
        /// app read from a file outlives its name, ADR 0009); a removed library leaves its files known,
        /// without a library, and takes its root folders with it by cascade (MAM-11).</summary>
        public void RemoveUntouched()
        {
            db.PhysicalFiles.RemoveRange(existing.Files.Where(Untouched));
            db.MediaVersions.RemoveRange(existing.Versions.Where(Untouched));
            db.MediaWorkIdentifiers.RemoveRange(existing.Identifiers.Where(Untouched));
            db.MediaEpisodePositions.RemoveRange(existing.Positions.Where(Untouched));
            // MAM-35 — an item whose image went, or that went itself, loses its poster: no card asks for a picture the
            // library no longer has.
            db.MediaWorkPosters.RemoveRange(existing.Posters.Where(Untouched));
            db.MediaWorkTitles.RemoveRange(existing.Titles.Where(Untouched));
            db.LibraryLocations.RemoveRange(existing.Locations.Where(Untouched));
        }

        /// <summary>
        /// MAM-11 — puts the root folders <paramref name="servicePaths"/> of the library
        /// <paramref name="location"/> in place of the stored ones, in the server's order. Removed and added
        /// rather than matched: a folder is nothing but its place in the library's list, so there is no
        /// identity to keep, and its library row keeps the provenance.
        /// </summary>
        private void ReplaceFolders(LibraryLocationEntity location, IReadOnlyList<string> servicePaths)
        {
            db.LibraryLocationFolders.RemoveRange(_folders[location.Id]);
            db.LibraryLocationFolders.AddRange(servicePaths.Select((servicePath, position) => new LibraryLocationFolderEntity
            {
                LibraryLocationId = location.Id,
                Position = position,
                ServicePath = servicePath,
            }));
        }

        private void UpsertTitle(LibraryItem item, ReportedTitle title, Guid workId)
        {
            var row = Find(_titles, (item.SourceItemId, title.Kind), () => new MediaWorkTitleEntity { Kind = title.Kind, CreatedUtc = now }, db.MediaWorkTitles);
            if (row is null)
                return;

            row.MediaWorkId = workId;
            row.Text = title.Text;
            row.NormalizedText = LibrarySearchText.Normalize(title.Text);
            row.Year = title.Year;
            Observe(row, item.SourceItemId);
        }

        private void UpsertIdentifier(LibraryItem item, MediaIdentifier identifier, Guid workId)
        {
            // A blank value names nothing (the grouping ignores it too).
            if (string.IsNullOrWhiteSpace(identifier.Value))
                return;
            var row = Find(_identifiers, (item.SourceItemId, identifier.Scheme), () => new MediaWorkIdentifierEntity { Scheme = identifier.Scheme, CreatedUtc = now }, db.MediaWorkIdentifiers);
            if (row is null)
                return;

            row.MediaWorkId = workId;
            row.Value = identifier.Value;
            Observe(row, item.SourceItemId);
        }

        /// <summary>
        /// MAM-32 — where the episode sits, when the library says anything of it: its series work and its
        /// numbers. An episode item that names none of them places nothing.
        /// </summary>
        private void UpsertPosition(LibraryItem item, LibraryEpisode episode, Guid workId, Guid? seriesWorkId)
        {
            if (seriesWorkId is null && episode.SeasonNumber is null && episode.EpisodeNumber is null)
                return;
            var row = Find(_positions, item.SourceItemId, () => new MediaEpisodePositionEntity { CreatedUtc = now }, db.MediaEpisodePositions);
            if (row is null)
                return;

            row.MediaWorkId = workId;
            row.SeriesWorkId = seriesWorkId;
            row.SeasonNumber = episode.SeasonNumber;
            row.EpisodeNumber = episode.EpisodeNumber;
            Observe(row, item.SourceItemId);
        }

        /// <summary>
        /// MAM-35 — the item's own poster, as its tag: on the item's work, wherever the item lands, and replaced in place
        /// when the library gives the image a new tag, which makes the card ask for the new picture.
        /// </summary>
        private void UpsertPoster(LibraryItem item, string posterTag, Guid workId)
        {
            var row = Find(_posters, item.SourceItemId, () => new MediaWorkPosterEntity { CreatedUtc = now }, db.MediaWorkPosters);
            if (row is null)
                return;

            row.MediaWorkId = workId;
            row.Tag = posterTag;
            Observe(row, item.SourceItemId);
        }

        private void UpsertFile(LibraryItem item, MediaVersionEntity version, LibraryFile file)
        {
            var row = Find(_files, ((Guid?)version.Id, file.ServicePath), () => new PhysicalFileEntity { MediaVersionId = version.Id, CreatedUtc = now }, db.PhysicalFiles);
            // The same path twice in one version: one name, one row.
            if (row is null)
                return;

            row.ServicePath = file.ServicePath;
            row.NormalizedServicePath = LibrarySearchText.Normalize(file.ServicePath);
            row.SizeBytes = file.SizeBytes;
            // Decided now, from the libraries just read (LibraryLocationMatcher): none or several → none.
            // DeviceId, Inode, LinkCount and the inspection columns are the app's own reading (MAM-9) and
            // never touched here.
            row.LibraryLocationId = LibraryLocationMatcher.Find(file.ServicePath, _reportedLocations) is { } location
                ? _locations[location.SourceLocationId].Id
                : null;
            Observe(row, item.SourceItemId);
            FilesCount++;
        }

        /// <summary>
        /// The row stored under <paramref name="key"/>, or a new one added to the context and indexed under
        /// it; <c>null</c> when this sync has already reached that row — the first fact with a key wins.
        /// </summary>
        private TRow? Find<TKey, TRow>(Dictionary<TKey, TRow> rows, TKey key, Func<TRow> create, DbSet<TRow> table)
            where TKey : notnull
            where TRow : BaseEntity
        {
            if (!rows.TryGetValue(key, out var row))
            {
                row = create();
                table.Add(row);
                rows[key] = row;
            }

            return _touched.Add(row.Id) ? row : null;
        }

        // The n-th version without a source id that the item had, else a new one.
        private MediaVersionEntity? FindWithoutSourceId(string itemId, int position)
        {
            if (_versionsWithoutSourceId.TryGetValue(itemId, out var stored) && position < stored.Count)
                return _touched.Add(stored[position].Id) ? stored[position] : null;

            var row = new MediaVersionEntity { CreatedUtc = now };
            db.MediaVersions.Add(row);
            _touched.Add(row.Id);
            return row;
        }

        // The provenance of every fact this sync reports (ADR 0006): the integration's current name, the
        // item that carried it and when it was last seen. First-seen time is only ever set on a new row.
        private void Observe(ProvenancedEntity row, string sourceItemId)
        {
            row.SourceKind = ProvenanceSourceKind.MediaLibrary;
            row.SourceConnectionId = integrationId;
            row.SourceName = sourceName;
            row.SourceItemId = sourceItemId;
            row.ObservedUtc = now;
        }

        private bool Untouched(BaseEntity row) => !_touched.Contains(row.Id);

        private static Dictionary<string, TRow> FirstByKey<TRow>(IEnumerable<TRow> rows, Func<TRow, string> key) =>
            rows.GroupBy(key, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        private static Dictionary<TKey, TRow> FirstByKey<TKey, TRow>(IEnumerable<TRow> rows, Func<TRow, TKey> key) where TKey : notnull =>
            rows.GroupBy(key).ToDictionary(group => group.Key, group => group.First());
    }
}
