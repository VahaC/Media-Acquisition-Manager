using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Tests.Search;

// MAM-32, ADR 0020 — a show's search, its releases matched to the library by kind: a TV release to series
// only — by a show's ids (IMDb, TMDb TV, TVDB), then its title with a year its seasons allow (with season 1
// its first year exactly, with later seasons only any year up to it, with none its span), then its
// title — and a movie release to movies only, as before; a film and a show of one title never match each
// other's releases, and neither do a film's and a show's TMDb numbers. Each TV match says how many episodes
// of the series the library holds among the seasons or episodes its title claims. Read against the real
// Toloka TV answers and listings of the test's own for the ids. The release list filters by kind, and the
// search offers the kinds its sources listed.
public sealed class SearchReleasesTvMatchingHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/searches";
    private const string TolokaAddress = "http://prowlarr.test:9696/1/api";
    private const string ApiKey = "planted-torznab-key-0001";
    private const string JellyfinApiKey = "0123456789abcdef0123456789abcdef";

    private const string FilmItemId = "1f1f1f1f1f1f1f1f1f1f1f1f1f1f1f1f";

    private const string ShowImdbId = "tt4236770";
    private const string ShowTvdbId = "341164";

    /// <summary>The show's TMDb id, which the film holds as well in TMDb's movie numbering: the same number,
    /// two works (ADR 0017).</summary>
    private const string SharedTmdbNumber = "73586";

    private const string FilmRelease = "Yellowstone (2009) BDRip 1080p";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private AuthorizedUser _member = null!;
    private FakeTorznabServer _toloka = null!;

    public SearchReleasesTvMatchingHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    /// <summary>
    /// Each release of the real Toloka TV answer, and a film's release, with the works it matches as
    /// "work evidence held N": S is the series Yellowstone (2018, seasons 1, 2 and 5 held: two episodes, one,
    /// two), M the documentary film "Yellowstone" (2009).
    /// </summary>
    public static TheoryData<string, string[]> Matches => new()
    {
        // The film's release: the film alone, never the show of its title.
        { FilmRelease, ["M TitleAndYear"] },
        // With season 1, the first year is the show's, 2018: every episode of the five seasons.
        { "Yellowstone (S1-5) (2018-2024) WEB-DL-AVC Ukr/Eng | Sub Eng", ["S TitleAndYear held 5"] },
        { "Yellowstone (S1-3) (2018-2020) WEBRip 1080p Ukr/Eng", ["S TitleAndYear held 3"] },
        // A later season's: the show began in its first year or before, as 2018 is; and what the library
        // holds of that season.
        { "Yellowstone (S5) (2022-2024) WEB-DL 1080p (S5)", ["S TitleAndYear held 2"] },
        { "Yellowstone (S5) (2022-2024) WEB-DL 1080p Ukr/Eng | Sub Eng (S5)", ["S TitleAndYear held 2"] },
        { "Yellowstone (S4) (2021) WEB-DL 1080p Ukr/Eng | Sub Eng (S4)", ["S TitleAndYear held 0"] },
        // The BBC documentary of 2009: a TV release, so never the film of 2009, and with season 1 a show
        // begun in 2009 — only a title match to the show of 2018.
        { "Yellowstone (S1) (2009) BDRip 1080p H.265 Ukr/Eng | Sub Eng (S1)", ["S TitleOnly held 2"] },
        { "World Natural Heritage USA: Yellowstone National Park (2013) IPTVRemux", [] },
        { "Wild Yellowstone She Wolf (2013) HDTV", [] },
        { "Wild Yellowstone She Wolf (2013) HDTV 1080i Ukr/Eng", [] },
    };

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        var users = new UserSeeder(_factory.Scope);
        var owner = await users.CreateAuthorizedOwner("owner");
        _member = await users.CreateAuthorized("member");

        var jellyfin = new FakeJellyfinServer { ApiKey = JellyfinApiKey };
        jellyfin.Movies.Clear();
        jellyfin.Movies.Add(JellyfinSamples.MovieWith(
            FilmItemId, "Yellowstone", null, 2009, "/media/documentary/Yellowstone (2009).mkv", "tt1426371", SharedTmdbNumber));
        jellyfin.Series.Add(JellyfinSamples.SeriesWith(ItemId('a'), "Yellowstone", 2018, "/media/tv-shows/Yellowstone", ShowImdbId, SharedTmdbNumber, ShowTvdbId));
        jellyfin.Episodes.Add(Episode('b', "Daybreak", 'a', 1, 1, "tt7529124"));
        jellyfin.Episodes.Add(Episode('c', "Kill the Messenger", 'a', 1, 2, "tt7529126"));
        jellyfin.Episodes.Add(Episode('d', "A Thundering", 'a', 2, 1, "tt8000001"));
        jellyfin.Episodes.Add(Episode('e', "One Hundred Years Is Nothing", 'a', 5, 1, "tt8000051"));
        jellyfin.Episodes.Add(Episode('f', "The Sting of Wisdom", 'a', 5, 2, "tt8000052"));
        // The cartoon series of the real cartoon answer: seasons 1 to 3, the halves of S03E09 one episode.
        jellyfin.Series.Add(JellyfinSamples.SeriesWith(ItemId('g'), "Mickey Mouse Clubhouse", 2006, "/media/cartoons/Mickey Mouse Clubhouse"));
        jellyfin.Episodes.Add(Episode('h', "Daisy Bo-Peep", 'g', 1, 1, "tt0000301"));
        jellyfin.Episodes.Add(Episode('i', "Goofy's Bird", 'g', 2, 1, "tt0000302"));
        jellyfin.Episodes.Add(Episode('j', "Mickey's Big Band Concert", 'g', 2, 2, "tt0000303"));
        jellyfin.Episodes.Add(Episode('k', "Road Rally", 'g', 3, 9, "tt0000304"));
        jellyfin.Episodes.Add(Episode('l', "Road Rally", 'g', 3, 9, "tt0000304"));
        jellyfin.Arm(_factory.JellyfinHttp);
        var integrations = new IntegrationConnectionSeeder(_factory.Scope);
        await new LibrarySnapshotSeeder(_factory.Scope).Sync(await integrations.Create("jellyfin", "Living room", apiKey: JellyfinApiKey), owner.User);

        // The film's release through the movie search, the show's through the TV search.
        _toloka = new FakeTorznabServer(TolokaAddress, ApiKey)
        {
            SearchXml = TorznabSamples.Feed(Item(FilmRelease, "t1", "2000")),
            TvSearchXml = TorznabSamples.TvSearch(TolokaAddress, ApiKey),
        };
        _toloka.Arm(_factory.TorznabHttp);
        await integrations.Create("torznab", "Toloka", TolokaAddress, apiKey: ApiKey);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Theory]
    [MemberData(nameof(Matches))]
    public async Task Releases_matches_a_tv_release_to_series_and_a_movie_release_to_movies_with_the_episodes_held(string title, string[] expected)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var search = await CreateSearchAsync("Yellowstone", cancellationToken);

        //Act
        var releases = await ReleasesAsync(search, "", cancellationToken);

        //Assert
        // The search lists the film and the show, each once, by the title alone: no year was asked for.
        var works = WorkNames(search);
        Assert.Equal(["M TitleOnly", "S TitleOnly"], search.Library.Select(match => $"{works[match.Work.Id]} {match.Evidence}").Order());
        var release = Assert.Single(releases, release => release.Title == title);
        Assert.Equal(expected, release.LibraryMatches.Select(match => Describe(works, match)));
        Assert.All(release.LibraryMatches, match => Assert.Empty(match.ConflictingSchemes));
    }

    [Fact]
    public async Task Releases_matches_the_releases_of_the_real_cartoon_answer_counting_an_episode_of_two_items_once()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        _toloka.SearchXml = TorznabSamples.Feed();
        _toloka.TvSearchXml = TorznabSamples.TvCartoonSearch(TolokaAddress, ApiKey);
        var search = await CreateSearchAsync("Mickey Mouse Clubhouse", cancellationToken);

        //Act
        var releases = await ReleasesAsync(search, "", cancellationToken);

        //Assert
        var works = WorkNames(search);
        Assert.Equal(TorznabSamples.TvCartoonItemCount, releases.Count);
        Assert.Equal(
            [
                "Mickey Mouse Clubhouse (S1) (2006-2007) Satrip (S1): C TitleAndYear held 1",
                // The four seasons' span holds 2006: all four episodes, S03E09 once.
                "Mickey Mouse Clubhouse (S1-4) (2006-2016) WEB-DL 1080p/DVDRip-AVC Ukr/Eng | sub Ukr (S1-4): C TitleAndYear held 4",
                // Season 2 began in 2008, after the show of 2006.
                "Mickey Mouse Clubhouse (S2) (2008-2010) SatRip (S2): C TitleAndYear held 2",
                "Mickey Mouse Clubhouse (S2) (2008-2010) SatRip (S2): C TitleAndYear held 2",
            ],
            releases
                .Select(release => $"{release.Title}: {string.Join(", ", release.LibraryMatches.Select(match => Describe(works, match)))}")
                .Order(StringComparer.Ordinal));
        Assert.All(releases, release => Assert.Equal(ReleaseKind.Tv, release.Kind));
    }

    [Fact]
    public async Task Releases_matches_a_tv_release_by_a_shows_ids_and_a_movie_release_by_a_films_never_the_other()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // Titles that name no work of the search: only the ids can match.
        _toloka.SearchXml = TorznabSamples.Feed(
            Item("Unrelated Film BDRip", "m1", "2000", $"""<torznab:attr name="tmdbid" value="{SharedTmdbNumber}" />"""));
        _toloka.TvSearchXml = TorznabSamples.Feed(
            Item("Unrelated S05E01 1080p", "t1", "5000", $"""<torznab:attr name="tvdbid" value="{ShowTvdbId}" />"""),
            Item("Unrelated (S1) WEB-DL", "t2", "5000", $"""<torznab:attr name="tmdbid" value="{SharedTmdbNumber}" />"""),
            Item("Unrelated WEB-DL", "t3", "5000", $"""<torznab:attr name="imdbid" value="{ShowImdbId}" />"""),
            // An episode's IMDb id is not the show's.
            Item("Unrelated S01E01 WEB-DL", "t4", "5000", """<torznab:attr name="imdbid" value="tt7529124" />"""));
        var search = await CreateSearchAsync("Yellowstone", cancellationToken);

        //Act
        var releases = await ReleasesAsync(search, "", cancellationToken);

        //Assert
        var works = WorkNames(search);
        Assert.Equal(
            [
                // The film's TMDb number matches the film, never the show that has it in TMDb's TV numbering.
                "Unrelated Film BDRip: Movie [Tmdb 73586] M Identifier",
                "Unrelated S05E01 1080p: Tv [Tvdb 341164] S Identifier held 1",
                "Unrelated (S1) WEB-DL: Tv [TmdbTv 73586] S Identifier held 2",
                // No season claimed: nothing to count.
                "Unrelated WEB-DL: Tv [Imdb tt4236770] S Identifier",
                "Unrelated S01E01 WEB-DL: Tv [Imdb tt7529124] ",
            ],
            releases.Select(release =>
                $"{release.Title}: {release.Kind} [{string.Join(", ", release.Identifiers.Select(identifier => $"{identifier.Scheme} {identifier.Value}"))}] "
                + string.Join(", ", release.LibraryMatches.Select(match => Describe(works, match)))));
    }

    [Fact]
    public async Task Releases_counts_the_episodes_held_of_an_episode_range_and_of_the_specials()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        _toloka.SearchXml = TorznabSamples.Feed();
        _toloka.TvSearchXml = TorznabSamples.Feed(
            Item("Yellowstone (S1E2-9 of 9) (2018) WEB-DL", "t1", "5000"),
            Item("Yellowstone S05E01-E10 1080p", "t2", "5000"),
            Item("Yellowstone (S0) (2018) WEB-DL", "t3", "5000"),
            Item("Yellowstone (2018-2024) WEB-DL", "t4", "5000"));
        var search = await CreateSearchAsync("Yellowstone", cancellationToken);

        //Act
        var releases = await ReleasesAsync(search, "", cancellationToken);

        //Assert
        var works = WorkNames(search);
        Assert.Equal(
            [
                // Of season 1's two episodes held, the second alone is in the range.
                "Yellowstone (S1E2-9 of 9) (2018) WEB-DL: S TitleAndYear held 1",
                // No year: the title alone.
                "Yellowstone S05E01-E10 1080p: S TitleOnly held 2",
                "Yellowstone (S0) (2018) WEB-DL: S TitleAndYear held 0",
                // A span without a season: no count.
                "Yellowstone (2018-2024) WEB-DL: S TitleAndYear",
            ],
            releases.Select(release => $"{release.Title}: {string.Join(", ", release.LibraryMatches.Select(match => Describe(works, match)))}"));
    }

    [Fact]
    public async Task Releases_matches_a_tv_release_by_the_year_its_seasons_allow()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        _toloka.SearchXml = TorznabSamples.Feed();
        _toloka.TvSearchXml = TorznabSamples.Feed(
            Item("Yellowstone (S1-2) (2017-2019) WEB-DL", "t1", "5000"),
            Item("Yellowstone (S1) (2018) WEB-DL", "t2", "5000"),
            Item("Yellowstone (S2) (2018) WEB-DL", "t3", "5000"),
            Item("Yellowstone (S2) (2017) WEB-DL", "t4", "5000"),
            Item("Yellowstone (S0) (2019) WEB-DL", "t5", "5000"),
            Item("Yellowstone (2016-2018) WEB-DL", "t6", "5000"),
            Item("Yellowstone (2019-2024) WEB-DL", "t7", "5000"));
        var search = await CreateSearchAsync("Yellowstone", cancellationToken);

        //Act
        var releases = await ReleasesAsync(search, "", cancellationToken);

        //Assert
        // The show of 2018 against each rule at its boundary year.
        var works = WorkNames(search);
        Assert.Equal(
            [
                // With season 1, its first year exactly: 2018 inside the span is not enough.
                "Yellowstone (S1-2) (2017-2019) WEB-DL: S TitleOnly held 3",
                "Yellowstone (S1) (2018) WEB-DL: S TitleAndYear held 2",
                // With later seasons only, any year up to its first: the show's own year too, never a later one.
                "Yellowstone (S2) (2018) WEB-DL: S TitleAndYear held 1",
                "Yellowstone (S2) (2017) WEB-DL: S TitleOnly held 1",
                "Yellowstone (S0) (2019) WEB-DL: S TitleAndYear held 0",
                // With no season, any year of the span, its ends included.
                "Yellowstone (2016-2018) WEB-DL: S TitleAndYear",
                "Yellowstone (2019-2024) WEB-DL: S TitleOnly",
            ],
            releases.Select(release => $"{release.Title}: {string.Join(", ", release.LibraryMatches.Select(match => Describe(works, match)))}"));
    }

    [Fact]
    public async Task Releases_of_a_search_that_found_a_series_by_an_episode_matches_the_shows_title_not_the_episodes()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        _toloka.SearchXml = TorznabSamples.Feed();
        _toloka.TvSearchXml = TorznabSamples.Feed(
            Item("Yellowstone (S1) (2018) WEB-DL", "t1", "5000"),
            Item("Kill the Messenger (S1) (2018) WEB-DL", "t2", "5000"));

        //Act
        // An episode's title: the search lists its series.
        var search = await CreateSearchAsync("Kill the Messenger", cancellationToken);
        var releases = await ReleasesAsync(search, "", cancellationToken);

        //Assert
        var works = WorkNames(search);
        Assert.Equal(["S TitleOnly"], search.Library.Select(match => $"{works[match.Work.Id]} {match.Evidence}"));
        // A release claims the show: matched by the series' own title, never by its episode's.
        Assert.Equal(
            ["Yellowstone (S1) (2018) WEB-DL: S TitleAndYear held 2", "Kill the Messenger (S1) (2018) WEB-DL: "],
            releases.Select(release => $"{release.Title}: {string.Join(", ", release.LibraryMatches.Select(match => Describe(works, match)))}"));
    }

    [Fact]
    public async Task Releases_says_what_each_release_claims_and_what_kind_it_is()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var search = await CreateSearchAsync("Yellowstone", cancellationToken);

        //Act
        var releases = await ReleasesAsync(search, "", cancellationToken);

        //Assert
        var pack = Assert.Single(releases, release => release.Title == "Yellowstone (S1-5) (2018-2024) WEB-DL-AVC Ukr/Eng | Sub Eng");
        Assert.Equal(ReleaseKind.Tv, pack.Kind);
        Assert.Equal(["Yellowstone"], pack.ClaimedWork.Titles);
        Assert.Equal((2018, (int?)2024), (pack.ClaimedWork.Year!.Value, pack.ClaimedWork.LastYear));
        Assert.Equal(new ClaimedEpisodesDto(1, 5, null, null), pack.ClaimedWork.Episodes);
        var season = Assert.Single(releases, release => release.Title == "Yellowstone (S4) (2021) WEB-DL 1080p Ukr/Eng | Sub Eng (S4)");
        Assert.Equal((2021, (int?)null), (season.ClaimedWork.Year!.Value, season.ClaimedWork.LastYear));
        Assert.Equal(new ClaimedEpisodesDto(4, 4, null, null), season.ClaimedWork.Episodes);
        // A movie's claim, read as before: no span, no seasons.
        var film = Assert.Single(releases, release => release.Title == FilmRelease);
        Assert.Equal(ReleaseKind.Movie, film.Kind);
        Assert.Equal(["Yellowstone"], film.ClaimedWork.Titles);
        Assert.Equal((2009, (int?)null), (film.ClaimedWork.Year!.Value, film.ClaimedWork.LastYear));
        Assert.Null(film.ClaimedWork.Episodes);
        await _factory.WithDbAsync(async db =>
        {
            var stored = await db.Releases.AsNoTracking().Select(release => new { release.Title, release.Kind }).ToListAsync(cancellationToken);
            Assert.Equal(TorznabSamples.TvItemCount, stored.Count(release => release.Kind == ReleaseKind.Tv));
            Assert.Equal(ReleaseKind.Movie, Assert.Single(stored, release => release.Title == FilmRelease).Kind);
        });
    }

    [Fact]
    public async Task Releases_answers_a_tv_releases_json_shape_with_its_kind_claims_and_held_count()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var search = await CreateSearchAsync("Yellowstone", cancellationToken);

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}/{search.Id}/releases?kind=tv&pageSize=100", _member.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync(cancellationToken))!.AsObject();
        var release = Assert.Single(
            body["items"]!.AsArray(),
            item => (string)item!["title"]! == "Yellowstone (S1-5) (2018-2024) WEB-DL-AVC Ukr/Eng | Sub Eng")!.AsObject();
        Assert.Equal("Tv", (string)release["kind"]!);
        var claimedWork = release["claimedWork"]!.AsObject();
        Assert.Equal(["titles", "year", "lastYear", "episodes"], claimedWork.Select(property => property.Key));
        Assert.Equal(2024, (int)claimedWork["lastYear"]!);
        Assert.Equal(["firstSeason", "lastSeason", "firstEpisode", "lastEpisode"], claimedWork["episodes"]!.AsObject().Select(property => property.Key));
        Assert.Null(claimedWork["episodes"]!["firstEpisode"]);
        var match = release["libraryMatches"]![0]!.AsObject();
        Assert.Equal(["workId", "evidence", "conflictingSchemes", "heldEpisodeCount"], match.Select(property => property.Key));
        Assert.Equal(5, (int)match["heldEpisodeCount"]!);
    }

    [Theory]
    [InlineData("kind=Tv", 9, "Tv")]
    [InlineData("kind=TV", 9, "Tv")]
    [InlineData("kind=%20tv%20", 9, "Tv")]
    [InlineData("kind=movie", 1, "Movie")]
    [InlineData("kind=", 10, null)]
    public async Task Releases_of_a_kind_lists_only_the_releases_of_that_kind(string query, int expectedCount, string? expectedKind)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var search = await CreateSearchAsync("Yellowstone", cancellationToken);

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}/{search.Id}/releases?{query}&pageSize=100", _member.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var list = (await response.Content.ReadFromJsonAsync<SearchReleaseListResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
        Assert.Equal((expectedCount, expectedCount), (list.TotalCount, list.Items.Count));
        if (expectedKind is not null)
            Assert.All(list.Items, release => Assert.Equal(expectedKind, release.Kind.ToString()));
    }

    [Theory]
    [InlineData("Series", "“Series” is not a kind filter. Use Movie or Tv.")]
    [InlineData("1", "“1” is not a kind filter. Use Movie or Tv.")]
    [InlineData("TvSeriesTvSeriesTvSeriesTvSeriesTvSeries", "“TvSeriesTvSeriesTvSeriesTvSeries…” is not a kind filter. Use Movie or Tv.")]
    public async Task Releases_of_a_kind_that_is_not_one_is_400_with_its_sentence(string kind, string expectedError)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var search = await CreateSearchAsync("Yellowstone", cancellationToken);

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}/{search.Id}/releases?kind={Uri.EscapeDataString(kind)}", _member.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(expectedError, await ErrorAsync(response, cancellationToken));
    }

    [Fact]
    public async Task Releases_of_a_kind_of_someone_elses_search_is_a_bare_404_whatever_the_kind()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var search = await CreateSearchAsync("Yellowstone", cancellationToken);
        var stranger = await new UserSeeder(_factory.Scope).CreateAuthorized("stranger");

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}/{search.Id}/releases?kind=Series", stranger.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsStringAsync(cancellationToken));
    }

    [Fact]
    public async Task Create_offers_the_kinds_its_sources_listed_movie_before_tv()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var both = await CreateSearchAsync("Yellowstone", cancellationToken);
        // A season asks the TV search alone.
        var season = await CreateSearchAsync("Yellowstone", cancellationToken, "5");
        _toloka.TvSearchXml = TorznabSamples.Feed();
        _toloka.SearchXml = TorznabSamples.Feed();
        var none = await CreateSearchAsync("Yellowstone", cancellationToken);

        //Assert
        Assert.Equal([ReleaseKind.Movie, ReleaseKind.Tv], both.Facets.Kinds);
        Assert.Equal([ReleaseKind.Tv], season.Facets.Kinds);
        Assert.Empty(none.Facets.Kinds);
        var read = await _factory.GetHttpRequest($"{BaseUrl}/{both.Id}", _member.AccessToken);
        Assert.Equal(
            [ReleaseKind.Movie, ReleaseKind.Tv],
            (await read.Content.ReadFromJsonAsync<SearchResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!.Facets.Kinds);
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>The works of a search by the names the expectations use: M the film, S and C the two series.</summary>
    private static Dictionary<Guid, string> WorkNames(SearchResponse search) =>
        search.Library.ToDictionary(
            match => match.Work.Id,
            match => match.Work.Kind == MediaWorkKind.Movie ? "M" : match.Work.Title == "Yellowstone" ? "S" : "C");

    private static string Describe(Dictionary<Guid, string> works, ReleaseLibraryMatchDto match) =>
        $"{works[match.WorkId]} {match.Evidence}"
        + (match.HeldEpisodeCount is { } held ? string.Create(CultureInfo.InvariantCulture, $" held {held}") : "");

    private static string ItemId(char letter) => new(letter, 32);

    /// <summary>An episode of a series of the test, its file named after its place.</summary>
    private static JsonObject Episode(char letter, string title, char seriesLetter, int season, int episode, string imdbId) =>
        JellyfinSamples.EpisodeWith(
            ItemId(letter),
            title,
            ItemId(seriesLetter),
            season,
            episode,
            string.Create(CultureInfo.InvariantCulture, $"/media/tv-shows/{seriesLetter}/S{season:00}E{episode:00} {letter}.mkv"),
            imdbId);

    /// <summary>One release Toloka lists in <paramref name="category"/>, its page under
    /// <paramref name="topic"/>, with <paramref name="extra"/>'s attrs (its ids).</summary>
    private static string Item(string title, string topic, string category, string extra = "") =>
        TorznabSamples.Item(
            title,
            $"{TorznabSamples.DownloadBaseFor(TolokaAddress)}download?apikey={ApiKey}&file={topic}",
            comments: $"https://toloka.to/{topic}",
            extra: $"<category>{category}</category>" + extra);

    /// <summary>A search by the member; throws unless it was created.</summary>
    private async Task<SearchResponse> CreateSearchAsync(string text, CancellationToken cancellationToken, string? season = null)
    {
        var response = await _factory.PostHttpRequest(BaseUrl, _member.AccessToken, new SearchRequest(text, Season: season));
        if (response.StatusCode != HttpStatusCode.Created)
            throw new InvalidOperationException($"The search answered {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync(cancellationToken)}");
        return (await response.Content.ReadFromJsonAsync<SearchResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
    }

    /// <summary>Every release of <paramref name="search"/> on one page, in the search's own order — none
    /// lists seeders — with <paramref name="query"/>; throws unless it was listed.</summary>
    private async Task<IReadOnlyList<SearchReleaseDto>> ReleasesAsync(SearchResponse search, string query, CancellationToken cancellationToken)
    {
        var response = await _factory.GetHttpRequest($"{BaseUrl}/{search.Id}/releases?pageSize=100{query}", _member.AccessToken);
        if (response.StatusCode != HttpStatusCode.OK)
            throw new InvalidOperationException($"The list answered {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync(cancellationToken)}");
        return (await response.Content.ReadFromJsonAsync<SearchReleaseListResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!.Items;
    }

    private static async Task<string?> ErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken) =>
        (string?)JsonNode.Parse(await response.Content.ReadAsStringAsync(cancellationToken))!["error"];
}
