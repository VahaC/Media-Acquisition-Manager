using System.IO.Pipelines;
using System.Net;
using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Abstractions.Library;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Infrastructure.Integrations.Jellyfin;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;

namespace MediaAcquisitionManager.Infrastructure.Tests.Integrations;

// MAM-34 follow-up (Vasyl's decision of 2026-10-06) — the lookup of an item's missing metadata the app asks Jellyfin for
// after writing its ids (IMediaLibraryIdentifierWriter.RequestMetadataLookupAsync): one POST /Items/{id}/Refresh with
// exactly the query of Jellyfin's own "Search for missing metadata" — never "replace all" —, no body, the key in the
// header; queued is 204; every refusal a status and a sentence, as the item's write answers; sent once, never again; a
// caller's mistake sent nowhere. A series' item is looked up the same way (Vasyl's final decision of 2026-10-06). The
// 204s, the 404 and the 401 are the answers Vasyl's live check recorded on 2026-10-06 (VendorSamples/README.md).
public sealed class JellyfinAdapterMetadataLookupTests : IDisposable
{
    private const string BaseUrl = "http://jellyfin.test:8096";
    private const string ApiKey = "4f1d0b9c2e7a4c3b8d6e5f7a9b0c1d2e";

    private const string LookupQuery =
        "?metadataRefreshMode=FullRefresh&imageRefreshMode=FullRefresh&replaceAllMetadata=false&replaceAllImages=false";

    private const string KeyRefusedError =
        "Jellyfin refused the API key. Create one in Jellyfin under Dashboard → API Keys and enter it here.";

    private const string ItemGoneError = "Jellyfin no longer has this item; sync the library and link it again.";

    private static readonly IntegrationConnectionSettings Settings = new(null, BaseUrl, null, null, ApiKey);

    private readonly IntegrationAdapterHarness _harness = new();
    private readonly FakeJellyfinServer _server = new(BaseUrl) { ApiKey = ApiKey };

    public JellyfinAdapterMetadataLookupTests()
    {
        _server.Movies.Add(JellyfinSamples.Item());
        _server.Series.Add(JellyfinSamples.SeriesItem());
        _server.Arm(_harness.JellyfinHttp);
    }

    private IMediaLibraryIdentifierWriter Writer => _harness.Adapter<IMediaLibraryIdentifierWriter>(JellyfinAdapter.TypeId);

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task RequestMetadataLookup_posts_search_for_missing_metadata_once_with_no_body_and_jellyfin_queues_it()
    {
        //Arrange
        var itemId = JellyfinSamples.LandingStriplingId;

        //Act
        var result = await Writer.RequestMetadataLookupAsync(Settings, itemId, MediaWorkKind.Movie, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal((IntegrationCallStatus.Succeeded, (string?)null), (result.Status, result.Error));
        Assert.Equal(new LibraryMetadataLookup(itemId), result.Value);
        var request = Assert.Single(_harness.JellyfinHttp.Requests);
        // What Jellyfin's own dialog sends for "Search for missing metadata": what is missing, nothing replaced.
        Assert.Equal($"POST /Items/{itemId}/Refresh{LookupQuery}", $"{request.Method} {request.Uri.PathAndQuery}");
        Assert.True(string.IsNullOrEmpty(request.Body));
        Assert.Equal([$"MediaBrowser Token=\"{ApiKey}\""], request.HeaderValues("Authorization"));
        Assert.DoesNotContain(ApiKey, request.Uri.OriginalString, StringComparison.Ordinal);
        Assert.Equal([itemId], _server.Lookups);
    }

    [Fact]
    public async Task RequestMetadataLookup_of_a_series_posts_the_same_search_once_and_jellyfin_queues_it()
    {
        //Arrange
        // The Penguins series item the live check looked up right after its write (series-refresh-response.txt).
        var itemId = JellyfinSamples.PenguinsId;

        //Act
        var result = await Writer.RequestMetadataLookupAsync(Settings, itemId, MediaWorkKind.Series, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal((IntegrationCallStatus.Succeeded, (string?)null), (result.Status, result.Error));
        Assert.Equal(new LibraryMetadataLookup(itemId), result.Value);
        var request = Assert.Single(_harness.JellyfinHttp.Requests);
        Assert.Equal($"POST /Items/{itemId}/Refresh{LookupQuery}", $"{request.Method} {request.Uri.PathAndQuery}");
        Assert.True(string.IsNullOrEmpty(request.Body));
        Assert.Equal([itemId], _server.Lookups);
    }

    [Fact]
    public async Task RequestMetadataLookup_keeps_a_base_path_of_the_address()
    {
        //Arrange
        var settings = Settings with { BaseUrl = "http://jellyfin.test/jellyfin" };
        var server = new FakeJellyfinServer(settings.BaseUrl);
        server.Movies.Add(JellyfinSamples.Item());
        server.Arm(_harness.JellyfinHttp);

        //Act
        var result = await Writer.RequestMetadataLookupAsync(settings, JellyfinSamples.LandingStriplingId, MediaWorkKind.Movie, TestContext.Current.CancellationToken);

        //Assert
        Assert.True(result.IsSuccess);
        Assert.Equal($"/jellyfin/Items/{JellyfinSamples.LandingStriplingId}/Refresh", Assert.Single(_harness.JellyfinHttp.Requests).Uri.AbsolutePath);
    }

    [Theory]
    [InlineData(JellyfinSamples.ItemRefreshUnknownItemResponseFile, IntegrationCallStatus.NotFound, ItemGoneError)]
    [InlineData(JellyfinSamples.ItemRefreshWrongKeyResponseFile, IntegrationCallStatus.AuthenticationFailed, KeyRefusedError)]
    public async Task RequestMetadataLookup_answers_jellyfins_refusals_with_their_status_and_sentence(string sample, IntegrationCallStatus status, string error)
    {
        //Arrange
        _server.Intercept = _ => VendorResponses.Recorded(sample);

        //Act
        var result = await Writer.RequestMetadataLookupAsync(Settings, JellyfinSamples.LandingStriplingId, MediaWorkKind.Movie, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal((status, (LibraryMetadataLookup?)null, error), (result.Status, result.Value, result.Error));
        Assert.Single(_harness.JellyfinHttp.Requests);
    }

    [Fact]
    public async Task RequestMetadataLookup_of_an_item_jellyfin_no_longer_has_is_not_found()
    {
        //Arrange
        _server.Movies.Clear();

        //Act
        var result = await Writer.RequestMetadataLookupAsync(Settings, JellyfinSamples.LandingStriplingId, MediaWorkKind.Movie, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal((IntegrationCallStatus.NotFound, ItemGoneError), (result.Status, result.Error));
        Assert.Empty(_server.Lookups);
    }

    [Theory]
    [InlineData(400, IntegrationCallStatus.Refused, "Jellyfin refused the request to look the item up; nothing was changed.")]
    [InlineData(403, IntegrationCallStatus.Refused, "Jellyfin refused to look the item up. Check that the API key belongs to an administrator.")]
    [InlineData(500, IntegrationCallStatus.ServiceError, "Jellyfin answered 500 Internal Server Error. Its own log says why.")]
    [InlineData(409, IntegrationCallStatus.UnexpectedResponse, "Jellyfin answered 409 Conflict, which it doesn't send for this request. Check the address.")]
    public async Task RequestMetadataLookup_answers_each_other_refusal_with_its_status_and_sentence_and_sends_it_once(
        int answer, IntegrationCallStatus status, string error)
    {
        //Arrange
        _server.LookupRefusal = (HttpStatusCode)answer;

        //Act
        var result = await Writer.RequestMetadataLookupAsync(Settings, JellyfinSamples.LandingStriplingId, MediaWorkKind.Movie, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal((status, error), (result.Status, result.Error));
        // Never again, whatever the answer (house rule 8): the owner's Retry asks once more.
        Assert.Single(_harness.JellyfinHttp.Requests);
    }

    [Fact]
    public async Task RequestMetadataLookup_whose_connection_fails_is_unreachable_with_the_shared_sentence_and_is_not_sent_again()
    {
        //Arrange
        _server.Intercept = request => throw new HttpRequestException(HttpRequestError.ConnectionError, $"Socket failure on {request.RequestUri} with the key {ApiKey}");

        //Act
        var result = await Writer.RequestMetadataLookupAsync(Settings, JellyfinSamples.LandingStriplingId, MediaWorkKind.Movie, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Unreachable, result.Status);
        Assert.Equal(
            $"Couldn't reach Jellyfin at {BaseUrl}: nothing accepted the connection. Check the address and port, and that Jellyfin is running.",
            result.Error);
        Assert.Single(_harness.JellyfinHttp.Requests);
    }

    [Fact]
    public async Task RequestMetadataLookup_cancelled_by_the_caller_while_it_is_answered_throws_and_sends_it_once()
    {
        //Arrange
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        _server.Intercept = _ =>
        {
            cancellation.Cancel();
            // A 200 whose body never finishes arriving.
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new Pipe().Reader.AsStream()) };
        };

        //Act
        var exception = await Record.ExceptionAsync(() =>
            Writer.RequestMetadataLookupAsync(Settings, JellyfinSamples.LandingStriplingId, MediaWorkKind.Movie, cancellation.Token));

        //Assert
        // Only the caller's own cancellation propagates, never as "timed out"; the link writer passes none (ADR 0013).
        Assert.IsAssignableFrom<OperationCanceledException>(exception);
        Assert.Single(_harness.JellyfinHttp.Requests);
    }

    [Theory]
    [InlineData("  ", MediaWorkKind.Movie)]
    [InlineData("../Users", MediaWorkKind.Movie)]
    [InlineData(JellyfinSamples.YellowstoneS01E01Id, MediaWorkKind.Episode)]
    public async Task RequestMetadataLookup_refuses_a_callers_mistake_without_sending_anything(string itemId, MediaWorkKind kind)
    {
        //Arrange

        //Act
        var exception = await Record.ExceptionAsync(() => Writer.RequestMetadataLookupAsync(Settings, itemId, kind, TestContext.Current.CancellationToken));

        //Assert
        // An item id Jellyfin never gives goes into no path; an episode item's ids are never written, so it is never
        // asked.
        Assert.IsType<ArgumentException>(exception);
        Assert.Empty(_harness.JellyfinHttp.Requests);
    }
}
