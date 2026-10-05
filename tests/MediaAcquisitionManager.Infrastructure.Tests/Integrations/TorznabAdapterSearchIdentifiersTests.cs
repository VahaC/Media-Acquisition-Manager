using System.Net;
using System.Security;
using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Abstractions.Media;
using MediaAcquisitionManager.Core.Abstractions.Search;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Infrastructure.Integrations.Torznab;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;

namespace MediaAcquisitionManager.Infrastructure.Tests.Integrations;

// MAM-12 — the registry ids a listing gives (ADR 0004 left reading them to MAM-12), for matching a release
// to the library by identifier: imdbid, imdb and tmdbid read in the form a library holds, anything that
// isn't an id ignored, and a listing that contradicts itself giving no id of that scheme at all. MAM-32 —
// tvdbid too, a TV listing's tmdbid as a show's, and every listing's kind from its categories. MAM-33 — in
// TV/Anime and TV/Documentary, where films sit beside series, from a season or episode marker as well.
public sealed class TorznabAdapterSearchIdentifiersTests : IDisposable
{
    private const string BaseUrl = "http://prowlarr.test:9696/1/api";
    private const string ApiKey = "0f9e8d7c6b5a49382716a5b4c3d2e1f0";
    private const string DownloadLink = $"http://prowlarr.test:9696/1/download?apikey={ApiKey}&link=Q2ZESjhQcm90&file=x";

    private static readonly IntegrationConnectionSettings Settings = new(null, BaseUrl, null, null, ApiKey);

    private readonly IntegrationAdapterHarness _harness = new();

    private IReleaseSearchProvider Search => _harness.Adapter<IReleaseSearchProvider>(TorznabAdapter.TypeId);

    public void Dispose() => _harness.Dispose();

    [Theory]
    [InlineData("imdbid", "tt0816692", "tt0816692")]
    // tt is written lower-cased, as IMDb and a library write it.
    [InlineData("imdbid", "TT0816692", "tt0816692")]
    [InlineData("imdbid", " tt0816692 ", "tt0816692")]
    // Newznab's number alone, with or without its leading zeros (Jackett writes it without).
    [InlineData("imdbid", "0816692", "tt0816692")]
    [InlineData("imdb", "0816692", "tt0816692")]
    [InlineData("imdb", "816692", "tt0816692")]
    [InlineData("IMDB", "90605", "tt0090605")]
    // An id past seven digits keeps them all.
    [InlineData("imdbid", "tt12345678", "tt12345678")]
    [InlineData("imdb", "12345678", "tt12345678")]
    public async Task Search_reads_an_imdb_id_in_the_form_a_library_holds(string name, string value, string expected)
    {
        //Arrange
        ArmItem(Attribute(name, value));

        //Act
        var result = await SearchAsync();

        //Assert
        var hit = Assert.Single(result.Value!);
        Assert.Equal([new MediaIdentifier(MediaIdentifierScheme.Imdb, expected)], hit.Identifiers);
    }

    [Theory]
    [InlineData("157336", "157336")]
    [InlineData(" 679 ", "679")]
    [InlineData("1", "1")]
    public async Task Search_reads_a_tmdb_id(string value, string expected)
    {
        //Arrange
        ArmItem(Attribute("tmdbid", value));

        //Act
        var result = await SearchAsync();

        //Assert
        var hit = Assert.Single(result.Value!);
        Assert.Equal([new MediaIdentifier(MediaIdentifierScheme.Tmdb, expected)], hit.Identifiers);
    }

    [Fact]
    public async Task Search_gives_the_imdb_id_before_the_tmdb_id_whatever_the_listings_order()
    {
        //Arrange
        ArmItem(Attribute("tmdbid", "157336") + Attribute("imdbid", "tt0816692"));

        //Act
        var result = await SearchAsync();

        //Assert
        var hit = Assert.Single(result.Value!);
        Assert.Equal(
            [new MediaIdentifier(MediaIdentifierScheme.Imdb, "tt0816692"), new MediaIdentifier(MediaIdentifierScheme.Tmdb, "157336")],
            hit.Identifiers);
    }

    [Fact]
    public async Task Search_reads_ids_in_any_attribute_namespace()
    {
        //Arrange
        const string attributes = """
                                  <newznab:attr xmlns:newznab="http://www.newznab.com/DTD/2010/feeds/attributes/" name="imdbid" value="tt0816692" />
                                  <attr name=" TMDBID " value="157336" />
                                  """;
        ArmItem(attributes);

        //Act
        var result = await SearchAsync();

        //Assert
        var hit = Assert.Single(result.Value!);
        Assert.Equal(
            [new MediaIdentifier(MediaIdentifierScheme.Imdb, "tt0816692"), new MediaIdentifier(MediaIdentifierScheme.Tmdb, "157336")],
            hit.Identifiers);
    }

    [Theory]
    [InlineData("imdbid", "")]
    [InlineData("imdbid", "   ")]
    // Jackett writes 0 for none.
    [InlineData("imdbid", "0")]
    [InlineData("imdb", "0")]
    [InlineData("imdbid", "tt0000000")]
    [InlineData("imdb", "0000000")]
    [InlineData("imdbid", "tt")]
    [InlineData("imdbid", "ttabc")]
    [InlineData("imdbid", "nm0000123")]
    [InlineData("imdbid", "tt-816692")]
    [InlineData("imdb", "816692a")]
    [InlineData("imdb", "8166 92")]
    // Full-width digits: ASCII only.
    [InlineData("imdb", "８１６６９２")]
    [InlineData("tmdbid", "")]
    [InlineData("tmdbid", "0")]
    [InlineData("tmdbid", "000")]
    [InlineData("tmdbid", "-1")]
    [InlineData("tmdbid", "tt157336")]
    [InlineData("tmdbid", "157,336")]
    // Past what a TMDb id is (MediaIdentifierRules: 1 to 12 digits).
    [InlineData("tmdbid", "1234567890123")]
    [InlineData("tvdbid", "0")]
    [InlineData("tvdbid", "abc")]
    [InlineData("tvdbid", "1234567890123")]
    // Not a scheme of this app.
    [InlineData("tvmazeid", "710")]
    public async Task Search_ignores_a_value_that_is_not_an_id_and_keeps_the_release(string name, string value)
    {
        //Arrange
        ArmItem(Attribute(name, value));

        //Act
        var result = await SearchAsync();

        //Assert
        var hit = Assert.Single(result.Value!);
        Assert.Empty(hit.Identifiers);
    }

    [Fact]
    public async Task Search_ignores_an_imdb_id_longer_than_an_identifier_holds()
    {
        //Arrange
        // MediaIdentifierRules: tt and at most 62 digits, what a 64-character column holds.
        ArmItem(Attribute("imdbid", "tt" + new string('1', 63)) + Attribute("tmdbid", "157336"));

        //Act
        var result = await SearchAsync();

        //Assert
        var hit = Assert.Single(result.Value!);
        Assert.Equal([new MediaIdentifier(MediaIdentifierScheme.Tmdb, "157336")], hit.Identifiers);
    }

    [Theory]
    // The same id written in two forms is one id.
    [InlineData("imdbid", "tt0816692", "imdb", "816692", "tt0816692")]
    [InlineData("imdbid", "TT0816692", "imdbid", "tt0816692", "tt0816692")]
    // A value that is no id doesn't contradict one that is.
    [InlineData("imdbid", "tt0816692", "imdb", "0", "tt0816692")]
    [InlineData("imdbid", "", "imdb", "0816692", "tt0816692")]
    public async Task Search_reads_one_imdb_id_from_attributes_that_agree(string firstName, string firstValue, string secondName, string secondValue, string expected)
    {
        //Arrange
        ArmItem(Attribute(firstName, firstValue) + Attribute(secondName, secondValue));

        //Act
        var result = await SearchAsync();

        //Assert
        var hit = Assert.Single(result.Value!);
        Assert.Equal([new MediaIdentifier(MediaIdentifierScheme.Imdb, expected)], hit.Identifiers);
    }

    [Theory]
    [InlineData("imdbid", "tt0816692", "imdb", "0816693")]
    [InlineData("imdbid", "tt0816692", "imdbid", "tt0090605")]
    [InlineData("imdb", "816692", "imdb", "90605")]
    public async Task Search_drops_an_imdb_id_the_listing_contradicts_and_keeps_its_other_ids(string firstName, string firstValue, string secondName, string secondValue)
    {
        //Arrange
        // A listing that names two works can't be matched to either.
        ArmItem(Attribute(firstName, firstValue) + Attribute("tmdbid", "157336") + Attribute(secondName, secondValue));

        //Act
        var result = await SearchAsync();

        //Assert
        var hit = Assert.Single(result.Value!);
        Assert.Equal([new MediaIdentifier(MediaIdentifierScheme.Tmdb, "157336")], hit.Identifiers);
    }

    [Fact]
    public async Task Search_drops_a_tmdb_id_the_listing_contradicts_and_keeps_its_imdb_id()
    {
        //Arrange
        ArmItem(Attribute("tmdbid", "157336") + Attribute("imdbid", "tt0816692") + Attribute("tmdbid", "679"));

        //Act
        var result = await SearchAsync();

        //Assert
        var hit = Assert.Single(result.Value!);
        Assert.Equal([new MediaIdentifier(MediaIdentifierScheme.Imdb, "tt0816692")], hit.Identifiers);
    }

    [Fact]
    public async Task Search_reads_a_tvdb_id_after_the_imdb_and_tmdb_ids()
    {
        //Arrange
        ArmItem(Attribute("tvdbid", " 341164 ") + Attribute("imdbid", "tt4236770"));

        //Act
        var result = await SearchAsync();

        //Assert
        var hit = Assert.Single(result.Value!);
        Assert.Equal(
            [new MediaIdentifier(MediaIdentifierScheme.Imdb, "tt4236770"), new MediaIdentifier(MediaIdentifierScheme.Tvdb, "341164")],
            hit.Identifiers);
    }

    [Theory]
    // ADR 0017 — TMDb numbers shows apart from movies: a TV listing's tmdbid is a show's.
    [InlineData("5040", MediaIdentifierScheme.TmdbTv)]
    [InlineData("5000", MediaIdentifierScheme.TmdbTv)]
    [InlineData("2040", MediaIdentifierScheme.Tmdb)]
    public async Task Search_reads_a_tmdb_id_in_the_space_of_the_listings_kind(string category, MediaIdentifierScheme expected)
    {
        //Arrange
        ArmItem($"<category>{category}</category>" + Attribute("tmdbid", "73586"));

        //Act
        var result = await SearchAsync();

        //Assert
        var hit = Assert.Single(result.Value!);
        Assert.Equal([new MediaIdentifier(expected, "73586")], hit.Identifiers);
    }

    [Theory]
    // MAM-32 — a TV category says TV, whatever else the listing names (Toloka's own section beside it).
    [InlineData("<category>5040</category><category>100173</category>", ReleaseSearchMode.General, ReleaseKind.Tv)]
    [InlineData("<category>5000</category>", ReleaseSearchMode.Movie, ReleaseKind.Tv)]
    [InlineData("<category>2070</category><category>5000</category>", ReleaseSearchMode.General, ReleaseKind.Tv)]
    [InlineData("<category>2040</category><category>100096</category>", ReleaseSearchMode.Tv, ReleaseKind.Movie)]
    // A gateway's own section alone says nothing the adapter can rely on.
    [InlineData("<category>100125</category>", ReleaseSearchMode.General, ReleaseKind.Movie)]
    // No category at all: the search that found it says.
    [InlineData("", ReleaseSearchMode.Tv, ReleaseKind.Tv)]
    [InlineData("", ReleaseSearchMode.General, ReleaseKind.Movie)]
    [InlineData("", ReleaseSearchMode.Movie, ReleaseKind.Movie)]
    public async Task Search_tells_a_tv_listing_from_a_movie_by_its_categories(string categories, ReleaseSearchMode mode, ReleaseKind expected)
    {
        //Arrange
        ArmItem(categories);

        //Act
        var result = await Search.SearchAsync(Settings, Query() with { Mode = mode }, TestContext.Current.CancellationToken);

        //Assert
        var hit = Assert.Single(result.Value!);
        Assert.Equal(expected, hit.Kind);
    }

    [Theory]
    // MAM-33, ADR 0020 — Toloka files films beside series in TV/Anime and TV/Documentary: there a listing is
    // a series only when its title or its description names seasons or episodes.
    [InlineData("5070", "Akira (1988) BDRemux 4K 2160p DV HDR H.265 Ukr/Jap | sub Ukr/Jap", null, ReleaseKind.Movie)]
    [InlineData("5070", "Lanterns (S1) (2026) WEB-DL 1080p H.265 (S1)", null, ReleaseKind.Tv)]
    // A series without seasons names its episodes alone.
    [InlineData("5070", "Death Note (E1-31 of 37) (2006) BDRip 1080p", null, ReleaseKind.Tv)]
    // The marker in Toloka's own title, the description, where Prowlarr's title lost it.
    [InlineData("5080", "Lanterns (2026) WEB-DL 1080p", "Ліхтарі (Сезон 1) / Lanterns (Season 1) (2026) WEB-DL 1080p", ReleaseKind.Tv)]
    [InlineData("5080", "Akira (1988) BDRemux", "Акіра / Akira (1988) BDRemux", ReleaseKind.Movie)]
    [InlineData(
        "5080,100140",
        "Schumacher '94 - Die Geburt einer Legende (2026) WEB-DL 1080p Ukr/Ger | Sub Ukr/Eng/Ger",
        "Шумахер 1994: Народження легенди / Schumacher '94 - Die Geburt einer Legende (2026) WEB-DL 1080p Ukr/Ger | Sub Ukr/Eng/Ger",
        ReleaseKind.Movie)]
    // Any other TV category still says TV, and a movie category beside them a film.
    [InlineData("5000,5070", "Akira (1988) BDRemux", null, ReleaseKind.Tv)]
    [InlineData("2040,5070", "Akira (1988) BDRemux", null, ReleaseKind.Movie)]
    public async Task Search_tells_a_film_from_a_series_in_tv_anime_and_tv_documentary_by_a_season_or_episode_marker(
        string categories, string title, string? description, ReleaseKind expected)
    {
        //Arrange
        var extra = string.Concat(categories.Split(',').Select(category => $"<category>{category}</category>"))
            + (description is null ? "" : $"<description>{SecurityElement.Escape(description)}</description>");
        ArmItem(title, extra);

        //Act
        var result = await SearchAsync();

        //Assert
        var hit = Assert.Single(result.Value!);
        Assert.Equal(expected, hit.Kind);
    }

    [Theory]
    // MAM-33, ADR 0017 — the numbering follows the kind the marker gave the listing.
    [InlineData("Wild Yellowstone She Wolf (2013) HDTV", MediaIdentifierScheme.Tmdb)]
    [InlineData("Yellowstone (S1) (2009) BDRip 1080p H.265 Ukr/Eng | Sub Eng (S1)", MediaIdentifierScheme.TmdbTv)]
    public async Task Search_reads_a_tmdb_id_in_tv_documentary_as_a_films_or_a_shows_by_the_listings_kind(
        string title, MediaIdentifierScheme expected)
    {
        //Arrange
        ArmItem(title, "<category>5080</category><category>100140</category>" + Attribute("tmdbid", "73586"));

        //Act
        var result = await SearchAsync();

        //Assert
        var hit = Assert.Single(result.Value!);
        Assert.Equal([new MediaIdentifier(expected, "73586")], hit.Identifiers);
    }

    [Fact]
    public async Task Search_of_a_documentary_film_filed_in_tv_documentary_reads_every_release_as_a_movie()
    {
        //Arrange
        // MAM-33 — Toloka's real answer to the movie search, which now asks TV/Anime and TV/Documentary too:
        // the film once in Movies and four times in «Документальні фільми в HD», which Prowlarr files as
        // TV/Documentary. None names a season, so all five are the film.
        _harness.TorznabHttp.Handler = (_, _) =>
            VendorResponses.Text(TorznabSamples.FilmInTvCategorySearch(BaseUrl, ApiKey), HttpStatusCode.OK, "application/rss+xml");
        var query = Query() with
        {
            Mode = ReleaseSearchMode.Movie,
            Text = "Schumacher '94 - Die Geburt einer Legende 2026",
            CategoryIds = ["2000", "2020", "2040", "2070", "5070", "5080"],
        };

        //Act
        var result = await Search.SearchAsync(Settings, query, TestContext.Current.CancellationToken);

        //Assert
        var hits = result.Value!;
        Assert.Equal(TorznabSamples.FilmInTvCategoryItemCount, hits.Count);
        Assert.Equal(
            TorznabSamples.FilmInTvCategoryDocumentaryItemCount,
            hits.Count(hit => hit.CategoryIds.SequenceEqual(["5080", "100140"])));
        Assert.All(hits, hit => Assert.Equal(ReleaseKind.Movie, hit.Kind));
        Assert.All(hits, hit => Assert.StartsWith("Шумахер 1994: Народження легенди / Schumacher '94", hit.Description, StringComparison.Ordinal));
    }

    [Theory]
    // MAM-32 — a source whose TV search takes no text gets the title as a general search in its TV
    // categories alone (ReleaseSearchPlanning.ForTv): what that lists in no category is TV.
    [InlineData(ReleaseSearchMode.General, "5000", ReleaseKind.Tv)]
    [InlineData(ReleaseSearchMode.General, "5000,5040,5070", ReleaseKind.Tv)]
    // MAM-33 — TV/Documentary too: it holds films, but nothing in a listing in no category says it is one.
    [InlineData(ReleaseSearchMode.General, "5000,5080", ReleaseKind.Tv)]
    // Any other category asked beside them may be what the listing is in, a gateway's own section too.
    [InlineData(ReleaseSearchMode.General, "2000,5000", ReleaseKind.Movie)]
    [InlineData(ReleaseSearchMode.General, "5000,100125", ReleaseKind.Movie)]
    [InlineData(ReleaseSearchMode.General, "2000", ReleaseKind.Movie)]
    [InlineData(ReleaseSearchMode.General, "", ReleaseKind.Movie)]
    // A TV or a movie search says what it asked for, whatever its categories.
    [InlineData(ReleaseSearchMode.Tv, "2000,5000", ReleaseKind.Tv)]
    [InlineData(ReleaseSearchMode.Movie, "5000", ReleaseKind.Movie)]
    public async Task Search_gives_a_listing_in_no_category_the_kind_the_query_asked_for(
        ReleaseSearchMode mode, string askedCategoryIds, ReleaseKind expected)
    {
        //Arrange
        ArmItem("");
        var query = Query() with { Mode = mode, CategoryIds = askedCategoryIds.Split(',', StringSplitOptions.RemoveEmptyEntries) };

        //Act
        var result = await Search.SearchAsync(Settings, query, TestContext.Current.CancellationToken);

        //Assert
        var hit = Assert.Single(result.Value!);
        Assert.Empty(hit.CategoryIds);
        Assert.Equal(expected, hit.Kind);
    }

    [Fact]
    public async Task Search_in_tv_categories_alone_reads_a_tmdb_id_of_a_listing_in_no_category_as_a_shows()
    {
        //Arrange
        // ADR 0017 — the listing is TV, so its tmdbid is in TMDb's numbering of shows.
        ArmItem(Attribute("tmdbid", "73586"));
        var query = Query() with { CategoryIds = ["5000", "5070"] };

        //Act
        var result = await Search.SearchAsync(Settings, query, TestContext.Current.CancellationToken);

        //Assert
        var hit = Assert.Single(result.Value!);
        Assert.Equal(ReleaseKind.Tv, hit.Kind);
        Assert.Equal([new MediaIdentifier(MediaIdentifierScheme.TmdbTv, "73586")], hit.Identifiers);
    }

    [Fact]
    public async Task Search_of_the_toloka_sample_reads_no_ids()
    {
        //Arrange
        // Toloka lists none (VendorSamples/Torznab), so its releases match the library by title and year.
        _harness.TorznabHttp.Handler = (_, _) => VendorResponses.Text(TorznabSamples.Search(BaseUrl, ApiKey), HttpStatusCode.OK, "application/rss+xml");

        //Act
        var result = await SearchAsync();

        //Assert
        Assert.Equal(TorznabSamples.ItemCount, result.Value!.Count);
        Assert.All(result.Value!, hit => Assert.Empty(hit.Identifiers));
    }

    private void ArmItem(string attributes) => ArmItem("Interstellar (2014)", attributes);

    private void ArmItem(string title, string attributes)
    {
        var feed = TorznabSamples.Feed(TorznabSamples.Item(title, DownloadLink, extra: attributes));
        _harness.TorznabHttp.Handler = (_, _) => VendorResponses.Text(feed, HttpStatusCode.OK, "application/rss+xml");
    }

    private Task<IntegrationCallResult<IReadOnlyList<ReleaseSearchHit>>> SearchAsync() =>
        Search.SearchAsync(Settings, Query(), TestContext.Current.CancellationToken);

    private static string Attribute(string name, string value) => $"""<torznab:attr name="{name}" value="{value}" />""";

    private static ReleaseSearchQuery Query() => new(ReleaseSearchMode.General, "Interstellar", null, [], [], 100);
}
