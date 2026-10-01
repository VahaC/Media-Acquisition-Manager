using System.Net;
using System.Text.Json.Nodes;
using MediaAcquisitionManager.Core.Abstractions.Downloads;
using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Infrastructure.Integrations.QBittorrent;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;

namespace MediaAcquisitionManager.Infrastructure.Tests.Integrations;

// MAM-11 — qBittorrent's categories as the download client contract lists them, against the real answers
// of the reference installation (VendorSamples/QBittorrent/categories.json and app-version.txt, 5.2.3):
// each category's key exactly as qBittorrent names it and the folder it saves to, the version gate, and
// every failure answered like the connection test (QBittorrentAdapterTests) answers it. The session the
// calls share is pinned in QBittorrentAdapterSessionsTests, the folder rules in QBittorrentCategoryPathsTests.
public sealed class QBittorrentAdapterDestinationsTests : IDisposable
{
    private const string BaseUrl = "http://qbittorrent.test:8080";
    private const string UserName = "admin";
    private const string Password = "correct-horse-battery";
    private const string SessionId = "Zq3xY9LmN2pQ7rT4vW8sK1aB6cD0eF5g";
    private const string SignInPath = "/api/v2/auth/login";
    private const string VersionPath = "/api/v2/app/version";
    private const string CategoriesPath = "/api/v2/torrents/categories";
    private const string DefaultSavePathPath = "/api/v2/app/defaultSavePath";

    private const string NotCategoriesError = "The service at this address answered, but not with qBittorrent's categories.";

    private static readonly IntegrationConnectionSettings Settings =
        new(Guid.Parse("0b6f2c4e-8a1d-4f3b-9c5e-7d2a1b3c4e5f"), BaseUrl, UserName, Password, null);

    private readonly IntegrationAdapterHarness _harness = new();

    private IDownloadClient Client => _harness.Adapter<IDownloadClient>(QBittorrentAdapter.TypeId);

    /// <summary>Bodies that answer 200 to torrents/categories but are not qBittorrent's object of
    /// categories.</summary>
    public static TheoryData<string> NotCategories =>
    [
        "",
        "<!DOCTYPE html><html><body>qBittorrent Web UI</body></html>",
        "[{",
        "null",
        "[]",
        "\"Movies\"",
        """{"Movies":1}""",
        """{"Movies":null}""",
        """{"Movies":["/downloads/movies"]}""",
        """{"Movies":{"savePath":"/downloads/movies"},"Series":"/downloads/series"}""",
    ];

    /// <summary>qBittorrent's sentence for each transport failure, and the status it maps to.</summary>
    public static TheoryData<HttpRequestError, IntegrationCallStatus, string> TransportFailures => new()
    {
        {
            HttpRequestError.NameResolutionError, IntegrationCallStatus.Unreachable,
            "Couldn't reach qBittorrent at http://qbittorrent.test:8080: the host name can't be found."
        },
        {
            HttpRequestError.ConnectionError, IntegrationCallStatus.Unreachable,
            "Couldn't reach qBittorrent at http://qbittorrent.test:8080: nothing accepted the connection. Check the address and port, and that qBittorrent is running."
        },
        {
            HttpRequestError.ConfigurationLimitExceeded, IntegrationCallStatus.UnexpectedResponse,
            "The answer from http://qbittorrent.test:8080 is far larger than anything qBittorrent sends. Check the address."
        },
        {
            HttpRequestError.ResponseEnded, IntegrationCallStatus.Unreachable,
            "Couldn't reach qBittorrent at http://qbittorrent.test:8080: the connection failed before it answered."
        },
    };

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task ListDestinations_lists_the_four_categories_of_the_sample_with_the_folders_they_save_to()
    {
        //Arrange
        _harness.QBittorrentHttp.Handler = Route();

        //Act
        var result = await Client.ListDestinationsAsync(Settings, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        Assert.Null(result.Error);
        // The keys exactly as qBittorrent names them, the space in "TV Shows" included: they are what a
        // submission sends.
        Assert.Equal(
            [
                "Cartoons | Cartoons | /downloads/cartoons",
                "Documentary | Documentary | /downloads/documentary",
                "Movies | Movies | /downloads/movies",
                "TV Shows | TV Shows | /downloads/tv-shows",
            ],
            Describe(result.Value!));
    }

    [Fact]
    public async Task ListDestinations_signs_in_then_reads_the_version_and_the_categories_with_the_session_cookie()
    {
        //Arrange
        _harness.QBittorrentHttp.Handler = Route();

        //Act
        await Client.ListDestinationsAsync(Settings, TestContext.Current.CancellationToken);

        //Assert
        // No default save path: every sample category has an absolute one.
        Assert.Collection(
            _harness.QBittorrentHttp.Requests,
            signIn =>
            {
                Assert.Equal(HttpMethod.Post, signIn.Method);
                Assert.Equal(BaseUrl + SignInPath, signIn.Uri.AbsoluteUri);
                Assert.Equal("username=admin&password=correct-horse-battery", signIn.Body);
            },
            version =>
            {
                Assert.Equal(HttpMethod.Get, version.Method);
                Assert.Equal(BaseUrl + VersionPath, version.Uri.AbsoluteUri);
                Assert.Equal([$"QBT_SID_8080={SessionId}"], version.HeaderValues("Cookie"));
            },
            categories =>
            {
                Assert.Equal(HttpMethod.Get, categories.Method);
                Assert.Equal(BaseUrl + CategoriesPath, categories.Uri.AbsoluteUri);
                Assert.Equal([$"QBT_SID_8080={SessionId}"], categories.HeaderValues("Cookie"));
                Assert.Null(categories.Body);
            });
    }

    [Fact]
    public async Task ListDestinations_sends_nothing_but_the_sign_in_and_gets_and_never_a_save_path_or_the_preferences()
    {
        //Arrange
        // Every request the listing can send: one category needs the default save path.
        _harness.QBittorrentHttp.Handler = Route(categories: () => Categories(("Movies", ""), ("Series", "/downloads/series")));

        //Act
        var result = await Client.ListDestinationsAsync(Settings, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        Assert.Equal(
            [$"POST {SignInPath}", $"GET {VersionPath}", $"GET {CategoriesPath}", $"GET {DefaultSavePathPath}"],
            _harness.QBittorrentHttp.Requests.Select(request => $"{request.Method} {request.Uri.AbsolutePath}"));
        // No parameter anywhere but the sign-in's credentials, so never a savepath (specification §3.2: a
        // category's folder is the client's own). And never app/preferences, which carries the Web UI's API
        // key and the proxy and mail passwords.
        Assert.All(_harness.QBittorrentHttp.Requests, request =>
        {
            Assert.Empty(request.Uri.Query);
            if (request.Method == HttpMethod.Post)
                Assert.Equal("username=admin&password=correct-horse-battery", request.Body);
            else
                Assert.Null(request.Body);
            Assert.DoesNotContain("preferences", request.Uri.AbsolutePath, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public async Task ListDestinations_keeps_the_order_qbittorrent_lists_the_categories_in()
    {
        //Arrange
        _harness.QBittorrentHttp.Handler = Route(categories: () => Categories(
            ("Серіали", "/downloads/tv"), ("Movies", "/downloads/movies"), ("Anime", "/downloads/anime")));

        //Act
        var result = await Client.ListDestinationsAsync(Settings, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(["Серіали", "Movies", "Anime"], result.Value!.Select(destination => destination.Key));
    }

    [Fact]
    public async Task ListDestinations_of_a_client_without_categories_lists_none()
    {
        //Arrange
        _harness.QBittorrentHttp.Handler = Route(categories: () => VendorResponses.Text("{}", HttpStatusCode.OK, "application/json"));

        //Act
        var result = await Client.ListDestinationsAsync(Settings, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        Assert.Empty(result.Value!);
        Assert.Equal(3, _harness.QBittorrentHttp.Requests.Count);
    }

    [Fact]
    public async Task ListDestinations_asks_for_the_default_save_path_for_a_category_without_one()
    {
        //Arrange
        _harness.QBittorrentHttp.Handler = Route(
            categories: () => Categories(("Movies", ""), ("Series", "series"), ("Cartoons", "/downloads/cartoons")),
            defaultSavePath: () => VendorResponses.Text("/downloads", HttpStatusCode.OK));

        //Act
        var result = await Client.ListDestinationsAsync(Settings, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        Assert.Equal(
            [
                "Movies | Movies | /downloads/Movies",
                "Series | Series | /downloads/series",
                "Cartoons | Cartoons | /downloads/cartoons",
            ],
            Describe(result.Value!));
        // Once for every category that needs it, with the session cookie.
        var defaultSavePath = Assert.Single(_harness.QBittorrentHttp.Requests, request => request.Uri.AbsolutePath == DefaultSavePathPath);
        Assert.Equal(HttpMethod.Get, defaultSavePath.Method);
        Assert.Equal([$"QBT_SID_8080={SessionId}"], defaultSavePath.HeaderValues("Cookie"));
    }

    [Fact]
    public async Task ListDestinations_puts_a_category_under_a_windows_hosts_default_save_path()
    {
        //Arrange
        // A Windows host: app/defaultSavePath answers with backslashes (Path::toString()), a category's own
        // folder comes with forward slashes (Path::data()).
        _harness.QBittorrentHttp.Handler = Route(
            categories: () => Categories(("Movies", ""), ("Series", "D:/Torrents/Series")),
            defaultSavePath: () => VendorResponses.Text(@"D:\Torrents", HttpStatusCode.OK));

        //Act
        var result = await Client.ListDestinationsAsync(Settings, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal([@"Movies | Movies | D:\Torrents\Movies", @"Series | Series | D:\Torrents\Series"], Describe(result.Value!));
    }

    [Fact]
    public async Task ListDestinations_lists_a_windows_hosts_unc_category_and_its_subcategory_inside_that_share()
    {
        //Arrange
        // 5.2 on Windows lists \\nas\media\Movies as //nas/media/Movies (Path::data()), and its subcategory
        // without a save path follows it.
        _harness.QBittorrentHttp.Handler = Route(categories: () => Categories(("Movies", "//nas/media/Movies"), ("Movies/4K", "")));

        //Act
        var result = await Client.ListDestinationsAsync(Settings, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(
            [@"Movies | Movies | \\nas\media\Movies", @"Movies/4K | Movies/4K | \\nas\media\Movies\4K"],
            Describe(result.Value!));
        Assert.DoesNotContain(_harness.QBittorrentHttp.Requests, request => request.Uri.AbsolutePath == DefaultSavePathPath);
    }

    [Theory]
    [InlineData("v4.6.7", "/downloads/Movies/4K", true)]
    [InlineData("v5.1.4", null, false)]
    [InlineData("v5.2.3", "/media/films/4K", false)]
    public async Task ListDestinations_works_out_a_subcategorys_folder_by_the_rules_of_the_version_that_lists_it(
        string version, string? expectedFolder, bool asksForTheDefault)
    {
        //Arrange
        _harness.QBittorrentHttp.Handler = Route(
            version: () => VendorResponses.Text(version, HttpStatusCode.OK),
            categories: () => Categories(("Movies", "/media/films"), ("Movies/4K", "")));

        //Act
        var result = await Client.ListDestinationsAsync(Settings, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        Assert.Equal(expectedFolder, result.Value!.Single(destination => destination.Key == "Movies/4K").ServicePath);
        Assert.Equal(asksForTheDefault, _harness.QBittorrentHttp.Requests.Any(request => request.Uri.AbsolutePath == DefaultSavePathPath));
    }

    [Fact]
    public async Task ListDestinations_takes_a_category_without_a_save_path_as_text_as_one_whose_folder_is_unknown()
    {
        //Arrange
        _harness.QBittorrentHttp.Handler = Route(categories: () => VendorResponses.Text(
            """{"Movies":{"name":"Movies"},"Series":{"name":"Series","savePath":5},"Cartoons":{"save_path":"/downloads/cartoons"}}""",
            HttpStatusCode.OK,
            "application/json"));

        //Act
        var result = await Client.ListDestinationsAsync(Settings, TestContext.Current.CancellationToken);

        //Assert
        // Never guessed: no path keeps automatic replacement off for the destination.
        Assert.Equal(["Movies | Movies | (unknown)", "Series | Series | (unknown)", "Cartoons | Cartoons | (unknown)"], Describe(result.Value!));
        Assert.DoesNotContain(_harness.QBittorrentHttp.Requests, request => request.Uri.AbsolutePath == DefaultSavePathPath);
    }

    [Fact]
    public async Task ListDestinations_leaves_out_a_category_whose_name_the_app_can_not_keep()
    {
        //Arrange
        var longest = new string('x', 256);
        var tooLong = new string('y', 257);
        _harness.QBittorrentHttp.Handler = Route(categories: () => Categories(
            ("Bell\u0007", "/downloads/bell"),
            ("Line\nBreak", "/downloads/line"),
            ("   ", "/downloads/blank"),
            ("", "/downloads/empty"),
            ("\u200B\u202E", "/downloads/invisible"),
            (tooLong, "/downloads/long"),
            (longest, "/downloads/longest"),
            ("Movies", "/downloads/movies")));

        //Act
        var result = await Client.ListDestinationsAsync(Settings, TestContext.Current.CancellationToken);

        //Assert
        // The rest of the list stays usable; 256 characters is the most a destination's key holds.
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        Assert.Equal([longest, "Movies"], result.Value!.Select(destination => destination.Key));
    }

    [Fact]
    public async Task ListDestinations_cleans_the_name_it_shows_and_keeps_the_key_exactly_as_qbittorrent_has_it()
    {
        //Arrange
        // A right-to-left override would make the name read backwards on screen.
        const string key = "Фільми\u202E  4K ";
        _harness.QBittorrentHttp.Handler = Route(categories: () => Categories((key, "/downloads/movies-4k")));

        //Act
        var result = await Client.ListDestinationsAsync(Settings, TestContext.Current.CancellationToken);

        //Assert
        var destination = Assert.Single(result.Value!);
        Assert.Equal(key, destination.Key);
        Assert.Equal("Фільми 4K", destination.DisplayName);
        Assert.Equal("/downloads/movies-4k", destination.ServicePath);
    }

    [Fact]
    public async Task ListDestinations_keeps_the_first_of_two_categories_with_one_name()
    {
        //Arrange
        _harness.QBittorrentHttp.Handler = Route(categories: () => VendorResponses.Text(
            """{"Movies":{"savePath":"/downloads/first"},"Movies":{"savePath":"/downloads/second"},"Series":{"savePath":"/downloads/series"}}""",
            HttpStatusCode.OK,
            "application/json"));

        //Act
        var result = await Client.ListDestinationsAsync(Settings, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(["Movies | Movies | /downloads/first", "Series | Series | /downloads/series"], Describe(result.Value!));
    }

    [Theory]
    [MemberData(nameof(NotCategories))]
    public async Task ListDestinations_of_a_body_that_is_not_qbittorrents_categories_is_an_unexpected_response(string body)
    {
        //Arrange
        _harness.QBittorrentHttp.Handler = Route(categories: () => VendorResponses.Text(body, HttpStatusCode.OK, "application/json"));

        //Act
        var result = await Client.ListDestinationsAsync(Settings, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.UnexpectedResponse, result.Status);
        Assert.Equal(NotCategoriesError, result.Error);
        Assert.Null(result.Value);
    }

    [Theory]
    [InlineData("v4.3.9")]
    [InlineData("v4.4.0")]
    [InlineData("v4.1.9.1")]
    [InlineData("4.2")]
    public async Task ListDestinations_of_a_qbittorrent_older_than_4_4_1_is_an_unsupported_version_and_reads_no_categories(string version)
    {
        //Arrange
        // 4.4.0 lists each category's folder as save_path (qBittorrent issue #15969), so its answer would
        // read as folders unknown rather than as an old version.
        _harness.QBittorrentHttp.Handler = Route(
            version: () => VendorResponses.Text(version, HttpStatusCode.OK),
            categories: () => VendorResponses.Text("""{"Movies":{"name":"Movies","save_path":"/downloads/movies"}}""", HttpStatusCode.OK, "application/json"));

        //Act
        var result = await Client.ListDestinationsAsync(Settings, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.UnsupportedVersion, result.Status);
        Assert.Equal($"This is qBittorrent {version}; the app works with qBittorrent 4.4.1 or later. Update qBittorrent, then test again.", result.Error);
        Assert.Null(result.Value);
        Assert.DoesNotContain(_harness.QBittorrentHttp.Requests, request => request.Uri.AbsolutePath == CategoriesPath);
    }

    [Theory]
    [InlineData("v4.4.1")]
    [InlineData("v4.6.7")]
    [InlineData("v5.0.5")]
    [InlineData("v5.2.3")]
    [InlineData("v5.3.0")]
    public async Task ListDestinations_of_qbittorrent_4_4_1_or_later_lists_its_categories(string version)
    {
        //Arrange
        _harness.QBittorrentHttp.Handler = Route(version: () => VendorResponses.Text(version, HttpStatusCode.OK));

        //Act
        var result = await Client.ListDestinationsAsync(Settings, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        Assert.Equal(4, result.Value!.Count);
    }

    [Fact]
    public async Task ListDestinations_of_a_version_it_can_not_compare_is_refused_rather_than_guessed()
    {
        //Arrange
        _harness.QBittorrentHttp.Handler = Route(version: () => VendorResponses.Text("v4.99999999999.0", HttpStatusCode.OK));

        //Act
        var result = await Client.ListDestinationsAsync(Settings, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.UnsupportedVersion, result.Status);
        Assert.Equal(
            "qBittorrent reported the version “v4.99999999999.0”, which the app can't compare with 4.4.1, the oldest version it works with.",
            result.Error);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Forbidden")]
    [InlineData("<!DOCTYPE html><html><body>qBittorrent Web UI</body></html>")]
    public async Task ListDestinations_whose_version_answer_is_not_a_version_is_an_unexpected_response(string body)
    {
        //Arrange
        _harness.QBittorrentHttp.Handler = Route(version: () => VendorResponses.Text(body, HttpStatusCode.OK));

        //Act
        var result = await Client.ListDestinationsAsync(Settings, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.UnexpectedResponse, result.Status);
        Assert.Equal("The service at this address answered, but not with a qBittorrent version.", result.Error);
        Assert.DoesNotContain(_harness.QBittorrentHttp.Requests, request => request.Uri.AbsolutePath == CategoriesPath);
    }

    [Theory]
    [InlineData(VersionPath, 404, IntegrationCallStatus.UnexpectedResponse,
        "qBittorrent's Web API wasn't found at this address (404 Not Found). Use the address of qBittorrent's Web UI, such as http://192.168.1.10:8080.")]
    [InlineData(CategoriesPath, 404, IntegrationCallStatus.UnexpectedResponse,
        "qBittorrent's Web API wasn't found at this address (404 Not Found). Use the address of qBittorrent's Web UI, such as http://192.168.1.10:8080.")]
    [InlineData(CategoriesPath, 500, IntegrationCallStatus.ServiceError, "qBittorrent answered 500 Internal Server Error. Its own log says why.")]
    [InlineData(CategoriesPath, 401, IntegrationCallStatus.UnexpectedResponse,
        "qBittorrent answered 401 Unauthorized, which it doesn't send for this request. Check the address.")]
    [InlineData(CategoriesPath, 204, IntegrationCallStatus.UnexpectedResponse,
        "qBittorrent answered 204 No Content, which it doesn't send for this request. Check the address.")]
    [InlineData(DefaultSavePathPath, 503, IntegrationCallStatus.ServiceError, "qBittorrent answered 503 Service Unavailable. Its own log says why.")]
    public async Task ListDestinations_answers_a_failed_read_with_its_status_and_sentence(
        string failingPath, int statusCode, IntegrationCallStatus expectedStatus, string expectedMessage)
    {
        //Arrange
        var route = Route(categories: () => Categories(("Movies", "")));
        _harness.QBittorrentHttp.Handler = (request, body) => request.RequestUri!.AbsolutePath == failingPath
            ? new HttpResponseMessage((HttpStatusCode)statusCode)
            : route(request, body);

        //Act
        var result = await Client.ListDestinationsAsync(Settings, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(expectedStatus, result.Status);
        Assert.Equal(expectedMessage, result.Error);
        Assert.Null(result.Value);
        // Nothing after the read that failed; a failure is never sent again.
        Assert.Equal(failingPath, _harness.QBittorrentHttp.Requests[^1].Uri.AbsolutePath);
        Assert.Single(_harness.QBittorrentHttp.Requests, request => request.Uri.AbsolutePath == failingPath);
    }

    [Theory]
    [MemberData(nameof(TransportFailures))]
    public async Task ListDestinations_maps_each_transport_error_to_its_sentence_without_the_exception_text(
        HttpRequestError error, IntegrationCallStatus expectedStatus, string expectedMessage)
    {
        //Arrange
        var route = Route();
        _harness.QBittorrentHttp.Handler = (request, body) => request.RequestUri!.AbsolutePath == CategoriesPath
            ? throw new HttpRequestException(error, $"Socket failure on {request.RequestUri} with {Password} and {SessionId}")
            : route(request, body);

        //Act
        var result = await Client.ListDestinationsAsync(Settings, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(expectedStatus, result.Status);
        Assert.Equal(expectedMessage, result.Error);
    }

    [Fact]
    public async Task ListDestinations_masks_the_password_and_the_session_in_its_error()
    {
        //Arrange
        // A redirect whose path repeats both: the one qBittorrent sentence that carries remote text.
        _harness.QBittorrentHttp.Handler = Route(categories: () => VendorResponses.Redirect($"{BaseUrl}/session/{SessionId}/{Password}/?next=%2F"));

        //Act
        var result = await Client.ListDestinationsAsync(Settings, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.UnexpectedResponse, result.Status);
        Assert.Equal(
            "The address answered with a redirect to http://qbittorrent.test:8080/session/***/***/. Enter the address the service is really at: the app doesn't follow redirects, so your credentials never go anywhere else.",
            result.Error);
    }

    [Fact]
    public void Capabilities_take_torrent_files_and_magnets_into_categories_with_folders_and_promise_no_job_id()
    {
        //Arrange

        //Act
        var capabilities = Client.Capabilities;

        //Assert
        Assert.Equal([ReleasePayloadKind.TorrentFile, ReleasePayloadKind.Magnet], capabilities.PayloadKinds);
        Assert.True(capabilities.DestinationsHavePaths);
        // torrents/add answers only "Ok." before 5.2: the info hash correlates (MAM-13).
        Assert.False(capabilities.SubmissionReturnsJobId);
    }

    /// <summary>
    /// The sign-in (204 with a 5.2 cookie), the version and categories samples, and a default save path of
    /// <c>/downloads</c> — each replaceable; anything else is a 404.
    /// </summary>
    private static Func<HttpRequestMessage, string?, HttpResponseMessage> Route(
        Func<HttpResponseMessage>? version = null, Func<HttpResponseMessage>? categories = null, Func<HttpResponseMessage>? defaultSavePath = null) =>
        (request, _) => request.RequestUri!.AbsolutePath switch
        {
            SignInPath => VendorResponses.WithSetCookie(
                new HttpResponseMessage(HttpStatusCode.NoContent), $"QBT_SID_8080={SessionId}; HttpOnly; SameSite=Strict; path=/"),
            VersionPath => version?.Invoke() ?? VendorResponses.Sample("QBittorrent/app-version.txt", "text/plain; charset=UTF-8"),
            CategoriesPath => categories?.Invoke() ?? VendorResponses.Sample("QBittorrent/categories.json", "application/json"),
            DefaultSavePathPath => defaultSavePath?.Invoke() ?? VendorResponses.Text("/downloads", HttpStatusCode.OK),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        };

    /// <summary>A torrents/categories answer shaped like 5.2's (the sample's), in the order given.</summary>
    private static HttpResponseMessage Categories(params (string Name, string SavePath)[] categories)
    {
        var root = new JsonObject();
        foreach (var (name, savePath) in categories)
        {
            root[name] = new JsonObject
            {
                ["download_path"] = null,
                ["name"] = name,
                ["savePath"] = savePath,
                ["share_limit_action"] = "Default",
            };
        }

        return VendorResponses.Text(root.ToJsonString(), HttpStatusCode.OK, "application/json");
    }

    private static List<string> Describe(IEnumerable<ClientDestination> destinations) =>
        [.. destinations.Select(destination => $"{destination.Key} | {destination.DisplayName} | {destination.ServicePath ?? "(unknown)"}")];
}
