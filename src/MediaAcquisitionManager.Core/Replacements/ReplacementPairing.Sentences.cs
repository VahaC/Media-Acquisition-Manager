using System.Diagnostics;
using System.Globalization;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Texts;

namespace MediaAcquisitionManager.Core.Replacements;

// The reasons the pairing gives: one message each (MAM-47, ADR 0042), whose template in replacements.json quotes the
// names and paths and names the library by its integration's name, as the download's reasons name its client. "Within an
// hour" is ReplacementVerificationRules.LibraryWait, and those sentences are only ever given once it ran out. A reason
// about a new file's series or an old copy changes the words around the path, so each is a key of its own (Subject),
// never "the old copy " glued in front of a path; a count today's English doesn't print ("film"/"films", "has"/"have",
// "it"/"them") is a key per shape too, since a plural form must write its count.
public static partial class ReplacementPairing
{
    // ── the library hasn't read a file (verdict a) ───────────────────────────────────────────────────────────────

    private static Message NewNotListed(string library, string path) =>
        Message.Of("replacements.pairing.new-not-listed", ("library", library), ("path", path));

    private static Message NewNotRefreshed(string library, string path) =>
        Message.Of("replacements.pairing.new-not-refreshed", ("library", library), ("path", path));

    private static Message OldNotListed(string library, string path) =>
        Message.Of("replacements.pairing.old-not-listed", ("library", library), ("path", path));

    private static Message OldNotRefreshed(string library, string path) =>
        Message.Of("replacements.pairing.old-not-refreshed", ("library", library), ("path", path));

    // ── the library hasn't identified a file yet (verdict c) ─────────────────────────────────────────────────────

    private static Message FilmWithoutId(string library, string path, IReadOnlyList<ReplacementPairingWork> films) =>
        Message.Of("replacements.pairing.film-without-id", ("library", library), ("path", path), ("works", Names(films)));

    private static Message EpisodeWithoutSeries(string library, string path, IReadOnlyList<ReplacementPairingWork> shows) =>
        Message.Of("replacements.pairing.episode-without-series", ("library", library), ("path", path), ("works", Names(shows)));

    private static Message OldEpisodeWithoutSeries(string library, string path, IReadOnlyList<ReplacementPairingWork> shows) =>
        Message.Of("replacements.pairing.old-episode-without-series", ("library", library), ("path", path), ("works", Names(shows)));

    private static Message NotNumbered(string library, string path) =>
        Message.Of("replacements.pairing.not-numbered", ("library", library), ("path", path));

    private static Message OtherSchemes(
        string library, string path, Subject subject, IReadOnlyList<string> shownIds, IReadOnlyList<(MessageValue Name, IReadOnlyList<string> Ids)> works) =>
        Message.Of(
            subject switch
            {
                Subject.NewFilm => "replacements.pairing.other-schemes-film",
                Subject.NewEpisode => "replacements.pairing.other-schemes-series",
                Subject.OldEpisode => "replacements.pairing.other-schemes-old-series",
                _ => throw new UnreachableException(),
            },
            ("library", library),
            ("path", path),
            ("ids", Texts(shownIds)),
            ("known", new ListValue(works.Select(work => (MessageValue)Message.Of(
                "replacements.pairing.known-only-by-clause", ("work", work.Name), ("ids", Texts(work.Ids)))))));

    // ── a new file's work (rule 1) ───────────────────────────────────────────────────────────────────────────────

    // MAM-47 — one series and several are two keys, as MadeEpisode's films are: Ukrainian names the series in the singular
    // («серіалу») or the plural («серіалів»), which one list template can't do.
    private static Message MadeFilm(string library, string path, IReadOnlyList<ReplacementPairingWork> shows) =>
        shows.Count == 1
            ? Message.Of("replacements.pairing.made-film-of-series", ("library", library), ("path", path), ("work", shows[0].Name))
            : Message.Of("replacements.pairing.made-film", ("library", library), ("path", path), ("works", Names(shows)));

    private static Message MadeEpisode(string library, string path, IReadOnlyList<ReplacementPairingWork> films) =>
        films.Count == 1
            ? Message.Of("replacements.pairing.made-episode-of-film", ("library", library), ("path", path), ("work", films[0].Name))
            : Message.Of("replacements.pairing.made-episode-of-films", ("library", library), ("path", path), ("works", Names(films)));

    private static Message SeveralWorks(string library, string path, Subject subject, IReadOnlyList<ReplacementPairingWork> works)
    {
        // An old copy is told by its own work alone (Pair passes it as the one candidate), so its ids never fit several.
        if (subject == Subject.OldEpisode)
            throw new UnreachableException("An old copy's series is compared with its own work alone.");
        return works.Count == 2
            ? Message.Of(
                "replacements.pairing.several-works-two", ("library", library), ("path", path), ("first", works[0].Name), ("second", works[1].Name))
            : Message.Of("replacements.pairing.several-works", ("library", library), ("path", path), ("works", Names(works)));
    }

    private static Message Conflicting(string library, string path, Subject subject, string shared, string differing, MessageValue workName) =>
        Message.Of(
            subject switch
            {
                Subject.NewFilm => "replacements.pairing.conflicting-film",
                Subject.NewEpisode => "replacements.pairing.conflicting-series",
                Subject.OldEpisode => "replacements.pairing.conflicting-old-series",
                _ => throw new UnreachableException(),
            },
            ("library", library),
            ("path", path),
            ("shared", shared),
            ("work", workName),
            ("differing", differing));

    private static Message Another(
        string library, string path, Subject subject, IReadOnlyList<string> shownIds, IReadOnlyList<ReplacementPairingWork> candidates) =>
        Message.Of(
            subject switch
            {
                Subject.NewFilm => "replacements.pairing.another-work",
                Subject.NewEpisode => "replacements.pairing.another-series",
                Subject.OldEpisode => "replacements.pairing.another-series-old",
                _ => throw new UnreachableException(),
            },
            ("library", library),
            ("path", path),
            ("ids", Texts(shownIds)),
            ("works", Names(candidates)));

    private static Message NoIdToCompare(
        string library, string path, Subject subject, IReadOnlyList<string> shownIds, IReadOnlyList<ReplacementPairingWork> works)
    {
        var oneWork = works.Count == 1;
        var oneId = shownIds.Count == 1;
        // MAM-47 — an old copy's series is compared with its own work alone (Pair passes it as the one candidate), so the
        // works it couldn't be compared with are that one work: no "have" key for it, as SeveralWorks has none, since a
        // dead sentence would only be translated for nothing.
        if (subject == Subject.OldEpisode && !oneWork)
            throw new UnreachableException("An old copy's series is compared with its own work alone.");
        var key = (subject, oneWork, oneId) switch
        {
            (Subject.NewFilm, true, true) => "replacements.pairing.no-id-to-compare.film.work-id",
            (Subject.NewFilm, true, false) => "replacements.pairing.no-id-to-compare.film.work-ids",
            (Subject.NewFilm, false, true) => "replacements.pairing.no-id-to-compare.film.works-id",
            (Subject.NewFilm, false, false) => "replacements.pairing.no-id-to-compare.film.works-ids",
            (Subject.NewEpisode, true, true) => "replacements.pairing.no-id-to-compare.series.work-id",
            (Subject.NewEpisode, true, false) => "replacements.pairing.no-id-to-compare.series.work-ids",
            (Subject.NewEpisode, false, true) => "replacements.pairing.no-id-to-compare.series.works-id",
            (Subject.NewEpisode, false, false) => "replacements.pairing.no-id-to-compare.series.works-ids",
            (Subject.OldEpisode, true, true) => "replacements.pairing.no-id-to-compare.old-series.work-id",
            (Subject.OldEpisode, true, false) => "replacements.pairing.no-id-to-compare.old-series.work-ids",
            _ => throw new UnreachableException(),
        };
        return Message.Of(
            key,
            ("library", library),
            ("path", path),
            oneWork ? ("work", works[0].Name) : ("works", Names(works)),
            oneId ? ("id", shownIds[0]) : ("ids", Texts(shownIds)));
    }

    // ── a ticked copy and its pair (rules 2 and 3) ───────────────────────────────────────────────────────────────

    private static Message OldNotNumbered(string library, string path) =>
        Message.Of("replacements.pairing.old-not-numbered", ("library", library), ("path", path));

    private static Message Half(string path, char half) =>
        Message.Of("replacements.pairing.old-half", ("path", path), ("letter", half.ToString()));

    private static Message OldCoversSeveral(string path, EpisodeSpan span) =>
        Message.Of(
            "replacements.pairing.old-covers-several",
            ("path", path),
            ("first", Label(span.First)),
            ("last", Label(span.Last)),
            ("season", Label(span.Season)));

    private static Message OldListedAsFilm(string library, string path) =>
        Message.Of("replacements.pairing.old-listed-as-film", ("library", library), ("path", path));

    private static Message OldListedAsNoEpisode(string library, string path) =>
        Message.Of("replacements.pairing.old-listed-as-no-episode", ("library", library), ("path", path));

    private static Message OldRenumbered(string library, string path, int? nowSeason, int? nowEpisode, int season, int episode) =>
        nowSeason is { } numberedSeason && nowEpisode is { } numberedEpisode
            ? Message.Of(
                "replacements.pairing.old-renumbered",
                ("library", library),
                ("path", path),
                ("nowSeason", Label(numberedSeason)),
                ("nowEpisode", Label(numberedEpisode)),
                ("season", Label(season)),
                ("episode", Label(episode)))
            : Message.Of(
                "replacements.pairing.old-unnumbered", ("library", library), ("path", path), ("season", Label(season)), ("episode", Label(episode)));

    private static Message NewCoversSeveral(string newPath, EpisodeSpan span, string oldPath) =>
        Message.Of(
            "replacements.pairing.new-covers-several",
            ("path", newPath),
            ("first", Label(span.First)),
            ("last", Label(span.Last)),
            ("season", Label(span.Season)),
            ("oldPath", oldPath));

    private static Message NewHalf(string newPath, char half, string oldPath) =>
        Message.Of("replacements.pairing.new-half", ("path", newPath), ("letter", half.ToString()), ("oldPath", oldPath));

    /// <summary>No new file at a copy's place: the film's work, or the episode of <paramref name="work"/> at
    /// <paramref name="place"/>.</summary>
    private static Message NoNewFile(ReplacementPairingWork work, EpisodePlace? place, string path) =>
        place is { } episode
            ? Message.Of(
                "replacements.pairing.no-new-file-episode",
                ("series", work.Name),
                ("season", Label(episode.Season)),
                ("episode", Label(episode.Episode)),
                ("path", path))
            : Message.Of("replacements.pairing.no-new-file-film", ("work", work.Name), ("path", path));

    /// <summary>Several new files at a copy's place, as <see cref="NoNewFile"/> names it; the count is spelled at the
    /// sentence's start ("Two new files are…", the hint <c>number:word</c>).</summary>
    private static Message TwoNewFiles(ReplacementPairingWork work, EpisodePlace? place, IReadOnlyList<string> paths) =>
        place is { } episode
            ? Message.Of(
                "replacements.pairing.new-files-episode",
                ("count", paths.Count),
                ("series", work.Name),
                ("season", Label(episode.Season)),
                ("episode", Label(episode.Episode)),
                ("paths", Texts(paths)))
            : Message.Of("replacements.pairing.new-files-film", ("count", paths.Count), ("work", work.Name), ("paths", Texts(paths)));

    private static Message OtherEdition(string newPath, IReadOnlyList<MediaEdition> claimed, IReadOnlyCollection<MediaEdition> release) =>
        release.Count == 0
            ? Message.Of("replacements.pairing.other-edition-release-none", ("path", newPath), ("claimed", EditionNames(claimed)))
            : Message.Of("replacements.pairing.other-edition", ("path", newPath), ("claimed", EditionNames(claimed)), ("release", EditionNames(release)));

    private static Message TooShort(string newPath, long newMilliseconds, long oldMilliseconds) =>
        Message.Of(
            "replacements.pairing.too-short",
            ("path", newPath),
            ("new", new DurationValue(newMilliseconds)),
            ("old", new DurationValue(oldMilliseconds)));

    // ── parts ────────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>An id as a sentence shows it: "IMDb tt0078748". The registries' own names, which the web app keeps as they
    /// are in every language (its <c>library.json</c>), so the pair is data, not words of ours.</summary>
    private static string Shown(MediaIdentifierScheme scheme, string value) => $"{SchemeName(scheme)} {value}";

    private static string SchemeName(MediaIdentifierScheme scheme) => scheme switch
    {
        MediaIdentifierScheme.Imdb => "IMDb",
        MediaIdentifierScheme.Tmdb => "TMDb",
        MediaIdentifierScheme.Tvdb => "TVDB",
        MediaIdentifierScheme.TmdbTv => "TMDb TV",
        _ => scheme.ToString(),
    };

    /// <summary>A season's or an episode's number as a sentence gives it: an identifier, written as it is in every
    /// language (as a year is), never grouped like an amount.</summary>
    private static string Label(int number) => number.ToString(CultureInfo.InvariantCulture);

    /// <summary>The works' names, which the template joins with "and" or "or".</summary>
    private static ListValue Names(IReadOnlyList<ReplacementPairingWork> works) => new(works.Select(work => work.Name));

    private static ListValue Texts(IEnumerable<string> items) => new(items.Select(item => (MessageValue)item));

    /// <summary>Editions as the Duplicates page names them (<c>common.edition.*</c>, the web app's own labels), in their
    /// order.</summary>
    private static ListValue EditionNames(IEnumerable<MediaEdition> editions) =>
        new(editions.Distinct().Order().Select(edition => (MessageValue)EditionName(edition)));

    /// <remarks>Every edition here was read by <c>EditionLabels</c> or kept only when defined (the verifier's
    /// <c>EditionsOf</c>), so a number no member names can't reach this; a label is a key, never the enum's name.</remarks>
    private static Message EditionName(MediaEdition edition) => edition switch
    {
        MediaEdition.Theatrical => Message.Of("common.edition.theatrical"),
        MediaEdition.DirectorsCut => Message.Of("common.edition.directors-cut"),
        MediaEdition.Extended => Message.Of("common.edition.extended"),
        MediaEdition.Unrated => Message.Of("common.edition.unrated"),
        MediaEdition.Uncut => Message.Of("common.edition.uncut"),
        MediaEdition.Remastered => Message.Of("common.edition.remastered"),
        MediaEdition.SpecialEdition => Message.Of("common.edition.special-edition"),
        MediaEdition.Imax => Message.Of("common.edition.imax"),
        MediaEdition.OpenMatte => Message.Of("common.edition.open-matte"),
        MediaEdition.FinalCut => Message.Of("common.edition.final-cut"),
        MediaEdition.Redux => Message.Of("common.edition.redux"),
        MediaEdition.International => Message.Of("common.edition.international"),
        MediaEdition.Alternate => Message.Of("common.edition.alternate"),
        MediaEdition.Assembly => Message.Of("common.edition.assembly"),
        MediaEdition.Widescreen => Message.Of("common.edition.widescreen"),
        MediaEdition.FullScreen => Message.Of("common.edition.full-screen"),
        _ => throw new ArgumentOutOfRangeException(nameof(edition), edition, "An edition no member of MediaEdition names has no label."),
    };
}
