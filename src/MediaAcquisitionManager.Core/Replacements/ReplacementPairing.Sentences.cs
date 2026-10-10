using System.Globalization;
using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Core.Replacements;

// The reasons the pairing gives: one readable sentence each, names and paths quoted, the library by its integration's
// name as the download's reasons name its client. "Within an hour" is ReplacementVerificationRules.LibraryWait, and those
// sentences are only ever given once it ran out.
public static partial class ReplacementPairing
{
    private static readonly string[] CountWords = ["Two", "Three", "Four", "Five", "Six", "Seven", "Eight", "Nine", "Ten"];

    // ── the library hasn't read a file (verdict a) ───────────────────────────────────────────────────────────────

    private static string NewNotListed(string library, string path) => $"{library} didn't list {path} within an hour of being asked to look.";

    private static string NewNotRefreshed(string library, string path) => $"{library} hadn't finished reading {path} within an hour.";

    private static string OldNotListed(string library, string path) =>
        $"{library} no longer lists the old copy {path}, so its episode numbers can't be read.";

    private static string OldNotRefreshed(string library, string path) =>
        $"{library} hadn't finished reading the old copy {path} within an hour, so its episode numbers can't be read.";

    // ── the library hasn't identified a file yet (verdict c) ─────────────────────────────────────────────────────

    private static string FilmWithoutId(string library, string path, IReadOnlyList<ReplacementPairingWork> films) =>
        $"{library} gave {path} no TMDb or IMDb id within an hour, so it can't be told to be {Names(films, "or")}.";

    private static string EpisodeWithoutSeries(string library, string path, IReadOnlyList<ReplacementPairingWork> shows) =>
        $"{library} gave {path} no series with a TMDb, TVDB or IMDb id within an hour, so it can't be told to be an episode of {Names(shows, "or")}.";

    private static string NotNumbered(string library, string path) =>
        $"{library} gave {path} no season and episode number within an hour, so it can't be paired with an old copy.";

    private static string OtherSchemes(
        string library, string path, bool ofSeries, IReadOnlyList<string> shownIds, IReadOnlyList<(string Name, IReadOnlyList<string> Ids)> works) =>
        $"{library} gave {(ofSeries ? "the series of " : "")}{path} {ReplacementText.List(shownIds)} within an hour, but "
            + $"{ReplacementText.List([.. works.Select(work => $"{work.Name} is known here only by {ReplacementText.List(work.Ids)}")])}.";

    // ── a new file's work (rule 1) ───────────────────────────────────────────────────────────────────────────────

    private static string MadeFilm(string library, string path, IReadOnlyList<ReplacementPairingWork> shows) =>
        $"{library} made {path} a film, while the old copies are episodes of {Names(shows, "and")}.";

    private static string MadeEpisode(string library, string path, IReadOnlyList<ReplacementPairingWork> films) =>
        $"{library} made {path} an episode, while the old copies are of the {(films.Count == 1 ? "film" : "films")} {Names(films, "and")}.";

    private static string SeveralWorks(string library, string path, IReadOnlyList<ReplacementPairingWork> works) =>
        works.Count == 2
            ? $"{library}'s ids for {path} fit both {works[0].Name} and {works[1].Name}, so the app can't tell which work it is."
            : $"{library}'s ids for {path} fit {Names(works, "and")}, so the app can't tell which work it is.";

    private static string Conflicting(string library, string path, bool ofSeries, string shared, string differing, string workName) =>
        ofSeries
            ? $"{library} gave the series of {path} conflicting ids: {shared} is {workName}'s, {differing} is not."
            : $"{library} gave {path} conflicting ids: {shared} is {workName}'s, {differing} is not.";

    private static string Another(
        string library, string path, bool ofSeries, IReadOnlyList<string> shownIds, IReadOnlyList<ReplacementPairingWork> candidates) =>
        ofSeries
            ? $"{library} placed {path} in another series ({string.Join(", ", shownIds)}), not {Names(candidates, "or")}."
            : $"{library} identified {path} as another work ({string.Join(", ", shownIds)}), not {Names(candidates, "or")}.";

    private static string NoIdToCompare(
        string library, string path, bool ofSeries, IReadOnlyList<string> shownIds, IReadOnlyList<ReplacementPairingWork> works) =>
        $"{library} gave {(ofSeries ? "the series of " : "")}{path} {ReplacementText.List(shownIds)}, but {Names(works, "and")} "
            + $"{(works.Count == 1 ? "has" : "have")} no {(ofSeries ? "TMDb, TVDB or IMDb" : "TMDb or IMDb")} id here to compare "
            + $"{(shownIds.Count == 1 ? "it" : "them")} with.";

    // ── a ticked copy and its pair (rules 2 and 3) ───────────────────────────────────────────────────────────────

    private static string OldNotNumbered(string library, string path) =>
        $"{library} gave the old copy {path} no season and episode number, so the app can't tell which new file replaces it.";

    private static string Half(string path, char half) =>
        $"{path} is one half of an episode (its name gives the episode the letter “{half}”), so it can't be replaced on its own.";

    private static string OldCoversSeveral(string path, EpisodeSpan span) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{path} covers episodes {span.First}–{span.Last} of season {span.Season}, so one new episode can't replace it.");

    private static string OldListedAsFilm(string library, string path) =>
        $"{library} lists the old copy {path} as a film, while its new file would be an episode, so the two can't be paired.";

    private static string OldListedAsNoEpisode(string library, string path) =>
        $"{library} lists the old copy {path} as neither a film nor an episode, so no new episode can replace it.";

    private static string OldRenumbered(string library, string path, int? nowSeason, int? nowEpisode, int season, int episode)
    {
        var now = nowSeason is { } numberedSeason && nowEpisode is { } numberedEpisode
            ? $"numbers the old copy {path} {SeasonAndEpisode(numberedSeason, numberedEpisode)}"
            : $"gives the old copy {path} no season and episode number";
        return $"{library} now {now}, not {SeasonAndEpisode(season, episode)} as when it was scheduled.";
    }

    private static string NewCoversSeveral(string newPath, EpisodeSpan span, string oldPath) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{newPath} covers episodes {span.First}–{span.Last} of season {span.Season}, so it can't replace {oldPath} alone.");

    private static string NewHalf(string newPath, char half, string oldPath) =>
        $"{newPath} is one half of an episode (its name gives the episode the letter “{half}”), so it can't replace {oldPath} on its own.";

    private static string NoNewFile(string what, string path) => $"No new file is {what}, so {path} has nothing to replace it.";

    private static string TwoNewFiles(string what, IReadOnlyList<string> paths) =>
        $"{CountWord(paths.Count)} new files are {what}: {ReplacementText.List(paths)}; the app can't tell which one replaces the old copy.";

    private static string OtherEdition(string newPath, IReadOnlyList<MediaEdition> claimed, IReadOnlyCollection<MediaEdition> release) =>
        $"{newPath} claims {EditionNames(claimed)}, while the release claimed {(release.Count == 0 ? "none" : EditionNames(release))}.";

    private static string TooShort(string newPath, long newMilliseconds, long oldMilliseconds) =>
        $"{newPath} runs {Clock(newMilliseconds)}, less than three quarters of the old copy's {Clock(oldMilliseconds)}.";

    // ── parts ────────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>An episode as a sentence names it: "The Penguins of Madagascar, season 2, episode 5".</summary>
    private static string EpisodeName(string seriesName, int season, int episode) =>
        string.Create(CultureInfo.InvariantCulture, $"{seriesName}, season {season}, episode {episode}");

    /// <summary>An episode's place as a sentence gives it: "season 2, episode 5".</summary>
    private static string SeasonAndEpisode(int season, int episode) =>
        string.Create(CultureInfo.InvariantCulture, $"season {season}, episode {episode}");

    /// <summary>An id as a sentence shows it: "IMDb tt0078748".</summary>
    private static string Shown(MediaIdentifierScheme scheme, string value) => $"{SchemeName(scheme)} {value}";

    private static string SchemeName(MediaIdentifierScheme scheme) => scheme switch
    {
        MediaIdentifierScheme.Imdb => "IMDb",
        MediaIdentifierScheme.Tmdb => "TMDb",
        MediaIdentifierScheme.Tvdb => "TVDB",
        MediaIdentifierScheme.TmdbTv => "TMDb TV",
        _ => scheme.ToString(),
    };

    private static string Names(IReadOnlyList<ReplacementPairingWork> works, string last) =>
        ReplacementText.List([.. works.Select(work => work.Name)], last);

    private static string CountWord(int count) =>
        count >= 2 && count - 2 < CountWords.Length ? CountWords[count - 2] : count.ToString(CultureInfo.InvariantCulture);

    /// <summary>Editions as the Duplicates page names them in English, in their order.</summary>
    private static string EditionNames(IEnumerable<MediaEdition> editions) =>
        ReplacementText.List([.. editions.Distinct().Order().Select(EditionName)]);

    private static string EditionName(MediaEdition edition) => edition switch
    {
        MediaEdition.Theatrical => "Theatrical cut",
        MediaEdition.DirectorsCut => "Director's cut",
        MediaEdition.Extended => "Extended cut",
        MediaEdition.Unrated => "Unrated",
        MediaEdition.Uncut => "Uncut",
        MediaEdition.Remastered => "Remastered",
        MediaEdition.SpecialEdition => "Special edition",
        MediaEdition.Imax => "IMAX",
        MediaEdition.OpenMatte => "Open matte",
        MediaEdition.FinalCut => "Final cut",
        MediaEdition.Redux => "Redux",
        MediaEdition.International => "International cut",
        MediaEdition.Alternate => "Alternate cut",
        MediaEdition.Assembly => "Assembly cut",
        MediaEdition.Widescreen => "Widescreen",
        MediaEdition.FullScreen => "Full screen",
        _ => edition.ToString(),
    };

    /// <summary>A running time as a clock shows it, hours unbounded: "1:56:30".</summary>
    private static string Clock(long milliseconds)
    {
        var time = TimeSpan.FromMilliseconds(milliseconds);
        return string.Create(CultureInfo.InvariantCulture, $"{(long)time.TotalHours}:{time.Minutes:00}:{time.Seconds:00}");
    }
}
