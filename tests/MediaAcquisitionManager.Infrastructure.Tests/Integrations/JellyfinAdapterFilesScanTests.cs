using System.Net;
using System.Text.Json.Nodes;
using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Abstractions.Library;
using MediaAcquisitionManager.Infrastructure.Integrations.Jellyfin;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Texts;

namespace MediaAcquisitionManager.Infrastructure.Tests.Integrations;

// MAM-25 — asking Jellyfin to look at a download's new files (IMediaLibraryFileLookup.RequestScanAsync): one
// POST /Library/Media/Updated naming every path the way Jellyfin's own folder watcher reports a file that appeared, read in
// Jellyfin 12.2's source, which answers 204 at once, before it looks. The request is sent once and asks for nothing else; a
// refusal is the key's, a 5xx the server's, and any other answer one Jellyfin doesn't send for it. Reading what Jellyfin made
// of the files has its own classes (JellyfinAdapterFilesReadTests, JellyfinAdapterFilesReadFailureTests). MAM-26 — the same
// request tells Jellyfin that old copies went into the quarantine, each path as "Deleted", or came back, as "Created"
// (ADR 0030); a change that is no member is a caller's mistake.
public sealed class JellyfinAdapterFilesScanTests : IDisposable
{
    private const string BaseUrl = "http://jellyfin.test:8096";
    private const string ApiKey = "4f1d0b9c2e7a4c3b8d6e5f7a9b0c1d2e";

    private const string FilmPath = "/media/movies/Alien (1979)/Alien.1979.2160p.UHD.BDRemux.mkv";
    private const string EpisodePath = "/media/tv-shows/Yellowstone/Yellowstone.2018.S02.1080p/Yellowstone.2018.S02E05.1080p.mkv";

    private const string KeyRefusedError =
        "Jellyfin refused the API key. Create one in Jellyfin under Dashboard → API Keys and enter it here.";

    private const string ScanForbiddenError =
        "Jellyfin accepted the key but won't let it ask the library to look at new files (403 Forbidden). Use a key from Dashboard → API Keys, not a user's access token.";

    private const string NotFoundError =
        "Jellyfin wasn't found at this address (404 Not Found for /Library/Media/Updated). If Jellyfin runs under a base path, include it, for example http://192.168.1.10:8096/jellyfin.";

    private static readonly IntegrationConnectionSettings Settings = new(null, BaseUrl, null, null, ApiKey);

    private readonly IntegrationAdapterHarness _harness = new();
    private readonly FakeJellyfinServer _server = new(BaseUrl) { ApiKey = ApiKey };

    public JellyfinAdapterFilesScanTests() => _server.Arm(_harness.JellyfinHttp);

    /// <summary>Each caller's mistake the lookup refuses before anything is sent; the paths are
    /// <see cref="LookupMistake"/>'s.</summary>
    public static TheoryData<string> LookupMistakes => new()
    {
        "no list", "no path", "one path more than the most", "a null path", "a blank path", "a relative path",
        "a home-relative path", "a drive-relative path", "a path with a line break", "a path with a tab",
        "a path with a dot-dot folder", "a path of 4097 characters",
    };

    private IMediaLibraryFileLookup Lookup => _harness.Adapter<IMediaLibraryFileLookup>(JellyfinAdapter.TypeId);

    public void Dispose() => _harness.Dispose();

    /// <summary>The paths each of <see cref="LookupMistakes"/> names.</summary>
    public static IReadOnlyList<string>? LookupMistake(string name) => name switch
    {
        "no list" => null,
        "no path" => [],
        "one path more than the most" => Films(IMediaLibraryFileLookup.MaximumPaths + 1),
        "a null path" => [FilmPath, null!],
        "a blank path" => [FilmPath, "  "],
        "a relative path" => ["media/movies/Alien (1979)/Alien.mkv"],
        "a home-relative path" => ["~/movies/Alien.mkv"],
        "a drive-relative path" => ["D:movies\\Alien.mkv"],
        "a path with a line break" => ["/media/movies/Alien (1979)/Alien.mkv\n"],
        "a path with a tab" => ["/media/movies/Alien\t(1979)/Alien.mkv"],
        "a path with a dot-dot folder" => ["/media/movies/../../etc/passwd"],
        "a path of 4097 characters" => ["/media/" + new string('a', 4090)],
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "No such mistake."),
    };

    /// <summary><paramref name="count"/> distinct film paths, each a valid path the lookup takes.</summary>
    public static IReadOnlyList<string> Films(int count) =>
        [.. Enumerable.Range(1, count).Select(number => $"/media/movies/Film {number}/Film {number}.mkv")];

    [Fact]
    public async Task RequestScan_posts_every_path_in_one_body_with_the_key_and_is_taken_on_204()
    {
        //Arrange
        string[] paths = [FilmPath, EpisodePath];

        //Act
        var result = await Lookup.RequestScanAsync(Settings, paths, LibraryFileChange.Created, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        Assert.Null(result.Error);
        Assert.Equal(2, result.Value!.PathCount);
        // One request, and nothing else: no read, no refresh of an item.
        var request = Assert.Single(_harness.JellyfinHttp.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("http://jellyfin.test:8096/Library/Media/Updated", request.Uri.AbsoluteUri);
        Assert.Equal([$"MediaBrowser Token=\"{ApiKey}\""], request.HeaderValues("Authorization"));
        Assert.Equal(["application/json"], request.HeaderValues("Content-Type"));
        Assert.Equal(
            """{"Updates":[{"Path":"/media/movies/Alien (1979)/Alien.1979.2160p.UHD.BDRemux.mkv","UpdateType":"Created"},{"Path":"/media/tv-shows/Yellowstone/Yellowstone.2018.S02.1080p/Yellowstone.2018.S02E05.1080p.mkv","UpdateType":"Created"}]}""",
            request.Body);
        Assert.DoesNotContain(ApiKey, request.Uri.OriginalString, StringComparison.Ordinal);
        Assert.Equal(paths, Assert.Single(_server.ScanRequests));
        Assert.Equal(["Created", "Created"], Assert.Single(_server.ScanUpdateTypes));
    }

    [Fact]
    public async Task RequestScan_of_paths_that_went_posts_each_as_deleted_and_is_taken_on_204()
    {
        //Arrange
        // MAM-26 — the old copy and its subtitle, moved into the quarantine: told at the paths they went from.
        string[] paths = [FilmPath, "/media/movies/Alien (1979)/Alien.1979.2160p.UHD.BDRemux.uk.srt"];

        //Act
        var result = await Lookup.RequestScanAsync(Settings, paths, LibraryFileChange.Deleted, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal((IntegrationCallStatus.Succeeded, (string?)null, 2), (result.Status, TestTexts.En(result.Error), result.Value!.PathCount));
        // One request, the same one a download's new files get, and nothing else.
        var request = Assert.Single(_harness.JellyfinHttp.Requests);
        Assert.Equal((HttpMethod.Post, "http://jellyfin.test:8096/Library/Media/Updated"), (request.Method, request.Uri.AbsoluteUri));
        Assert.Equal(
            """{"Updates":[{"Path":"/media/movies/Alien (1979)/Alien.1979.2160p.UHD.BDRemux.mkv","UpdateType":"Deleted"},{"Path":"/media/movies/Alien (1979)/Alien.1979.2160p.UHD.BDRemux.uk.srt","UpdateType":"Deleted"}]}""",
            request.Body);
        Assert.Equal(paths, Assert.Single(_server.ScanRequests));
        Assert.Equal(["Deleted", "Deleted"], Assert.Single(_server.ScanUpdateTypes));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public async Task RequestScan_of_a_change_that_is_no_member_is_a_callers_mistake_and_sends_nothing(int change)
    {
        //Arrange
        // MAM-26 — never guessed into Created or Deleted: a request that said the wrong thing would still be sent.

        //Act
        var exception = await Record.ExceptionAsync(() => Lookup.RequestScanAsync(Settings, [FilmPath], (LibraryFileChange)change, TestContext.Current.CancellationToken));

        //Assert
        Assert.IsType<ArgumentOutOfRangeException>(exception);
        Assert.Empty(_harness.JellyfinHttp.Requests);
    }

    [Fact]
    public async Task RequestScan_sends_each_path_exactly_as_given()
    {
        //Arrange
        // Jellyfin's own spelling of a file is the path: double spaces, an ampersand, Cyrillic, a Windows server's
        // backslashes — nothing is trimmed, normalised or escaped away.
        string[] paths =
        [
            "/media/cartoons/Tom & Jerry Kids season 1-4 (1990-1993) [ukr] TVRip [Hurtom] by MasterYura/02. Toys Will Be Toys  Droopy Delivers  My Pal.mkv",
            "/media/movies/Чужі (1979)/Чужі.1979.mkv",
            @"D:\Media\Movies\Alien (1979)\Alien.mkv",
        ];

        //Act
        var result = await Lookup.RequestScanAsync(Settings, paths, LibraryFileChange.Created, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        Assert.Equal(paths, Assert.Single(_server.ScanRequests));
        var updates = JsonNode.Parse(Assert.Single(_harness.JellyfinHttp.Requests).Body!)!["Updates"]!.AsArray();
        Assert.Equal(paths, updates.Select(update => (string?)update!["Path"]));
        Assert.All(updates, update => Assert.Equal("Created", (string?)update!["UpdateType"]));
    }

    [Fact]
    public async Task RequestScan_takes_the_most_paths_the_contract_allows_and_a_path_of_4096_characters()
    {
        //Arrange
        // MAM-25 — the contract's bound is the adapter's: the verifier asks in batches of exactly this many.
        string[] paths = [.. Films(IMediaLibraryFileLookup.MaximumPaths - 1), "/media/" + new string('a', 4089)];

        //Act
        var result = await Lookup.RequestScanAsync(Settings, paths, LibraryFileChange.Created, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal((500, 4096), (IMediaLibraryFileLookup.MaximumPaths, paths[^1].Length));
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        Assert.Equal(IMediaLibraryFileLookup.MaximumPaths, result.Value!.PathCount);
        Assert.Equal(IMediaLibraryFileLookup.MaximumPaths, Assert.Single(_server.ScanRequests).Count);
        Assert.Single(_harness.JellyfinHttp.Requests);
    }

    [Theory]
    [InlineData(401, IntegrationCallStatus.AuthenticationFailed, KeyRefusedError)]
    [InlineData(403, IntegrationCallStatus.AuthenticationFailed, ScanForbiddenError)]
    [InlineData(404, IntegrationCallStatus.UnexpectedResponse, NotFoundError)]
    [InlineData(500, IntegrationCallStatus.ServiceError, "Jellyfin answered 500 Internal Server Error. Its own log says why.")]
    [InlineData(503, IntegrationCallStatus.ServiceError, "Jellyfin answered 503 Service Unavailable. Its own log says why.")]
    [InlineData(200, IntegrationCallStatus.UnexpectedResponse, "Jellyfin answered 200 OK, which it doesn't send for this request. Check the address.")]
    [InlineData(400, IntegrationCallStatus.UnexpectedResponse, "Jellyfin answered 400 Bad Request, which it doesn't send for this request. Check the address.")]
    [InlineData(302, IntegrationCallStatus.UnexpectedResponse,
        "The address answered with a redirect. Enter the address the service is really at: the app doesn't follow redirects, so your credentials never go anywhere else.")]
    public async Task RequestScan_answered_with_anything_but_204_fails_with_its_status_and_sentence_and_is_never_sent_again(
        int statusCode, IntegrationCallStatus expectedStatus, string expectedMessage)
    {
        //Arrange
        // Jellyfin answers the request 204 whatever the path is (its source); a 200 is a proxy's page, not Jellyfin taking it.
        _server.ScanRefusal = (HttpStatusCode)statusCode;

        //Act
        var result = await Lookup.RequestScanAsync(Settings, [FilmPath], LibraryFileChange.Created, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(expectedStatus, result.Status);
        Assert.Equal(expectedMessage, TestTexts.En(result.Error));
        Assert.Null(result.Value);
        Assert.Single(_harness.JellyfinHttp.Requests);
    }

    [Fact]
    public async Task RequestScan_with_a_key_jellyfin_doesnt_know_is_refused_with_the_key_sentence()
    {
        //Arrange
        var settings = Settings with { ApiKey = "0000000000000000000000000000dead" };

        //Act
        var result = await Lookup.RequestScanAsync(settings, [FilmPath], LibraryFileChange.Created, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.AuthenticationFailed, result.Status);
        Assert.Equal(KeyRefusedError, TestTexts.En(result.Error));
        Assert.Empty(_server.ScanRequests);
    }

    [Fact]
    public async Task RequestScan_answered_with_a_redirect_follows_none_and_masks_the_key()
    {
        //Arrange
        _harness.JellyfinHttp.Handler = (_, _) => VendorResponses.Redirect($"http://elsewhere.test/{ApiKey}/Library/Media/Updated");

        //Act
        var result = await Lookup.RequestScanAsync(Settings, [FilmPath], LibraryFileChange.Created, TestContext.Current.CancellationToken);

        //Assert
        Assert.Single(_harness.JellyfinHttp.Requests);
        Assert.Equal(IntegrationCallStatus.UnexpectedResponse, result.Status);
        Assert.StartsWith("The address answered with a redirect to http://elsewhere.test/", TestTexts.En(result.Error), StringComparison.Ordinal);
        Assert.DoesNotContain(ApiKey, TestTexts.En(result.Error), StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(JellyfinAdapterTests.TransportFailures), MemberType = typeof(JellyfinAdapterTests))]
    public async Task RequestScan_that_fails_on_the_way_has_the_connection_tests_sentence_and_is_never_sent_again(
        HttpRequestError error, IntegrationCallStatus expectedStatus, string expectedMessage)
    {
        //Arrange
        _harness.JellyfinHttp.Handler = (request, _) =>
            throw new HttpRequestException(error, $"Socket failure on {request.RequestUri} with the key {ApiKey}");

        //Act
        var result = await Lookup.RequestScanAsync(Settings, [FilmPath], LibraryFileChange.Created, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal((expectedStatus, expectedMessage), (result.Status, TestTexts.En(result.Error)));
        Assert.Single(_harness.JellyfinHttp.Requests);
    }

    [Fact]
    public async Task RequestScan_goes_through_the_shared_client_and_its_fifteen_seconds()
    {
        //Arrange
        _harness.JellyfinHttp.Handler = (_, _) => throw new TaskCanceledException("The request timed out.");

        //Act
        var result = await Lookup.RequestScanAsync(Settings, [FilmPath], LibraryFileChange.Created, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.TimedOut, result.Status);
        Assert.Equal("Jellyfin at http://jellyfin.test:8096 didn't answer within 15 seconds.", TestTexts.En(result.Error));
        Assert.Single(_harness.JellyfinHttp.Requests);
    }

    [Theory]
    [MemberData(nameof(LookupMistakes))]
    public async Task RequestScan_refuses_a_callers_mistake_without_sending_anything(string mistake)
    {
        //Arrange
        var paths = LookupMistake(mistake);

        //Act
        var exception = await Record.ExceptionAsync(() => Lookup.RequestScanAsync(Settings, paths!, LibraryFileChange.Created, TestContext.Current.CancellationToken));

        //Assert
        Assert.IsAssignableFrom<ArgumentException>(exception);
        Assert.Empty(_harness.JellyfinHttp.Requests);
    }

    [Fact]
    public async Task RequestScan_without_a_connection_throws_without_sending_anything()
    {
        //Arrange

        //Act
        var exception = await Record.ExceptionAsync(() => Lookup.RequestScanAsync(null!, [FilmPath], LibraryFileChange.Created, TestContext.Current.CancellationToken));

        //Assert
        Assert.IsType<ArgumentNullException>(exception);
        Assert.Empty(_harness.JellyfinHttp.Requests);
    }

    [Fact]
    public void Find_of_the_file_lookup_gives_jellyfins_media_library_adapter()
    {
        //Arrange
        var registry = _harness.Registry;

        //Act
        var lookup = registry.Find<IMediaLibraryFileLookup>(JellyfinAdapter.TypeId);

        //Assert
        // An optional capability found the way the identifier is (ADR 0005): a further interface of the library's adapter.
        Assert.NotNull(lookup);
        Assert.Same(registry.Find<IMediaLibraryProvider>(JellyfinAdapter.TypeId), lookup);
    }
}
