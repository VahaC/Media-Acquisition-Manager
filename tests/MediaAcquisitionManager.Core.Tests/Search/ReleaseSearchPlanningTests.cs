using MediaAcquisitionManager.Core.Abstractions.Media;
using MediaAcquisitionManager.Core.Abstractions.Search;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Search;

namespace MediaAcquisitionManager.Core.Tests.Search;

// MAM-10 — which of a source's searches a release search goes to, and with what, decided once for every
// source type: the adapter only translates the queries.
public sealed class ReleaseSearchPlanningTests
{
    private static readonly MediaIdentifier Imdb = new(MediaIdentifierScheme.Imdb, "tt0816692");
    private static readonly MediaIdentifier Tmdb = new(MediaIdentifierScheme.Tmdb, "157336");

    // Toloka through Prowlarr (VendorSamples/Torznab/prowlarr-toloka-caps.xml): a movie search that
    // takes only text, the four movie categories among others, at most 100 results.
    private static readonly ReleaseSearchCapabilities Toloka = new(
        HasTextSearch: true,
        HasMovieSearch: true,
        MovieSearchTakesText: true,
        MovieSearchIdentifierSchemes: [],
        MovieSearchTakesYear: false,
        ResultLimit: 100,
        Categories:
        [
            new ReleaseCategory("2000", "Movies", null, HoldsMovies: true),
            new ReleaseCategory("2040", "Movies/HD", "2000", HoldsMovies: true),
            new ReleaseCategory("3000", "Audio", null, HoldsMovies: false),
            new ReleaseCategory("3020", "Audio/Video", "3000", HoldsMovies: false),
            new ReleaseCategory("100096", "|-Фільми HD", null, HoldsMovies: false),
        ]);

    [Fact]
    public void For_toloka_searches_its_movie_search_by_title_and_year_in_the_text_then_by_title()
    {
        //Arrange

        //Act
        var queries = ReleaseSearchPlanning.For("Interstellar", 2014, [Imdb, Tmdb], Toloka);

        //Assert
        // No identifier: Toloka's movie search takes none, so the title is all there is (ADR 0004).
        Assert.Equal(
            [
                new ReleaseSearchQuery(ReleaseSearchMode.Movie, "Interstellar 2014", null, [], ["2000", "2040"], 100),
                new ReleaseSearchQuery(ReleaseSearchMode.Movie, "Interstellar", null, [], ["2000", "2040"], 100),
            ],
            queries,
            QueryComparer.Instance);
    }

    [Fact]
    public void For_without_a_year_sends_the_title_once()
    {
        //Arrange

        //Act
        var queries = ReleaseSearchPlanning.For("Interstellar", null, [], Toloka);

        //Assert
        Assert.Equal(
            [new ReleaseSearchQuery(ReleaseSearchMode.Movie, "Interstellar", null, [], ["2000", "2040"], 100)],
            queries,
            QueryComparer.Instance);
    }

    [Fact]
    public void For_a_source_that_takes_identifiers_and_a_year_sends_the_identifier_alone_first()
    {
        //Arrange
        var capabilities = Toloka with
        {
            MovieSearchIdentifierSchemes = [MediaIdentifierScheme.Imdb, MediaIdentifierScheme.Tmdb],
            MovieSearchTakesYear = true,
        };

        //Act
        var queries = ReleaseSearchPlanning.For("Interstellar", 2014, [Imdb, Tmdb], capabilities);

        //Assert
        Assert.Equal(
            [
                new ReleaseSearchQuery(ReleaseSearchMode.Movie, null, null, [Imdb], ["2000", "2040"], 100),
                new ReleaseSearchQuery(ReleaseSearchMode.Movie, "Interstellar", 2014, [], ["2000", "2040"], 100),
                new ReleaseSearchQuery(ReleaseSearchMode.Movie, "Interstellar", null, [], ["2000", "2040"], 100),
            ],
            queries,
            QueryComparer.Instance);
    }

    [Fact]
    public void For_a_source_that_takes_only_tmdb_sends_the_tmdb_id()
    {
        //Arrange
        var capabilities = Toloka with { MovieSearchIdentifierSchemes = [MediaIdentifierScheme.Tmdb] };

        //Act
        var queries = ReleaseSearchPlanning.For("Interstellar", null, [Imdb, Tmdb], capabilities);

        //Assert
        Assert.Equal(
            [
                new ReleaseSearchQuery(ReleaseSearchMode.Movie, null, null, [Tmdb], ["2000", "2040"], 100),
                new ReleaseSearchQuery(ReleaseSearchMode.Movie, "Interstellar", null, [], ["2000", "2040"], 100),
            ],
            queries,
            QueryComparer.Instance);
    }

    [Fact]
    public void For_a_movie_search_that_takes_no_text_searches_the_title_through_the_general_search()
    {
        //Arrange
        var capabilities = Toloka with { MovieSearchTakesText = false, MovieSearchIdentifierSchemes = [MediaIdentifierScheme.Imdb] };

        //Act
        var queries = ReleaseSearchPlanning.For("Interstellar", 2014, [Imdb], capabilities);

        //Assert
        Assert.Equal(
            [
                new ReleaseSearchQuery(ReleaseSearchMode.Movie, null, null, [Imdb], ["2000", "2040"], 100),
                new ReleaseSearchQuery(ReleaseSearchMode.General, "Interstellar 2014", null, [], ["2000", "2040"], 100),
                new ReleaseSearchQuery(ReleaseSearchMode.General, "Interstellar", null, [], ["2000", "2040"], 100),
            ],
            queries,
            QueryComparer.Instance);
    }

    [Fact]
    public void For_a_source_without_movie_search_sends_the_title_to_the_general_search_and_no_identifier()
    {
        //Arrange
        var capabilities = Toloka with
        {
            HasMovieSearch = false,
            MovieSearchIdentifierSchemes = [MediaIdentifierScheme.Imdb],
            MovieSearchTakesYear = true,
        };

        //Act
        var queries = ReleaseSearchPlanning.For("Interstellar", 2014, [Imdb], capabilities);

        //Assert
        // A general search takes neither an identifier nor a year of its own.
        Assert.Equal(
            [
                new ReleaseSearchQuery(ReleaseSearchMode.General, "Interstellar 2014", null, [], ["2000", "2040"], 100),
                new ReleaseSearchQuery(ReleaseSearchMode.General, "Interstellar", null, [], ["2000", "2040"], 100),
            ],
            queries,
            QueryComparer.Instance);
    }

    [Fact]
    public void For_a_source_with_no_search_this_app_can_use_sends_nothing()
    {
        //Arrange
        var capabilities = Toloka with { HasTextSearch = false, HasMovieSearch = false };

        //Act
        var queries = ReleaseSearchPlanning.For("Interstellar", 2014, [Imdb], capabilities);

        //Assert
        Assert.Empty(queries);
    }

    [Fact]
    public void For_a_movie_search_without_text_and_without_a_usable_identifier_sends_nothing()
    {
        //Arrange
        var capabilities = Toloka with { HasTextSearch = false, MovieSearchTakesText = false, MovieSearchIdentifierSchemes = [MediaIdentifierScheme.Tmdb] };

        //Act
        var queries = ReleaseSearchPlanning.For("Interstellar", null, [Imdb], capabilities);

        //Assert
        Assert.Empty(queries);
    }

    [Theory]
    [InlineData(100, 100)]
    [InlineData(50, 50)]
    // A source that allows more is still asked for 100: every release found is a stored row.
    [InlineData(1000, ReleaseSearchPlanning.MaximumResults)]
    [InlineData(null, ReleaseSearchPlanning.MaximumResults)]
    [InlineData(0, ReleaseSearchPlanning.MaximumResults)]
    [InlineData(-5, ReleaseSearchPlanning.MaximumResults)]
    public void For_asks_for_the_sources_limit_up_to_one_hundred(int? sourceLimit, int expected)
    {
        //Arrange
        var capabilities = Toloka with { ResultLimit = sourceLimit };

        //Act
        var queries = ReleaseSearchPlanning.For("Interstellar", null, [], capabilities);

        //Assert
        Assert.Equal(expected, Assert.Single(queries).Limit);
    }

    [Fact]
    public void For_a_source_without_movie_categories_searches_every_category()
    {
        //Arrange
        var capabilities = Toloka with { Categories = [new ReleaseCategory("100096", "|-Фільми HD", null, HoldsMovies: false)] };

        //Act
        var queries = ReleaseSearchPlanning.For("Interstellar", null, [], capabilities);

        //Assert
        Assert.Empty(Assert.Single(queries).CategoryIds);
    }

    [Fact]
    public void For_names_a_movie_category_the_source_repeats_once()
    {
        //Arrange
        var capabilities = Toloka with
        {
            Categories = [new ReleaseCategory("2000", "Movies", null, true), new ReleaseCategory("2000", "Movies", null, true)],
        };

        //Act
        var queries = ReleaseSearchPlanning.For("Interstellar", null, [], capabilities);

        //Assert
        Assert.Equal(["2000"], Assert.Single(queries).CategoryIds);
    }

    // Records compare their lists by reference; a query is equal here when every list holds the same
    // items in the same order.
    private sealed class QueryComparer : IEqualityComparer<ReleaseSearchQuery>
    {
        public static readonly QueryComparer Instance = new();

        public bool Equals(ReleaseSearchQuery? x, ReleaseSearchQuery? y) =>
            x is not null && y is not null
            && x.Mode == y.Mode && x.Text == y.Text && x.Year == y.Year && x.Limit == y.Limit
            && x.Identifiers.SequenceEqual(y.Identifiers) && x.CategoryIds.SequenceEqual(y.CategoryIds);

        public int GetHashCode(ReleaseSearchQuery obj) => HashCode.Combine(obj.Mode, obj.Text, obj.Year, obj.Limit);
    }
}
