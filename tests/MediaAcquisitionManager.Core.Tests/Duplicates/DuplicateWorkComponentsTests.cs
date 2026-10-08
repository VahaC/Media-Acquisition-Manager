using System.Globalization;
using MediaAcquisitionManager.Core.Abstractions.Media;
using MediaAcquisitionManager.Core.Duplicates;
using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Core.Tests.Duplicates;

// MAM-37 — which works the Duplicates list takes for one work (Vasyl, 2026-10-08): works sharing a registry key are one,
// but two episodes of one show at different places are always different works, whatever ids they share — a Penguins of
// Madagascar pair under one IMDb id, Mickey Mouse Clubhouse's S03E31 under all of S03E33's ids. A show is its ids and its
// titles; a film the owner linked as an episode sits where the link says; a film item that may be either of two such
// episodes joins neither; a work the library holds at two places of one show joins nothing; films whose ids disagree stay
// one. The ids are production's where it holds the case; the components are worked out by the grouping itself, from
// works built as the loader builds them (RegistryKeys.Of). Over HTTP, LibraryDuplicatesSameWorkHttpTests.
public sealed class DuplicateWorkComponentsTests
{
    private const string PenguinsTitle = "The Penguins of Madagascar";
    private const string RogerDodgerImdbId = "tt1544978";
    private const string MickeyTitle = "Mickey Mouse Clubhouse";

    // The Penguins of Madagascar's ids, as production's library holds them since the series was identified.
    private static readonly MediaIdentifier[] PenguinsIds =
    [
        new(MediaIdentifierScheme.Imdb, "tt0892700"),
        new(MediaIdentifierScheme.TmdbTv, "7869"),
        new(MediaIdentifierScheme.Tvdb, "85075"),
    ];

    private static readonly MediaIdentifier[] MickeyIds =
    [
        new(MediaIdentifierScheme.Imdb, "tt0784896"),
        new(MediaIdentifierScheme.TmdbTv, "3934"),
        new(MediaIdentifierScheme.Tvdb, "79854"),
    ];

    [Fact]
    public void WorkComponents_two_episodes_of_one_show_at_different_places_under_one_imdb_id_are_apart()
    {
        //Arrange
        // S01E25 "Roger Dodger" and S01E37 "All King, No Kingdom": one IMDb id (IMDb lists them as paired segments), two
        // TVDB ids, two places.
        List<DuplicateWork> works =
        [
            Episode(1, [Imdb(RogerDodgerImdbId), Tvdb("477811")], Place(PenguinsIds, PenguinsTitle, 1, 25)),
            Episode(2, [Imdb(RogerDodgerImdbId), Tvdb("1292941")], Place(PenguinsIds, PenguinsTitle, 1, 37)),
        ];

        //Act
        var components = ComponentsOf(works);

        //Assert
        Assert.Equal(["1", "2"], components);
    }

    [Fact]
    public void WorkComponents_two_episodes_with_all_of_one_episodes_ids_at_different_places_are_apart()
    {
        //Arrange
        // "The Golden Boo Boo" (S03E31) carries every id of "Donald Hatches an Egg" (S03E33).
        List<DuplicateWork> works =
        [
            Episode(1, [Imdb("tt5625312"), Tvdb("4354865")], Place(MickeyIds, MickeyTitle, 3, 31)),
            Episode(2, [Imdb("tt5625312"), Tvdb("4354865")], Place(MickeyIds, MickeyTitle, 3, 33)),
        ];

        //Act
        var components = ComponentsOf(works);

        //Assert
        Assert.Equal(["1", "2"], components);
    }

    [Fact]
    public void WorkComponents_episodes_of_a_show_without_ids_at_different_places_are_apart_by_its_title()
    {
        //Arrange
        // Two libraries know the show by no id, each by its title in its own letter case; the episodes have one IMDb id.
        // The copies of S01E25 are one work, S01E37 another.
        List<DuplicateWork> works =
        [
            Episode(1, [Imdb(RogerDodgerImdbId)], Place([], "The Penguins Of Madagascar", 1, 25)),
            Episode(2, [Imdb(RogerDodgerImdbId)], Place([], PenguinsTitle, 1, 37)),
            Episode(3, [Imdb(RogerDodgerImdbId)], Place([], PenguinsTitle, 1, 25)),
        ];

        //Act
        var components = ComponentsOf(works);

        //Assert
        Assert.Equal(["1+3", "2"], components);
    }

    [Fact]
    public void WorkComponents_one_episode_at_one_place_known_by_two_shows_ids_is_one()
    {
        //Arrange
        // One episode by its own IMDb id, at one place, in a show each library knows under another TMDb show id: one
        // work, the disagreement for the owner to review (Vasyl, 2026-10-07).
        List<DuplicateWork> works =
        [
            Episode(1, [Imdb("tt1715023")], Place([new(MediaIdentifierScheme.TmdbTv, "3934")], MickeyTitle, 3, 9)),
            Episode(2, [Imdb("tt1715023")], Place([new(MediaIdentifierScheme.TmdbTv, "9999")], MickeyTitle, 3, 9)),
        ];

        //Act
        var components = ComponentsOf(works);

        //Assert
        Assert.Equal(["1+2"], components);
    }

    [Fact]
    public void WorkComponents_one_episode_id_in_two_shows_sharing_no_id_or_title_joins_them()
    {
        //Arrange
        // One episode by its own IMDb id at S03E09 of a show and at S01E05 of another that shares no id and no title with
        // it: nothing says they are one show, so the places can't disagree — one work, the shows' ids for the owner to
        // review.
        List<DuplicateWork> works =
        [
            Episode(1, [Imdb("tt1715023")], Place(MickeyIds, MickeyTitle, 3, 9)),
            Episode(2, [Imdb("tt1715023")], Place([new(MediaIdentifierScheme.TmdbTv, "9999")], "Клуб Міккі Мауса", 1, 5)),
        ];

        //Act
        var components = ComponentsOf(works);

        //Assert
        Assert.Equal(["1+2"], components);
    }

    [Fact]
    public void WorkComponents_a_film_item_linked_at_one_place_and_the_librarys_episode_at_another_are_apart()
    {
        //Arrange
        // MAM-34 — the owner linked the film item to the TMDb show as S01E08; the library's own episode under that IMDb
        // id is S01E09. Placed by the link, the film item is held to its place like an episode.
        MediaIdentifier[] show = [new(MediaIdentifierScheme.TmdbTv, "88829")];
        List<DuplicateWork> works =
        [
            Film(1, [Imdb("tt0000108")], Place(show, null, 1, 8)),
            Episode(2, [Imdb("tt0000108")], Place(show, "Greatest Events of World War II in Colour", 1, 9)),
        ];

        //Act
        var components = ComponentsOf(works);

        //Assert
        Assert.Equal(["1", "2"], components);
    }

    [Fact]
    public void WorkComponents_a_film_item_with_the_imdb_id_of_two_episodes_at_different_places_joins_neither()
    {
        //Arrange
        // A film item with the Penguins pair's IMDb id may be either episode: it joins neither, as LibraryWorkGrouping's
        // item that fits two works. A second film item with that id is still its copy.
        List<DuplicateWork> works =
        [
            Episode(1, [Imdb(RogerDodgerImdbId), Tvdb("477811")], Place(PenguinsIds, PenguinsTitle, 1, 25)),
            Episode(2, [Imdb(RogerDodgerImdbId), Tvdb("1292941")], Place(PenguinsIds, PenguinsTitle, 1, 37)),
            Film(3, [Imdb(RogerDodgerImdbId)]),
            Film(4, [Imdb(RogerDodgerImdbId)]),
        ];

        //Act
        var components = ComponentsOf(works);

        //Assert
        Assert.Equal(["1", "2", "3+4"], components);
    }

    [Fact]
    public void WorkComponents_a_work_at_two_places_of_a_show_joins_nothing()
    {
        //Arrange
        // A show the library knows by its title alone, and S03E31 with every id of S03E33: the sync made the two episodes
        // one work at two places. It is a copy of nothing, so the two film items with S03E33's IMDb id don't join it, and
        // stay each other's copy.
        List<DuplicateWork> works =
        [
            Episode(1, [Imdb("tt5625312"), Tvdb("4354865")], Place([], MickeyTitle, 3, 31), Place([], MickeyTitle, 3, 33)),
            Film(2, [Imdb("tt5625312")]),
            Film(3, [Imdb("tt5625312")]),
        ];

        //Act
        var components = ComponentsOf(works);

        //Assert
        Assert.Equal(["1", "2+3"], components);
    }

    [Fact]
    public void WorkComponents_a_work_at_two_places_of_a_show_joins_no_episode_of_another_show()
    {
        //Arrange
        // The same work, and S03E33 in two libraries that know the show by its ids and its Ukrainian title only: nothing
        // says that is the first one's show, so no places disagree — yet the work at two places joins neither copy, and
        // the copies stay one.
        List<DuplicateWork> works =
        [
            Episode(1, [Imdb("tt5625312"), Tvdb("4354865")], Place([], MickeyTitle, 3, 31), Place([], MickeyTitle, 3, 33)),
            Episode(2, [Imdb("tt5625312"), Tvdb("4354865")], Place(MickeyIds, "Клуб Міккі Мауса", 3, 33)),
            Episode(3, [Imdb("tt5625312"), Tvdb("4354865")], Place(MickeyIds, "Клуб Міккі Мауса", 3, 33)),
        ];

        //Act
        var components = ComponentsOf(works);

        //Assert
        Assert.Equal(["1", "2+3"], components);
    }

    [Fact]
    public void WorkComponents_a_film_item_with_one_episodes_imdb_id_joins_it()
    {
        //Arrange
        // ADR 0017 — The Continental's first episode filed as a film in one library and as the episode of its show in
        // another, both with the episode's IMDb id.
        List<DuplicateWork> works =
        [
            Film(1, [Imdb("tt23899004")]),
            Episode(2, [Imdb("tt23899004")], Place([new(MediaIdentifierScheme.TmdbTv, "117480")], "The Continental: From the World of John Wick", 1, 1)),
        ];

        //Act
        var components = ComponentsOf(works);

        //Assert
        Assert.Equal(["1+2"], components);
    }

    [Fact]
    public void WorkComponents_two_films_with_one_imdb_id_and_different_tmdb_ids_stay_one()
    {
        //Arrange
        // Vasyl, 2026-10-07, unchanged for films: one work, the disagreement shown.
        List<DuplicateWork> works =
        [
            Film(1, [Imdb("tt0090605"), new(MediaIdentifierScheme.Tmdb, "679")]),
            Film(2, [Imdb("tt0090605"), new(MediaIdentifierScheme.Tmdb, "8077")]),
        ];

        //Act
        var components = ComponentsOf(works);

        //Assert
        Assert.Equal(["1+2"], components);
    }

    [Fact]
    public void WorkComponents_copies_of_one_episode_join_and_another_episode_under_its_imdb_id_stays_apart()
    {
        //Arrange
        // The bedroom knows S01E25 by its IMDb id alone, in a show it knows by its title: the IMDb id is all the copies
        // share, and S01E37, which holds it too, comes first. Its refusal must not keep the copies apart.
        List<DuplicateWork> works =
        [
            Episode(1, [Imdb(RogerDodgerImdbId), Tvdb("1292941")], Place(PenguinsIds, PenguinsTitle, 1, 37)),
            Episode(2, [Imdb(RogerDodgerImdbId), Tvdb("477811")], Place(PenguinsIds, PenguinsTitle, 1, 25)),
            Episode(3, [Imdb(RogerDodgerImdbId)], Place([], "The Penguins Of Madagascar", 1, 25)),
        ];

        //Act
        var components = ComponentsOf(works);

        //Assert
        Assert.Equal(["1", "2+3"], components);
    }

    [Fact]
    public void WorkComponents_gives_the_same_components_whatever_order_the_works_come_in()
    {
        //Arrange
        // The works are taken in id order whatever order they come in, so every order of these five gives the same
        // components down to their numbers, the root each work gets. Who is with whom here would hold in any order; a
        // chain whose members hang on the order is the next test's.
        List<DuplicateWork> works =
        [
            Episode(1, [Imdb(RogerDodgerImdbId), Tvdb("1292941")], Place(PenguinsIds, PenguinsTitle, 1, 37)),
            Episode(2, [Imdb(RogerDodgerImdbId), Tvdb("477811")], Place(PenguinsIds, PenguinsTitle, 1, 25)),
            Episode(3, [Imdb(RogerDodgerImdbId)], Place([], "The Penguins Of Madagascar", 1, 25)),
            Film(4, [Imdb(RogerDodgerImdbId)]),
            Film(5, [Imdb(RogerDodgerImdbId)]),
        ];
        var inIdOrder = DuplicateGrouping.WorkComponents(works, DuplicateGrouping.ShowPlacesOf(works));

        //Act
        List<Dictionary<Guid, int>> everyOrder =
        [
            .. Permutations(works).Select(order => DuplicateGrouping.WorkComponents(order, DuplicateGrouping.ShowPlacesOf(order))),
        ];

        //Assert
        Assert.Equal(["1", "2+3", "4+5"], ComponentsOf(works));
        Assert.Equal(120, everyOrder.Count);
        Assert.All(everyOrder, componentOf => Assert.Equal(inIdOrder.OrderBy(work => work.Key), componentOf.OrderBy(work => work.Key)));
    }

    [Fact]
    public void WorkComponents_a_chain_through_another_show_joins_the_same_works_whatever_order_they_come_in()
    {
        //Arrange
        // Work 2, in Mickey Mouse Clubhouse, shares an IMDb id with work 1 and a TVDB id with work 3, at two places of The
        // Penguins of Madagascar: it can join either, never both, so which one hangs on the order of the joins. Taken in
        // id order, 1 and 2 meet first, whatever order the works come in.
        List<DuplicateWork> works =
        [
            Episode(1, [Imdb("tt0000001")], Place(PenguinsIds, PenguinsTitle, 1, 1)),
            Episode(2, [Imdb("tt0000001"), Tvdb("1000002")], Place(MickeyIds, MickeyTitle, 1, 5)),
            Episode(3, [Tvdb("1000002")], Place(PenguinsIds, PenguinsTitle, 1, 2)),
        ];

        //Act
        List<List<string>> everyOrder = [.. Permutations(works).Select(ComponentsOf)];

        //Assert
        Assert.Equal(6, everyOrder.Count);
        Assert.All(everyOrder, components => Assert.Equal(["1+2", "3"], components));
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    // The components as the works' numbers, each component joined by "+" in number order, the components in order of
    // their first.
    private static List<string> ComponentsOf(IReadOnlyList<DuplicateWork> works)
    {
        var componentOf = DuplicateGrouping.WorkComponents(works, DuplicateGrouping.ShowPlacesOf(works));
        return
        [
            .. componentOf
                .GroupBy(work => work.Value)
                .Select(component => component.Select(work => NumberOf(work.Key)).Order().ToList())
                .OrderBy(component => component[0])
                .Select(component => string.Join('+', component)),
        ];
    }

    private static DuplicateWork Episode(int number, MediaIdentifier[] identifiers, params DuplicateEpisodePlace[] places) =>
        WorkOf(number, MediaWorkKind.Episode, identifiers, places);

    private static DuplicateWork Film(int number, MediaIdentifier[] identifiers, params DuplicateEpisodePlace[] places) =>
        WorkOf(number, MediaWorkKind.Movie, identifiers, places);

    // A work as the loader builds it: its keys from its own ids and its places.
    private static DuplicateWork WorkOf(int number, MediaWorkKind kind, MediaIdentifier[] identifiers, DuplicateEpisodePlace[] places) =>
        new(IdOf(number), kind, RegistryKeys.Of(kind, identifiers, places), [], places);

    // A place in a show with these ids and title; a link's show has no title.
    private static DuplicateEpisodePlace Place(MediaIdentifier[] showIds, string? showTitle, int season, int episode) =>
        new(showIds, showTitle is null ? [] : [showTitle], season, episode);

    private static MediaIdentifier Imdb(string value) => new(MediaIdentifierScheme.Imdb, value);

    private static MediaIdentifier Tvdb(string value) => new(MediaIdentifierScheme.Tvdb, value);

    // A work's id that sorts as its number does, so a test says which work comes first.
    private static Guid IdOf(int number) => Guid.Parse(string.Create(CultureInfo.InvariantCulture, $"00000000-0000-0000-0000-{number:D12}"));

    private static int NumberOf(Guid id) => int.Parse(id.ToString("N")[20..], NumberStyles.None, CultureInfo.InvariantCulture);

    // Every order of the works, each once.
    private static IEnumerable<List<DuplicateWork>> Permutations(List<DuplicateWork> works)
    {
        if (works.Count <= 1)
        {
            yield return works;
            yield break;
        }

        for (var index = 0; index < works.Count; index++)
        {
            var first = works[index];
            foreach (var rest in Permutations([.. works.Where((_, other) => other != index)]))
                yield return [first, .. rest];
        }
    }
}
