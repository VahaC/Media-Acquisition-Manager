using System.Globalization;
using MediaAcquisitionManager.Api.Mapping;
using MediaAcquisitionManager.Api.Services.Search;
using MediaAcquisitionManager.Core.Abstractions.Media;
using MediaAcquisitionManager.Core.Abstractions.Metadata;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Library;
using MediaAcquisitionManager.Core.Search;

namespace MediaAcquisitionManager.Api.Services.Replacements;

// MAM-24 — which works and copies a release offers (ADR 0032): the work-level reasons, a series' claimed episodes at
// their places, and the confirmation's own words.
public sealed partial class ReplacementCandidateReader
{
    /// <summary>
    /// One matched work: its name, why none of its copies can be replaced, and — a series — its held episodes the
    /// release claims, each at its first claimed place in the Library's order (R3: an episode two items place in two
    /// seasons is listed in the one the release claims), by season, then number, then title.
    /// </summary>
    private static WorkPlan PlanOf(ReleaseLibraryMatch match, LibraryWorkMatch work, bool isTv, ClaimedEpisodes? claimed, LibraryRows rows)
    {
        var title = work.Work.Title ?? work.Work.OriginalTitle;
        List<EpisodePlan> episodes = isTv && claimed is not null
            ? [.. rows.ClaimedPositionsBySeries[match.WorkId]
                .GroupBy(position => position.MediaWorkId)
                .Select(episode => (WorkId: episode.Key, Place: InLibraryOrder(episode).First()))
                .Select(episode => new EpisodePlan(
                    episode.WorkId,
                    episode.Place.SeasonNumber,
                    episode.Place.EpisodeNumber,
                    TitleOf(rows.Titles[episode.WorkId].ToList())))
                .OrderBy(episode => episode.SeasonNumber is null ? 2 : episode.SeasonNumber == 0 ? 1 : 0)
                .ThenBy(episode => episode.SeasonNumber)
                .ThenBy(episode => episode.EpisodeNumber ?? int.MaxValue)
                .ThenBy(episode => episode.Title, LibraryTitleOrder.Comparer)
                .ThenBy(episode => episode.Title, StringComparer.Ordinal)
                .ThenBy(episode => episode.WorkId)]
            : [];
        ReplacementWorkUnavailableReason? unavailable = match.Match switch
        {
            // Specification §3.1: a title alone never authorises a replacement; ADR 0032: nor do ids that disagree.
            { Evidence: MatchEvidence.TitleOnly } => ReplacementWorkUnavailableReason.TitleOnly,
            { ConflictingSchemes.Count: > 0 } => ReplacementWorkUnavailableReason.ConflictingIds,
            _ when isTv && claimed is null => ReplacementWorkUnavailableReason.NoClaimedSeason,
            _ when isTv && episodes.Count == 0 => ReplacementWorkUnavailableReason.NoClaimedEpisodes,
            _ => null,
        };
        return new WorkPlan(match, work.Work.Kind, title, work.Work.Year, ConfirmationTextOf(title, work.Work.Year, isTv ? claimed : null, isTv), unavailable, episodes);
    }

    /// <summary>
    /// Every copy each work lists, in the order the dialog shows them: a film's versions in the Library's order (source,
    /// edition label, first seen, id), each version's files by path; a series' episodes in their order, each the same
    /// way. A TV copy is tied to its own library item's place (R3): a version whose item places its episode outside the
    /// claim, or under another series, can't be ticked, and the place a version keeps is its own.
    /// </summary>
    private static List<Listing> ListingsOf(IReadOnlyList<WorkPlan> plans, ClaimedEpisodes? claimed, LibraryRows rows)
    {
        var listings = new List<Listing>();
        for (var workIndex = 0; workIndex < plans.Count; workIndex++)
        {
            var plan = plans[workIndex];
            if (plan.Kind != MediaWorkKind.Series)
            {
                foreach (var (version, file) in CopiesOf(plan.Match.WorkId, rows))
                    listings.Add(new Listing(workIndex, null, version, file, plan.Match.WorkId, null, null, OutsideTheClaim: false));
                continue;
            }

            for (var episodeIndex = 0; episodeIndex < plan.Episodes.Count; episodeIndex++)
            {
                var episode = plan.Episodes[episodeIndex];
                foreach (var (version, file) in CopiesOf(episode.WorkId, rows))
                {
                    var own = rows.PositionsByEpisode[episode.WorkId].FirstOrDefault(position =>
                        position.SourceConnectionId == version.SourceConnectionId
                        && string.Equals(position.SourceItemId, version.SourceItemId, StringComparison.Ordinal));
                    var inClaim = own is not null
                        && own.SeriesWorkId == plan.Match.WorkId
                        && claimed is not null
                        && claimed.Holds(own.SeasonNumber, own.EpisodeNumber);
                    listings.Add(new Listing(
                        workIndex,
                        episodeIndex,
                        version,
                        file,
                        episode.WorkId,
                        inClaim ? own!.SeasonNumber : episode.SeasonNumber,
                        inClaim ? own!.EpisodeNumber : episode.EpisodeNumber,
                        OutsideTheClaim: !inClaim));
                }
            }
        }

        return listings;
    }

    /// <summary>A work's versions in the Library's order with each one's files by path (<c>LibraryMapper</c>'s).</summary>
    private static IEnumerable<(MediaVersionEntity Version, PhysicalFileEntity File)> CopiesOf(Guid workId, LibraryRows rows) =>
        rows.VersionsByWork[workId]
            .OrderBy(version => version.SourceName, LibraryTitleOrder.Comparer)
            .ThenBy(version => version.EditionLabel, LibraryTitleOrder.Comparer)
            .ThenBy(version => version.CreatedUtc)
            .ThenBy(version => version.Id)
            .SelectMany(version => rows.FilesByVersion[version.Id]
                .OrderBy(file => file.ServicePath, StringComparer.Ordinal)
                .ThenBy(file => file.Id)
                .Select(file => (version, file)));

    /// <summary>An episode's places in the Library's order (<c>LibraryMapper.ToEpisodeDto</c>'s): the regular seasons,
    /// then the specials, then none; then the number, none last.</summary>
    private static IOrderedEnumerable<MediaEpisodePositionEntity> InLibraryOrder(IEnumerable<MediaEpisodePositionEntity> positions) =>
        positions
            .OrderBy(position => position.SeasonNumber is null ? 2 : position.SeasonNumber == 0 ? 1 : 0)
            .ThenBy(position => position.SeasonNumber)
            .ThenBy(position => position.EpisodeNumber ?? int.MaxValue)
            .ThenBy(position => position.SeriesWorkId is null)
            .ThenBy(position => position.Id);

    /// <summary>A work's title as its card shows it: displayed, else original.</summary>
    private static string? TitleOf(IReadOnlyList<MediaWorkTitleEntity> titles) =>
        (LibraryMapper.FirstInTitleOrder(titles, MediaTitleKind.Display) ?? LibraryMapper.FirstInTitleOrder(titles, MediaTitleKind.Original))?.Text;

    /// <summary>
    /// R3 — the confirmation's own words, built here once for the dialog, the refusal and the audit row: "This release
    /// is Alien (1979)", "This release is Alien" without a year; a series with its year when known and what the
    /// release claims of it — ", season 2", ", seasons 1–5", ", season 2, episodes 1–5", ", season 2, episode 3".
    /// </summary>
    private static string ConfirmationTextOf(string? title, int? year, ClaimedEpisodes? claimed, bool isTv)
    {
        var name = title ?? "a work with no title";
        var work = year is { } known ? string.Create(CultureInfo.InvariantCulture, $"{name} ({known})") : name;
        if (!isTv || claimed is null)
            return $"This release is {work}";

        var seasons = claimed.FirstSeason == claimed.LastSeason
            ? string.Create(CultureInfo.InvariantCulture, $"season {claimed.FirstSeason}")
            : string.Create(CultureInfo.InvariantCulture, $"seasons {claimed.FirstSeason}–{claimed.LastSeason}");
        var episodes = claimed switch
        {
            { FirstEpisode: { } first, LastEpisode: { } last } when first == last =>
                string.Create(CultureInfo.InvariantCulture, $", episode {first}"),
            { FirstEpisode: { } first, LastEpisode: { } last } => string.Create(CultureInfo.InvariantCulture, $", episodes {first}–{last}"),
            _ => "",
        };
        return $"This release is {work}, {seasons}{episodes}";
    }

    /// <summary>One matched work before its copies are read.</summary>
    /// <param name="Match">How the release matched it.</param>
    /// <param name="Kind">Its kind.</param>
    /// <param name="Title">Its title, displayed else original.</param>
    /// <param name="Year">Its year.</param>
    /// <param name="ConfirmationText">The confirmation's words, before twins are told apart.</param>
    /// <param name="Unavailable">Why none of its copies can be replaced.</param>
    /// <param name="Episodes">A series' claimed held episodes in their order; none for a film.</param>
    private sealed record WorkPlan(
        ReleaseLibraryMatch Match,
        MediaWorkKind Kind,
        string? Title,
        int? Year,
        string ConfirmationText,
        ReplacementWorkUnavailableReason? Unavailable,
        IReadOnlyList<EpisodePlan> Episodes);

    /// <summary>One claimed held episode at its place.</summary>
    private sealed record EpisodePlan(Guid WorkId, int? SeasonNumber, int? EpisodeNumber, string? Title);

    /// <summary>
    /// One file of one version as one work lists it, before its reasons are read.
    /// </summary>
    /// <param name="WorkIndex">The work listing it, by its place among the matches.</param>
    /// <param name="EpisodeIndex">The episode listing it, by its place in the work's; <c>null</c> for a film's.</param>
    /// <param name="Version">Its version.</param>
    /// <param name="Row">Its file row.</param>
    /// <param name="WorkId">The film or the episode.</param>
    /// <param name="SeasonNumber">An episode's season as its version's item places it.</param>
    /// <param name="EpisodeNumber">An episode's number there.</param>
    /// <param name="OutsideTheClaim">A TV copy whose own item places its episode outside the claim.</param>
    private sealed record Listing(
        int WorkIndex,
        int? EpisodeIndex,
        MediaVersionEntity Version,
        PhysicalFileEntity Row,
        Guid WorkId,
        int? SeasonNumber,
        int? EpisodeNumber,
        bool OutsideTheClaim);
}
