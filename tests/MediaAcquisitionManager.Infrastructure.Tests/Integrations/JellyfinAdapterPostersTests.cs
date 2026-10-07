using System.Net;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Abstractions.Library;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Infrastructure.Integrations.Jellyfin;
using MediaAcquisitionManager.TestInfrastructure.Files;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;

namespace MediaAcquisitionManager.Infrastructure.Tests.Integrations;

// MAM-35 — a library work's poster from Jellyfin (ADR 0027): the sync reads each item's own primary image tag from the real
// pages the live check of 2026-10-07 recorded with the sync's query (VendorSamples/Jellyfin/*-page-with-images.json), and the
// poster it names is fetched with one keyed GET on the shared client, answered as the image route was recorded
// (image-primary*-response.txt), kept only as a JPEG.
public sealed class JellyfinAdapterPostersTests : IDisposable
{
    private const string BaseUrl = "http://jellyfin.test:8096";
    private const string ApiKey = "4f1d0b9c2e7a4c3b8d6e5f7a9b0c1d2e";

    private const string NotAJpegError = "Jellyfin answered the poster with something that isn't a JPEG image.";
    private const string NoPosterError = "Jellyfin has no poster for this item.";

    // The tags the live check read (2026-10-07).
    private const string District13Tag = "876b255c3ee8ce3ab0bb55c4c03ab0ef";
    private const string ContinentalTag = "41f7c98888a81ea514189c27c6c3970c";
    private const string AliensTheatricalTag = "a608b4104259bc0bb01c6ad262c53aed";
    private const string AliensSpecialEditionTag = "5e28b80243c705099a2c9b398fd53fd2";
    private const string DaybreakTag = "c46fbb41d55088253b976fab0894d6a8";
    private const string TomAndJerryKidsTag = "4a58e8e95f7f6ad8307f541d94e23800";

    private static readonly IntegrationConnectionSettings Settings = new(null, BaseUrl, null, null, ApiKey);

    private readonly IntegrationAdapterHarness _harness = new();
    private readonly FakeJellyfinServer _server = new(BaseUrl) { ApiKey = ApiKey };

    private IMediaLibraryProvider Library => _harness.Adapter<IMediaLibraryProvider>(JellyfinAdapter.TypeId);

    public void Dispose() => _harness.Dispose();

    // ── the sync's tag ───────────────────────────────────────────────────────

    [Fact]
    public async Task ListItems_reads_each_sampled_movies_own_poster_tag()
    {
        //Arrange
        ArmPage(JellyfinSamples.MoviesWithImagesPageFile);

        //Act
        var result = await Library.ListItemsAsync(Settings, new LibraryPageRequest(MediaWorkKind.Movie, 0, 50), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        Assert.Equal(
            [
                (JellyfinSamples.District13Id, District13Tag),
                (JellyfinSamples.ContinentalId, ContinentalTag),
                (JellyfinSamples.AliensTheatricalId, AliensTheatricalTag),
                (JellyfinSamples.AliensSpecialEditionId, AliensSpecialEditionTag),
            ],
            result.Value!.Items.Select(item => (item.SourceItemId, item.PosterTag)));
    }

    [Fact]
    public async Task ListItems_reads_each_sampled_series_own_poster_tag()
    {
        //Arrange
        ArmPage(JellyfinSamples.SeriesWithImagesPageFile);

        //Act
        var result = await Library.ListItemsAsync(Settings, new LibraryPageRequest(MediaWorkKind.Series, 0, 50), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        Assert.Equal(
            [
                (JellyfinSamples.ClarksonsFarmId, "84efddafd1408cbacb7700107c602dff"),
                (JellyfinSamples.HowTheUniverseWorksId, "658e4aa24e972c31d2f4af570181385c"),
                (JellyfinSamples.MickeyMouseClubhouseId, "01de7f6467277e81d5abec6fc8256006"),
                // The tag the Penguins' Identify gave it (series-item-after-identify.json).
                (JellyfinSamples.PenguinsId, "52464c1fd74945e5f6990cb1901cf7c6"),
                (JellyfinSamples.YellowstoneId, JellyfinSamples.YellowstonePosterTag),
            ],
            result.Value!.Items.Select(item => (item.SourceItemId, item.PosterTag)));
    }

    [Fact]
    public async Task ListItems_reads_each_sampled_episodes_own_poster_tag_and_none_for_the_virtual_special()
    {
        //Arrange
        ArmPage(JellyfinSamples.EpisodesWithImagesPageFile);

        //Act
        var result = await Library.ListItemsAsync(Settings, new LibraryPageRequest(MediaWorkKind.Episode, 0, 50), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        Assert.Equal(
            [
                // ImageTags {}: no picture of its own, whatever its series and season have.
                (JellyfinSamples.FollowTheMouseId, null),
                (JellyfinSamples.BigBangId, "ea78070489e6279af4ba737e6119f610"),
                // Its own, never Yellowstone's 7060953c… its SeriesPrimaryImageTag names.
                (JellyfinSamples.YellowstoneS01E01Id, DaybreakTag),
                // The episode of no series: its own picture.
                (JellyfinSamples.TomAndJerryKidsId, TomAndJerryKidsTag),
                // The two halves of one episode, a picture each.
                (JellyfinSamples.RoadRallyAId, "b4c46ed28a9b22f17cdfcbc96d8937ba"),
                (JellyfinSamples.RoadRallyBId, "03c967be49c613b617f83562b53e09f9"),
            ],
            result.Value!.Items.Select(item => (item.SourceItemId, (string?)item.PosterTag)));
    }

    [Fact]
    public async Task ListItems_reads_no_poster_tag_for_the_virtual_special_although_it_carries_its_series_and_seasons()
    {
        //Arrange
        var special = JellyfinSamples.EpisodesWithImages().Single(item => (string?)item["Id"] == JellyfinSamples.FollowTheMouseId);
        var lent = ((string?)special["SeriesPrimaryImageTag"], (string?)special["ParentPrimaryImageTag"], (string?)special["ParentPrimaryImageItemId"]);
        _server.Episodes.Add(special);
        _server.Arm(_harness.JellyfinHttp);

        //Act
        var result = await Library.ListItemsAsync(Settings, new LibraryPageRequest(MediaWorkKind.Episode, 0, 50), TestContext.Current.CancellationToken);

        //Assert
        // The sample does lend it its series' and its season's pictures; the sync takes neither.
        Assert.Equal(("01de7f6467277e81d5abec6fc8256006", "da00d3a532fd43d272382e6bc7256fa0", "5c87923f585cb1e41f825cd8ece847f8"), lent);
        var item = Assert.Single(result.Value!.Items);
        Assert.True(item.IsMissing);
        Assert.Null(item.PosterTag);
    }

    [Fact]
    public async Task ListItems_reads_no_poster_tag_for_an_episode_without_its_own_image_whatever_its_series_has()
    {
        //Arrange
        var daybreak = JellyfinSamples.EpisodesWithImages().Single(item => (string?)item["Id"] == JellyfinSamples.YellowstoneS01E01Id);
        daybreak["ImageTags"] = new JsonObject();
        _server.Episodes.Add(daybreak);
        _server.Arm(_harness.JellyfinHttp);

        //Act
        var result = await Library.ListItemsAsync(Settings, new LibraryPageRequest(MediaWorkKind.Episode, 0, 50), TestContext.Current.CancellationToken);

        //Assert
        // Its SeriesPrimaryImageTag (Yellowstone's poster) and ParentPrimaryImageTag stay unread: a card shows the item's own
        // picture or none.
        Assert.Equal(JellyfinSamples.YellowstonePosterTag, (string?)daybreak["SeriesPrimaryImageTag"]);
        Assert.Null(Assert.Single(result.Value!.Items).PosterTag);
    }

    [Theory]
    [InlineData("{\"Primary\": \"a\"}", "a")]
    [InlineData("{\"Primary\": \"ABCdef0123456789ABCdef0123456789ABCdef0123456789ABCdef0123456789\"}", "ABCdef0123456789ABCdef0123456789ABCdef0123456789ABCdef0123456789")]
    [InlineData("{\"Thumb\": \"0123456789abcdef0123456789abcdef\", \"Primary\": \"876b255c3ee8ce3ab0bb55c4c03ab0ef\"}", District13Tag)]
    public async Task ListItems_reads_a_poster_tag_of_letters_and_digits(string imageTags, string expected)
    {
        //Arrange
        ArmDistrict13(imageTags);

        //Act
        var result = await Library.ListItemsAsync(Settings, new LibraryPageRequest(MediaWorkKind.Movie, 0, 50), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(expected, Assert.Single(result.Value!.Items).PosterTag);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("\"876b255c3ee8ce3ab0bb55c4c03ab0ef\"")]
    [InlineData("{\"Thumb\": \"876b255c3ee8ce3ab0bb55c4c03ab0ef\"}")]
    [InlineData("{\"primary\": \"876b255c3ee8ce3ab0bb55c4c03ab0ef\"}")]
    [InlineData("{\"Primary\": null}")]
    [InlineData("{\"Primary\": 12}")]
    [InlineData("{\"Primary\": \"\"}")]
    [InlineData("{\"Primary\": \"   \"}")]
    [InlineData("{\"Primary\": \"applied-primary-1\"}")]
    [InlineData("{\"Primary\": \"876b255c3ee8ce3ab0bb55c4c03ab0ef\\n\"}")]
    [InlineData("{\"Primary\": \"../../System/Info\"}")]
    [InlineData("{\"Primary\": \"abc&format=Png\"}")]
    [InlineData("{\"Primary\": \"тег\"}")]
    [InlineData("{\"Primary\": \"ABCdef0123456789ABCdef0123456789ABCdef0123456789ABCdef0123456789X\"}")]
    public async Task ListItems_reads_no_poster_tag_the_app_could_not_keep_and_still_reads_the_item(string? imageTags)
    {
        //Arrange
        ArmDistrict13(imageTags);

        //Act
        var result = await Library.ListItemsAsync(Settings, new LibraryPageRequest(MediaWorkKind.Movie, 0, 50), TestContext.Current.CancellationToken);

        //Assert
        // A picture the app can't name a file by is no picture; the item is read all the same (a sync that dropped it would
        // delete its facts).
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        var item = Assert.Single(result.Value!.Items);
        Assert.Equal(JellyfinSamples.District13Id, item.SourceItemId);
        Assert.NotEmpty(item.Versions);
        Assert.Null(item.PosterTag);
    }

    [Theory]
    [InlineData("district13")]
    [InlineData("abc123")]
    [InlineData("zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz")]
    [InlineData("ABCdef0123456789ABCdef0123456789ABCdef0123456789ABCdef0123456789")]
    public async Task ListItems_reads_no_poster_tag_for_an_item_whose_id_is_not_a_guid_and_still_reads_the_item(string id)
    {
        //Arrange
        // Ids a poster's file name could hold (LibraryPosterNames) that GetPoster refuses: a row naming one would make the
        // poster endpoint throw on every view of the card instead of answering 404.
        var movie = JellyfinSamples.MoviesWithImages().Single(item => (string?)item["Id"] == JellyfinSamples.District13Id);
        movie["Id"] = id;
        _server.Movies.Clear();
        _server.Movies.Add(movie);
        _server.Arm(_harness.JellyfinHttp);

        //Act
        var result = await Library.ListItemsAsync(Settings, new LibraryPageRequest(MediaWorkKind.Movie, 0, 50), TestContext.Current.CancellationToken);

        //Assert
        // The sample does carry its picture's tag; the item is read all the same, without it.
        Assert.Equal(District13Tag, (string?)movie["ImageTags"]!["Primary"]);
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        var item = Assert.Single(result.Value!.Items);
        Assert.Equal(id, item.SourceItemId);
        Assert.NotEmpty(item.Versions);
        Assert.Null(item.PosterTag);
    }

    [Fact]
    public async Task ListItems_after_an_identify_reads_the_tag_of_the_poster_it_gave()
    {
        //Arrange
        using var jellyfin = new JellyfinIdentifyHarness();
        var library = jellyfin.Adapters.Adapter<IMediaLibraryProvider>(JellyfinAdapter.TypeId);
        var page = new LibraryPageRequest(MediaWorkKind.Movie, 0, 50);
        var before = await library.ListItemsAsync(JellyfinIdentifyHarness.Settings, page, TestContext.Current.CancellationToken);
        await jellyfin.IdentifyAsync(JellyfinIdentifyHarness.FilmRequest(), TestContext.Current.CancellationToken);

        //Act
        var after = await library.ListItemsAsync(JellyfinIdentifyHarness.Settings, page, TestContext.Current.CancellationToken);

        //Assert
        // item.json's own tag before; Apply replaced the images, and the next sync reads the new one (MAM-34 does not sync).
        Assert.Equal("e153236c337b97a15f9a8027d46419b2", PosterTagOf(before, JellyfinSamples.LandingStriplingId));
        var tag = PosterTagOf(after, JellyfinSamples.LandingStriplingId);
        Assert.Equal((string?)jellyfin.Film["ImageTags"]!["Primary"], tag);
        Assert.NotEqual("e153236c337b97a15f9a8027d46419b2", tag);
    }

    // ── the poster ───────────────────────────────────────────────────────────

    [Fact]
    public async Task GetPoster_asks_for_the_items_primary_image_as_a_342_wide_jpeg_with_the_key_in_the_media_browser_header()
    {
        //Arrange
        _server.Series.AddRange(JellyfinSamples.SeriesWithImages());
        _server.Arm(_harness.JellyfinHttp);

        //Act
        var result = await Library.GetPosterAsync(Settings, JellyfinSamples.YellowstoneId, JellyfinSamples.YellowstonePosterTag, TestContext.Current.CancellationToken);

        //Assert
        var request = Assert.Single(_harness.JellyfinHttp.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("http://jellyfin.test:8096/Items/bad3e58c7b81156c7bccd6235599accd/Images/Primary", request.Uri.GetLeftPart(UriPartial.Path));
        // format=Jpg: without it Jellyfin picks WebP when the Accept header allows it; quality=90 makes it 35 KB, not 115.
        Assert.Equal(
            ["tag=7060953c5bc967a2e947ecc514b77a37", "format=Jpg", "maxWidth=342", "quality=90"],
            request.Uri.Query.TrimStart('?').Split('&'));
        // The route needs no key, but the key goes only to the saved address, in the header, never into the URI (ADR 0007).
        Assert.Equal([$"MediaBrowser Token=\"{ApiKey}\""], request.HeaderValues("Authorization"));
        Assert.DoesNotContain(ApiKey, request.Uri.OriginalString, StringComparison.Ordinal);
        Assert.Equal(["image/jpeg"], request.HeaderValues("Accept"));
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        Assert.Equal("image/jpeg", result.Value!.MediaType);
        Assert.Equal(TestImages.Jpeg(), result.Value.Content);
    }

    [Fact]
    public async Task GetPoster_reads_the_recorded_answer_whole()
    {
        //Arrange
        _harness.JellyfinHttp.Handler = (_, _) => FakeJellyfinServer.RecordedPoster();

        //Act
        var result = await Library.GetPosterAsync(Settings, JellyfinSamples.YellowstoneId, JellyfinSamples.YellowstonePosterTag, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        Assert.Equal(new LibraryPoster(result.Value!.Content, "image/jpeg"), result.Value);
        Assert.Equal(TestImages.Jpeg(), result.Value.Content);
    }

    [Fact]
    public async Task GetPoster_keeps_a_base_path_of_the_address_and_sends_the_id_in_jellyfins_own_form()
    {
        //Arrange
        var server = new FakeJellyfinServer("http://jellyfin.test:8096/jellyfin") { ApiKey = ApiKey };
        server.Series.AddRange(JellyfinSamples.SeriesWithImages());
        server.Arm(_harness.JellyfinHttp);
        var settings = Settings with { BaseUrl = "http://jellyfin.test:8096/jellyfin" };

        //Act
        var result = await Library.GetPosterAsync(settings, "BAD3E58C-7B81-156C-7BCC-D6235599ACCD", JellyfinSamples.YellowstonePosterTag, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        Assert.Equal(
            "http://jellyfin.test:8096/jellyfin/Items/bad3e58c7b81156c7bccd6235599accd/Images/Primary",
            Assert.Single(_harness.JellyfinHttp.Requests).Uri.GetLeftPart(UriPartial.Path));
    }

    [Theory]
    [InlineData("image/jpeg")]
    [InlineData("IMAGE/JPEG")]
    [InlineData("Image/Jpeg")]
    public async Task GetPoster_keeps_a_jpeg_whatever_the_letter_case_of_its_media_type(string contentType)
    {
        //Arrange
        _harness.JellyfinHttp.Handler = (_, _) => Image(TestImages.Jpeg(), contentType);

        //Act
        var result = await Library.GetPosterAsync(Settings, JellyfinSamples.YellowstoneId, JellyfinSamples.YellowstonePosterTag, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        Assert.Equal("image/jpeg", result.Value!.MediaType);
    }

    [Theory]
    [InlineData("image/png")]
    [InlineData("image/webp")]
    [InlineData("image/svg+xml")]
    [InlineData("text/html")]
    [InlineData("application/json")]
    [InlineData("application/octet-stream")]
    [InlineData(null)]
    public async Task GetPoster_of_anything_but_a_jpeg_is_an_unexpected_response(string? contentType)
    {
        //Arrange
        // JPEG bytes under another type: the type is what the browser would be told, so it decides.
        _harness.JellyfinHttp.Handler = (_, _) => Image(TestImages.Jpeg(), contentType);

        //Act
        var result = await Library.GetPosterAsync(Settings, JellyfinSamples.YellowstoneId, JellyfinSamples.YellowstonePosterTag, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.UnexpectedResponse, result.Status);
        Assert.Equal(NotAJpegError, result.Error);
        Assert.Null(result.Value);
    }

    [Fact]
    public async Task GetPoster_with_an_empty_body_is_an_unexpected_response()
    {
        //Arrange
        _harness.JellyfinHttp.Handler = (_, _) => Image([], "image/jpeg");

        //Act
        var result = await Library.GetPosterAsync(Settings, JellyfinSamples.YellowstoneId, JellyfinSamples.YellowstonePosterTag, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.UnexpectedResponse, result.Status);
        Assert.Equal(NotAJpegError, result.Error);
    }

    [Fact]
    public async Task GetPoster_of_an_item_without_a_primary_image_is_not_found()
    {
        //Arrange
        _server.Episodes.AddRange(JellyfinSamples.EpisodesWithImages());
        _server.Arm(_harness.JellyfinHttp);

        //Act
        // The virtual special has no image of its own; the tag is never checked, so any well-formed one is asked.
        var result = await Library.GetPosterAsync(Settings, JellyfinSamples.FollowTheMouseId, "01de7f6467277e81d5abec6fc8256006", TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.NotFound, result.Status);
        Assert.Equal(NoPosterError, result.Error);
        Assert.Single(_harness.JellyfinHttp.Requests);
    }

    [Theory]
    [InlineData(JellyfinSamples.ImagePrimaryMissingResponseFile)]
    [InlineData(JellyfinSamples.ImagePrimaryUnknownItemResponseFile)]
    public async Task GetPoster_answered_with_the_recorded_404_or_400_is_not_found(string recordedFile)
    {
        //Arrange
        // 404: an item without the image; 400: an id Jellyfin no longer knows — removed since the sync read it.
        _harness.JellyfinHttp.Handler = (_, _) => VendorResponses.Recorded(recordedFile);

        //Act
        var result = await Library.GetPosterAsync(Settings, JellyfinSamples.FollowTheMouseId, DaybreakTag, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.NotFound, result.Status);
        Assert.Equal(NoPosterError, result.Error);
    }

    [Fact]
    public async Task GetPoster_of_an_item_jellyfin_no_longer_has_is_not_found()
    {
        //Arrange
        _server.Arm(_harness.JellyfinHttp);

        //Act
        var result = await Library.GetPosterAsync(Settings, "00000000000000000000000000000000", DaybreakTag, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.NotFound, result.Status);
        Assert.Equal(NoPosterError, result.Error);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "Jellyfin refused the API key. Create one in Jellyfin under Dashboard → API Keys and enter it here.")]
    [InlineData(HttpStatusCode.Forbidden, "Jellyfin accepted the key but won't let it read the library (403 Forbidden). Use a key from Dashboard → API Keys, not a user's access token.")]
    public async Task GetPoster_whose_key_is_refused_is_an_authentication_failure(HttpStatusCode status, string expected)
    {
        //Arrange
        // Jellyfin itself never refuses the image route; a proxy in front of it may, as it would any call.
        _harness.JellyfinHttp.Handler = (_, _) => new HttpResponseMessage(status);

        //Act
        var result = await Library.GetPosterAsync(Settings, JellyfinSamples.YellowstoneId, JellyfinSamples.YellowstonePosterTag, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal((IntegrationCallStatus.AuthenticationFailed, expected), (result.Status, result.Error));
    }

    [Fact]
    public async Task GetPoster_answered_with_a_server_error_is_a_service_error()
    {
        //Arrange
        _harness.JellyfinHttp.Handler = (_, _) => VendorResponses.Text("Error processing request.", HttpStatusCode.InternalServerError);

        //Act
        var result = await Library.GetPosterAsync(Settings, JellyfinSamples.YellowstoneId, JellyfinSamples.YellowstonePosterTag, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.ServiceError, result.Status);
        Assert.Equal("Jellyfin answered 500 Internal Server Error. Its own log says why.", result.Error);
    }

    [Fact]
    public async Task GetPoster_answered_with_a_redirect_follows_none_and_masks_the_key()
    {
        //Arrange
        _harness.JellyfinHttp.Handler = (_, _) => VendorResponses.Redirect($"http://elsewhere.test/{ApiKey}/poster.jpg");

        //Act
        var result = await Library.GetPosterAsync(Settings, JellyfinSamples.YellowstoneId, JellyfinSamples.YellowstonePosterTag, TestContext.Current.CancellationToken);

        //Assert
        // The handler doesn't follow it, so the key went nowhere else; the sentence names where it pointed, masked.
        Assert.Single(_harness.JellyfinHttp.Requests);
        Assert.Equal(IntegrationCallStatus.UnexpectedResponse, result.Status);
        Assert.StartsWith("The address answered with a redirect to http://elsewhere.test/", result.Error, StringComparison.Ordinal);
        Assert.DoesNotContain(ApiKey, result.Error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(HttpRequestError.ConnectionError, IntegrationCallStatus.Unreachable, "Couldn't reach Jellyfin at http://jellyfin.test:8096: nothing accepted the connection. Check the address and port, and that Jellyfin is running.")]
    [InlineData(HttpRequestError.NameResolutionError, IntegrationCallStatus.Unreachable, "Couldn't reach Jellyfin at http://jellyfin.test:8096: the host name can't be found.")]
    [InlineData(HttpRequestError.ConfigurationLimitExceeded, IntegrationCallStatus.UnexpectedResponse, "The answer from http://jellyfin.test:8096 is far larger than anything Jellyfin sends. Check the address.")]
    public async Task GetPoster_that_fails_on_the_way_has_the_connection_tests_sentence(
        HttpRequestError error, IntegrationCallStatus expectedStatus, string expectedMessage)
    {
        //Arrange
        _harness.JellyfinHttp.Handler = (request, _) =>
            throw new HttpRequestException(error, $"Socket failure on {request.RequestUri} with the key {ApiKey}");

        //Act
        var result = await Library.GetPosterAsync(Settings, JellyfinSamples.YellowstoneId, JellyfinSamples.YellowstonePosterTag, TestContext.Current.CancellationToken);

        //Assert
        // The address the owner saved, which is where the poster was asked for; never the exception's text.
        Assert.Equal((expectedStatus, expectedMessage), (result.Status, result.Error));
    }

    [Fact]
    public async Task GetPoster_goes_through_the_shared_client_and_its_fifteen_seconds()
    {
        //Arrange
        // A cancellation nobody asked for is the client's timeout (HttpClient's own rule): its sentence names the timeout of
        // the client the call went through — 15 seconds for the shared one, 20 minutes for Apply's.
        _harness.JellyfinHttp.Handler = (_, _) => throw new TaskCanceledException("The request timed out.");

        //Act
        var result = await Library.GetPosterAsync(Settings, JellyfinSamples.YellowstoneId, JellyfinSamples.YellowstonePosterTag, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.TimedOut, result.Status);
        Assert.Equal("Jellyfin at http://jellyfin.test:8096 didn't answer within 15 seconds.", result.Error);
    }

    [Fact]
    public async Task GetPoster_cancelled_by_the_caller_throws()
    {
        //Arrange
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        _harness.JellyfinHttp.Handler = (_, _) =>
        {
            cancellation.Cancel();
            throw new TaskCanceledException("The caller went away.");
        };

        //Act
        var exception = await Record.ExceptionAsync(() => Library.GetPosterAsync(Settings, JellyfinSamples.YellowstoneId, JellyfinSamples.YellowstonePosterTag, cancellation.Token));

        //Assert
        Assert.IsAssignableFrom<OperationCanceledException>(exception);
    }

    [Theory]
    [InlineData("")]
    [InlineData("bad3e58c7b81156c7bccd6235599acc")]
    [InlineData("bad3e58c7b81156c7bccd6235599accdd")]
    [InlineData("..")]
    [InlineData("../System/Info")]
    [InlineData("bad3e58c7b81156c7bccd6235599accd/../../System/Info")]
    [InlineData("bad3e58c7b81156c7bccd6235599accd?tag=x")]
    [InlineData("Items")]
    public async Task GetPoster_with_an_id_jellyfin_never_gives_throws_without_sending_anything(string sourceItemId)
    {
        //Arrange
        _server.Arm(_harness.JellyfinHttp);

        //Act
        var exception = await Record.ExceptionAsync(() => Library.GetPosterAsync(Settings, sourceItemId, JellyfinSamples.YellowstonePosterTag, TestContext.Current.CancellationToken));

        //Assert
        Assert.IsType<ArgumentException>(exception);
        Assert.Empty(_harness.JellyfinHttp.Requests);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("applied-primary-1")]
    [InlineData("7060953c5bc967a2e947ecc514b77a37&format=Png")]
    [InlineData("7060953c5bc967a2e947ecc514b77a37\n")]
    [InlineData("../x")]
    [InlineData("%2F")]
    [InlineData("тег")]
    [InlineData("ABCdef0123456789ABCdef0123456789ABCdef0123456789ABCdef0123456789X")]
    public async Task GetPoster_with_a_tag_the_sync_never_reads_throws_without_sending_anything(string posterTag)
    {
        //Arrange
        _server.Arm(_harness.JellyfinHttp);

        //Act
        var exception = await Record.ExceptionAsync(() => Library.GetPosterAsync(Settings, JellyfinSamples.YellowstoneId, posterTag, TestContext.Current.CancellationToken));

        //Assert
        Assert.IsType<ArgumentException>(exception);
        Assert.Empty(_harness.JellyfinHttp.Requests);
    }

    [Fact]
    public async Task GetPoster_without_an_id_or_a_tag_throws_without_sending_anything()
    {
        //Arrange
        _server.Arm(_harness.JellyfinHttp);

        //Act
        var withoutId = await Record.ExceptionAsync(() => Library.GetPosterAsync(Settings, null!, JellyfinSamples.YellowstonePosterTag, TestContext.Current.CancellationToken));
        var withoutTag = await Record.ExceptionAsync(() => Library.GetPosterAsync(Settings, JellyfinSamples.YellowstoneId, null!, TestContext.Current.CancellationToken));

        //Assert
        Assert.IsType<ArgumentNullException>(withoutId);
        Assert.IsType<ArgumentNullException>(withoutTag);
        Assert.Empty(_harness.JellyfinHttp.Requests);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    /// <summary>Answers every request with the recorded page at <paramref name="sampleFile"/>, byte for byte.</summary>
    private void ArmPage(string sampleFile) =>
        _harness.JellyfinHttp.Handler = (_, _) => VendorResponses.Sample(sampleFile, "application/json; charset=utf-8");

    /// <summary>The fake library holding 13-й район alone, as read with its image tag, its <c>ImageTags</c> replaced by
    /// <paramref name="imageTags"/> (JSON; <c>null</c> leaves the property out).</summary>
    private void ArmDistrict13(string? imageTags)
    {
        var movie = JellyfinSamples.MoviesWithImages().Single(item => (string?)item["Id"] == JellyfinSamples.District13Id);
        movie.Remove("ImageTags");
        if (imageTags is not null)
            movie["ImageTags"] = JsonNode.Parse(imageTags);
        _server.Movies.Clear();
        _server.Movies.Add(movie);
        _server.Arm(_harness.JellyfinHttp);
    }

    private static string? PosterTagOf(IntegrationCallResult<LibraryItemPage> page, string itemId) =>
        page.Value!.Items.Single(item => item.SourceItemId == itemId).PosterTag;

    private static HttpResponseMessage Image(byte[] bytes, string? contentType)
    {
        var content = new ByteArrayContent(bytes);
        if (contentType is not null)
            content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
    }
}
