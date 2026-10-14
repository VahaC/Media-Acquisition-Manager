using System.Net;
using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Abstractions.Library;
using MediaAcquisitionManager.Infrastructure.Integrations.Jellyfin;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;

namespace MediaAcquisitionManager.Infrastructure.Tests.Integrations;

// MAM-8 — Jellyfin's libraries (virtual folders) as the media library contract reads them, against the
// real answer of the reference installation (VendorSamples/Jellyfin/virtual-folders.json): the id, the
// name and the root folders of each, which a sync matches file paths against.
public sealed class JellyfinAdapterLocationsTests : IDisposable
{
    private const string BaseUrl = "http://jellyfin.test:8096";
    private const string ApiKey = "4f1d0b9c2e7a4c3b8d6e5f7a9b0c1d2e";

    private const string NotJellyfinError =
        "The service at this address answered, but not the way Jellyfin does. Check the address.";

    private static readonly IntegrationConnectionSettings Settings = new(null, BaseUrl, null, null, ApiKey);

    private readonly IntegrationAdapterHarness _harness = new();

    private IMediaLibraryProvider Library => _harness.Adapter<IMediaLibraryProvider>(JellyfinAdapter.TypeId);

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task ListLocations_reads_the_five_libraries_of_the_sample()
    {
        //Arrange
        _harness.JellyfinHttp.Handler = (_, _) => VendorResponses.Sample("Jellyfin/virtual-folders.json", "application/json; charset=utf-8");

        //Act
        var result = await Library.ListLocationsAsync(Settings, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        Assert.Null(result.Error);
        // The box-set library lives in Jellyfin's config, not in the media folder: listed all the same,
        // it simply holds no file.
        Assert.Equal(
            [
                "78ee018b5034640ba414833f0fe9d6b3 Фільми: /media/movies",
                "2e7d31f7c627f72ad0912eff1c00353d Мульфільми: /media/cartoons",
                "dea595f0747f8862151db0192e565150 Документалки: /media/documentary",
                "f28915b8cd74d585b0c181975ec68c3a Колекції: /config/data/collections",
                "f97af1337db58125aaa9a3f3695ab163 Серіали: /media/tv-shows",
            ],
            Describe(result.Value!));
    }

    [Fact]
    public async Task ListLocations_says_the_box_set_library_of_the_sample_holds_no_media_files_and_the_others_do()
    {
        //Arrange
        _harness.JellyfinHttp.Handler = (_, _) => VendorResponses.Sample("Jellyfin/virtual-folders.json", "application/json; charset=utf-8");

        //Act
        var result = await Library.ListLocationsAsync(Settings, TestContext.Current.CancellationToken);

        //Assert
        // MAM-30, Vasyl's decision 9 — "Колекції" is CollectionType boxsets: Jellyfin's own records, never a destination.
        Assert.Equal(
            [
                ("Фільми", true),
                ("Мульфільми", true),
                ("Документалки", true),
                ("Колекції", false),
                ("Серіали", true),
            ],
            result.Value!.Select(location => (location.Name, location.HoldsMediaFiles)));
    }

    [Theory]
    [InlineData("\"boxsets\"", false)]
    [InlineData("\"BoxSets\"", false)]
    [InlineData("\"playlists\"", false)]
    [InlineData("\"movies\"", true)]
    [InlineData("\"tvshows\"", true)]
    [InlineData("\"music\"", true)]
    [InlineData("\"homevideos\"", true)]
    [InlineData("\"a type Jellyfin adds later\"", true)]
    [InlineData("null", true)]
    [InlineData("5", true)]
    public async Task ListLocations_reads_whether_a_library_holds_media_files_from_its_collection_type(string collectionType, bool expected)
    {
        //Arrange
        _harness.JellyfinHttp.Handler = (_, _) => VendorResponses.Text(
            $$"""[{"ItemId":"a","Name":"Library","CollectionType":{{collectionType}},"Locations":["/media/a"]}]""",
            HttpStatusCode.OK,
            "application/json");

        //Act
        var result = await Library.ListLocationsAsync(Settings, TestContext.Current.CancellationToken);

        //Assert
        // Its folders are read either way: the sync still matches files against every library.
        var location = Assert.Single(result.Value!);
        Assert.Equal(expected, location.HoldsMediaFiles);
        Assert.Equal(["/media/a"], location.ServicePaths);
    }

    [Fact]
    public async Task ListLocations_of_a_library_without_a_collection_type_holds_media_files()
    {
        //Arrange
        // A mixed library: Jellyfin leaves the type out.
        _harness.JellyfinHttp.Handler = (_, _) => VendorResponses.Text(
            """[{"ItemId":"a","Name":"Mixed","Locations":["/media/mixed"]}]""",
            HttpStatusCode.OK,
            "application/json");

        //Act
        var result = await Library.ListLocationsAsync(Settings, TestContext.Current.CancellationToken);

        //Assert
        Assert.True(Assert.Single(result.Value!).HoldsMediaFiles);
    }

    [Fact]
    public async Task ListLocations_gets_the_virtual_folders_with_the_key_in_the_media_browser_header()
    {
        //Arrange
        new FakeJellyfinServer(BaseUrl).Arm(_harness.JellyfinHttp);

        //Act
        await Library.ListLocationsAsync(Settings, TestContext.Current.CancellationToken);

        //Assert
        var request = Assert.Single(_harness.JellyfinHttp.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("http://jellyfin.test:8096/Library/VirtualFolders", request.Uri.AbsoluteUri);
        Assert.Equal([$"MediaBrowser Token=\"{ApiKey}\""], request.HeaderValues("Authorization"));
        Assert.Equal(["application/json"], request.HeaderValues("Accept"));
        Assert.DoesNotContain(ApiKey, request.Uri.OriginalString, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ListLocations_keeps_a_base_path_of_the_address()
    {
        //Arrange
        var settings = Settings with { BaseUrl = "http://jellyfin.test/jellyfin" };
        new FakeJellyfinServer(settings.BaseUrl).Arm(_harness.JellyfinHttp);

        //Act
        var result = await Library.ListLocationsAsync(settings, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        Assert.Equal(5, result.Value!.Count);
        Assert.Equal("http://jellyfin.test/jellyfin/Library/VirtualFolders", Assert.Single(_harness.JellyfinHttp.Requests).Uri.AbsoluteUri);
    }

    [Theory]
    [InlineData(401, IntegrationCallStatus.AuthenticationFailed,
        "Jellyfin refused the API key. Create one in Jellyfin under Dashboard → API Keys and enter it here.")]
    [InlineData(403, IntegrationCallStatus.AuthenticationFailed,
        "Jellyfin accepted the key but won't let it read the library (403 Forbidden). Use a key from Dashboard → API Keys, not a user's access token.")]
    [InlineData(404, IntegrationCallStatus.UnexpectedResponse,
        "Jellyfin wasn't found at this address (404 Not Found for /Library/VirtualFolders). If Jellyfin runs under a base path, include it, for example http://192.168.1.10:8096/jellyfin.")]
    [InlineData(500, IntegrationCallStatus.ServiceError, "Jellyfin answered 500 Internal Server Error. Its own log says why.")]
    [InlineData(204, IntegrationCallStatus.UnexpectedResponse,
        "Jellyfin answered 204 No Content, which it doesn't send for this request. Check the address.")]
    public async Task ListLocations_answers_each_failure_with_its_status_and_sentence(
        int statusCode, IntegrationCallStatus expectedStatus, string expectedMessage)
    {
        //Arrange
        _harness.JellyfinHttp.Handler = (_, _) => new HttpResponseMessage((HttpStatusCode)statusCode);

        //Act
        var result = await Library.ListLocationsAsync(Settings, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(expectedStatus, result.Status);
        Assert.Equal(expectedMessage, result.Error);
        Assert.Null(result.Value);
    }

    [Fact]
    public async Task ListLocations_reports_a_redirect_with_the_key_it_carries_masked()
    {
        //Arrange
        _harness.JellyfinHttp.Handler = (_, _) => VendorResponses.Redirect($"/{ApiKey}/web/index.html?api_key={ApiKey}", HttpStatusCode.MovedPermanently);

        //Act
        var result = await Library.ListLocationsAsync(Settings, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.UnexpectedResponse, result.Status);
        Assert.Equal(
            "The address answered with a redirect to http://jellyfin.test:8096/***/web/index.html. Enter the address the service is really at: the app doesn't follow redirects, so your credentials never go anywhere else.",
            result.Error);
        Assert.Single(_harness.JellyfinHttp.Requests);
    }

    [Theory]
    [MemberData(nameof(JellyfinAdapterTests.TransportFailures), MemberType = typeof(JellyfinAdapterTests))]
    public async Task ListLocations_maps_each_transport_error_to_its_sentence_without_the_exception_text(
        HttpRequestError error, IntegrationCallStatus expectedStatus, string expectedMessage)
    {
        //Arrange
        _harness.JellyfinHttp.Handler = (request, _) =>
            throw new HttpRequestException(error, $"Socket failure on {request.RequestUri} with the key {ApiKey}");

        //Act
        var result = await Library.ListLocationsAsync(Settings, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(expectedStatus, result.Status);
        Assert.Equal(expectedMessage, result.Error);
    }

    [Theory]
    [InlineData("")]
    [InlineData("<!DOCTYPE html><html><body>Welcome to nginx!</body></html>")]
    [InlineData("[{")]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("\"Фільми\"")]
    [InlineData("""{"Items":[]}""")]
    public async Task ListLocations_of_a_body_that_is_not_a_list_of_libraries_is_an_unexpected_response(string body)
    {
        //Arrange
        _harness.JellyfinHttp.Handler = (_, _) => VendorResponses.Text(body, HttpStatusCode.OK, "application/json");

        //Act
        var result = await Library.ListLocationsAsync(Settings, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.UnexpectedResponse, result.Status);
        Assert.Equal(NotJellyfinError, result.Error);
    }

    [Fact]
    public async Task ListLocations_skips_an_entry_without_a_usable_id()
    {
        //Arrange
        _harness.JellyfinHttp.Handler = (_, _) => VendorResponses.Text(
            """
            [
              1, null, "Фільми",
              {"Name":"No id","Locations":["/media/a"]},
              {"ItemId":"","Name":"Blank id","Locations":["/media/b"]},
              {"ItemId":12,"Name":"Number id","Locations":["/media/c"]},
              {"ItemId":"a\u0007b","Name":"Control character","Locations":["/media/d"]},
              {"ItemId":"78ee018b5034640ba414833f0fe9d6b3","Name":"Фільми","Locations":["/media/movies"]}
            ]
            """,
            HttpStatusCode.OK,
            "application/json");

        //Act
        var result = await Library.ListLocationsAsync(Settings, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        Assert.Equal(["78ee018b5034640ba414833f0fe9d6b3 Фільми: /media/movies"], Describe(result.Value!));
    }

    [Fact]
    public async Task ListLocations_keeps_the_first_of_two_libraries_with_one_id()
    {
        //Arrange
        _harness.JellyfinHttp.Handler = (_, _) => VendorResponses.Text(
            """[{"ItemId":"a","Name":"Фільми","Locations":["/media/movies"]},{"ItemId":"a","Name":"Копія","Locations":["/media/copy"]}]""",
            HttpStatusCode.OK,
            "application/json");

        //Act
        var result = await Library.ListLocationsAsync(Settings, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(["a Фільми: /media/movies"], Describe(result.Value!));
    }

    [Fact]
    public async Task ListLocations_names_a_library_without_a_name_by_its_id()
    {
        //Arrange
        _harness.JellyfinHttp.Handler = (_, _) => VendorResponses.Text(
            """[{"ItemId":"a"},{"ItemId":"b","Name":"  "},{"ItemId":"c","Name":null},{"ItemId":"d","Name":5}]""",
            HttpStatusCode.OK,
            "application/json");

        //Act
        var result = await Library.ListLocationsAsync(Settings, TestContext.Current.CancellationToken);

        //Assert
        // Never a blank name in the Library page's filter.
        Assert.Equal(["a a: ", "b b: ", "c c: ", "d d: "], Describe(result.Value!));
    }

    [Fact]
    public async Task ListLocations_cleans_and_caps_a_name_and_masks_the_key_in_it()
    {
        //Arrange
        var tail = new string('x', 300);
        _harness.JellyfinHttp.Handler = (_, _) => VendorResponses.Text(
            $$"""[{"ItemId":"a","Name":"Фільми‮\u0007\r\n {{ApiKey}} {{tail}}","Locations":[]}]""",
            HttpStatusCode.OK,
            "application/json");

        //Act
        var result = await Library.ListLocationsAsync(Settings, TestContext.Current.CancellationToken);

        //Assert
        var location = Assert.Single(result.Value!);
        Assert.Equal(("Фільми *** " + tail)[..199] + "…", location.Name);
        Assert.Equal(200, location.Name.Length);
    }

    [Fact]
    public async Task ListLocations_keeps_the_root_folders_given_as_text_exactly_as_sent()
    {
        //Arrange
        // A root is compared with file paths, so it is never trimmed or cleaned.
        _harness.JellyfinHttp.Handler = (_, _) => VendorResponses.Text(
            """
            [
              {"ItemId":"a","Name":"Фільми","Locations":["", "  ", 5, null, "/media/movies", " /media/movies 2 "]},
              {"ItemId":"b","Name":"No roots"},
              {"ItemId":"c","Name":"Roots as text","Locations":"/media/c"}
            ]
            """,
            HttpStatusCode.OK,
            "application/json");

        //Act
        var result = await Library.ListLocationsAsync(Settings, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(["a Фільми: /media/movies|| /media/movies 2 ", "b No roots: ", "c Roots as text: "], Describe(result.Value!));
    }

    // Each library as "id name: root|root".
    private static List<string> Describe(IReadOnlyList<LibraryLocation> locations) =>
        [.. locations.Select(location => $"{location.SourceLocationId} {location.Name}: {string.Join("||", location.ServicePaths)}")];
}
