using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Tests.Releases;

// MAM-32 — the release search asks every source for TV beside movies, over real HTTP against the real
// Torznab adapter with the network faked by the real Toloka answers (its TV search for "Yellowstone"):
// what it sends (the movie tiers, then the TV tiers; a season to the TV search alone), what it lists (the
// movies, then the TV releases, a listing both find once), what a failing TV query costs (the whole
// source), and what it keeps (each release's kind, categories and ids) and answers (each release's kind).
// MAM-33 — the movie search asks TV/Anime and TV/Documentary too, and a release there that names no season
// or episode is a film (ADR 0020).
public sealed class ReleaseSearchTvHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/releases/search";
    private const string FeedAddress = "http://prowlarr.test:9696/1/api";
    private const string ApiKey = "planted-torznab-key-0001";

    // The categories of Toloka's caps sample each family searches in. MAM-33 — TV/Anime and TV/Documentary
    // in both: Toloka files films beside series there (ADR 0020).
    private const string MovieCategories = "cat=2000,2020,2040,2070,5070,5080";
    private const string TvCategories = "cat=5000,5040,5050,5060,5070,5080";

    private const string SeasonSentence = "“Toloka” offers no search this app can use for a season: no TV category, or neither a search by text nor a TV search by title or by an id that was asked for.";

    private static readonly int SampleMovieCount = TorznabSamples.ItemCount - TorznabSamples.NonMovieItemCount;

    // The TV search sample's releases, in its order.
    private static readonly string[] YellowstoneTitles =
    [
        "Yellowstone (S1-5) (2018-2024) WEB-DL-AVC Ukr/Eng | Sub Eng",
        "Yellowstone (S5) (2022-2024) WEB-DL 1080p (S5)",
        "Yellowstone (S5) (2022-2024) WEB-DL 1080p Ukr/Eng | Sub Eng (S5)",
        "Yellowstone (S4) (2021) WEB-DL 1080p Ukr/Eng | Sub Eng (S4)",
        "Yellowstone (S1-3) (2018-2020) WEBRip 1080p Ukr/Eng",
        "Yellowstone (S1) (2009) BDRip 1080p H.265 Ukr/Eng | Sub Eng (S1)",
        "World Natural Heritage USA: Yellowstone National Park (2013) IPTVRemux",
        "Wild Yellowstone She Wolf (2013) HDTV",
        "Wild Yellowstone She Wolf (2013) HDTV 1080i Ukr/Eng",
    ];

    /// <summary>
    /// The kinds of <see cref="YellowstoneTitles"/>: MAM-33 — the last three, in TV/Documentary and naming no
    /// season, are films; the BBC's "Yellowstone (S1) (2009)" beside them a series (ADR 0020).
    /// </summary>
    private static readonly ReleaseKind[] YellowstoneKinds =
    [
        .. Enumerable.Repeat(ReleaseKind.Tv, TorznabSamples.TvItemCount - TorznabSamples.TvFilmItemCount),
        .. Enumerable.Repeat(ReleaseKind.Movie, TorznabSamples.TvFilmItemCount),
    ];

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private IIntegrationConnectionSeeder _integrations = null!;
    private AuthorizedUser _owner = null!;
    private AuthorizedUser _member = null!;
    private FakeTorznabServer _toloka = null!;

    public ReleaseSearchTvHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _integrations = new IntegrationConnectionSeeder(_factory.Scope);
        var users = new UserSeeder(_factory.Scope);
        _owner = await users.CreateAuthorizedOwner("owner");
        _member = await users.CreateAuthorized("member");
        _toloka = new FakeTorznabServer(FeedAddress, ApiKey) { TvSearchXml = TorznabSamples.TvSearch(FeedAddress, ApiKey) };
        _toloka.Arm(_factory.TorznabHttp);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Search_lists_the_movie_releases_then_the_tv_releases_and_stores_each_with_its_kind_and_categories()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _integrations.Create("torznab", "Toloka", FeedAddress, apiKey: ApiKey);

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _member.AccessToken, new ReleaseSearchRequest("Yellowstone"));

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var source = Assert.Single((await ReadAsync(response, cancellationToken)).Sources);
        Assert.Equal(IntegrationCallStatus.Succeeded, source.Status);
        // The movie search's answer first, as the movie tiers found it, then the TV search's.
        Assert.Equal(SampleMovieCount + TorznabSamples.TvItemCount, source.Releases.Count);
        Assert.Equal(YellowstoneTitles, source.Releases.Skip(SampleMovieCount).Select(release => release.Title));
        Assert.Equal([.. Enumerable.Repeat(ReleaseKind.Movie, SampleMovieCount), .. YellowstoneKinds], source.Releases.Select(release => release.Kind));
        Assert.Equal(
            [
                $"{FeedAddress}?t=caps&apikey={ApiKey}",
                $"{FeedAddress}?t=movie&apikey={ApiKey}&q=Yellowstone&{MovieCategories}&limit=100",
                $"{FeedAddress}?t=tvsearch&apikey={ApiKey}&q=Yellowstone&{TvCategories}&limit=100",
            ],
            _factory.TorznabHttp.Requests.Select(request => request.Uri.AbsoluteUri));
        await _factory.WithDbAsync(async db =>
        {
            var stored = await db.Releases.AsNoTracking().OrderBy(release => release.Position).ToListAsync(cancellationToken);
            Assert.Equal(source.Releases.Select(release => release.Id), stored.Select(release => release.Id));
            Assert.Equal([.. Enumerable.Repeat(ReleaseKind.Movie, SampleMovieCount), .. YellowstoneKinds], stored.Select(release => release.Kind));
            // The listing's categories as it lists them: the standard one, then Toloka's own section.
            Assert.Equal("2040,100096", stored[0].CategoryIds);
            Assert.Equal(
                ["5000,100032", "5040,100173", "5040,100173", "5040,100173", "5040,100173", "5080,100140", "5080,100230", "5080,100228", "5080,100140"],
                stored.Skip(SampleMovieCount).Select(release => release.CategoryIds));
        });
    }

    [Fact]
    public async Task Search_answers_each_releases_kind_by_name_after_its_title()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _integrations.Create("torznab", "Toloka", FeedAddress, apiKey: ApiKey);

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _member.AccessToken, new ReleaseSearchRequest("Yellowstone"));

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync(cancellationToken))!.AsObject();
        var releases = body["sources"]![0]!["releases"]!.AsArray();
        // MAM-33 — the description between them.
        Assert.Equal(
            ["id", "title", "description", "kind", "sizeBytes", "seeders", "peers", "publishedUtc", "sourcePageUrl", "categories", "payloadKind", "claimed"],
            releases[0]!.AsObject().Select(property => property.Key));
        Assert.Equal(
            [.. Enumerable.Repeat("Movie", SampleMovieCount), .. YellowstoneKinds.Select(kind => kind.ToString())],
            releases.Select(release => (string)release!["kind"]!));
        await _factory.WithDbAsync(async db =>
        {
            var stored = await db.Releases.AsNoTracking().ToDictionaryAsync(release => release.Id, release => release.Kind, cancellationToken);
            Assert.All(releases, release => Assert.Equal((string)release!["kind"]!, stored[(Guid)release!["id"]!].ToString()));
        });
    }

    [Fact]
    public async Task Search_of_a_source_whose_tv_search_takes_no_title_answers_and_stores_what_its_tv_categories_list_in_no_category_as_tv()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _integrations.Create("torznab", "Toloka", FeedAddress, apiKey: ApiKey);
        // Its TV search takes a TVDB id alone, so the TV title tier is a general search in the TV categories
        // (ReleaseSearchPlanning.ForTv), and its movie tier one in the movie category.
        _toloka.CapsXml =
            """
            <caps>
              <searching>
                <search available="yes" supportedParams="q" />
                <movie-search available="no" />
                <tv-search available="yes" supportedParams="tvdbid" />
              </searching>
              <categories>
                <category id="2000" name="Movies" />
                <category id="5000" name="TV"><subcat id="5040" name="TV/HD" /></category>
              </categories>
            </caps>
            """;
        // Neither answer files its listings in a category.
        _toloka.SearchXml = TorznabSamples.Feed(Item("Yellowstone (1936) DVDRip", 1, "t1", ""));
        var tvFeed = TorznabSamples.Feed(
            Item("Yellowstone (S1-5) (2018-2024) WEB-DL", 2, "t2", ""), Item("Yellowstone (S5) (2022-2024) 1080p", 3, "t3", ""));
        _toloka.Intercept = request => request.RequestUri!.Query.Contains("cat=5000,5040", StringComparison.Ordinal)
            ? VendorResponses.Text(tvFeed, HttpStatusCode.OK, "application/rss+xml")
            : null;

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _member.AccessToken, new ReleaseSearchRequest("Yellowstone"));

        //Assert
        // What the movie category's search lists in none stays a movie; what the TV categories' lists is TV.
        Assert.Equal(
            [
                $"{FeedAddress}?t=caps&apikey={ApiKey}",
                $"{FeedAddress}?t=search&apikey={ApiKey}&q=Yellowstone&cat=2000&limit=100",
                $"{FeedAddress}?t=search&apikey={ApiKey}&q=Yellowstone&cat=5000,5040&limit=100",
            ],
            _factory.TorznabHttp.Requests.Select(request => request.Uri.AbsoluteUri));
        var source = Assert.Single((await ReadAsync(response, cancellationToken)).Sources);
        Assert.Equal(
            [
                ("Yellowstone (1936) DVDRip", ReleaseKind.Movie),
                ("Yellowstone (S1-5) (2018-2024) WEB-DL", ReleaseKind.Tv),
                ("Yellowstone (S5) (2022-2024) 1080p", ReleaseKind.Tv),
            ],
            source.Releases.Select(release => (release.Title, release.Kind)));
        Assert.All(source.Releases, release => Assert.Empty(release.Categories));
        await _factory.WithDbAsync(async db =>
            Assert.Equal(
                [(ReleaseKind.Movie, (string?)null), (ReleaseKind.Tv, null), (ReleaseKind.Tv, null)],
                (await db.Releases.AsNoTracking().OrderBy(release => release.Position).ToListAsync(cancellationToken))
                    .Select(release => (release.Kind, release.CategoryIds))));
    }

    [Theory]
    [InlineData("1", "1")]
    [InlineData(" 01 ", "1")]
    // The specials.
    [InlineData("0", "0")]
    public async Task Search_of_a_season_sends_the_tv_search_alone_with_the_season(string season, string sent)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _integrations.Create("torznab", "Toloka", FeedAddress, apiKey: ApiKey);
        _toloka.TvSearchXml = TorznabSamples.TvSeasonSearch(FeedAddress, ApiKey);

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _member.AccessToken, new ReleaseSearchRequest("Yellowstone", "2018", Season: season));

        //Assert
        // No movie search: a season is a show's. And no year: a TV search never sends one (ADR 0020).
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            [
                $"{FeedAddress}?t=caps&apikey={ApiKey}",
                $"{FeedAddress}?t=tvsearch&apikey={ApiKey}&q=Yellowstone&season={sent}&{TvCategories}&limit=100",
            ],
            _factory.TorznabHttp.Requests.Select(request => request.Uri.AbsoluteUri));
        var source = Assert.Single((await ReadAsync(response, cancellationToken)).Sources);
        Assert.Equal(YellowstoneTitles.Take(TorznabSamples.TvSeasonItemCount), source.Releases.Select(release => release.Title));
        await _factory.WithDbAsync(async db =>
            Assert.Equal(
                Enumerable.Repeat(ReleaseKind.Tv, TorznabSamples.TvSeasonItemCount),
                await db.Releases.AsNoTracking().OrderBy(release => release.Position).Select(release => release.Kind).ToListAsync(cancellationToken)));
    }

    [Theory]
    [InlineData("Yellowstone", null, null, "one", "“one” is not a season. Use a season number, such as 1, or 0 for the specials.")]
    [InlineData("Yellowstone", null, null, "-1", "“-1” is not a season. Use a season number, such as 1, or 0 for the specials.")]
    [InlineData("Yellowstone", null, null, "1.5", "“1.5” is not a season. Use a season number, such as 1, or 0 for the specials.")]
    [InlineData("Yellowstone", null, null, "10000", "“10000” is not a season. Use a season number, such as 1, or 0 for the specials.")]
    // The first value that is wrong answers: the season last.
    [InlineData(null, null, null, "one", "Enter the title to search for.")]
    [InlineData("Yellowstone", "20l8", null, "one", "“20l8” is not a year.")]
    [InlineData("Yellowstone", null, "tt4236770", "one", "“tt4236770” is not a TMDb id, which is a number such as 157336.")]
    public async Task Search_of_a_season_that_is_not_one_is_400_with_its_sentence_and_asks_no_source(
        string? text, string? year, string? tmdbId, string season, string expectedError)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _integrations.Create("torznab", "Toloka", FeedAddress, apiKey: ApiKey);

        //Act
        var response = await _factory.PostHttpRequest(
            BaseUrl, _member.AccessToken, new ReleaseSearchRequest(text, year, TmdbId: tmdbId, Season: season));

        //Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(expectedError, await response.ReadErrorAsync(cancellationToken));
        Assert.Empty(_factory.TorznabHttp.Requests);
        await _factory.WithDbAsync(async db => Assert.Equal(0, await db.Releases.CountAsync(cancellationToken)));
    }

    [Fact]
    public async Task Search_whose_tv_query_fails_fails_the_source_and_keeps_none_of_the_movies_it_found()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _integrations.Create("torznab", "Toloka", FeedAddress, apiKey: ApiKey);
        // Prowlarr's answer for an indexer it paused after failures (ADR 0004), to the TV search alone.
        _toloka.Intercept = request => request.RequestUri!.Query.Contains("t=tvsearch", StringComparison.Ordinal)
            ? VendorResponses.Text(
                """<error code="429" description="Indexer is disabled till 01/10/2026 14:05:00 due to recent failures." />""",
                HttpStatusCode.TooManyRequests,
                "application/xml")
            : null;

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _member.AccessToken, new ReleaseSearchRequest("Yellowstone"));

        //Assert
        // All or nothing: the movies alone would read as everything the source lists.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var source = Assert.Single((await ReadAsync(response, cancellationToken)).Sources);
        Assert.Equal(IntegrationCallStatus.Refused, source.Status);
        Assert.Empty(source.Releases);
        Assert.Contains(_factory.TorznabHttp.Requests, request => request.Uri.Query.Contains("t=movie", StringComparison.Ordinal));
        await _factory.WithDbAsync(async db => Assert.Equal(0, await db.Releases.CountAsync(cancellationToken)));
    }

    [Fact]
    public async Task Search_whose_tv_id_search_finds_nothing_searches_the_tv_title_and_never_sends_a_movies_tmdb_id()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _integrations.Create("torznab", "Toloka", FeedAddress, apiKey: ApiKey);
        _toloka.CapsXml = CapsTakingIds;
        // No movie of the name, and the TV search by the id finds nothing either.
        _toloka.SearchXml = TorznabSamples.Feed();
        _toloka.Intercept = request => request.RequestUri!.Query.Contains("t=tvsearch", StringComparison.Ordinal)
                && request.RequestUri.Query.Contains("imdbid=", StringComparison.Ordinal)
            ? VendorResponses.Text(TorznabSamples.Feed(), HttpStatusCode.OK, "application/rss+xml")
            : null;

        //Act
        var response = await _factory.PostHttpRequest(
            BaseUrl, _member.AccessToken, new ReleaseSearchRequest("Yellowstone", ImdbId: "tt4236770", TmdbId: "73586"));

        //Assert
        // The TMDb id the form takes is a movie's (ADR 0017): it goes to the movie search, never to the TV
        // search, which would read it as another show's.
        Assert.Equal(
            [
                $"{FeedAddress}?t=caps&apikey={ApiKey}",
                $"{FeedAddress}?t=movie&apikey={ApiKey}&imdbid=4236770&cat=2000&limit=100",
                $"{FeedAddress}?t=movie&apikey={ApiKey}&q=Yellowstone&cat=2000&limit=100",
                $"{FeedAddress}?t=tvsearch&apikey={ApiKey}&imdbid=4236770&cat=5000,5040&limit=100",
                $"{FeedAddress}?t=tvsearch&apikey={ApiKey}&q=Yellowstone&cat=5000,5040&limit=100",
            ],
            _factory.TorznabHttp.Requests.Select(request => request.Uri.AbsoluteUri));
        Assert.Equal(YellowstoneTitles, Assert.Single((await ReadAsync(response, cancellationToken)).Sources).Releases.Select(release => release.Title));
        await _factory.WithDbAsync(async db =>
            Assert.Equal(
                YellowstoneKinds,
                await db.Releases.AsNoTracking().OrderBy(release => release.Position).Select(release => release.Kind).ToListAsync(cancellationToken)));
    }

    [Fact]
    public async Task Search_of_a_movies_tmdb_id_alone_sends_it_to_the_movie_search_and_the_title_alone_to_the_tv_search()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _integrations.Create("torznab", "Toloka", FeedAddress, apiKey: ApiKey);
        // Both searches take a TMDb id: only the movie search may be sent the form's, a movie's (ADR 0017).
        _toloka.CapsXml = CapsTakingIds
            .Replace("supportedParams=\"q,imdbid\"", "supportedParams=\"q,tmdbid\"", StringComparison.Ordinal);

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _member.AccessToken, new ReleaseSearchRequest("Yellowstone", TmdbId: "73586"));

        //Assert
        // No IMDb id before it to pick instead: the TV search is sent the title, never the number, which
        // TMDb gives another show too.
        Assert.Equal(
            [
                $"{FeedAddress}?t=caps&apikey={ApiKey}",
                $"{FeedAddress}?t=movie&apikey={ApiKey}&tmdbid=73586&cat=2000&limit=100",
                $"{FeedAddress}?t=tvsearch&apikey={ApiKey}&q=Yellowstone&cat=5000,5040&limit=100",
            ],
            _factory.TorznabHttp.Requests.Select(request => request.Uri.AbsoluteUri));
        var source = Assert.Single((await ReadAsync(response, cancellationToken)).Sources);
        Assert.Equal(SampleMovieCount + TorznabSamples.TvItemCount, source.Releases.Count);
        await _factory.WithDbAsync(async db =>
            Assert.Equal(
                TorznabSamples.TvItemCount - TorznabSamples.TvFilmItemCount,
                await db.Releases.CountAsync(release => release.Kind == ReleaseKind.Tv, cancellationToken)));
    }

    [Fact]
    public async Task Search_of_a_season_by_an_imdb_id_sends_both_to_the_tv_search_and_stops_at_what_it_lists()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _integrations.Create("torznab", "Toloka", FeedAddress, apiKey: ApiKey);
        _toloka.CapsXml = CapsTakingIds;
        _toloka.TvSearchXml = TorznabSamples.TvSeasonSearch(FeedAddress, ApiKey);

        //Act
        var response = await _factory.PostHttpRequest(
            BaseUrl, _member.AccessToken, new ReleaseSearchRequest("Yellowstone", ImdbId: "tt4236770", Season: "2"));

        //Assert
        // The id with the season, and no title tier once it lists releases.
        Assert.Equal(
            [
                $"{FeedAddress}?t=caps&apikey={ApiKey}",
                $"{FeedAddress}?t=tvsearch&apikey={ApiKey}&imdbid=4236770&season=2&cat=5000,5040&limit=100",
            ],
            _factory.TorznabHttp.Requests.Select(request => request.Uri.AbsoluteUri));
        var source = Assert.Single((await ReadAsync(response, cancellationToken)).Sources);
        Assert.Equal(YellowstoneTitles.Take(TorznabSamples.TvSeasonItemCount), source.Releases.Select(release => release.Title));
        await _factory.WithDbAsync(async db =>
            Assert.Equal(
                Enumerable.Repeat(ReleaseKind.Tv, TorznabSamples.TvSeasonItemCount),
                await db.Releases.AsNoTracking().OrderBy(release => release.Position).Select(release => release.Kind).ToListAsync(cancellationToken)));
    }

    [Fact]
    public async Task Search_of_a_source_whose_tv_search_is_its_only_one_lists_its_tv_releases()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _integrations.Create("torznab", "Toloka", FeedAddress, apiKey: ApiKey);
        _toloka.CapsXml =
            """
            <caps>
              <searching>
                <search available="no" />
                <movie-search available="no" />
                <tv-search available="yes" supportedParams="q" />
              </searching>
              <categories>
                <category id="2000" name="Movies" />
                <category id="5000" name="TV" />
              </categories>
            </caps>
            """;

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _member.AccessToken, new ReleaseSearchRequest("Yellowstone"));

        //Assert
        // No movie query to send is no reason to leave the source alone while its TV search has one.
        var source = Assert.Single((await ReadAsync(response, cancellationToken)).Sources);
        Assert.Equal(IntegrationCallStatus.Succeeded, source.Status);
        Assert.Null(source.NotSearchedReason);
        Assert.Equal(YellowstoneTitles, source.Releases.Select(release => release.Title));
        Assert.Equal(
            [
                $"{FeedAddress}?t=caps&apikey={ApiKey}",
                $"{FeedAddress}?t=tvsearch&apikey={ApiKey}&q=Yellowstone&cat=5000&limit=100",
            ],
            _factory.TorznabHttp.Requests.Select(request => request.Uri.AbsoluteUri));
        await _factory.WithDbAsync(async db =>
            Assert.Equal(
                YellowstoneKinds,
                await db.Releases.AsNoTracking().OrderBy(release => release.Position).Select(release => release.Kind).ToListAsync(cancellationToken)));
    }

    [Fact]
    public async Task Search_lists_a_release_both_searches_find_once_as_the_movie_search_found_it()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _integrations.Create("torznab", "Toloka", FeedAddress, apiKey: ApiKey);
        // Filed under a movie and a TV category both, so both searches list it.
        var both = Item("Cosmos: A Spacetime Odyssey (2014) 1080p", 1, "t1", "<category>2000</category><category>5000</category>");
        _toloka.SearchXml = TorznabSamples.Feed(both, Item("Cosmos (2014) 720p", 2, null, "<category>2000</category>"));
        _toloka.TvSearchXml = TorznabSamples.Feed(
            both,
            // No id: nothing says it is the movie search's listing again.
            Item("Cosmos (2014) 720p", 2, null, "<category>5000</category>"),
            Item("Cosmos: Possible Worlds (S2) (2020) 1080p", 3, "t3", "<category>5040</category>"));

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _member.AccessToken, new ReleaseSearchRequest("Cosmos"));

        //Assert
        Assert.Equal(
            [
                "Cosmos: A Spacetime Odyssey (2014) 1080p",
                "Cosmos (2014) 720p",
                "Cosmos (2014) 720p",
                "Cosmos: Possible Worlds (S2) (2020) 1080p",
            ],
            Assert.Single((await ReadAsync(response, cancellationToken)).Sources).Releases.Select(release => release.Title));
        await _factory.WithDbAsync(async db =>
            Assert.Equal(
                [
                    ((int?)0, "t1", ReleaseKind.Tv, "2000,5000"),
                    (1, null, ReleaseKind.Movie, "2000"),
                    (2, null, ReleaseKind.Tv, "5000"),
                    (3, (string?)"t3", ReleaseKind.Tv, (string?)"5040"),
                ],
                (await db.Releases.AsNoTracking().OrderBy(release => release.Position).ToListAsync(cancellationToken))
                    .Select(release => (release.Position, release.SourceItemId, release.Kind, release.CategoryIds))));
    }

    [Fact]
    public async Task Search_stores_a_tv_releases_ids_its_tmdb_id_being_a_shows()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _integrations.Create("torznab", "Toloka", FeedAddress, apiKey: ApiKey);
        _toloka.SearchXml = TorznabSamples.Feed(
            Item("Interstellar (2014) 1080p", 1, "t1", """<category>2040</category><torznab:attr name="imdbid" value="tt0816692" /><torznab:attr name="tmdbid" value="157336" />"""));
        _toloka.TvSearchXml = TorznabSamples.Feed(
            Item(
                "Yellowstone (S1) (2018) 1080p",
                2,
                "t2",
                """<category>5040</category><category>100173</category><torznab:attr name="imdbid" value="tt4236770" /><torznab:attr name="tmdbid" value="73586" /><torznab:attr name="tvdbid" value="341164" />"""));

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _member.AccessToken, new ReleaseSearchRequest("Yellowstone"));

        //Assert
        // One TMDb column, its numbering told by the kind (ADR 0017): 73586 is the show's.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await _factory.WithDbAsync(async db =>
            Assert.Equal(
                [
                    (ReleaseKind.Movie, "tt0816692", "157336", (string?)null, "2040"),
                    (ReleaseKind.Tv, "tt4236770", "73586", "341164", "5040,100173"),
                ],
                (await db.Releases.AsNoTracking().OrderBy(release => release.Position).ToListAsync(cancellationToken))
                    .Select(release => (release.Kind, release.ImdbId, release.TmdbId, release.TvdbId, release.CategoryIds))));
    }

    [Fact]
    public async Task Search_keeps_the_categories_that_fit_their_column_each_whole()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _integrations.Create("torznab", "Toloka", FeedAddress, apiKey: ApiKey);
        var sections = Enumerable.Range(100000, 100).Select(id => $"{id}").ToList();
        _toloka.TvSearchXml = TorznabSamples.Feed(
            Item("Yellowstone (S1) (2018) 1080p", 1, "t1", "<category>5040</category>" + string.Concat(sections.Select(id => $"<category>{id}</category>"))));

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _member.AccessToken, new ReleaseSearchRequest("Yellowstone"));

        //Assert
        // 5040 and 72 of the six-digit ids are 508 characters; a 73rd would pass the column's 512.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await _factory.WithDbAsync(async db =>
        {
            var stored = Assert.Single(await db.Releases.AsNoTracking().Where(release => release.Kind == ReleaseKind.Tv).ToListAsync(cancellationToken));
            Assert.Equal(string.Join(',', ["5040", .. sections.Take(72)]), stored.CategoryIds);
            Assert.True(stored.CategoryIds!.Length <= ReleaseEntity.CategoryIdsMaximumLength);
        });
    }

    [Fact]
    public async Task Search_of_a_season_in_a_source_without_a_tv_category_says_so_without_searching()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _integrations.Create("torznab", "Toloka", FeedAddress, apiKey: ApiKey);
        _toloka.CapsXml = CapsTakingIds.Replace("""<category id="5000" name="TV"><subcat id="5040" name="TV/HD" /></category>""", "", StringComparison.Ordinal);

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _owner.AccessToken, new ReleaseSearchRequest("Yellowstone", Season: "1"));

        //Assert
        // Its movie search is never asked for a season, and with no TV category it has no TV search.
        var source = Assert.Single((await ReadAsync(response, cancellationToken)).Sources);
        Assert.Null(source.Status);
        Assert.Equal(ReleaseSourceNotSearchedReason.NoUsableSearch, source.NotSearchedReason);
        Assert.Equal(SeasonSentence, source.Error);
        Assert.Equal([$"{FeedAddress}?t=caps&apikey={ApiKey}"], _factory.TorznabHttp.Requests.Select(request => request.Uri.AbsoluteUri));
        await _factory.WithDbAsync(async db => Assert.Equal(0, await db.Releases.CountAsync(cancellationToken)));
    }

    /// <summary>A release on this feed, its download link numbered <paramref name="file"/>.</summary>
    private static string Item(string title, int file, string? guid, string extra) =>
        TorznabSamples.Item(title, $"{TorznabSamples.DownloadBaseFor(FeedAddress)}download?file={file}", guid, extra: extra);

    // A feed whose movie search takes an IMDb id and whose TV search takes a show's ids and a season.
    private static string CapsTakingIds =>
        """
        <caps>
          <searching>
            <search available="yes" supportedParams="q" />
            <movie-search available="yes" supportedParams="q,imdbid" />
            <tv-search available="yes" supportedParams="q,season,imdbid,tvdbid,tmdbid" />
          </searching>
          <categories>
            <category id="2000" name="Movies" />
            <category id="5000" name="TV"><subcat id="5040" name="TV/HD" /></category>
          </categories>
        </caps>
        """;

    private static async Task<ReleaseSearchResponse> ReadAsync(HttpResponseMessage response, CancellationToken cancellationToken) =>
        (await response.Content.ReadFromJsonAsync<ReleaseSearchResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
}
