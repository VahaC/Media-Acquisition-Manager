using System.Net;
using System.Text.Json.Nodes;
using MediaAcquisitionManager.Core.Abstractions.Library;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Texts;
using static MediaAcquisitionManager.TestInfrastructure.Integrations.JellyfinIdentifyHarness;

namespace MediaAcquisitionManager.Infrastructure.Tests.Integrations;

// MAM-34 (0.2.1) — everything Jellyfin's Identify decides without sending Apply: an item of another type, a locked item, no
// result (or more than one) carrying the id, and — only when the caller protects held ids, as a bulk click and the repair do — an
// id the item holds that the identification would discard. Each is a value, not a failure: the caller stores its sentence beside
// the link. None of them sends Apply, and the item is not touched. (A library that saves beside the media has its own class,
// JellyfinAdapterIdentifyLibraryTests.)
public sealed class JellyfinAdapterIdentifyRefusalTests : IDisposable
{
    private const string ItemProtectedSentence =
        "This item is locked in Jellyfin (the whole item, its name or its overview), so Jellyfin wouldn't take the match. Unlock it there, then retry.";

    private readonly JellyfinIdentifyHarness _jellyfin = new();

    public void Dispose() => _jellyfin.Dispose();

    [Fact]
    public async Task Identify_of_an_item_of_another_type_is_refused_not_identifiable_and_searches_nothing()
    {
        //Arrange
        // A film's id onto the Penguins series item: its ids would mean something else — a show's TMDb number is not a
        // film's (ADR 0017) — and Jellyfin's Apply wouldn't check the type.
        var request = FilmRequest() with { SourceItemId = JellyfinSamples.PenguinsId };

        //Act
        var result = await _jellyfin.IdentifyAsync(request, TestContext.Current.CancellationToken);

        //Assert
        var identification = result.Value!;
        Assert.Equal(LibraryIdentifyOutcome.Refused, identification.Outcome);
        Assert.Equal(LibraryIdentifyRefusal.KindNotIdentifiable, identification.Refusal);
        Assert.False(identification.Sent);
        Assert.Equal(
            "Jellyfin holds an item of type “Series” under this id, not a movie, so the app didn't identify it. Sync the library and link it again.",
            TestTexts.En(identification.Sentence));
        Assert.Empty(identification.IdsBefore);
        // The users and the item, and nothing after.
        Assert.Equal(2, _jellyfin.Http.Requests.Count);
        Assert.Empty(_jellyfin.Server.Applied);
    }

    [Fact]
    public async Task Identify_of_a_film_item_asked_for_as_a_show_is_refused_not_identifiable()
    {
        //Arrange
        var request = ShowRequest() with { SourceItemId = JellyfinSamples.LandingStriplingId };

        //Act
        var result = await _jellyfin.IdentifyAsync(request, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(LibraryIdentifyRefusal.KindNotIdentifiable, result.Value!.Refusal);
        Assert.Equal(
            "Jellyfin holds an item of type “Movie” under this id, not a series, so the app didn't identify it. Sync the library and link it again.",
            TestTexts.En(result.Value.Sentence));
        Assert.Empty(_jellyfin.Server.RemoteSearches);
    }

    [Fact]
    public async Task Identify_of_an_episode_item_is_refused_not_identifiable_whatever_the_caller_calls_it()
    {
        //Arrange
        // An episode item under a film's id: Jellyfin has no search for an episode, and its Apply would give it its series'
        // ids. The type the library holds decides, not what the caller says.
        _jellyfin.Server.Episodes.Add(JellyfinSamples.EpisodeWith(
            "55555555555555555555555555555555", "Episode", JellyfinSamples.PenguinsId, 1, 1, $"{JellyfinSamples.CartoonsRoot}/Penguins/e01.mkv", "tt0000001", "1"));
        var request = FilmRequest() with { SourceItemId = "55555555555555555555555555555555" };

        //Act
        var result = await _jellyfin.IdentifyAsync(request, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(LibraryIdentifyRefusal.KindNotIdentifiable, result.Value!.Refusal);
        Assert.Contains("type “Episode”", TestTexts.En(result.Value.Sentence), StringComparison.Ordinal);
        Assert.Empty(_jellyfin.Server.Applied);
    }

    [Theory]
    [InlineData("the whole item")]
    [InlineData("its name")]
    [InlineData("its overview")]
    public async Task Identify_of_a_locked_item_is_refused_protected_and_searches_nothing(string locked)
    {
        //Arrange
        // A locked name would fail the confirmation after Apply had already deleted the images, and a locked overview would
        // keep the old text while the name, year and id still pass.
        switch (locked)
        {
            case "the whole item":
                _jellyfin.Film["LockData"] = true;
                break;
            case "its name":
                _jellyfin.Film["LockedFields"] = new JsonArray("Name");
                break;
            default:
                _jellyfin.Film["LockedFields"] = new JsonArray("Genres", "Overview");
                break;
        }

        //Act
        var result = await _jellyfin.IdentifyAsync(FilmRequest(), TestContext.Current.CancellationToken);

        //Assert
        var identification = result.Value!;
        Assert.Equal(LibraryIdentifyOutcome.Refused, identification.Outcome);
        Assert.Equal(LibraryIdentifyRefusal.ItemProtected, identification.Refusal);
        Assert.False(identification.Sent);
        Assert.Equal(ItemProtectedSentence, TestTexts.En(identification.Sentence));
        // The users, the item and the libraries: the library's options are read first, then the item's lock.
        Assert.Equal(3, _jellyfin.Http.Requests.Count);
        Assert.Empty(_jellyfin.Server.RemoteSearches);
        Assert.Empty(_jellyfin.Server.Applied);
    }

    [Fact]
    public async Task Identify_of_an_item_with_other_locked_fields_goes_on()
    {
        //Arrange
        // Genres, the cast and the like are the owner's own Jellyfin choice: only the name and the overview decide.
        _jellyfin.Film["LockedFields"] = new JsonArray("Genres", "Cast", "Studios");

        //Act
        var result = await _jellyfin.IdentifyAsync(FilmRequest(), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(LibraryIdentifyOutcome.Identified, result.Value!.Outcome);
    }

    [Theory]
    [InlineData("no row")]
    [InlineData("a row of another id")]
    [InlineData("a row whose key is lower case")]
    [InlineData("a row with the id under another key")]
    [InlineData("a row without provider ids")]
    [InlineData("a row that is not an object")]
    [InlineData("the unrelated film of the same number under a tv id")]
    public async Task Identify_with_no_row_carrying_the_id_is_no_match_and_applies_nothing(string answer)
    {
        //Arrange
        // The search answers 200 and nothing it gave carries exactly the requested id under exactly the key: the lower-case key
        // the live check sent answered [], a TV id searched as a film answered an unrelated film.
        var rows = answer switch
        {
            "no row" => "[]",
            "a row of another id" => """[{"Name":"Landing Stripling","ProviderIds":{"Tmdb":"1"},"SearchProviderName":"TheMovieDb"}]""",
            "a row whose key is lower case" => """[{"Name":"Landing Stripling","ProviderIds":{"tmdb":"223021"},"SearchProviderName":"TheMovieDb"}]""",
            "a row with the id under another key" => """[{"Name":"Landing Stripling","ProviderIds":{"TmdbCollection":"223021"},"SearchProviderName":"TheMovieDb"}]""",
            "a row without provider ids" => """[{"Name":"Landing Stripling"}]""",
            "a row that is not an object" => """["Landing Stripling"]""",
            _ => """[{"Name":"The Fest","ProviderIds":{"Tmdb":"7869","Imdb":"tt0475953"},"ProductionYear":2005,"SearchProviderName":"TheMovieDb"}]""",
        };
        _jellyfin.Server.Intercept = request => IsSearch(request) ? JsonAnswer(rows) : null;

        //Act
        var result = await _jellyfin.IdentifyAsync(FilmRequest(), TestContext.Current.CancellationToken);

        //Assert
        var identification = result.Value!;
        Assert.Equal(LibraryIdentifyOutcome.NoMatch, identification.Outcome);
        Assert.Null(identification.Refusal);
        Assert.False(identification.Sent);
        Assert.Equal(
            "Jellyfin found no film with TMDb id 223021. Check the id, and that TheMovieDb is among this library's metadata sources in Jellyfin.",
            TestTexts.En(identification.Sentence));
        Assert.Empty(identification.IdsBefore);
        Assert.Empty(_jellyfin.ApplyRequests());
    }

    [Fact]
    public async Task Identify_with_an_id_the_provider_does_not_know_is_no_match_as_the_recorded_answer_was()
    {
        //Arrange
        // Recorded: an unknown id answers 200 with an empty list.
        _jellyfin.Server.Intercept = request => IsSearch(request)
            ? VendorResponses.Recorded("Jellyfin/remote-search-movie-unknown-tmdb-id-response.txt")
            : null;

        //Act
        var result = await _jellyfin.IdentifyAsync(FilmRequest(), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(LibraryIdentifyOutcome.NoMatch, result.Value!.Outcome);
        Assert.Empty(_jellyfin.Server.Applied);
    }

    [Fact]
    public async Task Identify_of_a_show_with_no_match_says_so_in_the_shows_sentence()
    {
        //Arrange
        _jellyfin.Server.Matches.Clear();

        //Act
        var result = await _jellyfin.IdentifyAsync(ShowRequest(), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(LibraryIdentifyOutcome.NoMatch, result.Value!.Outcome);
        Assert.Equal(
            "Jellyfin found no show with TMDb id 7869. Check the id, and that TheMovieDb is among this library's metadata sources in Jellyfin.",
            TestTexts.En(result.Value.Sentence));
    }

    [Fact]
    public async Task Identify_of_an_episode_movie_with_no_match_says_so_in_the_open_movie_databases_sentence()
    {
        //Arrange
        _jellyfin.Server.Matches.Clear();

        //Act
        var result = await _jellyfin.IdentifyAsync(EpisodeMovieRequest(), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(LibraryIdentifyOutcome.NoMatch, result.Value!.Outcome);
        Assert.Equal(
            "Jellyfin's Open Movie Database found no title with IMDb id tt9103946. Check that it is among this library's metadata sources in Jellyfin.",
            TestTexts.En(result.Value.Sentence));
    }

    [Fact]
    public async Task Identify_in_a_library_that_does_not_list_the_provider_is_no_match_as_jellyfin_answers_it()
    {
        //Arrange
        // Jellyfin searches only the providers the item's library enables (IncludeDisabledProviders is false): a library
        // without TheMovieDb among its film fetchers answers [].
        var options = _jellyfin.Server.LibraryOptionsOf(JellyfinSamples.CartoonsRoot);
        var movieOptions = options["TypeOptions"]!.AsArray().OfType<JsonObject>().Single(typeOptions => (string?)typeOptions["Type"] == "Movie");
        movieOptions["MetadataFetchers"] = new JsonArray("AniList", "The Open Movie Database");

        //Act
        var result = await _jellyfin.IdentifyAsync(FilmRequest(), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(LibraryIdentifyOutcome.NoMatch, result.Value!.Outcome);
        Assert.Single(_jellyfin.Server.RemoteSearches);
        Assert.Empty(_jellyfin.Server.Applied);
    }

    [Fact]
    public async Task Identify_with_two_rows_carrying_the_id_is_no_match_ambiguous()
    {
        //Arrange
        var row = JellyfinSamples.RemoteSearchMovieByTmdbRows()[0]!.ToJsonString();
        _jellyfin.Server.Intercept = request => IsSearch(request) ? JsonAnswer($"[{row},{row}]") : null;

        //Act
        var result = await _jellyfin.IdentifyAsync(FilmRequest(), TestContext.Current.CancellationToken);

        //Assert
        var identification = result.Value!;
        Assert.Equal(LibraryIdentifyOutcome.NoMatch, identification.Outcome);
        Assert.Equal("Jellyfin found more than one result for TMDb id 223021, so the app picked none.", TestTexts.En(identification.Sentence));
        Assert.Empty(_jellyfin.Server.Applied);
    }

    [Fact]
    public async Task Identify_of_an_episode_movie_with_two_rows_carrying_its_imdb_id_is_no_match_ambiguous()
    {
        //Arrange
        // MAM-47 — the registry is part of the sentence's key (no-match.ambiguous-imdb), so the IMDb wording is pinned too.
        var row = JellyfinSamples.RemoteSearchOmdbRows()[0]!.ToJsonString();
        _jellyfin.Server.Intercept = request => IsSearch(request) ? JsonAnswer($"[{row},{row}]") : null;

        //Act
        var result = await _jellyfin.IdentifyAsync(EpisodeMovieRequest(), TestContext.Current.CancellationToken);

        //Assert
        var identification = result.Value!;
        Assert.Equal(LibraryIdentifyOutcome.NoMatch, identification.Outcome);
        Assert.Equal("Jellyfin found more than one result for IMDb id tt9103946, so the app picked none.", TestTexts.En(identification.Sentence));
        Assert.Empty(_jellyfin.Server.Applied);
    }

    [Fact]
    public async Task Identify_of_an_episode_movie_with_a_row_without_a_name_is_no_match()
    {
        //Arrange
        _jellyfin.Server.Intercept = request => IsSearch(request)
            ? JsonAnswer("""[{"ProviderIds":{"Imdb":"tt9103946"},"SearchProviderName":"The Open Movie Database"}]""")
            : null;

        //Act
        var result = await _jellyfin.IdentifyAsync(EpisodeMovieRequest(), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(LibraryIdentifyOutcome.NoMatch, result.Value!.Outcome);
        Assert.Equal("Jellyfin's result for IMDb id tt9103946 has no name, so the app picked none.", TestTexts.En(result.Value.Sentence));
        Assert.Empty(_jellyfin.Server.Applied);
    }

    [Theory]
    [InlineData("""[{"ProviderIds":{"Tmdb":"223021"},"SearchProviderName":"TheMovieDb"}]""")]
    [InlineData("""[{"Name":"   ","ProviderIds":{"Tmdb":"223021"},"SearchProviderName":"TheMovieDb"}]""")]
    [InlineData("""[{"Name":7,"ProviderIds":{"Tmdb":"223021"},"SearchProviderName":"TheMovieDb"}]""")]
    public async Task Identify_with_a_row_without_a_name_is_no_match(string rows)
    {
        //Arrange
        _jellyfin.Server.Intercept = request => IsSearch(request) ? JsonAnswer(rows) : null;

        //Act
        var result = await _jellyfin.IdentifyAsync(FilmRequest(), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(LibraryIdentifyOutcome.NoMatch, result.Value!.Outcome);
        Assert.Equal("Jellyfin's result for TMDb id 223021 has no name, so the app picked none.", TestTexts.En(result.Value.Sentence));
        Assert.Empty(_jellyfin.Server.Applied);
    }

    [Theory]
    // A different TMDb id, in any letter case of its key, and beside the IMDb id that agrees.
    [InlineData("""{"Tmdb":"1001"}""", "TMDb", "1001", "223021")]
    [InlineData("""{"TMDB":"1001"}""", "TMDb", "1001", "223021")]
    [InlineData("""{"Tmdb":"1001","Imdb":"tt0056164"}""", "TMDb", "1001", "223021")]
    // A different IMDb id than the one the row carries.
    [InlineData("""{"Imdb":"tt0000001"}""", "IMDb", "tt0000001", "tt0056164")]
    // The Polanski mistake: the TMDb id the earlier code wrote equals the link's, the IMDb id is the documentary's.
    [InlineData("""{"Tmdb":"223021","Imdb":"tt1163854"}""", "IMDb", "tt1163854", "tt0056164")]
    public async Task Identify_with_protected_held_ids_refuses_the_ones_the_identification_would_discard(
        string providerIds, string scheme, string heldValue, string wantedValue)
    {
        //Arrange
        // What the library holds that the identification would replace: the four evident mistakes of the owner's library sit
        // here — a link to another work than the one Jellyfin knows. The bulk click and the repair protect them.
        _jellyfin.Film["ProviderIds"] = JsonNode.Parse(providerIds);

        //Act
        var result = await _jellyfin.IdentifyAsync(FilmRequest(protectHeldIds: true), TestContext.Current.CancellationToken);

        //Assert
        var identification = result.Value!;
        Assert.Equal(LibraryIdentifyOutcome.Refused, identification.Outcome);
        Assert.Equal(LibraryIdentifyRefusal.ConflictingId, identification.Refusal);
        Assert.False(identification.Sent);
        Assert.Equal(
            $"Jellyfin holds {scheme} id {heldValue} for this item, not {wantedValue}, and the app doesn't replace a library's own ids on its own. If the link is right, press Retry to identify it anyway; otherwise change the link.",
            TestTexts.En(identification.Sentence));
        // Nothing sent: the item read and the search made, no more.
        Assert.Equal(4, _jellyfin.Http.Requests.Count);
        Assert.Empty(_jellyfin.Server.Applied);
    }

    [Fact]
    public async Task Identify_of_an_episode_movie_with_protected_held_ids_refuses_a_different_imdb_id_which_is_typically_the_shows()
    {
        //Arrange
        // The item of an episode usually holds the SHOW's IMDb id (The Continental's, in the reference library).
        _jellyfin.EpisodeMovie["ProviderIds"] = new JsonObject { ["Imdb"] = "tt6486762" };

        //Act
        var result = await _jellyfin.IdentifyAsync(EpisodeMovieRequest(protectHeldIds: true), TestContext.Current.CancellationToken);

        //Assert
        var identification = result.Value!;
        Assert.Equal(LibraryIdentifyRefusal.ConflictingId, identification.Refusal);
        Assert.Equal(
            "Jellyfin holds IMDb id tt6486762 for this item, not tt9103946, and the app doesn't replace a library's own ids on its own. If the link is right, press Retry to identify it anyway; otherwise change the link.",
            TestTexts.En(identification.Sentence));
        Assert.Empty(_jellyfin.Server.Applied);
    }

    [Fact]
    public async Task Identify_of_an_episode_movie_holding_the_shows_imdb_id_replaces_it_when_the_owner_asks_himself()
    {
        //Arrange
        // The Continental mistake as the owner's own click meets it: the item holds the series' IMDb id, the link names the
        // episode's. Only the bulk paths protect it; his own Retry is his word.
        _jellyfin.EpisodeMovie["ProviderIds"] = new JsonObject { ["Imdb"] = "tt29669983" };

        //Act
        var result = await _jellyfin.IdentifyAsync(EpisodeMovieRequest(protectHeldIds: false), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(LibraryIdentifyOutcome.Identified, result.Value!.Outcome);
        Assert.Equal("""{"Imdb":"tt9103946"}""", _jellyfin.EpisodeMovie["ProviderIds"]!.ToJsonString());
    }

    [Fact]
    public async Task Identify_of_an_episode_movie_holding_the_continentals_series_imdb_id_is_refused_when_protected()
    {
        //Arrange
        _jellyfin.EpisodeMovie["ProviderIds"] = new JsonObject { ["Imdb"] = "tt29669983" };

        //Act
        var result = await _jellyfin.IdentifyAsync(EpisodeMovieRequest(protectHeldIds: true), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(LibraryIdentifyRefusal.ConflictingId, result.Value!.Refusal);
        Assert.Contains("IMDb id tt29669983", TestTexts.En(result.Value.Sentence), StringComparison.Ordinal);
        Assert.Empty(_jellyfin.Server.Applied);
    }

    [Fact]
    public async Task Identify_with_protected_held_imdb_and_a_row_without_imdb_is_refused_with_the_uncheckable_sentence()
    {
        //Arrange
        // A Polanski-type mistake: the item holds the link's TMDb id — the earlier code wrote it — and the IMDb id of another
        // film, and TMDb happens to give no IMDb id to compare it with: it fails closed.
        _jellyfin.Film["ProviderIds"] = new JsonObject { ["Tmdb"] = "223021", ["Imdb"] = "tt1163854" };
        _jellyfin.Server.Matches[0].Row["ProviderIds"]!.AsObject().Remove("Imdb");

        //Act
        var result = await _jellyfin.IdentifyAsync(FilmRequest(protectHeldIds: true), TestContext.Current.CancellationToken);

        //Assert
        var identification = result.Value!;
        Assert.Equal(LibraryIdentifyOutcome.Refused, identification.Outcome);
        Assert.Equal(LibraryIdentifyRefusal.ConflictingId, identification.Refusal);
        Assert.Equal(
            "Jellyfin holds IMDb id tt1163854 for this item and TMDb gives no IMDb id for this link to compare it with, so the app doesn't replace a library's own id on its own. If the link is right, press Retry to identify it anyway; otherwise change the link.",
            TestTexts.En(identification.Sentence));
        Assert.Empty(_jellyfin.Server.Applied);
    }

    [Fact]
    public async Task Identify_with_unprotected_held_imdb_and_a_row_without_imdb_replaces_it()
    {
        //Arrange
        // The owner's own click: "If the link is right, press Retry to identify it anyway".
        _jellyfin.Film["ProviderIds"] = new JsonObject { ["Tmdb"] = "223021", ["Imdb"] = "tt1163854" };
        _jellyfin.Server.Matches[0].Row["ProviderIds"]!.AsObject().Remove("Imdb");

        //Act
        var result = await _jellyfin.IdentifyAsync(FilmRequest(protectHeldIds: false), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(LibraryIdentifyOutcome.Identified, result.Value!.Outcome);
        Assert.Single(_jellyfin.Server.Applied);
    }

    [Theory]
    [InlineData("""{"Tmdb":"1001"}""")]
    [InlineData("""{"Tmdb":"1001","Imdb":"tt0000001"}""")]
    [InlineData("""{"Imdb":"tt0000001"}""")]
    public async Task Identify_with_unprotected_held_ids_replaces_them(string providerIds)
    {
        //Arrange
        // The same items as the protected theory, but the owner's own word: Identify replaces the ids as Jellyfin's button
        // does — a replace or a hand fix of a wrong match would otherwise be refused by the ids Identify left itself.
        _jellyfin.Film["ProviderIds"] = JsonNode.Parse(providerIds);

        //Act
        var result = await _jellyfin.IdentifyAsync(FilmRequest(protectHeldIds: false), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(LibraryIdentifyOutcome.Identified, result.Value!.Outcome);
        Assert.Single(_jellyfin.Server.Applied);
        Assert.Equal("223021", (string?)_jellyfin.Film["ProviderIds"]!["Tmdb"]);
    }

    [Fact]
    public async Task Identify_of_an_episode_movie_does_not_count_a_held_film_id_as_a_conflict()
    {
        //Arrange
        // The item is an episode, so a film's TMDb id on it is the one that is wrong: Apply drops it (recorded live).
        _jellyfin.EpisodeMovie["ProviderIds"] = new JsonObject { ["Imdb"] = "tt9103946", ["Tmdb"] = "1001" };

        //Act
        var result = await _jellyfin.IdentifyAsync(EpisodeMovieRequest(protectHeldIds: true), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(LibraryIdentifyOutcome.Identified, result.Value!.Outcome);
        Assert.Equal("""{"Imdb":"tt9103946"}""", _jellyfin.EpisodeMovie["ProviderIds"]!.ToJsonString());
    }

    [Theory]
    [InlineData("""{"Tmdb":"not-an-id"}""")]
    [InlineData("""{"Tmdb":"  "}""")]
    [InlineData("""{"Imdb":"nm0000001"}""")]
    [InlineData("""{"Tvdb":"48181","TvdbSlug":"landing-stripling"}""")]
    [InlineData("""{"TmdbCollection":"8091"}""")]
    [InlineData("""{"Tmdb":"223021","Imdb":"tt0056164"}""")]
    [InlineData("""{"tmdb":" 223021 ","IMDB":"TT0056164"}""")]
    public async Task Identify_with_protected_held_ids_that_are_malformed_blank_other_keys_or_equal_never_conflicts(string providerIds)
    {
        //Arrange
        // A malformed or blank value is not held, as a sync doesn't read it either; Tvdb and the other keys never count; an
        // equal value never conflicts, in any letter case of its key.
        _jellyfin.Film["ProviderIds"] = JsonNode.Parse(providerIds);

        //Act
        var result = await _jellyfin.IdentifyAsync(FilmRequest(protectHeldIds: true), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(LibraryIdentifyOutcome.Identified, result.Value!.Outcome);
        Assert.Single(_jellyfin.Server.Applied);
    }

    [Fact]
    public async Task Identify_of_a_show_with_a_different_held_tmdb_id_is_refused_when_protected()
    {
        //Arrange
        _jellyfin.Show["ProviderIds"] = new JsonObject { ["Tmdb"] = "1001", ["Imdb"] = "tt0892700" };

        //Act
        var result = await _jellyfin.IdentifyAsync(ShowRequest(protectHeldIds: true), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(LibraryIdentifyRefusal.ConflictingId, result.Value!.Refusal);
        Assert.Equal(
            "Jellyfin holds TMDb id 1001 for this item, not 7869, and the app doesn't replace a library's own ids on its own. If the link is right, press Retry to identify it anyway; otherwise change the link.",
            TestTexts.En(result.Value.Sentence));
        Assert.Empty(_jellyfin.Server.Applied);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static bool IsSearch(HttpRequestMessage request) =>
        request.Method == HttpMethod.Post && request.RequestUri!.AbsolutePath.StartsWith("/Items/RemoteSearch/", StringComparison.Ordinal)
        && !request.RequestUri.AbsolutePath.StartsWith("/Items/RemoteSearch/Apply/", StringComparison.Ordinal);
}
