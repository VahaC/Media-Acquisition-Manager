using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Library;

namespace MediaAcquisitionManager.Core.Tests.Library;

// MAM-34 — a library work's TMDb suggestions: the evidence for each, the order the owner sees them in (a linked
// neighbour's relative, the exact title, the year, the kind, the runtime, the library's genre, the closer title, TMDb's
// order), and the one confident suggestion "Accept all confident" may link unseen (decision 8): of the work's kind, the
// only exact title of the work's year, its runtime agreeing with a measured duration.
public sealed class LinkSuggestionRankingTests
{
    // "120. Landing Stripling" in the cartoon library: 6.2 minutes measured, 1962.
    private static readonly LinkSuggestionSubject Short = new(MediaWorkKind.Movie, ["Landing Stripling"], 1962, [6], CatalogGenreRole.Animation, []);

    [Fact]
    public void Rank_makes_the_one_exact_title_of_the_year_with_an_agreeing_runtime_confident()
    {
        //Arrange
        var stripling = Film("223021", "Landing Stripling", 1962, runtime: 6, roles: [CatalogGenreRole.Animation], position: 0);
        var other = Film("1", "Landing Strip", 1962, runtime: 6, roles: [CatalogGenreRole.Animation], position: 1);

        //Act
        var ranked = LinkSuggestionRanking.Rank(Short, [other, stripling]);

        //Assert
        Assert.Equal(["223021", "1"], ranked.Select(candidate => candidate.Candidate.Id));
        Assert.Equal([true, false], ranked.Select(candidate => candidate.IsConfident));
        Assert.Equal(
            new LinkEvidence(
                new TitleEvidence(1, true, LinkTitleKind.Title, "Landing Stripling"),
                new YearEvidence(1962, 1962, 0),
                new RuntimeEvidence(6, 6, true),
                true,
                new GenreEvidence(CatalogGenreRole.Animation, LinkGenreAgreement.Agrees),
                null),
            ranked[0].Evidence);
    }

    [Fact]
    public void Rank_makes_nothing_confident_when_two_exact_titles_share_the_year()
    {
        //Arrange
        // Two answers are a doubt the owner settles, whatever their kinds.
        var film = Film("223021", "Landing Stripling", 1962, runtime: 6);
        var show = Show("5", "Landing Stripling", 1962, runtime: 6);

        //Act
        var ranked = LinkSuggestionRanking.Rank(Short, [film, show]);

        //Assert
        Assert.All(ranked, candidate => Assert.False(candidate.IsConfident));
    }

    [Fact]
    public void Rank_makes_nothing_confident_without_a_measured_duration()
    {
        //Arrange
        var subject = Short with { VersionMinutes = [] };

        //Act
        var ranked = LinkSuggestionRanking.Rank(subject, [Film("223021", "Landing Stripling", 1962, runtime: 6)]);

        //Assert
        var candidate = Assert.Single(ranked);
        Assert.False(candidate.IsConfident);
        Assert.Equal(new RuntimeEvidence(null, 6, null), candidate.Evidence.Runtime);
    }

    [Fact]
    public void Rank_makes_nothing_confident_without_the_catalogs_runtime()
    {
        //Arrange
        // TMDb's 0 is its "not known".

        //Act
        var ranked = LinkSuggestionRanking.Rank(Short, [Film("223021", "Landing Stripling", 1962, runtime: 0)]);

        //Assert
        var candidate = Assert.Single(ranked);
        Assert.False(candidate.IsConfident);
        Assert.Equal(new RuntimeEvidence(6, null, null), candidate.Evidence.Runtime);
    }

    [Fact]
    public void Rank_makes_nothing_confident_when_the_runtime_disagrees()
    {
        //Arrange

        //Act
        var ranked = LinkSuggestionRanking.Rank(Short, [Film("223021", "Landing Stripling", 1962, runtime: 90)]);

        //Assert
        var candidate = Assert.Single(ranked);
        Assert.False(candidate.IsConfident);
        Assert.Equal(new RuntimeEvidence(6, 90, false), candidate.Evidence.Runtime);
    }

    [Fact]
    public void Rank_makes_nothing_confident_a_year_off()
    {
        //Arrange

        //Act
        var ranked = LinkSuggestionRanking.Rank(Short, [Film("223021", "Landing Stripling", 1963, runtime: 6)]);

        //Assert
        Assert.False(Assert.Single(ranked).IsConfident);
    }

    [Fact]
    public void Rank_never_makes_a_show_confident_for_a_film()
    {
        //Arrange
        // A show for a film is an episode-to-show link, never confident.

        //Act
        var ranked = LinkSuggestionRanking.Rank(Short, [Show("223021", "Landing Stripling", 1962, runtime: 6)]);

        //Assert
        var candidate = Assert.Single(ranked);
        Assert.False(candidate.IsConfident);
        Assert.False(candidate.Evidence.KindAgrees);
    }

    [Fact]
    public void Rank_makes_a_show_confident_for_a_series()
    {
        //Arrange
        var series = new LinkSuggestionSubject(MediaWorkKind.Series, ["The Penguins Of Madagascar"], 2008, [23], null, []);

        //Act
        var ranked = LinkSuggestionRanking.Rank(series, [Show("7869", "Пінгвіни Мадагаскару", 2008, runtime: 23, original: "The Penguins of Madagascar")]);

        //Assert
        var candidate = Assert.Single(ranked);
        Assert.True(candidate.IsConfident);
        Assert.Equal(new TitleEvidence(1, true, LinkTitleKind.OriginalTitle, "The Penguins of Madagascar"), candidate.Evidence.Title);
    }

    [Fact]
    public void Rank_never_makes_anything_confident_for_an_episode_with_no_series()
    {
        //Arrange
        var episode = new LinkSuggestionSubject(MediaWorkKind.Episode, ["Tom and Jerry Kids"], 1990, [11], null, []);

        //Act
        var ranked = LinkSuggestionRanking.Rank(episode, [Show("3", "Tom and Jerry Kids", 1990, runtime: 11), Film("4", "Tom and Jerry Kids", 1990, runtime: 11)]);

        //Assert
        Assert.All(ranked, candidate => Assert.False(candidate.IsConfident || candidate.Evidence.KindAgrees));
    }

    [Fact]
    public void Rank_puts_a_linked_neighbours_relative_first()
    {
        //Arrange
        // A Tom & Jerry short beside others of Gene Deitch's: his short ranks above an exact title by another hand.
        var subject = Short with { Siblings = [new SiblingFacts("119. Tall in the Trap", MediaWorkKind.Movie, "100", null, ["deitch"])] };
        var exact = Film("1", "Landing Stripling", 1962, runtime: 6);
        var relative = Film("2", "Landing Strippling", 1962, runtime: 6, directors: ["deitch"]);

        //Act
        var ranked = LinkSuggestionRanking.Rank(subject, [exact, relative]);

        //Assert
        Assert.Equal(["2", "1"], ranked.Select(candidate => candidate.Candidate.Id));
        Assert.Equal(new SiblingEvidence(LinkSiblingReason.SameDirector, "119. Tall in the Trap"), ranked[0].Evidence.Sibling);
        Assert.Null(ranked[1].Evidence.Sibling);
    }

    [Fact]
    public void Rank_names_a_neighbour_of_the_same_collection_first_of_its_reasons()
    {
        //Arrange
        var subject = Short with
        {
            Siblings =
            [
                new SiblingFacts("Other", MediaWorkKind.Movie, "100", null, ["someone"]),
                new SiblingFacts("Alien", MediaWorkKind.Movie, "348", "8091", ["scott"]),
            ],
        };
        var candidate = Film("679", "Aliens", 1986, runtime: 137, collection: "8091", directors: ["scott"]);

        //Act
        var ranked = LinkSuggestionRanking.Rank(subject, [candidate]);

        //Assert
        Assert.Equal(new SiblingEvidence(LinkSiblingReason.SameCollection, "Alien"), Assert.Single(ranked).Evidence.Sibling);
    }

    [Fact]
    public void Rank_names_a_neighbour_linked_to_the_same_show()
    {
        //Arrange
        // Planet Earth's E07 linked to show 1044 lifts 1044 for E08; a film of that number is another work (ADR 0017).
        var subject = new LinkSuggestionSubject(
            MediaWorkKind.Movie, ["Planet Earth"], null, [], CatalogGenreRole.Documentary,
            [new SiblingFacts("Planet_Earth_E07 - Great Plains", MediaWorkKind.Series, "1044", null, [])]);

        //Act
        var ranked = LinkSuggestionRanking.Rank(subject, [Film("1044", "Planet Earth", 2006, runtime: 50), Show("1044", "Планета Земля", 2006, runtime: 53)]);

        //Assert
        Assert.Equal([MediaWorkKind.Series, MediaWorkKind.Movie], ranked.Select(candidate => candidate.Candidate.Kind));
        Assert.Equal(new SiblingEvidence(LinkSiblingReason.SameShow, "Planet_Earth_E07 - Great Plains"), ranked[0].Evidence.Sibling);
        Assert.Null(ranked[1].Evidence.Sibling);
    }

    [Fact]
    public void Rank_orders_an_exact_title_before_a_closer_year()
    {
        //Arrange

        //Act
        var ranked = LinkSuggestionRanking.Rank(Short, [Film("1", "Landing Strip", 1962, runtime: 6), Film("2", "Landing Stripling", 1970, runtime: 6)]);

        //Assert
        Assert.Equal(["2", "1"], ranked.Select(candidate => candidate.Candidate.Id));
    }

    [Fact]
    public void Rank_orders_the_same_year_then_a_year_off_then_the_rest()
    {
        //Arrange
        Func<string, int?, LinkCandidateFacts> exact = (id, year) => Film(id, "Landing Stripling", year, runtime: 6);

        //Act
        var ranked = LinkSuggestionRanking.Rank(Short, [exact("unknown", null), exact("far", 1990), exact("off", 1961), exact("same", 1962)]);

        //Assert
        // "far" and "unknown" tie on every step; their catalog order (Position 0 for each) then their ids decide.
        Assert.Equal(["same", "off", "far", "unknown"], ranked.Select(candidate => candidate.Candidate.Id));
        Assert.Equal(new YearEvidence(1962, 1961, 1), ranked[1].Evidence.Year);
        Assert.Equal(new YearEvidence(1962, null, null), ranked[3].Evidence.Year);
    }

    [Fact]
    public void Rank_orders_the_works_kind_before_the_runtime()
    {
        //Arrange

        //Act
        var ranked = LinkSuggestionRanking.Rank(Short, [Show("show", "Landing Stripling", 1962, runtime: 6), Film("film", "Landing Stripling", 1962, runtime: 60)]);

        //Assert
        Assert.Equal(["film", "show"], ranked.Select(candidate => candidate.Candidate.Id));
    }

    [Fact]
    public void Rank_orders_an_agreeing_runtime_then_an_unknown_one_then_one_that_disagrees()
    {
        //Arrange

        //Act
        var ranked = LinkSuggestionRanking.Rank(
            Short,
            [Film("disagrees", "Landing Stripling", 1962, runtime: 60), Film("unknown", "Landing Stripling", 1962, runtime: null), Film("agrees", "Landing Stripling", 1962, runtime: 7)]);

        //Assert
        Assert.Equal(["agrees", "unknown", "disagrees"], ranked.Select(candidate => candidate.Candidate.Id));
    }

    [Fact]
    public void Rank_orders_the_librarys_genre_then_an_unknown_one_then_another()
    {
        //Arrange

        //Act
        var ranked = LinkSuggestionRanking.Rank(
            Short,
            [
                Film("other", "Landing Stripling", 1962, runtime: 6, roles: [CatalogGenreRole.Other]),
                Film("none", "Landing Stripling", 1962, runtime: 6, roles: []),
                Film("animation", "Landing Stripling", 1962, runtime: 6, roles: [CatalogGenreRole.Animation, CatalogGenreRole.Other]),
            ]);

        //Assert
        Assert.Equal(["animation", "none", "other"], ranked.Select(candidate => candidate.Candidate.Id));
        Assert.Equal(
            [LinkGenreAgreement.Agrees, LinkGenreAgreement.Unknown, LinkGenreAgreement.Disagrees],
            ranked.Select(candidate => candidate.Evidence.Genre.Agreement));
    }

    [Fact]
    public void Rank_in_a_library_with_no_genre_knows_no_genre_agreement()
    {
        //Arrange
        var subject = Short with { LibraryRole = null };

        //Act
        var ranked = LinkSuggestionRanking.Rank(subject, [Film("1", "Landing Stripling", 1962, runtime: 6, roles: [CatalogGenreRole.Other])]);

        //Assert
        Assert.Equal(new GenreEvidence(null, LinkGenreAgreement.Unknown), Assert.Single(ranked).Evidence.Genre);
    }

    [Fact]
    public void Rank_orders_a_closer_title_then_the_catalogs_order_then_kind_and_id()
    {
        //Arrange
        var subject = new LinkSuggestionSubject(MediaWorkKind.Movie, ["Planet Earth"], null, [], null, []);

        //Act
        var ranked = LinkSuggestionRanking.Rank(
            subject,
            [
                Film("far", "Earth", null, runtime: null, position: 0),
                Film("b", "Planet Earth II", null, runtime: null, position: 2),
                Film("a", "Planet Earth II", null, runtime: null, position: 2),
                Film("first", "Planet Earth II", null, runtime: null, position: 1),
            ]);

        //Assert
        Assert.Equal(["first", "a", "b", "far"], ranked.Select(candidate => candidate.Candidate.Id));
    }

    [Fact]
    public void Rank_reads_an_alternative_title_and_says_which_it_was()
    {
        //Arrange
        var subject = new LinkSuggestionSubject(MediaWorkKind.Series, ["Planet Earth"], 2006, [], null, []);
        var show = Show("1044", "Планета Земля", 2006, runtime: 53, original: "Planet Earth: Season One", alternatives: ["Planet Erde", "Planet Earth"]);

        //Act
        var ranked = LinkSuggestionRanking.Rank(subject, [show]);

        //Assert
        Assert.Equal(new TitleEvidence(1, true, LinkTitleKind.AlternativeTitle, "Planet Earth"), Assert.Single(ranked).Evidence.Title);
    }

    [Fact]
    public void Rank_without_an_exact_title_names_the_closest()
    {
        //Arrange
        var subject = new LinkSuggestionSubject(MediaWorkKind.Movie, ["Landing Striplin"], null, [], null, []);

        //Act
        var ranked = LinkSuggestionRanking.Rank(subject, [Film("1", "Посадкова смуга", null, runtime: null, original: "Landing Stripling")]);

        //Assert
        var title = Assert.Single(ranked).Evidence.Title;
        Assert.Equal((false, LinkTitleKind.OriginalTitle, "Landing Stripling"), (title.Exact, title.MatchedKind, title.MatchedTitle));
        Assert.InRange(title.Similarity, 0.9, 0.99);
    }

    [Fact]
    public void Rank_compares_the_version_nearest_the_catalogs_runtime()
    {
        //Arrange
        // Aliens held as the theatrical cut (137) and the special edition (154).
        var subject = new LinkSuggestionSubject(MediaWorkKind.Movie, ["Aliens"], 1986, [154, 137], null, []);

        //Act
        var ranked = LinkSuggestionRanking.Rank(subject, [Film("679", "Aliens", 1986, runtime: 137)]);

        //Assert
        var candidate = Assert.Single(ranked);
        Assert.Equal(new RuntimeEvidence(137, 137, true), candidate.Evidence.Runtime);
        Assert.True(candidate.IsConfident);
    }

    [Fact]
    public void Rank_of_no_candidates_is_empty()
    {
        //Arrange

        //Act
        var ranked = LinkSuggestionRanking.Rank(Short, []);

        //Assert
        Assert.Empty(ranked);
    }

    [Theory]
    [InlineData(6, 6, true)]
    [InlineData(8, 6, true)]
    [InlineData(4, 6, true)]
    [InlineData(9, 6, false)]
    [InlineData(130, 121, true)]
    [InlineData(133, 121, true)]
    [InlineData(134, 121, false)]
    [InlineData(108, 121, false)]
    public void RuntimesAgree_within_two_minutes_or_a_tenth_of_the_catalogs_runtime(int library, int catalog, bool expected)
    {
        //Arrange

        //Act
        var agrees = LinkSuggestionRanking.RuntimesAgree(library, catalog);

        //Assert
        Assert.Equal(expected, agrees);
    }

    private static LinkCandidateFacts Film(
        string id, string title, int? year, int? runtime, string? original = null, CatalogGenreRole[]? roles = null,
        string? collection = null, string[]? directors = null, int position = 0) =>
        new(MediaWorkKind.Movie, id, title, original, [], year, runtime, roles ?? [], collection, directors ?? [], position);

    private static LinkCandidateFacts Show(
        string id, string title, int? year, int? runtime, string? original = null, string[]? alternatives = null, int position = 0) =>
        new(MediaWorkKind.Series, id, title, original, alternatives ?? [], year, runtime, [CatalogGenreRole.Other], null, [], position);
}
