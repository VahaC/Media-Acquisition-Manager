using System.Globalization;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Api.Mapping;
using MediaAcquisitionManager.Api.Services.CopyRemovals;
using MediaAcquisitionManager.Core.Duplicates;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Library;
using MediaAcquisitionManager.Core.Paths;
using MediaAcquisitionManager.Core.Replacements;

namespace MediaAcquisitionManager.Api.Services.Duplicates;

// MAM-40 — the removal's reads of the library: each file of a group as the removal rules read it, the list's verdict on
// each, and the group's heading the removal keeps — one reading for the list and the Remove request, so the page never
// offers what the request refuses.
public sealed partial class LibraryDuplicateService
{
    public async Task<DuplicateGroupFiles?> FindForRemovalAsync(string key, CancellationToken cancellationToken = default)
    {
        // As FindAsync reads the library: the owner-only names change no key.
        var library = await LoadAsync(forOwner: false, cancellationToken);
        var group = DuplicateGrouping.Group(library.Names, library.Works, new Dictionary<long, string?>())
            .FirstOrDefault(found => found.Key.Equals(key, StringComparison.Ordinal));
        return group is null
            ? null
            : new DuplicateGroupFiles(group, CandidatesOf(group, library, library.Names.ToDictionary(name => name.FileId)), HeadingOf(group, library));
    }

    /// <summary>
    /// The verdict on removing each of <paramref name="group"/>'s files, by file id, as <paramref name="judge"/> reads the
    /// rules (<see cref="CopyRemovalRules"/>); none for a group of another class — one file's names or a possible match —,
    /// which offers no removal.
    /// </summary>
    private static Dictionary<Guid, CopyRemovalVerdictDto> VerdictsOf(
        DuplicateGroup group, Snapshot library, IReadOnlyDictionary<Guid, DuplicateName> namesById, CopyRemovalJudge? judge)
    {
        if (judge is null || group.Class is not (DuplicateClass.SameWork or DuplicateClass.ByteIdentical))
            return [];

        var files = CandidatesOf(group, library, namesById);
        var workIds = group.WorkIds.ToHashSet();
        return files.ToDictionary(
            file => file.FileId,
            file => judge.ReasonOf(group.Class, workIds, file, files) is { } reason ? new CopyRemovalVerdictDto(false, reason) : new CopyRemovalVerdictDto(true, null));
    }

    /// <summary>Each of <paramref name="group"/>'s files as the removal rules read it, in the group's order: its row's app
    /// path through its library's mappings now, its last look and the works holding it — each name found in
    /// <paramref name="namesById"/>, built once per read rather than once per group.</summary>
    private static List<CopyRemovalCandidate> CandidatesOf(DuplicateGroup group, Snapshot library, IReadOnlyDictionary<Guid, DuplicateName> namesById) =>
    [
        .. group.FileIds.Select(fileId =>
        {
            var name = namesById[fileId];
            var file = library.Files[fileId];
            var mapped = PathMapper.Map(file.ServicePath, MappingsOf(file, library.PathMappings));
            NormalizedPath? appPath = mapped.HasAppPath && NormalizedPath.TryParse(mapped.AppPath, out var parsed, out _) ? parsed : null;
            return new CopyRemovalCandidate(
                file.Id,
                appPath,
                file.ServicePath,
                file.InspectionStatus,
                name.DeviceId,
                name.Inode,
                name.SizeBytes,
                file.ReadModifiedUtc,
                [.. name.Holders.Select(holder => holder.WorkId).Distinct()]);
        }),
    ];

    /// <summary>
    /// What <paramref name="group"/>'s card is headed by, as the page shows it: its first work in the list's order — a film
    /// by its title and year, an episode by its series' title, its place and its own title —; the first file's own name for
    /// a group no library reported.
    /// </summary>
    private static string HeadingOf(DuplicateGroup group, Snapshot library)
    {
        if (group.WorkIds.Count == 0)
            return ReplacementText.FileNameOf(group.FileIds.Select(fileId => library.Files[fileId].ServicePath).Min(StringComparer.Ordinal) ?? "");

        var workId = group.WorkIds
            .Select(id => (Id: id, Order: OrderOf(id, library)))
            .OrderBy(work => work.Order.Title, LibraryTitleOrder.Comparer)
            .ThenBy(work => work.Order.Title, StringComparer.Ordinal)
            .ThenBy(work => work.Order.SeasonRank)
            .ThenBy(work => work.Order.Season)
            .ThenBy(work => work.Order.Episode)
            .First()
            .Id;
        var titles = (IReadOnlyList<MediaWorkTitleEntity>)[.. library.Titles[workId]];
        // MAM-8 — the card's title and year: a displayed title, else an original one; no year made up from another's.
        var title = LibraryMapper.FirstInTitleOrder(titles, MediaTitleKind.Display) ?? LibraryMapper.FirstInTitleOrder(titles, MediaTitleKind.Original);
        if (library.WorkRows[workId].Kind != MediaWorkKind.Episode)
            return title is { Year: { } year } ? string.Create(CultureInfo.InvariantCulture, $"{title.Text} ({year})") : title?.Text ?? "";

        var place = LibraryMapper.ToEpisodeDto([.. library.Positions[workId]]);
        var seriesTitle = place.SeriesId is { } seriesId ? CardTitleOf([.. library.Titles[seriesId]]) : null;
        var number = place is { SeasonNumber: { } season, EpisodeNumber: { } episode }
            ? string.Create(CultureInfo.InvariantCulture, $"S{season:00}E{episode:00}")
            : null;
        return string.Join(" · ", new[] { seriesTitle, number, title?.Text }.Where(part => !string.IsNullOrEmpty(part)));
    }
}
