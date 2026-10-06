using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;

namespace MediaAcquisitionManager.Infrastructure.Tests.Integrations;

// MAM-34 (0.2.1) — the fake Jellyfin's Identify, which every link test stands on, does what the live check of 2026-10-06
// recorded (VendorSamples/Jellyfin): a search answers the provider's rows by endpoint, key (spelled exactly: "tmdb" answers []),
// id, provider and language, with the namespace trap — a TV id searched as a film answers whatever film has the number —; Apply
// is synchronous and replaces the item's name, year, ids, overview and images, in the language of the item's own library, and
// answers the recorded 204 of its kind; and the switches make an Apply refused, ignored, partial, lost or slow. If this fake
// were looser than Jellyfin, the tests above it would pass for code that fails against the real thing.
public sealed class FakeJellyfinServerIdentifyTests : IDisposable
{
    private const string BaseUrl = "http://jellyfin.test:8096";
    private const string ApiKey = "4f1d0b9c2e7a4c3b8d6e5f7a9b0c1d2e";

    private readonly RecordingHttpHandler _handler = new();
    private readonly FakeJellyfinServer _server = new(BaseUrl) { ApiKey = ApiKey };
    private readonly HttpClient _client;

    public FakeJellyfinServerIdentifyTests()
    {
        _server.Movies.Add(JellyfinSamples.Item());
        _server.Series.Add(JellyfinSamples.SeriesItem());
        _server.Movies.Add(JellyfinSamples.EpisodeMovieItem());
        _server.Arm(_handler);
        _client = new HttpClient(_handler) { BaseAddress = new Uri(BaseUrl + "/") };
        _client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", $"MediaBrowser Token=\"{ApiKey}\"");
    }

    public void Dispose() => _client.Dispose();

    // The film the live check identified, as the fake holds it: the sample movies come before it in the list.
    private JsonObject Film => _server.Movies.Single(movie => (string?)movie["Id"] == JellyfinSamples.LandingStriplingId);

    [Fact]
    public async Task RemoteSearch_of_a_tmdb_id_answers_the_recorded_row_of_the_kind()
    {
        //Arrange
        var recorded = JellyfinSamples.RemoteSearchMovieByTmdbRows();
        _server.Matches.Add(new FakeJellyfinMatch("Movie", "TheMovieDb", "Tmdb", "223021", recorded[0]!.AsObject()));
        _server.Matches.Add(new FakeJellyfinMatch(
            "Series", "TheMovieDb", "Tmdb", "7869", JellyfinSamples.RemoteSearchSeriesByTmdbRows()[0]!.AsObject()));

        //Act
        var film = await SearchAsync("Movie", "Tmdb", "223021", "TheMovieDb");
        var show = await SearchAsync("Series", "Tmdb", "7869", "TheMovieDb");

        //Assert
        Assert.True(JsonNode.DeepEquals(recorded, film));
        Assert.True(JsonNode.DeepEquals(JellyfinSamples.RemoteSearchSeriesByTmdbRows(), show));
        // Each answers through the endpoint of its own kind only.
        Assert.Empty(await SearchAsync("Series", "Tmdb", "223021", "TheMovieDb"));
        Assert.Empty(await SearchAsync("Movie", "Tmdb", "7869", "TheMovieDb"));
    }

    [Fact]
    public async Task RemoteSearch_with_a_lower_case_key_answers_no_rows()
    {
        //Arrange
        // Recorded: the key must be exactly "Tmdb"; "tmdb" answers 200 with no rows.
        _server.AddFilm("223021", "Landing Stripling", 1962, "tt0056164");

        //Act
        var lowerCase = await SearchAsync("Movie", "tmdb", "223021", "TheMovieDb");
        var exact = await SearchAsync("Movie", "Tmdb", "223021", "TheMovieDb");

        //Assert
        Assert.Empty(lowerCase);
        Assert.Single(exact);
    }

    [Fact]
    public async Task RemoteSearch_of_a_tv_id_on_the_movie_endpoint_answers_the_unrelated_film_the_live_check_saw()
    {
        //Arrange
        // The namespace trap, recorded: a film search with the TV id 7869 answered "The Fest" (2005), a series search with the
        // film id 223021 an unrelated "Ham.W.Shaha".
        var recorded = JellyfinSamples.RemoteSearchMovieByTvIdRows()[0]!;
        _server.AddFilm("7869", "The Fest", 2005, "tt0475953", overview: "");
        _server.AddShow("223021", "Ham.W.Shaha", 2012);

        //Act
        var film = await SearchAsync("Movie", "Tmdb", "7869", "TheMovieDb");
        var show = await SearchAsync("Series", "Tmdb", "223021", "TheMovieDb");

        //Assert
        var row = Assert.Single(film)!.AsObject();
        Assert.Equal((string?)recorded["Name"], (string?)row["Name"]);
        Assert.Equal(recorded["ProviderIds"]!.ToJsonString(), row["ProviderIds"]!.ToJsonString());
        Assert.Equal((int?)recorded["ProductionYear"], (int?)row["ProductionYear"]);
        Assert.Equal("Ham.W.Shaha", (string?)Assert.Single(show)!["Name"]);
    }

    [Fact]
    public async Task RemoteSearch_of_an_unknown_id_answers_no_rows()
    {
        //Arrange
        _server.AddFilm("223021", "Landing Stripling", 1962);

        //Act
        var rows = await SearchAsync("Movie", "Tmdb", "999999999", "TheMovieDb");

        //Assert
        Assert.Empty(rows);
    }

    [Fact]
    public async Task RemoteSearch_by_imdb_through_the_open_movie_database_answers_the_episode_row_and_through_the_moviedb_none()
    {
        //Arrange
        // Recorded: the Open Movie Database finds an episode by its IMDb id (one row, no overview); TheMovieDb answered []; with
        // no provider name the same single row.
        var recorded = JellyfinSamples.RemoteSearchOmdbRows()[0]!;
        _server.AddEpisodeMovie("tt9103946", "Blitzkrieg", 2019);

        //Act
        var omdb = await SearchAsync("Movie", "Imdb", "tt9103946", "The Open Movie Database");
        var tmdb = await SearchAsync("Movie", "Imdb", "tt9103946", "TheMovieDb");
        var any = await SearchAsync("Movie", "Imdb", "tt9103946", provider: null);

        //Assert
        var row = Assert.Single(omdb)!.AsObject();
        Assert.Equal((string?)recorded["Name"], (string?)row["Name"]);
        Assert.Equal(recorded["ProviderIds"]!.ToJsonString(), row["ProviderIds"]!.ToJsonString());
        Assert.Equal((int?)recorded["ProductionYear"], (int?)row["ProductionYear"]);
        Assert.False(row.ContainsKey("Overview"));
        Assert.Empty(tmdb);
        Assert.Equal(row.ToJsonString(), Assert.Single(any)!.ToJsonString());
    }

    [Fact]
    public void The_rows_the_fake_adds_have_the_members_of_the_rows_the_live_check_recorded()
    {
        //Arrange

        //Act
        var film = _server.AddFilm("1001", "A film", 1999, "tt0000001");
        var show = _server.AddShow("2002", "A show", 2001, "tt0000002", "3003");
        var episode = _server.AddEpisodeMovie("tt0000003", "An episode", 2019);

        //Assert
        Assert.Equal(Members(JellyfinSamples.RemoteSearchMovieByTmdbRows()[0]!), Members(film.Row));
        Assert.Equal(Members(JellyfinSamples.RemoteSearchSeriesByTmdbRows()[0]!), Members(show.Row));
        Assert.Equal(Members(JellyfinSamples.RemoteSearchOmdbRows()[0]!), Members(episode.Row));
        Assert.Equal("""{"Tmdb":"2002","Imdb":"tt0000002","Tvdb":"3003"}""", show.Row["ProviderIds"]!.ToJsonString());
    }

    [Fact]
    public async Task Apply_replaces_name_year_ids_images_and_etag_of_the_stored_item_and_a_later_read_and_list_see_them()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var row = JellyfinSamples.RemoteSearchMovieByTmdbRows()[0]!.AsObject();
        var before = JellyfinSamples.Item();

        //Act
        var apply = await ApplyAsync(JellyfinSamples.LandingStriplingId, row);
        var read = await _client.GetAsync($"Items/{JellyfinSamples.LandingStriplingId}?userId={JellyfinSamples.AdministratorUserId}", cancellationToken);
        var list = await _client.GetAsync("Items?includeItemTypes=Movie&limit=50", cancellationToken);

        //Assert
        // The recorded 204: no body, and Jellyfin's Content-Language header.
        Assert.Equal(HttpStatusCode.NoContent, apply.StatusCode);
        Assert.Empty(await apply.Content.ReadAsStringAsync(cancellationToken));
        Assert.Equal(["en-US"], apply.Content.Headers.ContentLanguage);
        var item = JsonNode.Parse(await read.Content.ReadAsStringAsync(cancellationToken))!;
        Assert.Equal("Landing Stripling", (string?)item["Name"]);
        Assert.Equal(1962, (int?)item["ProductionYear"]);
        Assert.Equal("""{"Tmdb":"223021","Imdb":"tt0056164"}""", item["ProviderIds"]!.ToJsonString());
        Assert.StartsWith("Tom and Jerry are sleeping outside", (string?)item["Overview"], StringComparison.Ordinal);
        Assert.NotEqual((string?)before["Etag"], (string?)item["Etag"]);
        Assert.NotEqual((string?)before["ImageTags"]!["Primary"], (string?)item["ImageTags"]!["Primary"]);
        Assert.Single(item["BackdropImageTags"]!.AsArray());
        // Everything else as it was: Apply is Jellyfin's, not a rewrite of the file's facts.
        Assert.Equal((string?)before["Path"], (string?)item["Path"]);
        var listed = JsonNode.Parse(await list.Content.ReadAsStringAsync(cancellationToken))!["Items"]!.AsArray()
            .Single(movie => (string?)movie!["Id"] == JellyfinSamples.LandingStriplingId)!;
        Assert.Equal("Landing Stripling", (string?)listed["Name"]);
        // The body stays readable as sent, and the query as asked.
        var request = Assert.Single(_handler.Requests, recorded => recorded.Method == HttpMethod.Post);
        Assert.Equal(row.ToJsonString(), request.Body);
        Assert.Equal("?replaceAllImages=true", request.Uri.Query);
        var applied = Assert.Single(_server.Applied);
        Assert.Equal((JellyfinSamples.LandingStriplingId, "true"), (applied.ItemId, applied.ReplaceAllImages));
        Assert.True(applied.EndedAt >= applied.StartedAt);
    }

    [Fact]
    public async Task Apply_of_an_omdb_row_drops_the_held_film_id_and_keeps_the_overview()
    {
        //Arrange
        // Recorded on Blitzkrieg: Name and year corrected, the Primary image replaced, the overview kept (the row has none) and
        // no backdrop; the Open Movie Database result drops a held film id and adds nothing.
        var episode = _server.Movies.Single(movie => (string?)movie["Id"] == JellyfinSamples.BlitzkriegId);
        episode["ProviderIds"] = new JsonObject { ["Imdb"] = "tt9103946", ["Tmdb"] = "1001" };
        var overview = (string?)episode["Overview"];
        var before = (string?)episode["ImageTags"]!["Primary"];

        //Act
        var apply = await ApplyAsync(JellyfinSamples.BlitzkriegId, JellyfinSamples.RemoteSearchOmdbRows()[0]!.AsObject());

        //Assert
        Assert.Equal(HttpStatusCode.NoContent, apply.StatusCode);
        Assert.Equal("Blitzkrieg", (string?)episode["Name"]);
        Assert.Equal(2019, (int?)episode["ProductionYear"]);
        Assert.Equal("""{"Imdb":"tt9103946"}""", episode["ProviderIds"]!.ToJsonString());
        Assert.Equal(overview, (string?)episode["Overview"]);
        Assert.NotEqual(before, (string?)episode["ImageTags"]!["Primary"]);
        Assert.Empty(episode["BackdropImageTags"]!.AsArray());
    }

    [Fact]
    public async Task Apply_of_a_tmdb_row_keeps_the_films_tvdb_ids_and_drops_the_tvmaze_id_of_a_series_episodes()
    {
        //Arrange
        // Recorded: the film's Tvdb and TvdbSlug were re-added by other providers, and the Penguins' episodes lost their TvMaze
        // id while keeping the others.
        Film["ProviderIds"] = new JsonObject { ["Tvdb"] = "48181", ["TvdbSlug"] = "landing-stripling", ["Tmdb"] = "1001" };
        _server.Series[0]["ProviderIds"] = new JsonObject { ["Tmdb"] = "7869", ["TvMaze"] = "4037" };
        _server.Episodes.Add(JellyfinSamples.EpisodeWith("33333333333333333333333333333333", "Episode", JellyfinSamples.PenguinsId, 1, 1, "/media/cartoons/p/e01.mkv", "tt0000001", "1"));
        _server.Episodes[0]["ProviderIds"]!["TvMaze"] = "9";
        _server.Episodes.Add(JellyfinSamples.EpisodeWith("44444444444444444444444444444444", "Other", "55555555555555555555555555555555", 1, 1, "/media/cartoons/o/e01.mkv", "tt0000009", "2"));
        _server.Episodes[1]["ProviderIds"]!["TvMaze"] = "10";

        //Act
        await ApplyAsync(JellyfinSamples.LandingStriplingId, JellyfinSamples.RemoteSearchMovieByTmdbRows()[0]!.AsObject());
        await ApplyAsync(JellyfinSamples.PenguinsId, JellyfinSamples.RemoteSearchSeriesByTmdbRows()[0]!.AsObject());

        //Assert
        Assert.Equal("""{"Tmdb":"223021","Imdb":"tt0056164","Tvdb":"48181","TvdbSlug":"landing-stripling"}""", Film["ProviderIds"]!.ToJsonString());
        Assert.Equal("""{"Tmdb":"7869","Imdb":"tt0892700","Tvdb":"85075"}""", _server.Series[0]["ProviderIds"]!.ToJsonString());
        Assert.Equal("""{"Imdb":"tt0000001","Tvdb":"1"}""", _server.Episodes[0]["ProviderIds"]!.ToJsonString());
        // Another series' episode is none of its business.
        Assert.Equal("10", (string?)_server.Episodes[1]["ProviderIds"]!["TvMaze"]);
        // Answered by kind: the film's recorded 204 and the series' recorded 204.
        Assert.Equal(2, _server.Applied.Count);
    }

    [Fact]
    public async Task Apply_of_a_locked_item_replaces_only_the_ids_and_the_etag()
    {
        //Arrange
        // In Jellyfin's source a locked item takes the ids and a new Etag and nothing else: the reason the adapter refuses it
        // upfront, and what a half-applied item would look like if it didn't.
        var film = Film;
        film["LockData"] = true;
        var etag = (string?)film["Etag"];

        //Act
        await ApplyAsync(JellyfinSamples.LandingStriplingId, JellyfinSamples.RemoteSearchMovieByTmdbRows()[0]!.AsObject());

        //Assert
        Assert.Equal("""{"Tmdb":"223021","Imdb":"tt0056164"}""", film["ProviderIds"]!.ToJsonString());
        Assert.NotEqual(etag, (string?)film["Etag"]);
        Assert.Equal("120. Landing Stripling", (string?)film["Name"]);
        Assert.Null(film["Overview"]);
        Assert.Equal(JellyfinSamples.Item()["ImageTags"]!.ToJsonString(), film["ImageTags"]!.ToJsonString());
    }

    [Fact]
    public async Task Apply_of_an_item_with_a_locked_name_keeps_its_name()
    {
        //Arrange
        Film["LockedFields"] = new JsonArray("Name");

        //Act
        await ApplyAsync(JellyfinSamples.LandingStriplingId, JellyfinSamples.RemoteSearchMovieByTmdbRows()[0]!.AsObject());

        //Assert
        Assert.Equal("120. Landing Stripling", (string?)Film["Name"]);
        Assert.StartsWith("Tom and Jerry are sleeping outside", (string?)Film["Overview"], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Apply_of_an_unknown_item_is_the_recorded_404_and_with_a_wrong_key_the_recorded_401()
    {
        //Arrange
        var row = JellyfinSamples.RemoteSearchMovieByTmdbRows()[0]!.AsObject();

        //Act
        var unknown = await ApplyAsync(Guid.NewGuid().ToString("N"), row);
        _server.ApiKey = "another-key";
        var wrongKey = await ApplyAsync(JellyfinSamples.LandingStriplingId, row);
        var wrongKeySearch = await _client.PostAsync(
            "Items/RemoteSearch/Movie", new StringContent("{}", Encoding.UTF8, "application/json"), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Contains("\"status\":404", await unknown.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Unauthorized, wrongKey.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, wrongKeySearch.StatusCode);
        Assert.Empty(_server.Applied);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.BadRequest)]
    public async Task A_refused_apply_answers_its_status_and_applies_nothing(HttpStatusCode refusal)
    {
        //Arrange
        _server.ApplyRefusal = refusal;

        //Act
        var apply = await ApplyAsync(JellyfinSamples.LandingStriplingId, JellyfinSamples.RemoteSearchMovieByTmdbRows()[0]!.AsObject());

        //Assert
        Assert.Equal(refusal, apply.StatusCode);
        Assert.Equal("120. Landing Stripling", (string?)Film["Name"]);
        Assert.Empty(_server.Applied);
    }

    [Fact]
    public async Task An_ignored_apply_answers_204_and_applies_nothing()
    {
        //Arrange
        _server.IgnoresApply = true;
        var etag = (string?)Film["Etag"];

        //Act
        var apply = await ApplyAsync(JellyfinSamples.LandingStriplingId, JellyfinSamples.RemoteSearchMovieByTmdbRows()[0]!.AsObject());

        //Assert
        Assert.Equal(HttpStatusCode.NoContent, apply.StatusCode);
        Assert.Equal("120. Landing Stripling", (string?)Film["Name"]);
        Assert.Equal(etag, (string?)Film["Etag"]);
        // The server took it: Applied says so, whatever it then did.
        Assert.Single(_server.Applied);
    }

    [Fact]
    public async Task An_apply_that_took_only_the_ids_leaves_name_and_year_removes_the_images_and_changes_the_etag()
    {
        //Arrange
        // What a real partial Apply leaves when the providers fail after replaceAllImages deleted the images: an unchanged Etag
        // would hide the lost-poster case.
        _server.AppliesIdsOnly = true;
        var film = Film;
        var etag = (string?)film["Etag"];

        //Act
        var apply = await ApplyAsync(JellyfinSamples.LandingStriplingId, JellyfinSamples.RemoteSearchMovieByTmdbRows()[0]!.AsObject());

        //Assert
        Assert.Equal(HttpStatusCode.NoContent, apply.StatusCode);
        Assert.Equal("""{"Tmdb":"223021","Imdb":"tt0056164"}""", film["ProviderIds"]!.ToJsonString());
        Assert.Equal("120. Landing Stripling", (string?)film["Name"]);
        Assert.Empty(film["ImageTags"]!.AsObject());
        Assert.Empty(film["BackdropImageTags"]!.AsArray());
        Assert.NotEqual(etag, (string?)film["Etag"]);
    }

    [Fact]
    public async Task Search_without_a_search_info_is_a_500()
    {
        //Arrange
        // The real controller dereferences the body's SearchInfo.
        using var body = new StringContent("""{"ItemId":"750eaa45-b083-6d08-e4ae-4b35750b93ca"}""", Encoding.UTF8, "application/json");

        //Act
        var response = await _client.PostAsync("Items/RemoteSearch/Movie", body, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Single(_server.RemoteSearches);
    }

    [Fact]
    public async Task A_refused_search_answers_its_status()
    {
        //Arrange
        _server.SearchRefusal = HttpStatusCode.Forbidden;

        //Act
        var rows = await _client.PostAsync(
            "Items/RemoteSearch/Movie", new StringContent("{}", Encoding.UTF8, "application/json"), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(HttpStatusCode.Forbidden, rows.StatusCode);
    }

    [Fact]
    public async Task Search_in_a_library_without_the_provider_among_its_fetchers_answers_no_rows()
    {
        //Arrange
        // IncludeDisabledProviders is false: ItemId only picks the providers from the item's library options.
        _server.AddFilm("223021", "Landing Stripling", 1962, "tt0056164");
        var movieOptions = _server.LibraryOptionsOf(JellyfinSamples.CartoonsRoot)["TypeOptions"]!.AsArray()
            .OfType<JsonObject>().Single(options => (string?)options["Type"] == "Movie");
        movieOptions["MetadataFetchers"] = new JsonArray("AniList");

        //Act
        var withItem = await SearchAsync("Movie", "Tmdb", "223021", "TheMovieDb", JellyfinSamples.LandingStriplingId);
        var withoutItem = await SearchAsync("Movie", "Tmdb", "223021", "TheMovieDb", itemId: null);

        //Assert
        Assert.Empty(withItem);
        Assert.Single(withoutItem);
    }

    [Fact]
    public async Task Search_names_the_row_in_the_bodys_language_and_apply_names_the_item_in_its_librarys_language()
    {
        //Arrange
        // ItemId does NOT pick the language: the body's SearchInfo.MetadataLanguage does, and falls back to the server's English
        // (ProviderManager.cs:969-977), while Apply's refresh uses the item's library's. A search that left the language out
        // gives an English row for a film in "Фільми", which Apply then names in Ukrainian.
        _server.AddFilm("1001", "The Knife in the Water", 1962, "tt0056291", namesByLanguage: new Dictionary<string, string> { ["uk"] = "Ніж у воді" });
        var path = $"{JellyfinSamples.MoviesRoot}/Film (1962)/Film (1962).mkv";
        var film = JellyfinSamples.Item();
        film["Id"] = "66666666666666666666666666666666";
        film["Path"] = path;
        _server.Movies.Add(film);

        //Act
        var inUkrainian = await SearchAsync("Movie", "Tmdb", "1001", "TheMovieDb", film["Id"]!.GetValue<string>(), language: "uk");
        var inDefault = await SearchAsync("Movie", "Tmdb", "1001", "TheMovieDb", film["Id"]!.GetValue<string>());
        await ApplyAsync("66666666666666666666666666666666", inDefault[0]!.AsObject());

        //Assert
        Assert.Equal("Ніж у воді", (string?)inUkrainian[0]!["Name"]);
        Assert.Equal("The Knife in the Water", (string?)inDefault[0]!["Name"]);
        // The English row was posted, and the item was named in its library's language all the same.
        Assert.Equal("Ніж у воді", (string?)film["Name"]);
    }

    [Fact]
    public async Task A_lost_apply_answer_fails_the_request_after_the_apply_landed()
    {
        //Arrange
        _server.AfterApply = _ => throw new HttpRequestException("The answer never came.");

        //Act
        var exception = await Record.ExceptionAsync(
            () => ApplyAsync(JellyfinSamples.LandingStriplingId, JellyfinSamples.RemoteSearchMovieByTmdbRows()[0]!.AsObject()));

        //Assert
        Assert.IsType<HttpRequestException>(exception);
        Assert.Equal("Landing Stripling", (string?)Film["Name"]);
        Assert.Single(_server.Applied);
    }

    [Fact]
    public async Task Concurrent_applies_are_counted_per_type()
    {
        //Arrange
        // Two films and a series at once, as the worker's lanes make them: a slow Apply (a series took 159 s live) is a handler
        // thread that waits.
        _server.ApplyDelay = TimeSpan.FromMilliseconds(500);
        var secondFilm = JellyfinSamples.Item();
        secondFilm["Id"] = "77777777777777777777777777777777";
        _server.Movies.Add(secondFilm);
        var filmRow = JellyfinSamples.RemoteSearchMovieByTmdbRows()[0]!.AsObject();
        var showRow = JellyfinSamples.RemoteSearchSeriesByTmdbRows()[0]!.AsObject();

        //Act
        await Task.WhenAll(
            Task.Run(() => ApplyAsync(JellyfinSamples.LandingStriplingId, filmRow.DeepClone().AsObject()), TestContext.Current.CancellationToken),
            Task.Run(() => ApplyAsync("77777777777777777777777777777777", filmRow.DeepClone().AsObject()), TestContext.Current.CancellationToken),
            Task.Run(() => ApplyAsync(JellyfinSamples.PenguinsId, showRow.DeepClone().AsObject()), TestContext.Current.CancellationToken));

        //Assert
        Assert.Equal(3, _server.PeakAppliesInFlight);
        Assert.Equal(2, _server.PeakAppliesInFlightByType["Movie"]);
        Assert.Equal(1, _server.PeakAppliesInFlightByType["Series"]);
        var applied = _server.Applied.ToArray();
        Assert.Equal(3, applied.Length);
        // The spans overlap: the latest start is before the earliest end.
        Assert.True(applied.Max(apply => apply.StartedAt) < applied.Min(apply => apply.EndedAt));
        Assert.Equal(3, _handler.Requests.Count);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private async Task<JsonArray> SearchAsync(
        string endpoint, string key, string value, string? provider, string? itemId = JellyfinSamples.LandingStriplingId, string? language = null)
    {
        var info = new JsonObject { ["ProviderIds"] = new JsonObject { [key] = value } };
        if (language is not null)
            info["MetadataLanguage"] = language;
        var body = new JsonObject { ["SearchInfo"] = info };
        if (itemId is not null)
            body["ItemId"] = itemId;
        if (provider is not null)
            body["SearchProviderName"] = provider;

        using var content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        using var response = await _client.PostAsync($"Items/RemoteSearch/{endpoint}", content, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonNode.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!.AsArray();
    }

    private Task<HttpResponseMessage> ApplyAsync(string itemId, JsonObject row)
    {
        var content = new StringContent(row.ToJsonString(), Encoding.UTF8, "application/json");
        return _client.PostAsync($"Items/RemoteSearch/Apply/{itemId}?replaceAllImages=true", content, TestContext.Current.CancellationToken);
    }

    private static string Members(JsonNode row) => string.Join(",", row.AsObject().Select(property => property.Key));
}
