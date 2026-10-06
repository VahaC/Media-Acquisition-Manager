using System.Text.Json.Nodes;
using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Abstractions.Library;
using MediaAcquisitionManager.Core.Abstractions.Media;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using static MediaAcquisitionManager.TestInfrastructure.Integrations.JellyfinIdentifyHarness;

namespace MediaAcquisitionManager.Infrastructure.Tests.Integrations;

// MAM-34 (0.2.1) — Jellyfin's Identify (IMediaLibraryItemIdentifier) as the owner's TMDb link of a film, a show or an episode
// Jellyfin lists as a film, against the fake Jellyfin serving the three items the live check of 2026-10-06 identified and the
// search results it recorded (VendorSamples/Jellyfin: remote-search*, *-remote-search-apply-response.txt, *-after-identify.json):
// the requests, in their order; the body of the search, key and language exactly as Jellyfin takes them; Apply sent once, with
// the chosen result as it came and replaceAllImages=true; and the one allowed change of Jellyfin, which is Identify. What the
// shortcut and the confirmation read from the item, the libraries it is refused in, every refusal and every failure have their
// own classes (JellyfinAdapterIdentify*Tests).
public sealed class JellyfinAdapterIdentifyTests : IDisposable
{
    private readonly JellyfinIdentifyHarness _jellyfin = new();

    public void Dispose() => _jellyfin.Dispose();

    [Fact]
    public async Task Identify_of_a_film_reads_the_users_the_item_and_the_libraries_then_searches_applies_once_and_reads_the_item_again()
    {
        //Arrange
        var itemPath = $"/Items/{JellyfinSamples.LandingStriplingId}";

        //Act
        var result = await _jellyfin.IdentifyAsync(FilmRequest(), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        Assert.Null(result.Error);
        var identification = result.Value!;
        Assert.Equal(LibraryIdentifyOutcome.Identified, identification.Outcome);
        Assert.Null(identification.Refusal);
        Assert.True(identification.Sent);
        Assert.Null(identification.Sentence);
        Assert.Empty(identification.IdsBefore);
        Assert.Equal(["Imdb tt0056164", "Tmdb 223021"], Describe(identification.IdsAfter));
        Assert.Equal(
            [
                "GET /Users",
                $"GET {itemPath}?userId={JellyfinSamples.AdministratorUserId}",
                "GET /Library/VirtualFolders",
                "POST /Items/RemoteSearch/Movie",
                $"POST /Items/RemoteSearch/Apply/{JellyfinSamples.LandingStriplingId}?replaceAllImages=true",
                $"GET {itemPath}?userId={JellyfinSamples.AdministratorUserId}",
            ],
            _jellyfin.Lines());
        // What the live check saw Apply do to it.
        Assert.Equal(JellyfinSamples.LandingStriplingName, (string?)_jellyfin.Film["Name"]);
    }

    [Fact]
    public async Task Identify_posts_the_search_body_with_exactly_the_tmdb_key_the_dashed_item_id_and_the_moviedb_provider()
    {
        //Arrange
        // The cartoon library has no metadata language, so the body carries none.

        //Act
        await _jellyfin.IdentifyAsync(FilmRequest(), TestContext.Current.CancellationToken);

        //Assert
        var search = Assert.Single(_jellyfin.SearchRequests());
        Assert.Equal(
            """{"SearchInfo":{"ProviderIds":{"Tmdb":"223021"}},"ItemId":"750eaa45-b083-6d08-e4ae-4b35750b93ca","SearchProviderName":"TheMovieDb"}""",
            search.Body);
        Assert.Equal(["application/json"], search.HeaderValues("Content-Type"));
        Assert.Equal(_jellyfin.Server.RemoteSearches.Select(received => received.Body), [search.Body!]);
    }

    [Theory]
    // The library "Фільми": language uk, country UA.
    [InlineData(JellyfinSamples.MoviesRoot, "film", ",\"MetadataLanguage\":\"uk\",\"MetadataCountryCode\":\"UA\"")]
    // "Серіали": language en, a blank country that is left out.
    [InlineData(JellyfinSamples.TvShowsRoot, "show", ",\"MetadataLanguage\":\"en\"")]
    // "Мульфільми" and "Документалки": no language, so neither member.
    [InlineData(JellyfinSamples.CartoonsRoot, "film", "")]
    [InlineData(JellyfinSamples.DocumentaryRoot, "film", "")]
    public async Task Identify_posts_the_libraries_language_and_country_in_the_search_body(string root, string kind, string expectedLanguageMembers)
    {
        //Arrange
        // Jellyfin searches in the server's language unless the body names one, and Apply refreshes in the library's: the
        // search is made in the language Apply will use. The language is the library's setting, not the app's choice.
        const string itemId = "11111111111111111111111111111111";
        LibraryIdentifyRequest request;
        if (kind == "film")
        {
            _jellyfin.AddFilmUnder(root, itemId);
            request = FilmRequest() with { SourceItemId = itemId };
        }
        else
        {
            _jellyfin.AddShowUnder(root, itemId);
            request = ShowRequest() with { SourceItemId = itemId };
        }

        //Act
        await _jellyfin.IdentifyAsync(request, TestContext.Current.CancellationToken);

        //Assert
        var tmdbId = kind == "film" ? "223021" : "7869";
        Assert.Equal(
            "{\"SearchInfo\":{\"ProviderIds\":{\"Tmdb\":\"" + tmdbId + "\"}" + expectedLanguageMembers
                + "},\"ItemId\":\"11111111-1111-1111-1111-111111111111\",\"SearchProviderName\":\"TheMovieDb\"}",
            Assert.Single(_jellyfin.SearchRequests()).Body);
    }

    [Fact]
    public async Task Identify_applies_the_chosen_row_unchanged_with_replace_all_images_true()
    {
        //Arrange
        var recorded = JellyfinSamples.RemoteSearchMovieByTmdbRows()[0]!;

        //Act
        await _jellyfin.IdentifyAsync(FilmRequest(), TestContext.Current.CancellationToken);

        //Assert
        var apply = Assert.Single(_jellyfin.ApplyRequests());
        // The row exactly as Jellyfin gave it, its overview and artists included; the query only replaceAllImages, and no
        // language anywhere: Apply uses the item's library's.
        Assert.True(JsonNode.DeepEquals(recorded, JsonNode.Parse(apply.Body!)));
        Assert.Equal("?replaceAllImages=true", apply.Uri.Query);
        Assert.Equal(["application/json"], apply.HeaderValues("Content-Type"));
        var applied = Assert.Single(_jellyfin.Server.Applied);
        Assert.Equal("true", applied.ReplaceAllImages);
    }

    [Fact]
    public async Task Identify_of_a_film_in_a_ukrainian_library_compares_the_ukrainian_row_with_the_item_and_is_identified()
    {
        //Arrange
        // TMDb's English name is what an unspecific search answers and the item's library language is what Apply gives the
        // item: a search that left the language out would compare an English row with a Ukrainian item and never confirm.
        _jellyfin.Server.AddFilm(
            "1001", "The Knife in the Water", 1962, "tt0056291", namesByLanguage: new Dictionary<string, string> { ["uk"] = "Ніж у воді" });
        var itemId = (string)_jellyfin.AddFilmUnder(JellyfinSamples.MoviesRoot, "22222222222222222222222222222222")["Id"]!;
        var request = new LibraryIdentifyRequest(itemId, MediaWorkKind.Movie, new MediaIdentifier(MediaIdentifierScheme.Tmdb, "1001"), false);

        //Act
        var result = await _jellyfin.IdentifyAsync(request, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(LibraryIdentifyOutcome.Identified, result.Value!.Outcome);
        Assert.Contains("\"MetadataLanguage\":\"uk\"", Assert.Single(_jellyfin.SearchRequests()).Body, StringComparison.Ordinal);
        Assert.Equal("Ніж у воді", (string?)_jellyfin.Server.Movies.Single(movie => (string?)movie["Id"] == itemId)["Name"]);
        Assert.Equal("Ніж у воді", (string?)Assert.Single(_jellyfin.Server.Applied).Row["Name"]);
    }

    [Fact]
    public async Task Identify_of_a_film_in_a_library_without_a_language_compares_the_servers_english_row_and_is_identified()
    {
        //Arrange
        _jellyfin.Server.AddFilm(
            "1001", "The Knife in the Water", 1962, "tt0056291", namesByLanguage: new Dictionary<string, string> { ["uk"] = "Ніж у воді" });
        var itemId = (string)_jellyfin.AddFilmUnder(JellyfinSamples.CartoonsRoot, "22222222222222222222222222222222")["Id"]!;
        var request = new LibraryIdentifyRequest(itemId, MediaWorkKind.Movie, new MediaIdentifier(MediaIdentifierScheme.Tmdb, "1001"), false);

        //Act
        var result = await _jellyfin.IdentifyAsync(request, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(LibraryIdentifyOutcome.Identified, result.Value!.Outcome);
        Assert.DoesNotContain("MetadataLanguage", Assert.Single(_jellyfin.SearchRequests()).Body, StringComparison.Ordinal);
        Assert.Equal("The Knife in the Water", (string?)_jellyfin.Server.Movies.Single(movie => (string?)movie["Id"] == itemId)["Name"]);
    }

    [Fact]
    public async Task Identify_of_a_show_searches_the_series_endpoint_with_the_shows_tmdb_id_under_the_tmdb_key_and_never_the_movie_one()
    {
        //Arrange
        var itemPath = $"/Items/{JellyfinSamples.PenguinsId}";

        //Act
        var result = await _jellyfin.IdentifyAsync(ShowRequest(), TestContext.Current.CancellationToken);

        //Assert
        var identification = result.Value!;
        Assert.Equal(LibraryIdentifyOutcome.Identified, identification.Outcome);
        Assert.True(identification.Sent);
        Assert.Equal(
            [
                "GET /Users",
                $"GET {itemPath}?userId={JellyfinSamples.AdministratorUserId}",
                "GET /Library/VirtualFolders",
                "POST /Items/RemoteSearch/Series",
                $"POST /Items/RemoteSearch/Apply/{JellyfinSamples.PenguinsId}?replaceAllImages=true",
                $"GET {itemPath}?userId={JellyfinSamples.AdministratorUserId}",
            ],
            _jellyfin.Lines());
        // A show's TMDb number is its own: it goes under the same key, to the series search only, never the film search.
        Assert.Equal(
            """{"SearchInfo":{"ProviderIds":{"Tmdb":"7869"}},"ItemId":"9ac821b3-ddea-72fd-14ba-1835b4153e50","SearchProviderName":"TheMovieDb"}""",
            Assert.Single(_jellyfin.SearchRequests()).Body);
        Assert.DoesNotContain(_jellyfin.Lines(), line => line.EndsWith("/RemoteSearch/Movie", StringComparison.Ordinal));
        Assert.Equal("The Penguins of Madagascar", (string?)_jellyfin.Show["Name"]);
        Assert.Equal(2008, (int?)_jellyfin.Show["ProductionYear"]);
    }

    [Fact]
    public async Task Identify_of_an_episode_movie_searches_the_movie_endpoint_by_its_imdb_id_through_the_open_movie_database()
    {
        //Arrange
        var originalOverview = (string?)_jellyfin.EpisodeMovie["Overview"];

        //Act
        var result = await _jellyfin.IdentifyAsync(EpisodeMovieRequest(), TestContext.Current.CancellationToken);

        //Assert
        var identification = result.Value!;
        Assert.Equal(LibraryIdentifyOutcome.Identified, identification.Outcome);
        Assert.Equal(
            """{"SearchInfo":{"ProviderIds":{"Imdb":"tt9103946"}},"ItemId":"03299393-01cb-93b1-c47e-ea6efe4975c2","SearchProviderName":"The Open Movie Database"}""",
            Assert.Single(_jellyfin.SearchRequests()).Body);
        Assert.EndsWith("/RemoteSearch/Movie", Assert.Single(_jellyfin.SearchRequests()).Uri.AbsolutePath, StringComparison.Ordinal);
        // As the live check saw it: the name and year corrected, the ids unchanged, and the overview the item had kept —
        // the Open Movie Database result has none.
        Assert.Equal("Blitzkrieg", (string?)_jellyfin.EpisodeMovie["Name"]);
        Assert.Equal(2019, (int?)_jellyfin.EpisodeMovie["ProductionYear"]);
        Assert.Equal("""{"Imdb":"tt9103946"}""", _jellyfin.EpisodeMovie["ProviderIds"]!.ToJsonString());
        Assert.Equal(originalOverview, (string?)_jellyfin.EpisodeMovie["Overview"]);
        Assert.Equal(["Imdb tt9103946"], Describe(identification.IdsBefore));
        Assert.Equal(["Imdb tt9103946"], Describe(identification.IdsAfter));
    }

    [Fact]
    public async Task Identify_reads_the_item_as_the_first_enabled_administrator()
    {
        //Arrange
        var disabledAdministrator = Guid.NewGuid().ToString("N");
        var laterAdministrator = Guid.NewGuid().ToString("N");
        _jellyfin.Server.Users.Insert(0, User(disabledAdministrator, isAdministrator: true, isDisabled: true));
        // An administrator whose id Jellyfin would never give: it can't go into a query.
        _jellyfin.Server.Users.Insert(1, User("../Users", isAdministrator: true, isDisabled: false));
        _jellyfin.Server.Users.Add(User(laterAdministrator, isAdministrator: true, isDisabled: false));

        //Act
        var result = await _jellyfin.IdentifyAsync(FilmRequest(), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        Assert.All(
            _jellyfin.Http.Requests.Where(request => request.Method == HttpMethod.Get && request.Uri.AbsolutePath.StartsWith("/Items/", StringComparison.Ordinal)),
            request => Assert.Equal($"?userId={JellyfinSamples.AdministratorUserId}", request.Uri.Query));
    }

    [Fact]
    public async Task Identify_sends_the_key_in_the_media_browser_header_of_every_request_and_nowhere_else()
    {
        //Arrange

        //Act
        await _jellyfin.IdentifyAsync(FilmRequest(), TestContext.Current.CancellationToken);

        //Assert
        var requests = _jellyfin.Http.Requests;
        Assert.Equal(6, requests.Count);
        Assert.All(requests, request =>
        {
            Assert.Equal([$"MediaBrowser Token=\"{ApiKey}\""], request.HeaderValues("Authorization"));
            Assert.Empty(request.HeaderValues("X-Emby-Token"));
            Assert.DoesNotContain(ApiKey, request.Uri.OriginalString, StringComparison.Ordinal);
            Assert.DoesNotContain(ApiKey, request.Body ?? "", StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task Identify_asks_for_the_apply_connection_to_close_so_no_pooled_connection_can_send_it_a_second_time()
    {
        //Arrange
        // The runtime's own handler sends a request again when a reused connection was closed by the other end before any
        // answer; for a search or a read that is harmless, for Apply it would be a second identification.

        //Act
        await _jellyfin.IdentifyAsync(FilmRequest(), TestContext.Current.CancellationToken);

        //Assert
        var apply = Assert.Single(_jellyfin.ApplyRequests());
        Assert.Equal(["close"], apply.HeaderValues("Connection"));
    }

    [Fact]
    public async Task Identify_keeps_a_base_path_of_the_address()
    {
        //Arrange
        var settings = Settings with { BaseUrl = "http://jellyfin.test/jellyfin" };
        var server = new FakeJellyfinServer(settings.BaseUrl);
        server.Movies.Add(JellyfinSamples.Item());
        server.Matches.Add(new FakeJellyfinMatch("Movie", FakeJellyfinServer.TmdbProvider, "Tmdb", "223021", JellyfinSamples.RemoteSearchMovieByTmdbRows()[0]!.AsObject()));
        server.Arm(_jellyfin.Http);

        //Act
        var result = await _jellyfin.Identifier.IdentifyAsync(settings, FilmRequest(), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(LibraryIdentifyOutcome.Identified, result.Value!.Outcome);
        Assert.Equal(
            [
                "/jellyfin/Users",
                $"/jellyfin/Items/{JellyfinSamples.LandingStriplingId}",
                "/jellyfin/Library/VirtualFolders",
                "/jellyfin/Items/RemoteSearch/Movie",
                $"/jellyfin/Items/RemoteSearch/Apply/{JellyfinSamples.LandingStriplingId}",
                $"/jellyfin/Items/{JellyfinSamples.LandingStriplingId}",
            ],
            _jellyfin.Http.Requests.Select(request => request.Uri.AbsolutePath));
    }

    [Theory]
    [InlineData("film")]
    [InlineData("show")]
    [InlineData("episode-movie")]
    public async Task Identify_never_sends_an_item_update_or_a_refresh(string flow)
    {
        //Arrange
        // The one thing that changes Jellyfin is Identify: reads, the two searches, and Apply. Not the item POSTed back with
        // new ids, which the app did before 0.2.1, and not a refresh.
        var request = flow switch
        {
            "film" => FilmRequest(),
            "show" => ShowRequest(),
            _ => EpisodeMovieRequest(),
        };

        //Act
        await _jellyfin.IdentifyAsync(request, TestContext.Current.CancellationToken);

        //Assert
        var requests = _jellyfin.Http.Requests;
        Assert.NotEmpty(requests);
        Assert.All(requests, recorded =>
        {
            var path = recorded.Uri.AbsolutePath;
            Assert.DoesNotContain("Refresh", path, StringComparison.Ordinal);
            if (recorded.Method == HttpMethod.Post)
            {
                Assert.True(
                    path is "/Items/RemoteSearch/Movie" or "/Items/RemoteSearch/Series"
                        || path.StartsWith("/Items/RemoteSearch/Apply/", StringComparison.Ordinal),
                    $"POST {path} is neither a search nor an Apply.");
            }
            else
            {
                Assert.Equal(HttpMethod.Get, recorded.Method);
            }
        });
        Assert.Single(_jellyfin.ApplyRequests());
    }

    [Fact]
    public void IdentifiableKinds_are_films_and_series_and_an_episode_item_says_why_its_link_stays_in_the_app()
    {
        //Arrange

        //Act
        var kinds = _jellyfin.Identifier.IdentifiableKinds;

        //Assert
        Assert.Equal([MediaWorkKind.Movie, MediaWorkKind.Series], kinds);
        Assert.Equal(
            "Jellyfin identifies movie and series items only, so a link on an episode stays in the app.",
            _jellyfin.Identifier.UnidentifiableKindReason(MediaWorkKind.Episode));
    }

    [Fact]
    public async Task Identify_records_the_ids_before_and_after_with_the_apps_schemes_a_shows_tmdb_id_as_tmdbtv_and_its_tvdb_id_kept()
    {
        //Arrange
        // A series Jellyfin matched with another IMDb id, a TVDB id and an id of another registry.
        _jellyfin.Show["ProviderIds"] = new JsonObject { ["Imdb"] = "tt0000001", ["Tvdb"] = "85075", ["TvMaze"] = "4037" };

        //Act
        var result = await _jellyfin.IdentifyAsync(ShowRequest(), TestContext.Current.CancellationToken);

        //Assert
        var identification = result.Value!;
        Assert.Equal(LibraryIdentifyOutcome.Identified, identification.Outcome);
        Assert.Equal(["Imdb tt0000001", "Tvdb 85075"], Describe(identification.IdsBefore));
        Assert.Equal(["Imdb tt0892700", "TmdbTv 7869", "Tvdb 85075"], Describe(identification.IdsAfter));
    }

    [Fact]
    public async Task Identify_of_a_film_records_its_tmdb_id_as_tmdb_and_never_its_tvdb_id()
    {
        //Arrange
        _jellyfin.Film["ProviderIds"] = new JsonObject { ["Tvdb"] = "48181", ["Tmdb"] = "1001", ["TvdbSlug"] = "landing-stripling" };

        //Act
        var result = await _jellyfin.IdentifyAsync(FilmRequest(), TestContext.Current.CancellationToken);

        //Assert
        var identification = result.Value!;
        Assert.Equal(["Tmdb 1001"], Describe(identification.IdsBefore));
        Assert.Equal(["Imdb tt0056164", "Tmdb 223021"], Describe(identification.IdsAfter));
        // The ids other providers re-added stay in Jellyfin (the live check's after-identify item holds them).
        Assert.Equal("""{"Tmdb":"223021","Imdb":"tt0056164","Tvdb":"48181","TvdbSlug":"landing-stripling"}""", _jellyfin.Film["ProviderIds"]!.ToJsonString());
    }

    [Theory]
    [InlineData("no request")]
    [InlineData("a blank item id")]
    [InlineData("an item id Jellyfin never gives")]
    [InlineData("an episode item")]
    [InlineData("no id")]
    [InlineData("a show's id on a film")]
    [InlineData("a film's id on a show")]
    [InlineData("an IMDb id on a show")]
    [InlineData("a TVDB id on a film")]
    [InlineData("a malformed TMDb id")]
    [InlineData("a TMDb id with spaces")]
    [InlineData("a blank TMDb id")]
    [InlineData("a malformed IMDb id")]
    [InlineData("a TMDb id on an episode listed as a film")]
    public async Task Identify_refuses_a_callers_mistake_without_sending_anything(string mistake)
    {
        //Arrange
        // The namespace guard of ADR 0026, in the contract: a TV id never reaches the film search, a film id never the series
        // search, and nothing but the three pairs of the contract is identified.
        var request = Mistake(mistake);

        //Act
        var exception = await Record.ExceptionAsync(() => _jellyfin.IdentifyAsync(request!, TestContext.Current.CancellationToken));

        //Assert
        Assert.IsAssignableFrom<ArgumentException>(exception);
        Assert.Empty(_jellyfin.Http.Requests);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    /// <summary>The request each caller's mistake names.</summary>
    private static LibraryIdentifyRequest? Mistake(string name) => name switch
    {
        "no request" => null,
        "a blank item id" => FilmRequest() with { SourceItemId = "  " },
        "an item id Jellyfin never gives" => FilmRequest() with { SourceItemId = "../Users" },
        "an episode item" => FilmRequest() with { Kind = MediaWorkKind.Episode, Identifier = new MediaIdentifier(MediaIdentifierScheme.Imdb, "tt9103946") },
        "no id" => FilmRequest() with { Identifier = null! },
        "a show's id on a film" => FilmRequest() with { Identifier = new MediaIdentifier(MediaIdentifierScheme.TmdbTv, "7869") },
        "a film's id on a show" => ShowRequest() with { Identifier = new MediaIdentifier(MediaIdentifierScheme.Tmdb, "223021") },
        "an IMDb id on a show" => ShowRequest() with { Identifier = new MediaIdentifier(MediaIdentifierScheme.Imdb, "tt0892700") },
        "a TVDB id on a film" => FilmRequest() with { Identifier = new MediaIdentifier(MediaIdentifierScheme.Tvdb, "48181") },
        "a malformed TMDb id" => FilmRequest() with { Identifier = new MediaIdentifier(MediaIdentifierScheme.Tmdb, "223021a") },
        "a TMDb id with spaces" => FilmRequest() with { Identifier = new MediaIdentifier(MediaIdentifierScheme.Tmdb, " 223021") },
        "a blank TMDb id" => FilmRequest() with { Identifier = new MediaIdentifier(MediaIdentifierScheme.Tmdb, "") },
        "a malformed IMDb id" => EpisodeMovieRequest() with { Identifier = new MediaIdentifier(MediaIdentifierScheme.Imdb, "nm0001") },
        "a TMDb id on an episode listed as a film" => EpisodeMovieRequest() with { Identifier = new MediaIdentifier(MediaIdentifierScheme.TmdbTv, "7869") },
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "No such mistake."),
    };

    private static JsonObject User(string id, bool isAdministrator, bool isDisabled) => new()
    {
        ["Name"] = "Someone",
        ["Id"] = id,
        ["Policy"] = new JsonObject { ["IsAdministrator"] = isAdministrator, ["IsDisabled"] = isDisabled },
    };
}
