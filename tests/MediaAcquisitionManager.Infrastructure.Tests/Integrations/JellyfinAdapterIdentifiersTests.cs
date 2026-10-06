using System.IO.Pipelines;
using System.Net;
using System.Text.Json.Nodes;
using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Abstractions.Library;
using MediaAcquisitionManager.Core.Abstractions.Media;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Infrastructure.Integrations.Jellyfin;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;

namespace MediaAcquisitionManager.Infrastructure.Tests.Integrations;

// MAM-34 — the owner's TMDb link written into a Jellyfin item's ProviderIds (IMediaLibraryIdentifierWriter), against
// the fake Jellyfin serving the items the live check read whole (VendorSamples/Jellyfin: item.json, series-item.json,
// users.json and the recorded .txt answers): a movie or series item — a series with its show's TMDb id under the same
// Tmdb key (Vasyl's final decision of 2026-10-06), an episode item refused before anything is sent —, read as the first
// enabled administrator, sent back once as read but for its ids and without its people — IMDb only where it holds none,
// a restore only over what still holds the app's value — and read again to confirm; nothing posted when nothing
// changes; every refusal a status and a sentence; and a write that may have landed (its answer lost, late or a server
// error, or a read after it that failed or disagreed) still a success, sent and not confirmed (R10), never sent twice. The lookup of the item's missing metadata asked after a write
// has a class of its own (JellyfinAdapterMetadataLookupTests).
public sealed class JellyfinAdapterIdentifiersTests : IDisposable
{
    private const string BaseUrl = "http://jellyfin.test:8096";
    private const string ApiKey = "4f1d0b9c2e7a4c3b8d6e5f7a9b0c1d2e";

    private const string KeyRefusedError =
        "Jellyfin refused the API key. Create one in Jellyfin under Dashboard → API Keys and enter it here.";

    private const string ItemGoneError = "Jellyfin no longer has this item; sync the library and link it again.";

    private const string NotJellyfinError =
        "The service at this address answered, but not the way Jellyfin does. Check the address.";

    private const string AnswerLostProblem =
        "The change was sent to Jellyfin, but its answer never came, so the app can't tell whether Jellyfin saved it; the next sync shows what it holds.";

    private const string NotConfirmedProblem = "Jellyfin took the change, but the item read again holds other ids than the ones sent.";

    private static readonly IntegrationConnectionSettings Settings = new(null, BaseUrl, null, null, ApiKey);

    /// <summary>A film link's edits, as the link writer sends them: its TMDb id whatever the item holds, and the
    /// IMDb id TMDb gave only where Jellyfin has none.</summary>
    private static readonly LibraryIdentifierEdit[] FilmLinkEdits =
    [
        new(MediaIdentifierScheme.Tmdb, JellyfinSamples.LandingStriplingTmdbId, LibraryIdentifierEditRule.Always),
        new(MediaIdentifierScheme.Imdb, JellyfinSamples.LandingStriplingImdbId, LibraryIdentifierEditRule.OnlyWhereAbsent),
    ];

    private readonly IntegrationAdapterHarness _harness = new();
    private readonly FakeJellyfinServer _server = new(BaseUrl) { ApiKey = ApiKey };

    public JellyfinAdapterIdentifiersTests()
    {
        // The two items the live check read whole, beside the sample movies the sync reads.
        _server.Movies.Add(JellyfinSamples.Item());
        _server.Series.Add(JellyfinSamples.SeriesItem());
        _server.Arm(_harness.JellyfinHttp);
    }

    private IMediaLibraryIdentifierWriter Writer => _harness.Adapter<IMediaLibraryIdentifierWriter>(JellyfinAdapter.TypeId);

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task WriteIdentifiers_reads_the_users_then_the_item_as_the_administrator_posts_it_once_and_reads_it_again()
    {
        //Arrange
        var itemPath = $"/Items/{JellyfinSamples.LandingStriplingId}";

        //Act
        var result = await Writer.WriteIdentifiersAsync(Settings, FilmChange(FilmLinkEdits), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        Assert.Null(result.Error);
        var write = result.Value!;
        Assert.Empty(write.Before);
        Assert.Equal(["Tmdb 223021", "Imdb tt0056164"], Describe(write.After));
        Assert.Equal([MediaIdentifierScheme.Tmdb, MediaIdentifierScheme.Imdb], write.Applied);
        Assert.True(write.Sent);
        Assert.True(write.Confirmed);
        Assert.Null(write.Problem);
        Assert.Equal(
            [
                "GET /Users",
                $"GET {itemPath}?userId={JellyfinSamples.AdministratorUserId}",
                $"POST {itemPath}",
                $"GET {itemPath}?userId={JellyfinSamples.AdministratorUserId}",
            ],
            _harness.JellyfinHttp.Requests.Select(request => $"{request.Method} {request.Uri.PathAndQuery}"));
    }

    [Fact]
    public async Task WriteIdentifiers_posts_the_movie_as_read_without_its_people_and_with_only_its_provider_ids_changed()
    {
        //Arrange
        var read = JellyfinSamples.Item();

        //Act
        await Writer.WriteIdentifiersAsync(Settings, FilmChange(FilmLinkEdits), TestContext.Current.CancellationToken);

        //Assert
        var request = Assert.Single(_harness.JellyfinHttp.Requests, recorded => recorded.Method == HttpMethod.Post);
        Assert.Equal(["application/json"], request.HeaderValues("Content-Type"));
        var posted = JsonNode.Parse(request.Body!)!.AsObject();
        // The 46 other fields in Jellyfin's order, each as read — Jellyfin's update replaces the item with what it is
        // sent, so a field changed would be changed in Jellyfin too. People alone is left out: sent, Jellyfin would
        // rebuild the cast from it; left out, it keeps the cast it has.
        Assert.Equal(read.Select(property => property.Key).Where(key => key != "People"), posted.Select(property => property.Key));
        Assert.True(JsonNode.DeepEquals(WithoutPeople(WithoutProviderIds(read)), WithoutProviderIds(posted)));
        // The ids exactly as the live check wrote them (item-after-update.json).
        Assert.Equal(JellyfinSamples.ItemAfterUpdate()["ProviderIds"]!.ToJsonString(), posted["ProviderIds"]!.ToJsonString());
    }

    [Fact]
    public async Task WriteIdentifiers_leaves_the_people_jellyfin_holds_out_of_the_body_so_it_keeps_its_cast()
    {
        //Arrange
        // The live check's item after "Search for missing metadata": Jellyfin filled in its director and producer.
        var refreshed = JellyfinSamples.ItemAfterMissingRefresh();
        _server.Movies[_server.Movies.IndexOf(Stored())] = refreshed;
        var read = refreshed.DeepClone().AsObject();
        var change = FilmChange(new LibraryIdentifierEdit(MediaIdentifierScheme.Tmdb, "1001", LibraryIdentifierEditRule.Always));

        //Act
        var result = await Writer.WriteIdentifiersAsync(Settings, change, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal((true, true), (result.Value!.Sent, result.Value.Confirmed));
        var posted = JsonNode.Parse(Assert.Single(_harness.JellyfinHttp.Requests, request => request.Method == HttpMethod.Post).Body!)!.AsObject();
        // Sent, the cast would be rebuilt from the names, roles and types alone (ItemUpdateController.UpdateItem).
        Assert.False(posted.ContainsKey("People"));
        Assert.Equal(["Gene Deitch", "William L. Snyder"], read["People"]!.AsArray().Select(person => (string?)person!["Name"]));
        Assert.True(JsonNode.DeepEquals(WithoutPeople(WithoutProviderIds(read)), WithoutProviderIds(posted)));
        Assert.Equal("""{"Tmdb":"1001","Imdb":"tt0056164","Tvdb":"48181","TvdbSlug":"landing-stripling"}""", posted["ProviderIds"]!.ToJsonString());
    }

    [Fact]
    public async Task WriteIdentifiers_of_a_series_posts_it_once_as_read_without_its_people_with_the_shows_id_under_tmdb_and_reads_it_again()
    {
        //Arrange
        // The Penguins series item the live check wrote, and its one edit: the show's TMDb id (Vasyl's final decision of
        // 2026-10-06 — a series is written like a film).
        var read = JellyfinSamples.SeriesItem();
        var itemPath = $"/Items/{JellyfinSamples.PenguinsId}";
        var change = SeriesChange(new LibraryIdentifierEdit(MediaIdentifierScheme.TmdbTv, JellyfinSamples.PenguinsTmdbTvId, LibraryIdentifierEditRule.Always));

        //Act
        var result = await Writer.WriteIdentifiersAsync(Settings, change, TestContext.Current.CancellationToken);

        //Assert
        var write = result.Value!;
        Assert.Equal((IntegrationCallStatus.Succeeded, true, true, (string?)null), (result.Status, write.Sent, write.Confirmed, write.Problem));
        Assert.Empty(write.Before);
        Assert.Equal(["TmdbTv 7869"], Describe(write.After));
        Assert.Equal([MediaIdentifierScheme.TmdbTv], write.Applied);
        Assert.Equal(
            [
                "GET /Users",
                $"GET {itemPath}?userId={JellyfinSamples.AdministratorUserId}",
                $"POST {itemPath}",
                $"GET {itemPath}?userId={JellyfinSamples.AdministratorUserId}",
            ],
            _harness.JellyfinHttp.Requests.Select(request => $"{request.Method} {request.Uri.PathAndQuery}"));
        var posted = JsonNode.Parse(Assert.Single(_harness.JellyfinHttp.Requests, request => request.Method == HttpMethod.Post).Body!)!.AsObject();
        // Every other field as read, in Jellyfin's order, and no People; the id under Jellyfin's Tmdb key, exactly as the
        // live check wrote it (series-item-after-update.json).
        Assert.Equal(read.Select(property => property.Key).Where(key => key != "People"), posted.Select(property => property.Key));
        Assert.True(JsonNode.DeepEquals(WithoutPeople(WithoutProviderIds(read)), WithoutProviderIds(posted)));
        Assert.Equal(JellyfinSamples.SeriesItemAfterUpdate()["ProviderIds"]!.ToJsonString(), posted["ProviderIds"]!.ToJsonString());
    }

    [Fact]
    public async Task WriteIdentifiers_of_a_series_sets_its_imdb_id_only_where_it_holds_none_and_keeps_its_other_ids()
    {
        //Arrange
        // A series Jellyfin matched without its show's TMDb id, holding another IMDb id and ids of other registries.
        StoredSeries()["ProviderIds"] = new JsonObject { ["Imdb"] = "tt0000001", ["Tvdb"] = "85075", ["TvMaze"] = "4037" };
        var change = SeriesChange(
            new LibraryIdentifierEdit(MediaIdentifierScheme.TmdbTv, JellyfinSamples.PenguinsTmdbTvId, LibraryIdentifierEditRule.Always),
            new LibraryIdentifierEdit(MediaIdentifierScheme.Imdb, "tt0892700", LibraryIdentifierEditRule.OnlyWhereAbsent));

        //Act
        var result = await Writer.WriteIdentifiersAsync(Settings, change, TestContext.Current.CancellationToken);

        //Assert
        var write = result.Value!;
        Assert.Equal((true, true), (write.Sent, write.Confirmed));
        Assert.Equal([MediaIdentifierScheme.TmdbTv], write.Applied);
        Assert.Equal(["TmdbTv 7869", "Imdb tt0000001"], Describe(write.After));
        var posted = JsonNode.Parse(Assert.Single(_harness.JellyfinHttp.Requests, request => request.Method == HttpMethod.Post).Body!)!.AsObject();
        Assert.Equal("""{"Imdb":"tt0000001","Tvdb":"85075","TvMaze":"4037","Tmdb":"7869"}""", posted["ProviderIds"]!.ToJsonString());
    }

    [Fact]
    public void WritableKinds_are_films_and_series_and_an_episode_item_says_why_its_link_stays_in_the_app()
    {
        //Arrange

        //Act
        var kinds = Writer.WritableKinds;

        //Assert
        Assert.Equal([MediaWorkKind.Movie, MediaWorkKind.Series], kinds);
        Assert.Equal(
            "Jellyfin's ids are written on movie and series items only, so a link on an episode stays in the app.",
            Writer.UnwritableKindReason(MediaWorkKind.Episode));
    }

    [Theory]
    [InlineData("tt1234567", "tt1234567")]
    // A malformed id still counts as Jellyfin's: the app never writes over what it holds.
    [InlineData("not-an-imdb-id", "not-an-imdb-id")]
    // A blank value is no id: the IMDb id goes under that same key.
    [InlineData("", "tt0056164")]
    [InlineData("   ", "tt0056164")]
    public async Task WriteIdentifiers_writes_the_imdb_id_only_where_the_item_holds_none(string heldImdb, string expectedImdb)
    {
        //Arrange
        HoldProviderIds(new JsonObject { ["Imdb"] = heldImdb });

        //Act
        var result = await Writer.WriteIdentifiersAsync(Settings, FilmChange(FilmLinkEdits), TestContext.Current.CancellationToken);

        //Assert
        var write = result.Value!;
        Assert.Equal(heldImdb == expectedImdb ? [MediaIdentifierScheme.Tmdb] : [MediaIdentifierScheme.Tmdb, MediaIdentifierScheme.Imdb], write.Applied);
        Assert.True(write.Confirmed);
        var posted = PostedProviderIds();
        Assert.Equal(expectedImdb, (string?)posted["Imdb"]);
        Assert.Equal(JellyfinSamples.LandingStriplingTmdbId, (string?)posted["Tmdb"]);
    }

    [Fact]
    public async Task WriteIdentifiers_reuses_the_spelling_of_a_key_the_item_has_and_keeps_every_other_id()
    {
        //Arrange
        // Jellyfin's ids ignore case, so a second spelling of the key would win over the edit: it goes.
        HoldProviderIds(new JsonObject { ["Tvdb"] = "79257", ["TMDB"] = "1001", ["TmdbCollection"] = "8091", ["tmdb"] = "1002" });

        //Act
        var result = await Writer.WriteIdentifiersAsync(Settings, FilmChange(FilmLinkEdits), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(["Tmdb 1001"], Describe(result.Value!.Before));
        Assert.True(result.Value.Confirmed);
        Assert.Equal(
            """{"Tvdb":"79257","TMDB":"223021","TmdbCollection":"8091","Imdb":"tt0056164"}""",
            PostedProviderIds().ToJsonString());
    }

    [Theory]
    // Taken out: the item held nothing before the app wrote.
    [InlineData(null, """{}""")]
    // Put back: the item held another id before the app wrote over it.
    [InlineData("1001", """{"Tmdb":"1001"}""")]
    public async Task WriteIdentifiers_with_only_where_expected_changes_back_what_still_holds_the_apps_value(string? restored, string expectedProviderIds)
    {
        //Arrange
        HoldProviderIds(new JsonObject { ["Tmdb"] = JellyfinSamples.LandingStriplingTmdbId, ["Imdb"] = JellyfinSamples.LandingStriplingImdbId });
        var change = FilmChange(
            new(MediaIdentifierScheme.Tmdb, restored, LibraryIdentifierEditRule.OnlyWhereExpected, JellyfinSamples.LandingStriplingTmdbId),
            new(MediaIdentifierScheme.Imdb, null, LibraryIdentifierEditRule.OnlyWhereExpected, JellyfinSamples.LandingStriplingImdbId));

        //Act
        var result = await Writer.WriteIdentifiersAsync(Settings, change, TestContext.Current.CancellationToken);

        //Assert
        var write = result.Value!;
        Assert.Equal(["Tmdb 223021", "Imdb tt0056164"], Describe(write.Before));
        Assert.Equal(restored is null ? [] : ["Tmdb 1001"], Describe(write.After));
        Assert.Equal([MediaIdentifierScheme.Tmdb, MediaIdentifierScheme.Imdb], write.Applied);
        Assert.Equal((true, true), (write.Sent, write.Confirmed));
        Assert.Equal(expectedProviderIds, PostedProviderIds().ToJsonString());
    }

    [Theory]
    // Someone changed the id since the app wrote it (decision 13): theirs stays.
    [InlineData("""{"Tmdb":"1001"}""", "223021")]
    // "Only where none is held": the item holds one.
    [InlineData("""{"Tmdb":"1001"}""", null)]
    public async Task WriteIdentifiers_with_only_where_expected_leaves_a_value_that_isnt_the_expected_one_and_posts_nothing(string held, string? expected)
    {
        //Arrange
        HoldProviderIds(JsonNode.Parse(held)!.AsObject());
        var change = FilmChange(new LibraryIdentifierEdit(MediaIdentifierScheme.Tmdb, expected is null ? "223021" : null, LibraryIdentifierEditRule.OnlyWhereExpected, expected));

        //Act
        var result = await Writer.WriteIdentifiersAsync(Settings, change, TestContext.Current.CancellationToken);

        //Assert
        var write = result.Value!;
        Assert.Equal((false, true), (write.Sent, write.Confirmed));
        Assert.Empty(write.Applied);
        Assert.Equal(["Tmdb 1001"], Describe(write.Before));
        Assert.Equal(["Tmdb 1001"], Describe(write.After));
        Assert.DoesNotContain(_harness.JellyfinHttp.Requests, request => request.Method == HttpMethod.Post);
    }

    [Theory]
    [InlineData("""{"Tmdb":"223021","Imdb":"tt0056164"}""", "tt0056164")]
    // Compared trimmed and in any letter case, as every reader of ids does.
    [InlineData("""{"tmdb":" 223021 ","IMDB":"TT0056164"}""", "TT0056164")]
    public async Task WriteIdentifiers_posts_nothing_when_the_item_already_holds_the_ids(string held, string heldImdb)
    {
        //Arrange
        HoldProviderIds(JsonNode.Parse(held)!.AsObject());

        //Act
        var result = await Writer.WriteIdentifiersAsync(Settings, FilmChange(FilmLinkEdits), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        var write = result.Value!;
        Assert.Equal((false, true, null), (write.Sent, write.Confirmed, write.Problem));
        Assert.Empty(write.Applied);
        Assert.Equal(["Tmdb 223021", $"Imdb {heldImdb}"], Describe(write.Before));
        Assert.Equal(Describe(write.Before), Describe(write.After));
        // The users and the item: nothing written, nothing read again.
        Assert.All(_harness.JellyfinHttp.Requests, request => Assert.Equal(HttpMethod.Get, request.Method));
        Assert.Equal(2, _harness.JellyfinHttp.Requests.Count);
    }

    [Fact]
    public async Task WriteIdentifiers_reads_the_item_as_the_first_enabled_administrator()
    {
        //Arrange
        var disabledAdministrator = Guid.NewGuid().ToString("N");
        var laterAdministrator = Guid.NewGuid().ToString("N");
        _server.Users.Insert(0, User(disabledAdministrator, isAdministrator: true, isDisabled: true));
        // An administrator whose id Jellyfin would never give: it can't go into a query.
        _server.Users.Insert(1, User("../Users", isAdministrator: true, isDisabled: false));
        _server.Users.Add(User(laterAdministrator, isAdministrator: true, isDisabled: false));

        //Act
        var result = await Writer.WriteIdentifiersAsync(Settings, FilmChange(FilmLinkEdits), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        Assert.All(
            _harness.JellyfinHttp.Requests.Where(request => request.Method == HttpMethod.Get && request.Uri.AbsolutePath.StartsWith("/Items/", StringComparison.Ordinal)),
            request => Assert.Equal($"?userId={JellyfinSamples.AdministratorUserId}", request.Uri.Query));
    }

    [Fact]
    public async Task WriteIdentifiers_without_an_enabled_administrator_is_refused_and_reads_no_item()
    {
        //Arrange
        _server.Users[1]!["Policy"]!["IsDisabled"] = true;

        //Act
        var result = await Writer.WriteIdentifiersAsync(Settings, FilmChange(FilmLinkEdits), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Refused, result.Status);
        Assert.Equal("Jellyfin has no enabled administrator account to read the item as.", result.Error);
        Assert.Null(result.Value);
        Assert.Equal("/Users", Assert.Single(_harness.JellyfinHttp.Requests).Uri.AbsolutePath);
    }

    [Theory]
    [InlineData(400, IntegrationCallStatus.Refused, "Jellyfin refused the item it was sent back; nothing was changed.")]
    [InlineData(401, IntegrationCallStatus.AuthenticationFailed, KeyRefusedError)]
    [InlineData(403, IntegrationCallStatus.Refused,
        "Jellyfin refused to change the item's ids. Check that the API key belongs to an administrator.")]
    [InlineData(404, IntegrationCallStatus.NotFound, ItemGoneError)]
    [InlineData(409, IntegrationCallStatus.UnexpectedResponse,
        "Jellyfin answered 409 Conflict, which it doesn't send for this request. Check the address.")]
    [InlineData(418, IntegrationCallStatus.UnexpectedResponse,
        "Jellyfin answered 418, which it doesn't send for this request. Check the address.")]
    [InlineData(302, IntegrationCallStatus.UnexpectedResponse,
        "The address answered with a redirect. Enter the address the service is really at: the app doesn't follow redirects, so your credentials never go anywhere else.")]
    public async Task WriteIdentifiers_answers_each_refusal_of_the_change_with_its_status_and_sentence(
        int statusCode, IntegrationCallStatus expectedStatus, string expectedMessage)
    {
        //Arrange
        _server.Intercept = request => request.Method == HttpMethod.Post
            ? new HttpResponseMessage((HttpStatusCode)statusCode) { ReasonPhrase = "Made ‮up by the server" }
            : null;

        //Act
        var result = await Writer.WriteIdentifiersAsync(Settings, FilmChange(FilmLinkEdits), TestContext.Current.CancellationToken);

        //Assert
        // Jellyfin changed nothing: a failure, not a write that may have landed — and nothing is read again.
        Assert.Equal(expectedStatus, result.Status);
        Assert.Equal(expectedMessage, result.Error);
        Assert.Null(result.Value);
        Assert.Equal(HttpMethod.Post, _harness.JellyfinHttp.Requests[^1].Method);
        Assert.Single(_harness.JellyfinHttp.Requests, request => request.Method == HttpMethod.Post);
    }

    [Theory]
    [InlineData(JellyfinSamples.ItemUpdateWrongKeyResponseFile, IntegrationCallStatus.AuthenticationFailed, KeyRefusedError)]
    [InlineData(JellyfinSamples.ItemUpdateUnknownItemResponseFile, IntegrationCallStatus.NotFound, ItemGoneError)]
    public async Task WriteIdentifiers_answers_the_refusals_jellyfin_recorded_with_their_status_and_sentence(
        string recordedAnswer, IntegrationCallStatus expectedStatus, string expectedMessage)
    {
        //Arrange
        // The live check's answers to a write with a wrong key, and to a write of an item that doesn't exist.
        _server.Intercept = request => request.Method == HttpMethod.Post ? VendorResponses.Recorded(recordedAnswer) : null;

        //Act
        var result = await Writer.WriteIdentifiersAsync(Settings, FilmChange(FilmLinkEdits), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(expectedStatus, result.Status);
        Assert.Equal(expectedMessage, result.Error);
        Assert.Empty(StoredProviderIds());
    }

    [Fact]
    public async Task WriteIdentifiers_of_an_item_jellyfin_no_longer_has_is_not_found_and_posts_nothing()
    {
        //Arrange
        var change = new LibraryIdentifierChange(Guid.NewGuid().ToString("N"), MediaWorkKind.Movie, FilmLinkEdits);

        //Act
        var result = await Writer.WriteIdentifiersAsync(Settings, change, TestContext.Current.CancellationToken);

        //Assert
        // The recorded 404 of a read of an item that doesn't exist — and none in the user-less list either.
        Assert.Equal(IntegrationCallStatus.NotFound, result.Status);
        Assert.Equal(ItemGoneError, result.Error);
        Assert.Equal(
            [
                "GET /Users",
                $"GET /Items/{change.SourceItemId}?userId={JellyfinSamples.AdministratorUserId}",
                $"GET /Items?ids={change.SourceItemId}&recursive=true&enableImages=false&enableUserData=false&enableTotalRecordCount=true",
            ],
            _harness.JellyfinHttp.Requests.Select(request => $"{request.Method} {request.Uri.PathAndQuery}"));
    }

    [Fact]
    public async Task WriteIdentifiers_of_an_item_hidden_from_the_administrator_is_refused_and_posts_nothing()
    {
        //Arrange
        // A parental limit or a blocked tag hides it from the administrator: Jellyfin answers that user's read 404, as
        // for an item that is gone, while its user-less list still holds it.
        _server.HiddenFromAdministrator.Add(JellyfinSamples.LandingStriplingId);

        //Act
        var result = await Writer.WriteIdentifiersAsync(Settings, FilmChange(FilmLinkEdits), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Refused, result.Status);
        Assert.Equal(
            "Jellyfin's administrator account can't see this item (a parental limit, a blocked tag or no access to its library), so the app can't read it as that account.",
            result.Error);
        Assert.DoesNotContain(_harness.JellyfinHttp.Requests, request => request.Method == HttpMethod.Post);
        Assert.Empty(StoredProviderIds());
    }

    [Fact]
    public async Task WriteIdentifiers_whose_read_jellyfin_refuses_is_an_unexpected_response_and_posts_nothing()
    {
        //Arrange
        // The live answer to a read without a user; the app always names one, so it means the address isn't Jellyfin's.
        _server.Intercept = request => request.RequestUri!.AbsolutePath.StartsWith("/Items/", StringComparison.Ordinal)
            ? VendorResponses.Recorded(JellyfinSamples.ItemReadWithoutUserResponseFile)
            : null;

        //Act
        var result = await Writer.WriteIdentifiersAsync(Settings, FilmChange(FilmLinkEdits), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.UnexpectedResponse, result.Status);
        Assert.Equal("Jellyfin answered 400 Bad Request, which it doesn't send for this request. Check the address.", result.Error);
        Assert.DoesNotContain(_harness.JellyfinHttp.Requests, request => request.Method == HttpMethod.Post);
    }

    [Fact]
    public async Task WriteIdentifiers_with_a_key_jellyfin_refuses_fails_at_the_first_read()
    {
        //Arrange
        _server.ApiKey = "another-key-0123456789";

        //Act
        var result = await Writer.WriteIdentifiersAsync(Settings, FilmChange(FilmLinkEdits), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.AuthenticationFailed, result.Status);
        Assert.Equal(KeyRefusedError, result.Error);
        Assert.Single(_harness.JellyfinHttp.Requests);
    }

    [Fact]
    public async Task WriteIdentifiers_of_an_item_of_another_type_is_an_unexpected_response_and_posts_nothing()
    {
        //Arrange
        // A film's id onto the Penguins series item the live check read: its ids would mean something else — a show's
        // TMDb number is not a film's (ADR 0017).
        var change = new LibraryIdentifierChange(
            JellyfinSamples.PenguinsId, MediaWorkKind.Movie, [new(MediaIdentifierScheme.Tmdb, "7869", LibraryIdentifierEditRule.Always)]);

        //Act
        var result = await Writer.WriteIdentifiersAsync(Settings, change, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.UnexpectedResponse, result.Status);
        Assert.Equal(
            "Jellyfin holds an item of type “Series” under this id, not a movie, so the app left its ids alone. Sync the library and link it again.",
            result.Error);
        Assert.DoesNotContain(_harness.JellyfinHttp.Requests, request => request.Method == HttpMethod.Post);
    }

    [Fact]
    public async Task WriteIdentifiers_names_another_type_cleaned_and_with_the_key_masked()
    {
        //Arrange
        Stored()["Type"] = $"Folder\u0007 {ApiKey}";

        //Act
        var result = await Writer.WriteIdentifiersAsync(Settings, FilmChange(FilmLinkEdits), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(
            "Jellyfin holds an item of type “Folder ***” under this id, not a movie, so the app left its ids alone. Sync the library and link it again.",
            result.Error);
    }

    [Theory]
    [InlineData("/Users", "{}")]
    [InlineData("/Users", "<!DOCTYPE html><html><body>Welcome to nginx!</body></html>")]
    [InlineData("/Items", "[]")]
    [InlineData("/Items", "<!DOCTYPE html><html><body>Welcome to nginx!</body></html>")]
    [InlineData("/Items", """{"Id":"750eaa45b0836d08e4ae4b35750b93ca","ProviderIds":{}}""")]
    [InlineData("/Items", """{"Id":"750eaa45b0836d08e4ae4b35750b93ca","Type":7,"ProviderIds":{}}""")]
    [InlineData("/Items", """{"Id":"750eaa45b0836d08e4ae4b35750b93ca","Type":"Movie","ProviderIds":"Tmdb=1"}""")]
    [InlineData("/Items", """{"Id":"750eaa45b0836d08e4ae4b35750b93ca","Type":"Movie","ProviderIds":[]}""")]
    public async Task WriteIdentifiers_of_an_answer_that_is_not_jellyfins_is_an_unexpected_response_and_posts_nothing(string path, string body)
    {
        //Arrange
        _server.Intercept = request => request.RequestUri!.AbsolutePath.StartsWith(path, StringComparison.Ordinal)
            ? VendorResponses.Text(body, HttpStatusCode.OK, "application/json")
            : null;

        //Act
        var result = await Writer.WriteIdentifiersAsync(Settings, FilmChange(FilmLinkEdits), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.UnexpectedResponse, result.Status);
        Assert.Equal(NotJellyfinError, result.Error);
        Assert.DoesNotContain(_harness.JellyfinHttp.Requests, request => request.Method == HttpMethod.Post);
    }

    [Fact]
    public async Task WriteIdentifiers_that_the_read_again_disagrees_with_is_sent_and_not_confirmed()
    {
        //Arrange
        // Jellyfin answers 204 and keeps what it had.
        _server.IgnoresWrites = true;

        //Act
        var result = await Writer.WriteIdentifiersAsync(Settings, FilmChange(FilmLinkEdits), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        var write = result.Value!;
        Assert.Equal((true, false), (write.Sent, write.Confirmed));
        Assert.Equal(NotConfirmedProblem, write.Problem);
        Assert.Equal([MediaIdentifierScheme.Tmdb, MediaIdentifierScheme.Imdb], write.Applied);
        Assert.Empty(write.After);
        Assert.Single(_harness.JellyfinHttp.Requests, request => request.Method == HttpMethod.Post);
        Assert.Equal(4, _harness.JellyfinHttp.Requests.Count);
    }

    [Theory]
    [InlineData("503", "Jellyfin answered 503 Service Unavailable. Its own log says why.")]
    // The base class's redirect sentence, the key in the address masked on its way into the sentence.
    [InlineData("redirect",
        "The address answered with a redirect to https://media.test/***/web/index.html. Enter the address the service is really at: the app doesn't follow redirects, so your credentials never go anywhere else.")]
    [InlineData("connection",
        "Couldn't reach Jellyfin at http://jellyfin.test:8096: nothing accepted the connection. Check the address and port, and that Jellyfin is running.")]
    public async Task WriteIdentifiers_whose_read_again_fails_is_sent_and_not_confirmed_with_the_reads_sentence(string failure, string expectedSentence)
    {
        //Arrange
        var itemReads = 0;
        _server.Intercept = request =>
        {
            if (request.Method != HttpMethod.Get || !request.RequestUri!.AbsolutePath.StartsWith("/Items/", StringComparison.Ordinal) || ++itemReads < 2)
                return null;
            return failure switch
            {
                "503" => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
                "redirect" => VendorResponses.Redirect($"https://media.test/{ApiKey}/web/index.html?api_key={ApiKey}"),
                _ => throw new HttpRequestException(HttpRequestError.ConnectionError, $"Socket failure on {request.RequestUri} with the key {ApiKey}"),
            };
        };

        //Act
        var result = await Writer.WriteIdentifiersAsync(Settings, FilmChange(FilmLinkEdits), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        var write = result.Value!;
        Assert.Equal((true, false), (write.Sent, write.Confirmed));
        Assert.Equal($"Jellyfin took the change, but the app couldn't read the item again to confirm it. {expectedSentence}", write.Problem);
        Assert.Empty(write.After);
        Assert.Equal(["Tmdb 223021", "Imdb tt0056164"], Describe(StoredIds()));
        Assert.Single(_harness.JellyfinHttp.Requests, request => request.Method == HttpMethod.Post);
    }

    [Theory]
    [InlineData(HttpRequestError.ResponseEnded)]
    [InlineData(HttpRequestError.InvalidResponse)]
    [InlineData(HttpRequestError.ConfigurationLimitExceeded)]
    [InlineData(HttpRequestError.Unknown)]
    public async Task WriteIdentifiers_whose_answer_is_lost_is_sent_and_not_confirmed_and_never_sent_again(HttpRequestError error)
    {
        //Arrange
        // The change landed in Jellyfin; its answer didn't reach the app.
        _server.AfterWrite = _ => throw new HttpRequestException(error, $"The answer never came, key {ApiKey}");

        //Act
        var result = await Writer.WriteIdentifiersAsync(Settings, FilmChange(FilmLinkEdits), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        var write = result.Value!;
        Assert.Equal((true, false), (write.Sent, write.Confirmed));
        Assert.Equal(AnswerLostProblem, write.Problem);
        Assert.Empty(write.Before);
        Assert.Empty(write.After);
        Assert.Equal([MediaIdentifierScheme.Tmdb, MediaIdentifierScheme.Imdb], write.Applied);
        // Sent once, never again (house rule 8), and not read again: what Jellyfin holds is the next sync's to read.
        Assert.Single(_harness.JellyfinHttp.Requests, request => request.Method == HttpMethod.Post);
        Assert.Equal(3, _harness.JellyfinHttp.Requests.Count);
        Assert.Equal(["Tmdb 223021", "Imdb tt0056164"], Describe(StoredIds()));
    }

    [Fact]
    public async Task WriteIdentifiers_whose_answer_outlives_the_timeout_is_sent_and_not_confirmed()
    {
        //Arrange
        using var harness = new IntegrationAdapterHarness(timeout: TimeSpan.FromMilliseconds(100));
        _server.Arm(harness.JellyfinHttp);
        _server.AfterWrite = _ => StalledAnswer();

        //Act
        var result = await harness.Adapter<IMediaLibraryIdentifierWriter>(JellyfinAdapter.TypeId)
            .WriteIdentifiersAsync(Settings, FilmChange(FilmLinkEdits), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        var write = result.Value!;
        Assert.Equal((true, false), (write.Sent, write.Confirmed));
        Assert.Equal(
            "The change was sent to Jellyfin, but it didn't answer within 0.1 seconds, so the app can't tell whether it saved it; the next sync shows what it holds.",
            write.Problem);
        Assert.Single(harness.JellyfinHttp.Requests, request => request.Method == HttpMethod.Post);
        Assert.Equal(3, harness.JellyfinHttp.Requests.Count);
    }

    [Theory]
    [InlineData(500, "500 Internal Server Error")]
    [InlineData(503, "503 Service Unavailable")]
    public async Task WriteIdentifiers_whose_change_jellyfin_failed_on_is_sent_and_not_confirmed(int statusCode, string expectedStatus)
    {
        //Arrange
        // Jellyfin failed while handling the change, perhaps after saving part of it.
        _server.AfterWrite = _ => new HttpResponseMessage((HttpStatusCode)statusCode) { ReasonPhrase = "Made ‮up by the server" };

        //Act
        var result = await Writer.WriteIdentifiersAsync(Settings, FilmChange(FilmLinkEdits), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        var write = result.Value!;
        Assert.Equal((true, false), (write.Sent, write.Confirmed));
        Assert.Equal(
            $"Jellyfin answered the change with {expectedStatus}, so the app can't tell whether it saved it. Its own log says why; the next sync shows what it holds.",
            write.Problem);
        Assert.Equal(3, _harness.JellyfinHttp.Requests.Count);
    }

    [Theory]
    [InlineData(HttpRequestError.NameResolutionError,
        "Couldn't reach Jellyfin at http://jellyfin.test:8096: the host name can't be found.")]
    [InlineData(HttpRequestError.ConnectionError,
        "Couldn't reach Jellyfin at http://jellyfin.test:8096: nothing accepted the connection. Check the address and port, and that Jellyfin is running.")]
    [InlineData(HttpRequestError.SecureConnectionError,
        "Couldn't reach Jellyfin at http://jellyfin.test:8096: the secure (HTTPS) connection failed, for example because its certificate isn't trusted.")]
    public async Task WriteIdentifiers_whose_change_never_connected_is_unreachable_and_not_sent(HttpRequestError error, string expectedMessage)
    {
        //Arrange
        // A failure of making the connection: nothing reached Jellyfin, so nothing may have landed.
        _server.Intercept = request => request.Method == HttpMethod.Post
            ? throw new HttpRequestException(error, $"Socket failure on {request.RequestUri} with the key {ApiKey}")
            : null;

        //Act
        var result = await Writer.WriteIdentifiersAsync(Settings, FilmChange(FilmLinkEdits), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Unreachable, result.Status);
        Assert.Equal(expectedMessage, result.Error);
        Assert.Null(result.Value);
        Assert.Equal(3, _harness.JellyfinHttp.Requests.Count);
    }

    [Theory]
    [MemberData(nameof(JellyfinAdapterTests.TransportFailures), MemberType = typeof(JellyfinAdapterTests))]
    public async Task WriteIdentifiers_maps_each_transport_error_of_a_read_to_its_sentence_without_the_exception_text(
        HttpRequestError error, IntegrationCallStatus expectedStatus, string expectedMessage)
    {
        //Arrange
        _harness.JellyfinHttp.Handler = (request, _) =>
            throw new HttpRequestException(error, $"Socket failure on {request.RequestUri} with the key {ApiKey}");

        //Act
        var result = await Writer.WriteIdentifiersAsync(Settings, FilmChange(FilmLinkEdits), TestContext.Current.CancellationToken);

        //Assert
        // The connection test's sentences, from one source; nothing was sent.
        Assert.Equal(expectedStatus, result.Status);
        Assert.Equal(expectedMessage, result.Error);
        Assert.Single(_harness.JellyfinHttp.Requests);
    }

    [Fact]
    public async Task WriteIdentifiers_whose_read_outlives_the_timeout_is_timed_out_and_posts_nothing()
    {
        //Arrange
        using var harness = new IntegrationAdapterHarness(timeout: TimeSpan.FromMilliseconds(100));
        harness.JellyfinHttp.Handler = (_, _) => StalledAnswer();

        //Act
        var result = await harness.Adapter<IMediaLibraryIdentifierWriter>(JellyfinAdapter.TypeId)
            .WriteIdentifiersAsync(Settings, FilmChange(FilmLinkEdits), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.TimedOut, result.Status);
        Assert.Equal("Jellyfin at http://jellyfin.test:8096 didn't answer within 0.1 seconds.", result.Error);
        Assert.Single(harness.JellyfinHttp.Requests);
    }

    [Fact]
    public async Task WriteIdentifiers_cancelled_by_the_caller_before_the_change_throws_and_posts_nothing()
    {
        //Arrange
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        _server.Intercept = request =>
        {
            if (!request.RequestUri!.AbsolutePath.StartsWith("/Items/", StringComparison.Ordinal))
                return null;
            cancellation.Cancel();
            return StalledAnswer();
        };

        //Act
        var exception = await Record.ExceptionAsync(() => Writer.WriteIdentifiersAsync(Settings, FilmChange(FilmLinkEdits), cancellation.Token));

        //Assert
        // Not a "timed out" result: nobody is waiting for one.
        Assert.IsAssignableFrom<OperationCanceledException>(exception);
        Assert.DoesNotContain(_harness.JellyfinHttp.Requests, request => request.Method == HttpMethod.Post);
    }

    [Fact]
    public async Task WriteIdentifiers_cancelled_by_the_caller_while_the_change_is_answered_throws_and_sends_it_only_once()
    {
        //Arrange
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        _server.AfterWrite = _ =>
        {
            cancellation.Cancel();
            return StalledAnswer();
        };

        //Act
        var exception = await Record.ExceptionAsync(() => Writer.WriteIdentifiersAsync(Settings, FilmChange(FilmLinkEdits), cancellation.Token));

        //Assert
        // Only the caller's own cancellation propagates; the link writer runs with none once the link is saved (R9).
        Assert.IsAssignableFrom<OperationCanceledException>(exception);
        Assert.Single(_harness.JellyfinHttp.Requests, request => request.Method == HttpMethod.Post);
        Assert.Equal(3, _harness.JellyfinHttp.Requests.Count);
    }

    [Theory]
    [InlineData("no change")]
    [InlineData("a blank item id")]
    [InlineData("an item id Jellyfin never gives")]
    [InlineData("an episode item")]
    [InlineData("a show's id on a movie item")]
    [InlineData("a film's id on a series item")]
    [InlineData("a TVDB id")]
    [InlineData("no edit")]
    [InlineData("an empty edit")]
    [InlineData("one scheme twice")]
    [InlineData("a malformed TMDb id")]
    [InlineData("a TMDb id with spaces")]
    [InlineData("a malformed IMDb id")]
    [InlineData("an unknown rule")]
    [InlineData("an only-where-absent edit that sets nothing")]
    [InlineData("an expected value without its rule")]
    [InlineData("a malformed expected value")]
    public async Task WriteIdentifiers_refuses_a_callers_mistake_without_sending_anything(string mistake)
    {
        //Arrange
        var change = Mistake(mistake);

        //Act
        var exception = await Record.ExceptionAsync(() => Writer.WriteIdentifiersAsync(Settings, change!, TestContext.Current.CancellationToken));

        //Assert
        Assert.IsAssignableFrom<ArgumentException>(exception);
        Assert.Empty(_harness.JellyfinHttp.Requests);
    }

    [Fact]
    public async Task WriteIdentifiers_sends_the_key_in_the_media_browser_header_of_every_request_and_nowhere_else()
    {
        //Arrange

        //Act
        await Writer.WriteIdentifiersAsync(Settings, FilmChange(FilmLinkEdits), TestContext.Current.CancellationToken);

        //Assert
        var requests = _harness.JellyfinHttp.Requests;
        Assert.Equal(4, requests.Count);
        Assert.All(requests, request =>
        {
            Assert.Equal([$"MediaBrowser Token=\"{ApiKey}\""], request.HeaderValues("Authorization"));
            Assert.Empty(request.HeaderValues("X-Emby-Token"));
            Assert.DoesNotContain(ApiKey, request.Uri.OriginalString, StringComparison.Ordinal);
            Assert.DoesNotContain(ApiKey, request.Body ?? "", StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task WriteIdentifiers_keeps_a_base_path_of_the_address()
    {
        //Arrange
        var settings = Settings with { BaseUrl = "http://jellyfin.test/jellyfin" };
        var server = new FakeJellyfinServer(settings.BaseUrl);
        server.Movies.Add(JellyfinSamples.Item());
        server.Arm(_harness.JellyfinHttp);

        //Act
        var result = await Writer.WriteIdentifiersAsync(settings, FilmChange(FilmLinkEdits), TestContext.Current.CancellationToken);

        //Assert
        Assert.True(result.Value!.Confirmed);
        Assert.Equal(
            ["/jellyfin/Users", .. Enumerable.Repeat($"/jellyfin/Items/{JellyfinSamples.LandingStriplingId}", 3)],
            _harness.JellyfinHttp.Requests.Select(request => request.Uri.AbsolutePath));
    }

    [Theory]
    [InlineData("a default refresh")]
    [InlineData("a search for missing metadata")]
    public async Task WriteIdentifiers_of_a_movie_a_refresh_left_holding_the_links_ids_posts_nothing(string refresh)
    {
        //Arrange
        // The live check's item after each refresh Vasyl ran on it: Jellyfin kept the TMDb and IMDb ids the app wrote.
        var refreshed = refresh == "a default refresh" ? JellyfinSamples.ItemAfterDefaultRefresh() : JellyfinSamples.ItemAfterMissingRefresh();
        _server.Movies[_server.Movies.IndexOf(Stored())] = refreshed;

        //Act
        var result = await Writer.WriteIdentifiersAsync(Settings, FilmChange(FilmLinkEdits), TestContext.Current.CancellationToken);

        //Assert
        var write = result.Value!;
        Assert.Equal((false, true), (write.Sent, write.Confirmed));
        Assert.Equal(["Tmdb 223021", "Imdb tt0056164"], Describe(write.Before));
        Assert.DoesNotContain(_harness.JellyfinHttp.Requests, request => request.Method == HttpMethod.Post);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    /// <summary>A change of "120. Landing Stripling", the movie item the live check wrote.</summary>
    private static LibraryIdentifierChange FilmChange(params LibraryIdentifierEdit[] edits) =>
        new(JellyfinSamples.LandingStriplingId, MediaWorkKind.Movie, edits);

    /// <summary>A change of the Penguins series item the live check wrote.</summary>
    private static LibraryIdentifierChange SeriesChange(params LibraryIdentifierEdit[] edits) =>
        new(JellyfinSamples.PenguinsId, MediaWorkKind.Series, edits);

    /// <summary>The change each caller's mistake names.</summary>
    private static LibraryIdentifierChange? Mistake(string name) => name switch
    {
        "no change" => null,
        "a blank item id" => new("  ", MediaWorkKind.Movie, FilmLinkEdits),
        "an item id Jellyfin never gives" => new("../Users", MediaWorkKind.Movie, FilmLinkEdits),
        "an episode item" => new(JellyfinSamples.LandingStriplingId, MediaWorkKind.Episode, [new(MediaIdentifierScheme.Imdb, "tt0056164", LibraryIdentifierEditRule.Always)]),
        "a show's id on a movie item" => FilmChange(new LibraryIdentifierEdit(MediaIdentifierScheme.TmdbTv, "1044", LibraryIdentifierEditRule.Always)),
        "a film's id on a series item" => SeriesChange(new LibraryIdentifierEdit(MediaIdentifierScheme.Tmdb, "223021", LibraryIdentifierEditRule.Always)),
        "a TVDB id" => FilmChange(new LibraryIdentifierEdit(MediaIdentifierScheme.Tvdb, "79257", LibraryIdentifierEditRule.Always)),
        "no edit" => FilmChange(),
        "an empty edit" => FilmChange(null!),
        "one scheme twice" => FilmChange(
            new LibraryIdentifierEdit(MediaIdentifierScheme.Tmdb, "1", LibraryIdentifierEditRule.Always),
            new LibraryIdentifierEdit(MediaIdentifierScheme.Tmdb, "2", LibraryIdentifierEditRule.Always)),
        "a malformed TMDb id" => FilmChange(new LibraryIdentifierEdit(MediaIdentifierScheme.Tmdb, "223021a", LibraryIdentifierEditRule.Always)),
        "a TMDb id with spaces" => FilmChange(new LibraryIdentifierEdit(MediaIdentifierScheme.Tmdb, " 223021", LibraryIdentifierEditRule.Always)),
        "a malformed IMDb id" => FilmChange(new LibraryIdentifierEdit(MediaIdentifierScheme.Imdb, "nm0001", LibraryIdentifierEditRule.OnlyWhereAbsent)),
        "an unknown rule" => FilmChange(new LibraryIdentifierEdit(MediaIdentifierScheme.Tmdb, "223021", (LibraryIdentifierEditRule)9)),
        "an only-where-absent edit that sets nothing" => FilmChange(new LibraryIdentifierEdit(MediaIdentifierScheme.Imdb, null, LibraryIdentifierEditRule.OnlyWhereAbsent)),
        "an expected value without its rule" => FilmChange(new LibraryIdentifierEdit(MediaIdentifierScheme.Tmdb, "1", LibraryIdentifierEditRule.Always, "223021")),
        "a malformed expected value" => FilmChange(new LibraryIdentifierEdit(MediaIdentifierScheme.Tmdb, null, LibraryIdentifierEditRule.OnlyWhereExpected, "tt0056164")),
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "No such mistake."),
    };

    /// <summary>The Landing Stripling item as the fake server holds it.</summary>
    private JsonObject Stored() =>
        _server.Movies.Single(movie => string.Equals((string?)movie["Id"], JellyfinSamples.LandingStriplingId, StringComparison.Ordinal));

    private JsonObject StoredProviderIds() => Stored()["ProviderIds"]!.AsObject();

    /// <summary>The Penguins series item as the fake server holds it.</summary>
    private JsonObject StoredSeries() =>
        _server.Series.Single(series => string.Equals((string?)series["Id"], JellyfinSamples.PenguinsId, StringComparison.Ordinal));

    /// <summary>The stored item's TMDb and IMDb ids, as the sync would read them.</summary>
    private List<MediaIdentifier> StoredIds()
    {
        var providerIds = StoredProviderIds();
        List<MediaIdentifier> ids = [];
        if ((string?)providerIds["Tmdb"] is { } tmdb)
            ids.Add(new MediaIdentifier(MediaIdentifierScheme.Tmdb, tmdb));
        if ((string?)providerIds["Imdb"] is { } imdb)
            ids.Add(new MediaIdentifier(MediaIdentifierScheme.Imdb, imdb));
        return ids;
    }

    private void HoldProviderIds(JsonObject providerIds) => Stored()["ProviderIds"] = providerIds;

    /// <summary>The <c>ProviderIds</c> of the one item posted.</summary>
    private JsonObject PostedProviderIds() =>
        JsonNode.Parse(Assert.Single(_harness.JellyfinHttp.Requests, request => request.Method == HttpMethod.Post).Body!)!["ProviderIds"]!.AsObject();

    private static JsonObject WithoutProviderIds(JsonObject item)
    {
        var copy = item.DeepClone().AsObject();
        copy.Remove("ProviderIds");
        return copy;
    }

    private static JsonObject WithoutPeople(JsonObject item)
    {
        var copy = item.DeepClone().AsObject();
        copy.Remove("People");
        return copy;
    }

    private static JsonObject User(string id, bool isAdministrator, bool isDisabled) => new()
    {
        ["Name"] = "Someone",
        ["Id"] = id,
        ["Policy"] = new JsonObject { ["IsAdministrator"] = isAdministrator, ["IsDisabled"] = isDisabled },
    };

    // Each id as "Scheme value".
    private static List<string> Describe(IReadOnlyList<MediaIdentifier> identifiers) =>
        [.. identifiers.Select(identifier => $"{identifier.Scheme} {identifier.Value}")];

    // A 200 whose body never finishes arriving: the pipe is never written to or completed.
    private static HttpResponseMessage StalledAnswer() =>
        new(HttpStatusCode.OK) { Content = new StreamContent(new Pipe().Reader.AsStream()) };
}
