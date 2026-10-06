using MediaAcquisitionManager.Api.Mapping;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Library;
using MediaAcquisitionManager.Core.Paths;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Services.LibraryLinks;

// MAM-34 — what a library work is to its TMDb suggestions (LinkSuggestionSubject): its cleaned titles and year, the
// durations the app measured of its files, what the library it sits in mostly holds, and its neighbours that already
// have a link. Read from the library's rows each time, never stored (decision 7).
public sealed partial class LibraryLinkSuggestionReader
{
    /// <summary>Each of <paramref name="workIds"/> that exists, as its suggestions are ranked for it.</summary>
    private async Task<Dictionary<Guid, WorkSubject>> SubjectsAsync(IReadOnlyCollection<Guid> workIds, CancellationToken cancellationToken)
    {
        if (workIds.Count == 0)
            return [];

        List<Guid> ids = [.. workIds.Distinct()];
        var works = await db.MediaWorks
            .AsNoTracking()
            .Where(work => EF.Parameter(ids).Contains(work.Id))
            .Select(work => new { work.Id, work.Kind })
            .ToListAsync(cancellationToken);
        if (works.Count == 0)
            return [];

        var titles = (await db.MediaWorkTitles.AsNoTracking().Where(title => EF.Parameter(ids).Contains(title.MediaWorkId)).ToListAsync(cancellationToken))
            .ToLookup(title => title.MediaWorkId);
        var files = (await LibraryLinkFiles.LoadAsync(db, ids, cancellationToken)).ToLookup(file => file.WorkId);
        var roles = await LibraryLinkProfiles.RolesAsync(db, cancellationToken);
        var neighbours = await NeighboursAsync(cancellationToken);

        return works.ToDictionary(
            work => work.Id,
            work =>
            {
                List<MediaWorkTitleEntity> workTitles = [.. titles[work.Id]];
                List<LibraryLinkFile> workFiles = [.. files[work.Id]];
                var subject = new LinkSuggestionSubject(
                    work.Kind,
                    LibraryLinkWorkTitles.Cleaned(workTitles),
                    LibraryLinkWorkTitles.YearOf(workTitles),
                    MinutesOf(work.Kind, workFiles),
                    RoleOf(workFiles, roles),
                    neighbours.Of(work.Id, workFiles));
                return new WorkSubject(subject, MarkerOf(workTitles, workFiles));
            });
    }

    /// <summary>
    /// The durations the app measured (ffprobe), one per version in whole minutes: a version of several parts counts only
    /// when every part was measured — the sum of some parts would be a runtime nobody holds. A series: the median of its
    /// episodes' — one typical episode's length, against TMDb's episode runtime.
    /// </summary>
    private static IReadOnlyList<int> MinutesOf(MediaWorkKind kind, IReadOnlyList<LibraryLinkFile> files)
    {
        List<int> versions =
        [
            .. files
                .GroupBy(file => (file.VersionWorkId, file.VersionId))
                .Where(version => version.All(file => file.DurationMilliseconds is > 0))
                .Select(version => Minutes(version.Sum(file => file.DurationMilliseconds!.Value))),
        ];
        if (kind != MediaWorkKind.Series || versions.Count == 0)
            return versions;

        var sorted = versions.Order().ToList();
        var middle = sorted.Count / 2;
        return [sorted.Count % 2 == 1 ? sorted[middle] : (int)Math.Round((sorted[middle - 1] + sorted[middle]) / 2.0, MidpointRounding.AwayFromZero)];
    }

    private static int Minutes(long milliseconds) => (int)Math.Round(milliseconds / 60_000.0, MidpointRounding.AwayFromZero);

    /// <summary>What the library the work's files sit in mostly holds; none when they sit in several, or in none known.</summary>
    private static CatalogGenreRole? RoleOf(IReadOnlyList<LibraryLinkFile> files, IReadOnlyDictionary<Guid, CatalogGenreRole?> roles)
    {
        var locations = files.Select(file => file.LibraryLocationId).OfType<Guid>().Distinct().ToList();
        return locations.Count == 1 ? roles.GetValueOrDefault(locations[0]) : null;
    }

    /// <summary>The first episode marker the work's titles, then its files' names, carry; <c>null</c> when none does.</summary>
    private static (int? Season, int Episode)? MarkerOf(IReadOnlyList<MediaWorkTitleEntity> titles, IReadOnlyList<LibraryLinkFile> files) =>
        titles
            .OrderBy(title => title.Kind == MediaTitleKind.Display ? 0 : 1)
            .Select(title => title.Text)
            .Concat(files.Select(file => FileNameOf(file.ServicePath)).OfType<string>())
            .Select(EpisodeMarker.Read)
            .FirstOrDefault(marker => marker is not null);

    private static string? FileNameOf(string servicePath) =>
        NormalizedPath.TryParse(servicePath, out var path, out _) && path.Segments.Count > 0
            ? Path.GetFileNameWithoutExtension(path.Segments[^1])
            : null;

    // ── the linked neighbours ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The works with an attached link, by the folders their files sit in, each with what the catalog says of the film or
    /// show it is linked to: its collection, its directors (a show's creators). A library's root folder is no one's
    /// folder: every film directly in "movies" sits there (the <see cref="FolderHint"/> rule).
    /// </summary>
    private async Task<Neighbours> NeighboursAsync(CancellationToken cancellationToken)
    {
        var links = (await db.LibraryItemLinks
                .AsNoTracking()
                .Where(link => link.MediaWorkId != null)
                .ToListAsync(cancellationToken))
            .GroupBy(link => link.MediaWorkId!.Value)
            // The latest link of a work of several items: the owner's last word.
            .ToDictionary(group => group.Key, group => group.OrderByDescending(link => link.SetUtc).ThenBy(link => link.Id).First());
        if (links.Count == 0)
            return Neighbours.None;

        List<Guid> linkedIds = [.. links.Keys];
        var files = await LibraryLinkFiles.LoadAsync(db, linkedIds, cancellationToken);
        var titles = (await db.MediaWorkTitles.AsNoTracking().Where(title => EF.Parameter(linkedIds).Contains(title.MediaWorkId)).ToListAsync(cancellationToken))
            .ToLookup(title => title.MediaWorkId);
        var rows = await LibraryLinkCatalogRows.LoadAsync(
            db, [.. links.Values.Select(link => (LibraryLinkProfiles.KindOf(link.Scheme), link.Value)).Distinct()], cancellationToken);
        var roots = await LibraryRootsAsync(cancellationToken);

        var facts = links.ToDictionary(
            pair => pair.Key,
            pair =>
            {
                var link = pair.Value;
                var kind = LibraryLinkProfiles.KindOf(link.Scheme);
                var row = rows.GetValueOrDefault((kind, link.Value));
                List<MediaWorkTitleEntity> workTitles = [.. titles[pair.Key]];
                var title = (LibraryMapper.FirstInTitleOrder(workTitles, MediaTitleKind.Display)
                    ?? LibraryMapper.FirstInTitleOrder(workTitles, MediaTitleKind.Original))?.Text;
                return new SiblingFacts(title ?? link.CatalogTitle ?? link.Value, kind, link.Value, row?.Work.CollectionId, row?.DirectorIds ?? []);
            });
        var byFolder = files
            .Select(file => (file.WorkId, Folder: FolderOf(file.ServicePath, roots)))
            .Where(entry => entry.Folder is not null)
            .Distinct()
            .ToLookup(entry => entry.Folder!, entry => entry.WorkId);
        return new Neighbours(byFolder, facts, roots);
    }

    private async Task<IReadOnlyList<NormalizedPath>> LibraryRootsAsync(CancellationToken cancellationToken) =>
    [
        .. (await db.LibraryLocationFolders.AsNoTracking().Select(folder => folder.ServicePath).ToListAsync(cancellationToken))
            .Select(root => NormalizedPath.TryParse(root, out var path, out _) ? path : null)
            .OfType<NormalizedPath>(),
    ];

    /// <summary>The folder <paramref name="servicePath"/> sits in; <c>null</c> for a library's root folder, a drive's top,
    /// or a path that doesn't parse.</summary>
    private static NormalizedPath? FolderOf(string servicePath, IReadOnlyList<NormalizedPath> roots)
    {
        if (!NormalizedPath.TryParse(servicePath, out var path, out _) || path.Segments.Count < 2)
            return null;

        var folder = path with { Segments = [.. path.Segments.Take(path.Segments.Count - 1)] };
        return roots.Any(folder.IsSameAs) ? null : folder;
    }

    /// <summary>What a work is to its suggestions, and the episode marker its names carry (prefilling a show's numbers).</summary>
    private sealed record WorkSubject(LinkSuggestionSubject Subject, (int? Season, int Episode)? Marker);

    /// <summary>The linked works by folder, and what each one's link names.</summary>
    private sealed class Neighbours(
        ILookup<NormalizedPath, Guid> byFolder, IReadOnlyDictionary<Guid, SiblingFacts> facts, IReadOnlyList<NormalizedPath> roots)
    {
        public static Neighbours None { get; } = new(Array.Empty<(NormalizedPath, Guid)>().ToLookup(entry => entry.Item1, entry => entry.Item2), new Dictionary<Guid, SiblingFacts>(), []);

        /// <summary>The linked works sharing a folder with <paramref name="files"/>, the work itself left out, in title
        /// order.</summary>
        public IReadOnlyList<SiblingFacts> Of(Guid workId, IReadOnlyList<LibraryLinkFile> files) =>
        [
            .. files
                .Select(file => FolderOf(file.ServicePath, roots))
                .OfType<NormalizedPath>()
                .Distinct()
                .SelectMany(folder => byFolder[folder])
                .Where(siblingId => siblingId != workId)
                .Distinct()
                .Select(siblingId => (Id: siblingId, Facts: facts[siblingId]))
                .OrderBy(sibling => sibling.Facts.Title, LibraryTitleOrder.Comparer)
                .ThenBy(sibling => sibling.Id)
                .Select(sibling => sibling.Facts),
        ];
    }
}
