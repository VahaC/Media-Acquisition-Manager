using System.IO.Pipelines;
using System.Net;
using MediaAcquisitionManager.Core.Abstractions.Downloads;
using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Downloads;
using MediaAcquisitionManager.Infrastructure.Integrations.QBittorrent;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Texts;

namespace MediaAcquisitionManager.Infrastructure.Tests.Integrations;

// MAM-14 — the torrents of one qBittorrent category, for a followed job its stored hashes no longer find (ADR
// 0013's rename of a hybrid sent as a magnet naming its v1 hash): the session and version read first, then one
// GET api/v2/torrents/info?category=… with the key escaped and never trimmed — or, for a key holding a '+', which
// no query carries to qBittorrent, every torrent kept to exactly that category — read exactly as the job lookup
// reads its answer (QBittorrentAdapterJobsTests), and every failure answered the same way.
public sealed class QBittorrentAdapterDestinationJobsTests : IDisposable
{
    private const string BaseUrl = FakeQBittorrentServer.DefaultBaseUrl;
    private const string SignInPath = "/api/v2/auth/login";
    private const string VersionPath = "/api/v2/app/version";
    private const string InfoPath = "/api/v2/torrents/info";

    private const string NotTorrentsError = "The service at this address answered, but not with qBittorrent's torrents.";

    /// <summary>The sample's three-movie pack.</summary>
    private const string PackHash = "1ec461458feffe9f27ae17e30d0a804e2ab0a88f";

    /// <summary>The sample's single-file movie.</summary>
    private const string MovieHash = "4a3c1931c85c9c568c39a8eb947ed382824b1cdd";

    /// <summary>A hybrid torrent's v1 hash, the one its magnet names.</summary>
    private const string HybridV1 = "93df2b76a73841bb9d0bedab5512733af2c37c5d";

    /// <summary>The same torrent's v2 hash, which qBittorrent learns once the metadata arrives.</summary>
    private const string HybridV2 = "0a1b2c3d4e5f60718293a4b5c6d7e8f90a1b2c3d4e5f60718293a4b5c6d7e8f9";

    private static readonly IntegrationConnectionSettings Saved = new(
        Guid.Parse("5b7d9f1a-3c4e-4a6b-8d0f-2e4a6c8e0b1d"), BaseUrl, FakeQBittorrentServer.DefaultUserName, FakeQBittorrentServer.DefaultPassword, null);

    private readonly IntegrationAdapterHarness _harness = new();
    private readonly FakeQBittorrentServer _qbittorrent = new();

    public QBittorrentAdapterDestinationJobsTests() => _qbittorrent.Arm(_harness.QBittorrentHttp);

    /// <summary>Bodies that answer 200 to torrents/info but are not qBittorrent's array of torrents.</summary>
    public static TheoryData<string> NotTorrents =>
    [
        "",
        "<!DOCTYPE html><html><body>qBittorrent Web UI</body></html>",
        "[{",
        "null",
        "{}",
        "[1]",
        "[null]",
        """[{"hash":"1ec461458feffe9f27ae17e30d0a804e2ab0a88f","name":"Taken"},"4a3c1931c85c9c568c39a8eb947ed382824b1cdd"]""",
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
    };

    private IDownloadClient Client => _harness.Adapter<IDownloadClient>(QBittorrentAdapter.TypeId);

    public void Dispose() => _harness.Dispose();

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    [InlineData(null)]
    public async Task ListDestinationJobs_of_a_blank_key_throws_and_sends_nothing(string? destinationKey)
    {
        //Arrange
        // qBittorrent reads category= as "no category" and would list every torrent without one.

        //Act
        var exception = await Record.ExceptionAsync(
            () => Client.ListDestinationJobsAsync(Saved, destinationKey!, TestContext.Current.CancellationToken));

        //Assert
        Assert.IsType<ArgumentException>(exception);
        Assert.Empty(_harness.QBittorrentHttp.Requests);
    }

    [Fact]
    public async Task ListDestinationJobs_signs_in_reads_the_version_and_asks_for_the_category_with_the_session()
    {
        //Arrange
        _qbittorrent.AddTorrent(new TorrentInfoHashes(PackHash, null), "Taken. Trilogy", "Movies");
        _qbittorrent.AddTorrent(new TorrentInfoHashes(MovieHash, null), "Im Westen nichts Neues", "Movies");
        _qbittorrent.AddTorrent(new TorrentInfoHashes("e40455cf36e8520a156762368034a98b91ddad5c", null), "The Grand Tour", "TV Shows");

        //Act
        var result = await Client.ListDestinationJobsAsync(Saved, "Movies", TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        Assert.Equal([PackHash, MovieHash], result.Value!.Select(job => job.ClientJobId));
        Assert.All(result.Value!, job => Assert.Equal("Movies", job.DestinationKey));
        Assert.Collection(
            _harness.QBittorrentHttp.Requests,
            signIn =>
            {
                Assert.Equal(HttpMethod.Post, signIn.Method);
                Assert.Equal(BaseUrl + SignInPath, signIn.Uri.AbsoluteUri);
            },
            version =>
            {
                Assert.Equal(HttpMethod.Get, version.Method);
                Assert.Equal(BaseUrl + VersionPath, version.Uri.AbsoluteUri);
            },
            info =>
            {
                Assert.Equal(HttpMethod.Get, info.Method);
                Assert.Equal($"{BaseUrl}{InfoPath}?category=Movies", info.Uri.AbsoluteUri);
                Assert.Equal(_harness.QBittorrentHttp.Requests[1].HeaderValues("Cookie"), info.HeaderValues("Cookie"));
                Assert.Null(info.Body);
            });
    }

    [Theory]
    [InlineData("TV Shows", "?category=TV%20Shows")]
    [InlineData(" Фільми 4K ", "?category=%20%D0%A4%D1%96%D0%BB%D1%8C%D0%BC%D0%B8%204K%20")]
    [InlineData("Movies&hashes=1ec461458feffe9f27ae17e30d0a804e2ab0a88f", "?category=Movies%26hashes%3D1ec461458feffe9f27ae17e30d0a804e2ab0a88f")]
    [InlineData("Movies/4K|HDR", "?category=Movies%2F4K%7CHDR")]
    // qBittorrent percent-decodes a query value before it reads a '+' as a space: an escaped '%' arrives as itself.
    [InlineData("100% Movies", "?category=100%25%20Movies")]
    public async Task ListDestinationJobs_sends_the_key_escaped_and_exactly_as_given(string destinationKey, string expectedQuery)
    {
        //Arrange
        // Never trimmed: qBittorrent compares a category by its whole name. Escaped whole, so nothing in a key
        // adds a parameter of its own. A key holding a '+' is never sent (the test below).
        _qbittorrent.AddTorrent(new TorrentInfoHashes(PackHash, null), "Taken. Trilogy", destinationKey);
        _qbittorrent.AddTorrent(new TorrentInfoHashes(MovieHash, null), "Im Westen nichts Neues", destinationKey.Trim() + "x");

        //Act
        var result = await Client.ListDestinationJobsAsync(Saved, destinationKey, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(PackHash, Assert.Single(result.Value!).ClientJobId);
        var info = Assert.Single(_harness.QBittorrentHttp.Requests, request => request.Uri.AbsolutePath == InfoPath);
        Assert.Equal(expectedQuery, info.Uri.Query);
    }

    [Theory]
    [InlineData("Movies+TV")]
    [InlineData("+")]
    [InlineData(" C++ Talks ")]
    public async Task ListDestinationJobs_of_a_key_with_a_plus_asks_for_every_torrent_and_keeps_exactly_that_category(string destinationKey)
    {
        //Arrange
        // qBittorrent's RequestParser::parseRequestLine percent-decodes a query value and only then reads '+' as a
        // space, so category=Movies%2BTV would ask for "Movies TV": the fake reads a query the same way.
        _qbittorrent.AddTorrent(new TorrentInfoHashes(PackHash, null), "Taken. Trilogy", destinationKey);
        _qbittorrent.AddTorrent(new TorrentInfoHashes(MovieHash, null), "Im Westen nichts Neues", destinationKey.Replace('+', ' '));
        _qbittorrent.AddTorrent(new TorrentInfoHashes("e40455cf36e8520a156762368034a98b91ddad5c", null), "The Grand Tour", destinationKey.ToUpperInvariant() + "x");
        _qbittorrent.AddTorrent(new TorrentInfoHashes(HybridV1, null), "Monsters, Inc. (2001)");

        //Act
        var result = await Client.ListDestinationJobsAsync(Saved, destinationKey, TestContext.Current.CancellationToken);

        //Assert
        // No category sent, so nothing for qBittorrent to misread; of every torrent, only those of exactly this
        // category, by ordinal comparison — never the one whose name has a space where the key has a '+'.
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        var job = Assert.Single(result.Value!);
        Assert.Equal((PackHash, destinationKey), (job.ClientJobId, job.DestinationKey));
        var info = Assert.Single(_harness.QBittorrentHttp.Requests, request => request.Uri.AbsolutePath == InfoPath);
        Assert.Equal((HttpMethod.Get, $"{BaseUrl}{InfoPath}"), (info.Method, info.Uri.AbsoluteUri));
    }

    [Fact]
    public async Task ListDestinationJobs_finds_a_hybrid_qbittorrent_renamed_with_the_v1_hash_among_its_hashes()
    {
        //Arrange
        // ADR 0013's gap: added by a magnet naming its v1 hash, then renamed by qBittorrent on libtorrent 2 to
        // its v2 hash cut to 40 characters once the metadata arrived — a lookup by the v1 hash no longer finds it.
        var cancellationToken = TestContext.Current.CancellationToken;
        _qbittorrent.AddTorrent(FakeQBittorrentTorrent.Of(new TorrentInfoHashes(HybridV1, null), "Monsters, Inc. (2001)", "Cartoons", "metaDL", 0));
        _qbittorrent.RenameAsHybrid(HybridV1, HybridV2);
        var byHash = await Client.FindJobsAsync(Saved, [HybridV1], cancellationToken);

        //Act
        var result = await Client.ListDestinationJobsAsync(Saved, "Cartoons", cancellationToken);

        //Assert
        Assert.Empty(byHash.Value!);
        var job = Assert.Single(result.Value!);
        Assert.Equal(HybridV2[..40], job.ClientJobId);
        Assert.Equal([HybridV2[..40], HybridV1, HybridV2], job.InfoHashes);
    }

    [Fact]
    public async Task ListDestinationJobs_of_a_category_without_torrents_finds_none()
    {
        //Arrange
        _qbittorrent.AddTorrent(new TorrentInfoHashes(PackHash, null), "Taken. Trilogy", "Movies");

        //Act
        var result = await Client.ListDestinationJobsAsync(Saved, "Documentary", TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        Assert.Empty(result.Value!);
    }

    [Fact]
    public async Task ListDestinationJobs_reads_the_four_torrents_of_the_sample_as_the_lookup_does()
    {
        //Arrange
        _qbittorrent.Intercept = request => request.RequestUri!.AbsolutePath == InfoPath
            ? VendorResponses.Sample("QBittorrent/torrents-info.json", "application/json")
            : null;

        //Act
        var result = await Client.ListDestinationJobsAsync(Saved, "Movies", TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        Assert.Equal(
            [
                "1ec461458feffe9f27ae17e30d0a804e2ab0a88f | Seeding | Movies",
                "4a3c1931c85c9c568c39a8eb947ed382824b1cdd | Seeding | Movies",
                "e40455cf36e8520a156762368034a98b91ddad5c | Seeding | TV Shows",
                "93df2b76a73841bb9d0bedab5512733af2c37c5d | Seeding | Cartoons",
            ],
            result.Value!.Select(job => $"{job.ClientJobId} | {job.State} | {job.DestinationKey}"));
    }

    [Theory]
    [MemberData(nameof(NotTorrents))]
    public async Task ListDestinationJobs_of_a_body_that_is_not_qbittorrents_torrents_is_an_unexpected_response(string body)
    {
        //Arrange
        _qbittorrent.Intercept = request => request.RequestUri!.AbsolutePath == InfoPath
            ? VendorResponses.Text(body, HttpStatusCode.OK, "application/json")
            : null;

        //Act
        var result = await Client.ListDestinationJobsAsync(Saved, "Movies", TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.UnexpectedResponse, result.Status);
        Assert.Equal(NotTorrentsError, TestTexts.En(result.Error));
        Assert.Null(result.Value);
    }

    [Theory]
    [InlineData(404, IntegrationCallStatus.UnexpectedResponse,
        "qBittorrent's Web API wasn't found at this address (404 Not Found). Use the address of qBittorrent's Web UI, such as http://192.168.1.10:8080.")]
    [InlineData(500, IntegrationCallStatus.ServiceError, "qBittorrent answered 500 Internal Server Error. Its own log says why.")]
    [InlineData(401, IntegrationCallStatus.UnexpectedResponse,
        "qBittorrent answered 401 Unauthorized, which it doesn't send for this request. Check the address.")]
    public async Task ListDestinationJobs_answers_a_failed_read_with_its_status_and_sentence(
        int statusCode, IntegrationCallStatus expectedStatus, string expectedError)
    {
        //Arrange
        // A category qBittorrent doesn't have answers 200 [], never 404: a 404 here is the address.
        _qbittorrent.Intercept = request => request.RequestUri!.AbsolutePath == InfoPath ? new HttpResponseMessage((HttpStatusCode)statusCode) : null;

        //Act
        var result = await Client.ListDestinationJobsAsync(Saved, "Movies", TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(expectedStatus, result.Status);
        Assert.Equal(expectedError, TestTexts.En(result.Error));
        Assert.Null(result.Value);
    }

    [Fact]
    public async Task ListDestinationJobs_whose_kept_session_qbittorrent_forgot_renews_it_once_and_reads_on()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        _qbittorrent.AddTorrent(new TorrentInfoHashes(PackHash, null), "Taken. Trilogy", "Movies");
        await Client.ListDestinationJobsAsync(Saved, "Movies", cancellationToken);
        _qbittorrent.ExpireSessions();

        //Act
        var result = await Client.ListDestinationJobsAsync(Saved, "Movies", cancellationToken);

        //Assert
        Assert.Equal(PackHash, Assert.Single(result.Value!).ClientJobId);
        Assert.Equal(
            [
                $"{HttpMethod.Get} {VersionPath}",
                $"{HttpMethod.Post} {SignInPath}",
                $"{HttpMethod.Get} {VersionPath}",
                $"{HttpMethod.Get} {InfoPath}",
            ],
            _harness.QBittorrentHttp.Requests.Skip(3).Select(request => $"{request.Method} {request.Uri.AbsolutePath}"));
        Assert.NotEqual(
            _harness.QBittorrentHttp.Requests[2].HeaderValues("Cookie"),
            _harness.QBittorrentHttp.Requests[^1].HeaderValues("Cookie"));
    }

    [Theory]
    [InlineData("v4.3.9")]
    [InlineData("v4.4.0")]
    public async Task ListDestinationJobs_of_a_qbittorrent_older_than_4_4_1_is_an_unsupported_version_and_reads_no_torrents(string version)
    {
        //Arrange
        _qbittorrent.Version = version;

        //Act
        var result = await Client.ListDestinationJobsAsync(Saved, "Movies", TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.UnsupportedVersion, result.Status);
        Assert.Equal($"This is qBittorrent {version}; the app works with qBittorrent 4.4.1 or later. Update qBittorrent, then test again.", TestTexts.En(result.Error));
        Assert.DoesNotContain(_harness.QBittorrentHttp.Requests, request => request.Uri.AbsolutePath == InfoPath);
    }

    [Theory]
    [MemberData(nameof(TransportFailures))]
    public async Task ListDestinationJobs_maps_each_transport_error_to_its_sentence_without_the_exception_text(
        HttpRequestError error, IntegrationCallStatus expectedStatus, string expectedError)
    {
        //Arrange
        _qbittorrent.Intercept = request => request.RequestUri!.AbsolutePath == InfoPath
            ? throw new HttpRequestException(error, $"Socket failure on {request.RequestUri} with {FakeQBittorrentServer.DefaultPassword}")
            : null;

        //Act
        var result = await Client.ListDestinationJobsAsync(Saved, "Movies", TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(expectedStatus, result.Status);
        Assert.Equal(expectedError, TestTexts.En(result.Error));
    }

    [Fact]
    public async Task ListDestinationJobs_that_outlives_the_timeout_is_timed_out()
    {
        //Arrange
        using var harness = new IntegrationAdapterHarness(timeout: TimeSpan.FromMilliseconds(100));
        var qbittorrent = new FakeQBittorrentServer
        {
            Intercept = request => request.RequestUri!.AbsolutePath == InfoPath ? StalledAnswer() : null,
        };
        qbittorrent.Arm(harness.QBittorrentHttp);

        //Act
        var result = await harness.Adapter<IDownloadClient>(QBittorrentAdapter.TypeId)
            .ListDestinationJobsAsync(Saved, "Movies", TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.TimedOut, result.Status);
        Assert.Equal("qBittorrent at http://qbittorrent.test:8080 didn't answer within 0.1 seconds.", TestTexts.En(result.Error));
    }

    [Fact]
    public async Task ListDestinationJobs_cancelled_by_the_caller_throws()
    {
        //Arrange
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        _qbittorrent.Intercept = request =>
        {
            if (request.RequestUri!.AbsolutePath != InfoPath)
                return null;
            cancellation.Cancel();
            return StalledAnswer();
        };

        //Act
        var exception = await Record.ExceptionAsync(() => Client.ListDestinationJobsAsync(Saved, "Movies", cancellation.Token));

        //Assert
        Assert.IsAssignableFrom<OperationCanceledException>(exception);
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────────

    // A 200 whose body never finishes arriving: the pipe is never written to or completed.
    private static HttpResponseMessage StalledAnswer() =>
        new(HttpStatusCode.OK) { Content = new StreamContent(new Pipe().Reader.AsStream()) };
}
