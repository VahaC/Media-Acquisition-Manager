using System.Net;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;

namespace MediaAcquisitionManager.Infrastructure.Tests.Integrations;

// MAM-30 — the fake Jackett feed answers as Jackett v0.24.2830 did (VendorSamples/Torznab/jackett-*, MAM-29): what the
// Api tests rely on it for is proved here against the recordings, so a test that passes through it passes through
// Jackett's answers, never the fake's own guesses.
public sealed class FakeTorznabServerJackettTests : IDisposable
{
    private const string FeedAddress = "http://jackett.test:9117/api/v2.0/indexers/toloka/results/torznab/";
    private const string Endpoint = FeedAddress + "api";
    private const string ApiKey = "0f9e8d7c6b5a49382716a5b4c3d2e1f0";

    private readonly RecordingHttpHandler _handler = new();
    private readonly HttpClient _client;

    public FakeTorznabServerJackettTests()
    {
        _client = new HttpClient(_handler);
        FakeTorznabServer.Jackett(FeedAddress, ApiKey).Arm(_handler);
    }

    public void Dispose()
    {
        _client.Dispose();
        _handler.Dispose();
    }

    [Fact]
    public async Task Caps_with_the_key_answers_jacketts_caps_sample()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        using var response = await _client.GetAsync($"{Endpoint}?t=caps&apikey={ApiKey}", cancellationToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(TorznabSamples.JackettCaps(), await response.Content.ReadAsStringAsync(cancellationToken));
    }

    [Theory]
    [InlineData("?t=caps&apikey=wrong")]
    [InlineData("?t=search&apikey=wrong&q=Interstellar")]
    [InlineData("?t=movie&q=Interstellar")]
    public async Task Caps_or_a_search_with_a_wrong_key_answers_the_recorded_200_with_error_100(string query)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        using var recorded = VendorResponses.Recorded(TorznabSamples.JackettWrongKeyResponseFile);

        //Act
        using var response = await _client.GetAsync(Endpoint + query, cancellationToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/xml", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal(await recorded.Content.ReadAsStringAsync(cancellationToken), await response.Content.ReadAsStringAsync(cancellationToken));
        Assert.Contains("""<error code="100" description="Invalid API Key" />""", await response.Content.ReadAsStringAsync(cancellationToken), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Search_answers_jacketts_movie_sample_with_every_link_on_this_feed_carrying_the_key_and_the_token()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        using var response = await _client.GetAsync($"{Endpoint}?t=movie&apikey={ApiKey}&q=Interstellar", cancellationToken);

        //Assert
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        // No LAN host of the recording is left, nor the sample's REDACTED.
        Assert.DoesNotContain("192.168.1.195", body, StringComparison.Ordinal);
        Assert.DoesNotContain("REDACTED", body, StringComparison.Ordinal);
        Assert.Contains(
            $"<link>http://jackett.test:9117/dl/toloka/?jackett_apikey={ApiKey}&amp;path=planted-jackett-path-0001&amp;file=Interstellar+(2014)+BDRemux+1080p",
            body,
            StringComparison.Ordinal);
        Assert.Contains("""<atom:link href="http://jackett.test:9117/" rel="self" type="application/rss+xml" />""", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Download_with_the_key_and_the_token_hands_the_torrent_over_with_the_recorded_headers()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        using var response = await _client.GetAsync(TorznabSamples.JackettLocator(FeedAddress).Replace("%7Bapi-key%7D", ApiKey, StringComparison.Ordinal), cancellationToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/x-bittorrent", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal("attachment", response.Content.Headers.ContentDisposition!.DispositionType);
        Assert.Equal(TorznabSamples.TorrentFile(), await response.Content.ReadAsByteArrayAsync(cancellationToken));
        // Not the recorded 350,636 bytes: the body is the test's own .torrent.
        Assert.Equal(TorznabSamples.TorrentFile().Length, response.Content.Headers.ContentLength);
    }

    [Theory]
    [InlineData("?jackett_apikey=wrong&path=planted-jackett-path-0001&file=x", HttpStatusCode.Unauthorized)]
    [InlineData("?path=planted-jackett-path-0001&file=x", HttpStatusCode.Unauthorized)]
    [InlineData("?jackett_apikey=" + ApiKey + "&path=mangled&file=x", HttpStatusCode.NotFound)]
    public async Task Download_with_a_wrong_key_or_token_answers_the_recorded_bare_status(string query, HttpStatusCode expected)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        using var response = await _client.GetAsync("http://jackett.test:9117/dl/toloka/" + query, cancellationToken);

        //Assert
        Assert.Equal(expected, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync(cancellationToken));
    }

    [Theory]
    [InlineData("http://jackett.test:9117/dl/other/?jackett_apikey=x")]
    [InlineData("http://jackett.test:9117/api/v2.0/indexers/other/results/torznab/api?t=caps")]
    [InlineData("http://jackett.test:9118/dl/toloka/")]
    public async Task A_request_to_neither_its_feed_nor_its_downloads_answers_503(string address)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        using var response = await _client.GetAsync(address, cancellationToken);

        //Assert
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }
}
