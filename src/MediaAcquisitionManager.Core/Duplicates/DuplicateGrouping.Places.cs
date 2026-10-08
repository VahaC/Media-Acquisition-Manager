using MediaAcquisitionManager.Core.Matching;

namespace MediaAcquisitionManager.Core.Duplicates;

// MAM-37 — where each work sits in each show (Vasyl, 2026-10-08): two episodes of one show at different places are always
// different works, whatever ids they share, so no join may put them in one component.
public static partial class DuplicateGrouping
{
    /// <summary>
    /// The places of each of <paramref name="works"/> in each show: by show, the season and episode of every place it
    /// sits at. A show is every id and title one place names, joined across all the works — a series' TVDB, TMDb and IMDb
    /// ids and its titles are one show, and a link's TMDb show is that show when the series holds its id. A work is placed
    /// when it has a show; a film is not, unless the owner linked it as an episode (MAM-34). Each work has an entry, empty
    /// when it isn't placed.
    /// </summary>
    /// <remarks>
    /// The titles count as well as the ids, so a show the library knows by no id is still one show: The Penguins of
    /// Madagascar was such a series ("named after its folder", no ids), and its episodes with one IMDb id at two places
    /// must stay apart all the same. Two shows of one title are one show here, which can only keep two works apart, never
    /// join them. The tokens are numbered in ordinal order, so a show's number never hangs on the order of the works.
    /// </remarks>
    internal static Dictionary<Guid, Dictionary<int, SortedSet<(int Season, int Episode)>>> ShowPlacesOf(IReadOnlyList<DuplicateWork> works)
    {
        var placeTokens = works.ToDictionary(work => work.WorkId, work => work.Places.Select(place => (Place: place, Tokens: TokensOf(place))).ToList());
        var indexOf = placeTokens.Values
            .SelectMany(places => places)
            .SelectMany(place => place.Tokens)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .Select((token, index) => (token, index))
            .ToDictionary(token => token.token, token => token.index, StringComparer.Ordinal);
        var parent = Enumerable.Range(0, indexOf.Count).ToArray();
        foreach (var (_, tokens) in placeTokens.Values.SelectMany(places => places))
        {
            foreach (var token in tokens.Skip(1))
                Union(parent, indexOf[tokens[0]], indexOf[token]);
        }

        return placeTokens.ToDictionary(
            work => work.Key,
            work => work.Value
                .Where(place => place.Tokens.Count > 0)
                .GroupBy(place => Find(parent, indexOf[place.Tokens[0]]))
                .ToDictionary(show => show.Key, show => new SortedSet<(int Season, int Episode)>(show.Select(place => (place.Place.SeasonNumber, place.Place.EpisodeNumber)))));
    }

    // What names a place's show: each of its ids as "<scheme>:<value>", compared as Normalized compares a key's value, and
    // each of its titles by its match key. A link's place has its TMDb show's id alone.
    private static List<string> TokensOf(DuplicateEpisodePlace place) =>
    [
        .. place.SeriesIdentifiers
            .Select(identifier => (identifier.Scheme, Value: identifier.Value.Trim().ToUpperInvariant()))
            .Where(identifier => identifier.Value.Length > 0)
            .Select(identifier => $"{identifier.Scheme}:{identifier.Value}"),
        .. place.SeriesTitles
            .Select(TitleMatchKey.For)
            .Where(key => key.Length > 0)
            .Select(key => "title:" + key),
    ];

    // Whether two works, or two components, sit at different places of one show: a show both hold at sets of places
    // that are not the same.
    private static bool Disagree(
        Dictionary<int, SortedSet<(int Season, int Episode)>> first, Dictionary<int, SortedSet<(int Season, int Episode)>> second) =>
        first.Any(show => second.TryGetValue(show.Key, out var places) && !places.SetEquals(show.Value));

    // Whether a work, or a component, sits at two places of one show: two episodes a sync made one work, a show without
    // ids between them (Vasyl, 2026-10-08, MAM-37: two episodes of one show at different numbers are never one work). Such
    // a work is a copy of nothing, not even of something it agrees with, so no join may take it in — a join would only
    // hand its places on, and the component it ended in would be dropped whole, other works' real copies with it.
    private static bool AtTwoPlaces(Dictionary<int, SortedSet<(int Season, int Episode)>> places) =>
        places.Values.Any(at => at.Count >= 2);
}
