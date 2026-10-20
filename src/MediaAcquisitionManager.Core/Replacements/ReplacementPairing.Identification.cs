using MediaAcquisitionManager.Core.Abstractions.Library;
using MediaAcquisitionManager.Core.Abstractions.Media;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Texts;
// A file's ids by scheme: the form each compares in, to the form it is shown in.
using Ids = System.Collections.Generic.Dictionary<
    MediaAcquisitionManager.Core.Enums.MediaIdentifierScheme,
    System.Collections.Generic.Dictionary<string, string>>;

namespace MediaAcquisitionManager.Core.Replacements;

// Rule 1: which matched work the library's ids identify a new file as.
public static partial class ReplacementPairing
{
    /// <summary>The schemes whose id makes a film item a matched film: TMDb's or IMDb's (ADR 0032).</summary>
    private static readonly MediaIdentifierScheme[] FilmSchemes = [MediaIdentifierScheme.Tmdb, MediaIdentifierScheme.Imdb];

    /// <summary>The schemes whose id makes an episode's series a matched show (ADR 0017): IMDb's, which numbers shows
    /// among everything else, and TMDb's and TVDB's of shows.</summary>
    private static readonly MediaIdentifierScheme[] SeriesSchemes =
        [MediaIdentifierScheme.Imdb, MediaIdentifierScheme.TmdbTv, MediaIdentifierScheme.Tvdb];

    /// <summary>Rule 1 for one new file: the library's word on it, and the matched work its ids make it.</summary>
    private static NewFileRead Identify(ReplacementPairingNewFile file, IReadOnlyList<ReplacementPairingWork> works, string library)
    {
        var path = MessageText.Clean(file.AppPath);
        var read = new NewFileRead(file, ReplacementText.FileNameOf(file.AppPath), null, null, null, null, null, false);
        var reading = file.Library;
        var films = works.Where(work => work.Kind == MediaWorkKind.Movie).ToList();
        var shows = works.Where(work => work.Kind == MediaWorkKind.Series).ToList();
        switch (reading.Kind)
        {
            case null:
                return read with { Waiting = NewNotListed(library, path) };
            // An extra or a home video: no work, so nothing to pair, and nothing to wait for.
            case LibraryFileKind.Other:
                return read;
            case not (LibraryFileKind.Movie or LibraryFileKind.Episode):
                return read;
        }

        if (!reading.Refreshed)
            return read with { Waiting = NewNotRefreshed(library, path) };

        if (reading.Kind == LibraryFileKind.Movie)
        {
            if (films.Count == 0)
                return read with { Problem = MadeFilm(library, path, shows) };

            var film = Identify(reading.Identifiers, films, FilmSchemes);
            switch (film)
            {
                case { Work: { } work }:
                    return read with { WorkId = work.WorkId };
                case { NoId: true }:
                    return read with { Pending = FilmWithoutId(library, path, films) };
            }

            var (pending, problem, another) = Unidentified(film, library, path, Subject.NewFilm, reading.Identifiers, FilmSchemes, films);
            return read with { Pending = pending, Problem = problem, OtherFilm = another };
        }

        if (shows.Count == 0)
            return read with { Problem = MadeEpisode(library, path, films) };
        var show = reading.SeriesItemId is null ? Identification.WithoutId : Identify(reading.SeriesIdentifiers, shows, SeriesSchemes);
        switch (show)
        {
            case { NoId: true }:
                return read with { Pending = EpisodeWithoutSeries(library, path, shows) };
            case { Work: null }:
                // An episode of another series stops the job whatever was paired: a season pack holds one show's.
                var (seriesPending, seriesProblem, _) = Unidentified(show, library, path, Subject.NewEpisode, reading.SeriesIdentifiers, SeriesSchemes, shows);
                return read with { Pending = seriesPending, Problem = seriesProblem };
        }

        var identified = read with { WorkId = show.Work!.WorkId };
        if (reading is not { SeasonNumber: { } season, EpisodeNumber: { } episode })
            return identified with { Pending = NotNumbered(library, path) };

        // The library's end number first; a range in the name only where it gives none, and only ever to widen.
        if (reading.EpisodeNumberEnd is { } end && end > episode)
            return identified with { Covers = new EpisodeSpan(season, episode, end) };
        return EpisodeRangeNames.RangeOf(read.Name) is { CoversSeveral: true } named
            ? identified with { Covers = new EpisodeSpan(season, Math.Min(episode, named.FirstEpisode), Math.Max(episode, named.LastEpisode)) }
            : identified with { Covers = new EpisodeSpan(season, episode, episode) };
    }

    /// <summary>
    /// The one of <paramref name="candidates"/> <paramref name="held"/> agrees with: an id of one of
    /// <paramref name="deciding"/> is among the work's, and every id of a scheme both have is among the work's. None when
    /// <paramref name="held"/> has no id of <paramref name="deciding"/>; several, or the first that shares an id without
    /// agreeing, or none at all, otherwise — with the works none of its ids could be compared with.
    /// </summary>
    private static Identification Identify(
        IReadOnlyList<MediaIdentifier> held, IReadOnlyList<ReplacementPairingWork> candidates, IReadOnlyList<MediaIdentifierScheme> deciding)
    {
        var heldIds = IdsOf(held);
        if (!deciding.Any(heldIds.ContainsKey))
            return Identification.WithoutId;

        var agreeing = candidates.Where(work => Agrees(heldIds, IdsOf(work.Identifiers), deciding)).ToList();
        if (agreeing.Count == 1)
            return new Identification(agreeing[0], [], null, [], false);
        if (agreeing.Count > 1)
            return new Identification(null, agreeing, null, [], false);
        return new Identification(
            null,
            [],
            candidates.FirstOrDefault(work => Shares(heldIds, IdsOf(work.Identifiers))),
            [.. candidates.Where(work => !Compared(heldIds, IdsOf(work.Identifiers), deciding))],
            false);
    }

    private static bool Agrees(Ids held, Ids work, IReadOnlyList<MediaIdentifierScheme> deciding) =>
        deciding.Any(scheme => held.TryGetValue(scheme, out var values) && work.TryGetValue(scheme, out var theirs) && values.Keys.Any(theirs.ContainsKey))
        && held.All(scheme => !work.TryGetValue(scheme.Key, out var theirs) || scheme.Value.Keys.All(theirs.ContainsKey));

    /// <summary>Whether both have an id of one of <paramref name="deciding"/>: only then can the ids differ.</summary>
    private static bool Compared(Ids held, Ids work, IReadOnlyList<MediaIdentifierScheme> deciding) =>
        deciding.Any(scheme => held.ContainsKey(scheme) && work.ContainsKey(scheme));

    private static bool Shares(Ids held, Ids work) =>
        held.Any(scheme => work.TryGetValue(scheme.Key, out var theirs) && scheme.Value.Keys.Any(theirs.ContainsKey));

    /// <summary>
    /// Ids by scheme, each keyed by the form it compares in — trimmed and upper-cased, as <c>WorkMatching</c> compares them
    /// (<c>tt0090605</c> is <c>TT0090605</c>) — and kept as given, trimmed, to be shown. A blank value names nothing.
    /// </summary>
    private static Ids IdsOf(IReadOnlyList<MediaIdentifier> identifiers)
    {
        var ids = new Ids();
        foreach (var identifier in identifiers)
        {
            var shown = identifier.Value.Trim();
            if (shown.Length == 0)
                continue;
            if (!ids.TryGetValue(identifier.Scheme, out var values))
                ids[identifier.Scheme] = values = new Dictionary<string, string>(StringComparer.Ordinal);
            values.TryAdd(shown.ToUpperInvariant(), shown);
        }

        return ids;
    }

    /// <summary>
    /// What an identification that found no one work makes of the file: ids that fit several works, conflicting ids or
    /// another work's stop the job (<c>Problem</c>) — another work's said so (<c>Another</c>), since a film of it is
    /// simply added instead once every ticked copy found its pair (<c>Pair</c>) —; ids that compare with nothing of a work
    /// wait for one that does (<c>Pending</c>) — unless no such work has an id of the deciding schemes at all, which stops
    /// it too. <paramref name="subject"/> says whose ids they are, which the reasons word.
    /// </summary>
    private static (Message? Pending, Message? Problem, bool Another) Unidentified(
        Identification identification,
        string library,
        string path,
        Subject subject,
        IReadOnlyList<MediaIdentifier> held,
        IReadOnlyList<MediaIdentifierScheme> deciding,
        IReadOnlyList<ReplacementPairingWork> candidates)
    {
        var heldIds = IdsOf(held);
        if (identification.Several.Count > 1)
            return (null, SeveralWorks(library, path, subject, identification.Several), false);
        if (identification.Sharing is { } sharing)
        {
            var theirs = IdsOf(sharing.Identifiers);
            var shared = heldIds.OrderBy(scheme => scheme.Key)
                .SelectMany(scheme => scheme.Value.Where(value => theirs.TryGetValue(scheme.Key, out var values) && values.ContainsKey(value.Key))
                    .Select(value => Shown(scheme.Key, value.Value)))
                .First();
            var differing = heldIds.OrderBy(scheme => scheme.Key)
                .SelectMany(scheme => scheme.Value.Where(value => theirs.TryGetValue(scheme.Key, out var values) && !values.ContainsKey(value.Key))
                    .Select(value => Shown(scheme.Key, value.Value)))
                .FirstOrDefault();
            // Sharing an id of a scheme that doesn't decide, with nothing differing, says no more than sharing none.
            if (differing is not null)
                return (null, Conflicting(library, path, subject, shared, differing, sharing.Name), false);
        }

        var shown = ShownOf(heldIds, deciding);
        // Only a different id of a scheme the work has one of makes the file another work. Ids of other schemes say
        // nothing of it (TMDb 348 beside a work known only by IMDb tt0078748): the library may still give the file one
        // the work has, or the owner fix the item, so it waits as a file without an id does. A work with no id of these
        // schemes at all — one confirmed by its title and year — could never be told by one, so that stops the job now.
        if (identification.Uncompared.Count > 0)
        {
            List<(MessageValue Name, IReadOnlyList<string> Ids)> known =
            [
                .. identification.Uncompared
                    .Select(work => (work.Name, Ids: (IReadOnlyList<string>)ShownOf(IdsOf(work.Identifiers), deciding)))
                    .Where(work => work.Ids.Count > 0),
            ];
            return known.Count > 0
                ? (OtherSchemes(library, path, subject, shown, known), null, false)
                : (null, NoIdToCompare(library, path, subject, shown, identification.Uncompared), false);
        }

        return (null, Another(library, path, subject, shown, candidates), true);
    }

    /// <summary>The ids of <paramref name="deciding"/> as a sentence shows them, by scheme: "IMDb tt0078748", "TMDb 348".</summary>
    private static List<string> ShownOf(Ids ids, IReadOnlyList<MediaIdentifierScheme> deciding) =>
    [
        .. ids.Where(scheme => deciding.Contains(scheme.Key))
            .OrderBy(scheme => scheme.Key)
            .SelectMany(scheme => scheme.Value.Values.Select(value => Shown(scheme.Key, value))),
    ];

    /// <summary>What a new file's ids make of it among a kind's matched works.</summary>
    /// <param name="Work">The one work they agree with.</param>
    /// <param name="Several">The works they agree with, when more than one.</param>
    /// <param name="Sharing">The first work they share an id with without agreeing.</param>
    /// <param name="Uncompared">The works none of them could be compared with: no scheme that decides has an id on both
    /// sides.</param>
    /// <param name="NoId">The file has no id that could decide.</param>
    private sealed record Identification(
        ReplacementPairingWork? Work,
        IReadOnlyList<ReplacementPairingWork> Several,
        ReplacementPairingWork? Sharing,
        IReadOnlyList<ReplacementPairingWork> Uncompared,
        bool NoId)
    {
        public static Identification WithoutId { get; } = new(null, [], null, [], true);
    }
}
