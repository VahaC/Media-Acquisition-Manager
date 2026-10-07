using System.Net;
using System.Text.Json.Nodes;
using MediaAcquisitionManager.Api.Http;
using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Abstractions.Library;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Infrastructure.Persistence;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Data;
using MediaAcquisitionManager.TestInfrastructure.Files;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MediaAcquisitionManager.Api.Tests.Library;

// MAM-35, ADR 0027 — a library work's poster (GET /api/library/posters/{fileName}), served by the app: only a name that is
// an item id and a tag some library reported, else a bare 404 with nothing asked of anyone; fetched from the Jellyfin the
// owner saved — the key in the header only, as a 342-wide JPEG — once, and kept in <data>/library-posters/: the next
// request reads the disk, the library switched on or off; 404 when Jellyfin has no such image or no longer knows the item,
// when the poster isn't kept and its library is switched off, or when its library was removed, kept or not; 502 with the
// library's sentence, masked, for the owner and a plain one for anyone else when the fetch fails. Signed in only, for
// every user; cached privately for 30 days. The library here is the four sample movies as Jellyfin listed them with their
// posters' tags.
public sealed class LibraryPostersHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/library/posters";
    private const string ApiKey = "0123456789abcdef0123456789abcdef";
    private const string SecondJellyfinAddress = "http://127.0.0.2:9";
    private const string SignInRequiredBody = """{"error":"Sign in to continue."}""";

    /// <summary>The Continental's poster tag as <c>movies-page-with-images.json</c> records it.</summary>
    private const string ContinentalTag = "41f7c98888a81ea514189c27c6c3970c";

    private const string FileName = JellyfinSamples.ContinentalId + "-" + ContinentalTag + ".jpg";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private IIntegrationConnectionSeeder _integrations = null!;
    private AuthorizedUser _owner = null!;
    private AuthorizedUser _anna = null!;
    private FakeJellyfinServer _jellyfin = null!;
    private IntegrationConnectionEntity _livingRoom = null!;

    public LibraryPostersHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    private string PostersDirectory => Path.Combine(_factory.DataDirectory, "library-posters");

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        // The data directory outlives a reset: each test starts with no poster kept.
        if (Directory.Exists(PostersDirectory))
            Directory.Delete(PostersDirectory, recursive: true);
        _integrations = new IntegrationConnectionSeeder(_factory.Scope);
        var users = new UserSeeder(_factory.Scope);
        _owner = await users.CreateAuthorizedOwner("owner");
        _anna = await users.CreateAuthorized("anna");
        _jellyfin = new FakeJellyfinServer { ApiKey = ApiKey };
        _jellyfin.Movies.Clear();
        _jellyfin.Movies.AddRange(JellyfinSamples.MoviesWithImages());
        _jellyfin.Arm(_factory.JellyfinHttp);
        _livingRoom = await _integrations.Create("jellyfin", "Living room", FakeJellyfinServer.DefaultBaseUrl, apiKey: ApiKey);
        // The poster rows as a sync stores them; then nothing asked of Jellyfin yet.
        await new LibrarySnapshotSeeder(_factory.Scope).Sync(_livingRoom, _owner.User);
        _factory.JellyfinHttp.Reset();
        _jellyfin.Arm(_factory.JellyfinHttp);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Poster_without_a_token_is_401_and_asks_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}/{FileName}");

        //Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(SignInRequiredBody, await response.Content.ReadAsStringAsync(cancellationToken));
        Assert.Empty(_factory.JellyfinHttp.Requests);
        Assert.False(Directory.Exists(PostersDirectory));
    }

    [Fact]
    public async Task Poster_first_asked_for_by_a_member_is_fetched_from_the_saved_jellyfin_with_the_key_in_the_header_and_kept()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}/{FileName}", _anna.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/jpeg", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(TestImages.Jpeg(), await response.Content.ReadAsByteArrayAsync(cancellationToken));
        // One GET, to the address the owner saved, of the item's own image as a 342-wide JPEG; the key in the header only
        // (ADR 0007), never in the address a proxy logs.
        var request = Assert.Single(_factory.JellyfinHttp.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal(
            $"http://127.0.0.1:9/Items/{JellyfinSamples.ContinentalId}/Images/Primary?tag={ContinentalTag}&format=Jpg&maxWidth=342&quality=90",
            request.Uri.AbsoluteUri);
        Assert.Equal([$"MediaBrowser Token=\"{ApiKey}\""], request.HeaderValues("Authorization"));
        Assert.Equal(["image/jpeg"], request.HeaderValues("Accept"));
        // Kept whole, under the data directory, and nothing else left there.
        Assert.Equal(TestImages.Jpeg(), await File.ReadAllBytesAsync(Path.Combine(PostersDirectory, FileName), cancellationToken));
        Assert.Equal([FileName], Directory.GetFiles(PostersDirectory).Select(Path.GetFileName));
    }

    [Fact]
    public async Task Poster_is_cached_privately_for_thirty_days_with_the_security_headers()
    {
        //Arrange

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}/{FileName}", _anna.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // Private: the answer needs the user's token. Immutable: the name carries the tag of one version of the image.
        Assert.Equal("private, max-age=2592000, immutable", response.Headers.NonValidated["Cache-Control"].ToString());
        Assert.Equal(["nosniff"], response.Headers.GetValues("X-Content-Type-Options"));
        Assert.Equal([SecurityHeadersMiddleware.ContentSecurityPolicy], response.Headers.GetValues("Content-Security-Policy"));
        Assert.Equal(["DENY"], response.Headers.GetValues("X-Frame-Options"));
    }

    [Fact]
    public async Task Poster_asked_for_again_is_read_from_disk_without_asking_jellyfin()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var first = await _factory.GetHttpRequest($"{BaseUrl}/{FileName}", _anna.AccessToken);
        var second = await _factory.GetHttpRequest($"{BaseUrl}/{FileName}", _owner.AccessToken);

        //Assert
        Assert.Equal((HttpStatusCode.OK, HttpStatusCode.OK), (first.StatusCode, second.StatusCode));
        Assert.Equal(TestImages.Jpeg(), await second.Content.ReadAsByteArrayAsync(cancellationToken));
        Assert.Equal("image/jpeg", second.Content.Headers.ContentType?.MediaType);
        Assert.Equal("private, max-age=2592000, immutable", second.Headers.NonValidated["Cache-Control"].ToString());
        Assert.Single(_factory.JellyfinHttp.Requests);
    }

    [Fact]
    public async Task Poster_kept_on_disk_is_served_with_its_library_switched_off()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        byte[] kept = [0xFF, 0xD8, 0xFF, 0xE0, 0x01, 0x02];
        await KeepAsync(FileName, kept, cancellationToken);
        await SwitchOffAsync(_livingRoom);

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}/{FileName}", _anna.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(kept, await response.Content.ReadAsByteArrayAsync(cancellationToken));
        Assert.Empty(_factory.JellyfinHttp.Requests);
    }

    [Fact]
    public async Task Poster_not_kept_with_its_library_switched_off_is_a_bare_404_and_asks_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await SwitchOffAsync(_livingRoom);

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}/{FileName}", _owner.AccessToken);

        //Assert
        // The app never calls a library the owner switched off: the page shows its placeholder, as for no poster.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsStringAsync(cancellationToken));
        Assert.Empty(_factory.JellyfinHttp.Requests);
        Assert.False(Directory.Exists(PostersDirectory));
    }

    [Fact]
    public async Task Poster_after_its_library_is_removed_is_a_bare_404_even_when_kept_as_its_rows_went_with_it()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await KeepAsync(FileName, TestImages.Jpeg(), cancellationToken);
        var removal = await _factory.DeleteHttpRequest($"/api/settings/integrations/{_livingRoom.Id}", _owner.AccessToken);

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}/{FileName}", _owner.AccessToken);

        //Assert
        // The removal purged the library's snapshot, its posters with it: no row names the file any more, so the endpoint
        // doesn't serve it, kept or not.
        Assert.Equal(HttpStatusCode.NoContent, removal.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsStringAsync(cancellationToken));
        Assert.Empty(_factory.JellyfinHttp.Requests);
        await _factory.WithDbAsync(async db => Assert.Equal(0, await db.MediaWorkPosters.CountAsync(cancellationToken)));
    }

    [Fact]
    public async Task Poster_of_a_tag_no_library_reported_is_a_bare_404_and_asks_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // The Continental's id with another tag: Jellyfin doesn't check the tag and would answer the image, so only the rows
        // keep the endpoint from being a proxy to the library.
        const string OtherTag = "0123456789abcdef0123456789abcdef";

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}/{JellyfinSamples.ContinentalId}-{OtherTag}.jpg", _anna.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsStringAsync(cancellationToken));
        Assert.Empty(_factory.JellyfinHttp.Requests);
        Assert.False(Directory.Exists(PostersDirectory));
    }

    [Theory]
    [InlineData(JellyfinSamples.ContinentalId + "-" + ContinentalTag + ".png")]
    [InlineData(JellyfinSamples.ContinentalId + "-" + ContinentalTag + ".JPG")]
    [InlineData(JellyfinSamples.ContinentalId + "-" + ContinentalTag)]
    [InlineData(JellyfinSamples.ContinentalId + ContinentalTag + ".jpg")]
    [InlineData(JellyfinSamples.ContinentalId + "-" + ContinentalTag + ".jpg.exe")]
    [InlineData(JellyfinSamples.ContinentalId + "-" + ContinentalTag + "-" + ContinentalTag + ".jpg")]
    [InlineData("." + JellyfinSamples.ContinentalId + "-" + ContinentalTag + ".jpg")]
    [InlineData("..%5C" + JellyfinSamples.ContinentalId + "-" + ContinentalTag + ".jpg")]
    [InlineData("..%5Cmam.db")]
    [InlineData("mam.db")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa-" + ContinentalTag + ".jpg")]
    public async Task Poster_with_a_name_that_is_no_poster_file_name_is_a_bare_404_even_when_a_row_holds_its_halves(string name)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // A row for a 65-character item id too, so only the name's pattern stands between that name and Jellyfin.
        await PlantPosterAsync(_livingRoom, new string('a', 65), ContinentalTag);

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}/{name}", _anna.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsStringAsync(cancellationToken));
        Assert.Empty(_factory.JellyfinHttp.Requests);
        Assert.False(Directory.Exists(PostersDirectory));
    }

    [Fact]
    public async Task Poster_of_an_item_jellyfin_has_no_image_for_is_a_bare_404_and_nothing_is_kept()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // The image went after the sync read its tag: Jellyfin answers its recorded 404.
        Movie(JellyfinSamples.ContinentalId)["ImageTags"] = new JsonObject();

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}/{FileName}", _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsStringAsync(cancellationToken));
        Assert.Single(_factory.JellyfinHttp.Requests);
        Assert.False(Directory.Exists(PostersDirectory) && Directory.EnumerateFileSystemEntries(PostersDirectory).Any());
    }

    [Fact]
    public async Task Poster_of_an_item_jellyfin_no_longer_knows_is_a_bare_404_and_nothing_is_kept()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // Removed from Jellyfin after the sync: it answers the id with its recorded 400.
        _jellyfin.Movies.RemoveAll(movie => (string?)movie["Id"] == JellyfinSamples.ContinentalId);

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}/{FileName}", _anna.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsStringAsync(cancellationToken));
        Assert.Single(_factory.JellyfinHttp.Requests);
        Assert.False(Directory.Exists(PostersDirectory) && Directory.EnumerateFileSystemEntries(PostersDirectory).Any());
    }

    [Fact]
    public async Task Poster_whose_fetch_fails_is_502_with_jellyfins_sentence_for_the_owner_and_nothing_kept()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        _jellyfin.Intercept = request => IsPosterRequest(request) ? new HttpResponseMessage(HttpStatusCode.InternalServerError) : null;

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}/{FileName}", _owner.AccessToken);
        var again = await _factory.GetHttpRequest($"{BaseUrl}/{FileName}", _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal("Jellyfin answered 500 Internal Server Error. Its own log says why.", await response.ReadErrorAsync(cancellationToken));
        Assert.False(response.Headers.NonValidated.Contains("Cache-Control"));
        // Nothing kept, so the next request asks again.
        Assert.Equal(HttpStatusCode.BadGateway, again.StatusCode);
        Assert.Equal(2, _factory.JellyfinHttp.Requests.Count);
        Assert.False(Directory.Exists(PostersDirectory) && Directory.EnumerateFileSystemEntries(PostersDirectory).Any());
    }

    [Fact]
    public async Task Poster_whose_fetch_fails_is_502_with_a_plain_sentence_for_anyone_but_the_owner()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        _jellyfin.Intercept = request => IsPosterRequest(request)
            ? throw new HttpRequestException(HttpRequestError.ConnectionError, "Connection refused.")
            : (HttpResponseMessage?)null;

        //Act
        var member = await _factory.GetHttpRequest($"{BaseUrl}/{FileName}", _anna.AccessToken);
        var owner = await _factory.GetHttpRequest($"{BaseUrl}/{FileName}", _owner.AccessToken);

        //Assert
        // The library's sentence names the address the owner saved: the rule of a sync run's error.
        Assert.Equal(HttpStatusCode.BadGateway, member.StatusCode);
        Assert.Equal("The media library couldn't send this poster.", await member.ReadErrorAsync(cancellationToken));
        Assert.Equal(HttpStatusCode.BadGateway, owner.StatusCode);
        Assert.Equal(
            "Couldn't reach Jellyfin at http://127.0.0.1:9: nothing accepted the connection. Check the address and port, and that Jellyfin is running.",
            await owner.ReadErrorAsync(cancellationToken));
    }

    [Fact]
    public async Task Poster_answered_with_a_redirect_that_carries_the_key_is_502_with_the_key_masked_and_follows_none()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        _jellyfin.Intercept = request => IsPosterRequest(request) ? VendorResponses.Redirect($"https://sso.test/{ApiKey}/sign-in?token={ApiKey}") : null;

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}/{FileName}", _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal(
            "The address answered with a redirect to https://sso.test/***/sign-in. Enter the address the service is really at: the app doesn't follow redirects, so your credentials never go anywhere else.",
            await response.ReadErrorAsync(cancellationToken));
        // The redirect was reported, never followed: one request, to the saved address.
        Assert.Equal("127.0.0.1", Assert.Single(_factory.JellyfinHttp.Requests).Uri.Host);
    }

    [Fact]
    public async Task Poster_of_a_library_whose_failure_quotes_its_key_gives_the_owner_the_sentence_masked()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        const string Secret = "fake-library-key-0123456789";
        const string ItemId = "c0ffee00c0ffee00c0ffee00c0ffee00";
        const string Tag = "abc123";
        var attic = await _integrations.Create(FakeLibraryItemIdentifier.TypeId, "Attic", apiKey: Secret);
        await PlantPosterAsync(attic, ItemId, Tag);
        var asked = new List<(string ItemId, string Tag)>();
        _factory.FakeLibraryIdentifier.Posters = (itemId, tag) =>
        {
            asked.Add((itemId, tag));
            return IntegrationCallResult<LibraryPoster>.Failed(IntegrationCallStatus.AuthenticationFailed, $"The fake library refused the key {Secret}.");
        };

        //Act
        var owner = await _factory.GetHttpRequest($"{BaseUrl}/{ItemId}-{Tag}.jpg", _owner.AccessToken);
        var member = await _factory.GetHttpRequest($"{BaseUrl}/{ItemId}-{Tag}.jpg", _anna.AccessToken);

        //Assert
        // Masked again with the call's secrets, after the adapter's own pass (the MAM-7 rule).
        Assert.Equal(HttpStatusCode.BadGateway, owner.StatusCode);
        Assert.Equal("The fake library refused the key ***.", await owner.ReadErrorAsync(cancellationToken));
        Assert.Equal(HttpStatusCode.BadGateway, member.StatusCode);
        Assert.Equal("The media library couldn't send this poster.", await member.ReadErrorAsync(cancellationToken));
        // The library that reported the row was asked, by its own item id and tag, with its key decrypted; no connection
        // test first.
        Assert.Equal([(ItemId, Tag), (ItemId, Tag)], asked);
        Assert.Equal([Secret, Secret], _factory.FakeLibraryIdentifier.Calls.Select(call => call.ApiKey));
        Assert.Empty(_factory.JellyfinHttp.Requests);
    }

    [Fact]
    public async Task Poster_of_a_library_whose_key_cant_be_read_is_502_with_the_key_sentence_for_the_owner_and_asks_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        const string ItemId = "d0d0d0d0d0d0d0d0d0d0d0d0d0d0d0d0";
        var attic = await _integrations.CreateWithUnreadableApiKey("jellyfin", "Attic");
        await PlantPosterAsync(attic, ItemId, ContinentalTag);

        //Act
        var owner = await _factory.GetHttpRequest($"{BaseUrl}/{ItemId}-{ContinentalTag}.jpg", _owner.AccessToken);
        var member = await _factory.GetHttpRequest($"{BaseUrl}/{ItemId}-{ContinentalTag}.jpg", _anna.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.BadGateway, owner.StatusCode);
        Assert.Equal(
            "The stored API key of “Attic” can't be read with this installation's encryption key. Enter the API key again and save.",
            await owner.ReadErrorAsync(cancellationToken));
        Assert.Equal(HttpStatusCode.BadGateway, member.StatusCode);
        Assert.Equal("The media library couldn't send this poster.", await member.ReadErrorAsync(cancellationToken));
        Assert.Empty(_factory.JellyfinHttp.Requests);
    }

    [Fact]
    public async Task Poster_reported_by_two_libraries_is_fetched_from_the_one_switched_on()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var bedroomServer = new FakeJellyfinServer(SecondJellyfinAddress) { ApiKey = ApiKey };
        bedroomServer.Movies.Clear();
        bedroomServer.Movies.AddRange(JellyfinSamples.MoviesWithImages());
        FakeJellyfinServer.Arm(_factory.JellyfinHttp, _jellyfin, bedroomServer);
        var bedroom = await _integrations.Create("jellyfin", "Bedroom", SecondJellyfinAddress, apiKey: ApiKey);
        await PlantPosterAsync(bedroom, JellyfinSamples.ContinentalId, ContinentalTag);
        await SwitchOffAsync(_livingRoom);

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}/{FileName}", _anna.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(TestImages.Jpeg(), await response.Content.ReadAsByteArrayAsync(cancellationToken));
        Assert.Equal("127.0.0.2", Assert.Single(_factory.JellyfinHttp.Requests).Uri.Host);
    }

    [Fact]
    public async Task Poster_whose_row_has_no_library_left_is_a_bare_404_and_asks_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        const string ItemId = "e0e0e0e0e0e0e0e0e0e0e0e0e0e0e0e0";
        // A row whose integration is gone (the foreign key's SetNull): the purge never leaves one, a hand edit might.
        await PlantPosterAsync(null, ItemId, ContinentalTag);

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}/{ItemId}-{ContinentalTag}.jpg", _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsStringAsync(cancellationToken));
        Assert.Empty(_factory.JellyfinHttp.Requests);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private JsonObject Movie(string id) => _jellyfin.Movies.Single(movie => (string?)movie["Id"] == id);

    private static bool IsPosterRequest(HttpRequestMessage request) =>
        request.RequestUri!.AbsolutePath.EndsWith("/Images/Primary", StringComparison.Ordinal);

    // Straight to the column, as the owner's switch on the Integrations page leaves it.
    private Task SwitchOffAsync(IntegrationConnectionEntity integration) => _factory.WithDbAsync(db =>
        db.IntegrationConnections
            .Where(connection => connection.Id == integration.Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(connection => connection.IsEnabled, false)));

    private async Task KeepAsync(string fileName, byte[] content, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(PostersDirectory);
        await File.WriteAllBytesAsync(Path.Combine(PostersDirectory, fileName), content, cancellationToken);
    }

    /// <summary>A poster row of <paramref name="integration"/>'s item <paramref name="itemId"/> on a movie of its own, as a
    /// sync would have stored it; no integration leaves the row without one.</summary>
    private async Task PlantPosterAsync(IntegrationConnectionEntity? integration, string itemId, string tag)
    {
        var works = new MediaWorkDataFactory(_factory.Scope.GetRequiredService<ApplicationDbContext>(), _factory.Time);
        var work = await works.Create(MediaWorkKind.Movie);
        await works.AddTitle(work.Id, $"Film {itemId}", MediaTitleKind.Display, 2024, integration?.Id, integration?.Name, itemId);
        await works.AddPoster(work.Id, tag, integration?.Id, integration?.Name ?? "Removed", itemId);
    }
}
