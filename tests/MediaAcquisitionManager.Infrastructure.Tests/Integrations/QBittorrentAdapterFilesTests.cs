using System.IO.Pipelines;
using System.Net;
using System.Text.Json.Nodes;
using MediaAcquisitionManager.Core.Abstractions.Downloads;
using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Downloads;
using MediaAcquisitionManager.Infrastructure.Integrations.QBittorrent;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Texts;

namespace MediaAcquisitionManager.Infrastructure.Tests.Integrations;

// MAM-14 — the files of one torrent: the session and version read first, then one
// GET api/v2/torrents/files?hash=<TorrentID>; the answer read as an allowlist against the real one of the
// reference installation (VendorSamples/QBittorrent/torrent-files-multi.json, 5.2.3), each path kept exactly as
// qBittorrent names it — double spaces and all — sizes beyond 32 bits, priority 0 read as unwanted; a 404 after
// the version has answered is the torrent gone (NotFound), and every other failure answered like the job lookup
// answers it.
public sealed class QBittorrentAdapterFilesTests : IDisposable
{
    private const string BaseUrl = FakeQBittorrentServer.DefaultBaseUrl;
    private const string SignInPath = "/api/v2/auth/login";
    private const string VersionPath = "/api/v2/app/version";
    private const string FilesPath = "/api/v2/torrents/files";

    private const string NotFilesError = "The service at this address answered, but not with qBittorrent's files.";
    private const string TorrentGoneError = "qBittorrent doesn't have this torrent any more.";

    /// <summary>The sample's three-movie pack.</summary>
    private const string PackHash = "1ec461458feffe9f27ae17e30d0a804e2ab0a88f";

    /// <summary>The pack's root folder, as every one of its files' names starts.</summary>
    private const string PackFolder = "Taken. Trilogy [Extended] (2008-2014) BDRemux 1080p [Hurtom]";

    /// <summary>A v2 info hash: 64 hexadecimal characters, named by qBittorrent by its first 40.</summary>
    private const string V2Hash = "e7f2a1c5b3d94e8f0a6c2b1d4e3f5a7c9b8d0e1f2a3b4c5d6e7f8a9b0c1d2e3f";

    private const string V2TorrentId = "e7f2a1c5b3d94e8f0a6c2b1d4e3f5a7c9b8d0e1f";

    private static readonly IntegrationConnectionSettings Saved = new(
        Guid.Parse("8d2f4a6c-1e3b-4c5d-9a7f-0b2e4d6f8a1c"), BaseUrl, FakeQBittorrentServer.DefaultUserName, FakeQBittorrentServer.DefaultPassword, null);

    private readonly IntegrationAdapterHarness _harness = new();
    private readonly FakeQBittorrentServer _qbittorrent = new();

    public QBittorrentAdapterFilesTests() => _qbittorrent.Arm(_harness.QBittorrentHttp);

    /// <summary>Bodies that answer 200 to torrents/files but are not qBittorrent's array of files, each with a
    /// usable name.</summary>
    public static TheoryData<string> NotFiles =>
    [
        "",
        "<!DOCTYPE html><html><body>qBittorrent Web UI</body></html>",
        "[{",
        "null",
        "{}",
        """{"name":"Taken (2008).mkv","size":1}""",
        "\"files\"",
        "[1]",
        "[null]",
        "[[]]",
        """[{"name":"Taken (2008).mkv","size":1},"Taken 2 (2012).mkv"]""",
        """[{"name":"Taken (2008).mkv","size":1},{"size":2}]""",
        """[{"name":"Taken (2008).mkv","size":1},{"name":null,"size":2}]""",
        """[{"name":"Taken (2008).mkv","size":1},{"name":42,"size":2}]""",
        """[{"name":"Taken (2008).mkv","size":1},{"name":"","size":2}]""",
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

    private IDownloadClient Client => _harness.Adapter<IDownloadClient>(QBittorrentAdapter.TypeId);

    public void Dispose() => _harness.Dispose();

    [Theory]
    [InlineData("")]
    [InlineData("1ec461458feffe9f27ae17e30d0a804e2ab0a88")]
    [InlineData("1ec461458feffe9f27ae17e30d0a804e2ab0a88f0")]
    [InlineData("e7f2a1c5b3d94e8f0a6c2b1d4e3f5a7c9b8d0e1f2a3b4c5d6e7f8a9b0c1d2e3")]
    [InlineData("1ec461458feffe9f27ae17e30d0a804e2ab0a88g")]
    [InlineData(" 1ec461458feffe9f27ae17e30d0a804e2ab0a88")]
    [InlineData("١ec461458feffe9f27ae17e30d0a804e2ab0a88f")]
    [InlineData("1ec461458feffe9f27ae17e30d0a804e2ab0a88f&hash=4a3c1931c85c9c568c39a8eb947ed382824b1cdd")]
    [InlineData(null)]
    public async Task ListJobFiles_with_an_id_that_is_not_a_torrent_id_throws_and_sends_nothing(string? clientJobId)
    {
        //Arrange

        //Act
        var exception = await Record.ExceptionAsync(() => Client.ListJobFilesAsync(Saved, clientJobId!, TestContext.Current.CancellationToken));

        //Assert
        Assert.IsType<ArgumentException>(exception);
        Assert.Empty(_harness.QBittorrentHttp.Requests);
    }

    [Fact]
    public async Task ListJobFiles_signs_in_reads_the_version_and_asks_for_the_files_with_the_session()
    {
        //Arrange
        _qbittorrent.AddTorrent(FakeQBittorrentTorrent.Of(new TorrentInfoHashes(PackHash, null), PackFolder, "Movies") with
        {
            Files = [new FakeQBittorrentFile($"{PackFolder}/Taken (2008).mkv", 28571138185, 1)],
        });

        //Act
        var result = await Client.ListJobFilesAsync(Saved, PackHash, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        Assert.Equal(new ClientJobFile($"{PackFolder}/Taken (2008).mkv", 28571138185, 1, true), Assert.Single(result.Value!));
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
                Assert.Single(version.HeaderValues("Cookie"));
            },
            files =>
            {
                Assert.Equal(HttpMethod.Get, files.Method);
                Assert.Equal($"{BaseUrl}{FilesPath}?hash={PackHash}", files.Uri.AbsoluteUri);
                Assert.Equal(_harness.QBittorrentHttp.Requests[1].HeaderValues("Cookie"), files.HeaderValues("Cookie"));
                Assert.Null(files.Body);
            });
    }

    [Theory]
    [InlineData("1EC461458FEFFE9F27AE17E30D0A804E2AB0A88F", "1ec461458feffe9f27ae17e30d0a804e2ab0a88f")]
    [InlineData(V2Hash, V2TorrentId)]
    [InlineData("E7F2A1C5B3D94E8F0A6C2B1D4E3F5A7C9B8D0E1F2A3B4C5D6E7F8A9B0C1D2E3F", V2TorrentId)]
    public async Task ListJobFiles_asks_by_the_torrent_id_lower_case_and_a_64_character_one_cut_to_40(string clientJobId, string expectedId)
    {
        //Arrange
        // qBittorrent reads hash as a TorrentID: a 64-character hash would name no torrent at all.

        //Act
        await Client.ListJobFilesAsync(Saved, clientJobId, TestContext.Current.CancellationToken);

        //Assert
        var files = Assert.Single(_harness.QBittorrentHttp.Requests, request => request.Uri.AbsolutePath == FilesPath);
        Assert.Equal($"?hash={expectedId}", files.Uri.Query);
    }

    [Fact]
    public async Task ListJobFiles_reads_the_three_files_of_the_sample_exactly()
    {
        //Arrange
        _qbittorrent.Intercept = request => request.RequestUri!.AbsolutePath == FilesPath
            ? VendorResponses.Sample("QBittorrent/torrent-files-multi.json", "application/json")
            : null;

        //Act
        var result = await Client.ListJobFilesAsync(Saved, PackHash, TestContext.Current.CancellationToken);

        //Assert
        // In qBittorrent's order, each under the torrent's root folder with its double space kept, its size
        // beyond int32 (the three add up to the pack's 81850937909), and is_seed, piece_range and availability
        // going no further.
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        Assert.Null(result.Error);
        Assert.Equal(
            [
                new ClientJobFile($"{PackFolder}/Taken (2008) BDRemux 1080p UkrEng  Sub UkrEng [Hurtom].mkv", 28571138185, 1, true),
                new ClientJobFile($"{PackFolder}/Taken 2 [Extended Cut] (2012) BDRemux 1080p 2xUkrEng  Sub Eng [Hurtom].mkv", 25264485639, 1, true),
                new ClientJobFile($"{PackFolder}/Taken 3 (Extended Cut) (2014) BDRemux 1080p UkrEng  Sub UkrEng [Hurtom].mkv", 28015314085, 1, true),
            ],
            result.Value!);
        Assert.Equal(81850937909, result.Value!.Sum(file => file.SizeBytes));
    }

    [Fact]
    public async Task ListJobFiles_of_a_torrent_still_fetching_its_metadata_is_empty()
    {
        //Arrange
        // A magnet's files aren't known until its metadata arrives: qBittorrent answers [] rather than 404.
        _qbittorrent.AddTorrent(FakeQBittorrentTorrent.Of(new TorrentInfoHashes(PackHash, null), PackHash, "Movies", "metaDL", 0));

        //Act
        var result = await Client.ListJobFilesAsync(Saved, PackHash, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        Assert.Empty(result.Value!);
    }

    [Fact]
    public async Task ListJobFiles_of_a_torrent_qbittorrent_does_not_have_is_not_found()
    {
        //Arrange
        _qbittorrent.AddTorrent(new TorrentInfoHashes("4a3c1931c85c9c568c39a8eb947ed382824b1cdd", null), "Im Westen nichts Neues", "Movies");

        //Act
        var result = await Client.ListJobFilesAsync(Saved, PackHash, TestContext.Current.CancellationToken);

        //Assert
        // The version has just answered at this address, so the 404 is the torrent gone, not a wrong address.
        Assert.Equal(IntegrationCallStatus.NotFound, result.Status);
        Assert.Equal(TorrentGoneError, TestTexts.En(result.Error));
        Assert.Null(result.Value);
    }

    [Fact]
    public async Task ListJobFiles_of_a_torrent_removed_in_qbittorrent_is_not_found()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        _qbittorrent.AddTorrent(FakeQBittorrentTorrent.Of(new TorrentInfoHashes(PackHash, null), PackFolder, "Movies") with
        {
            Files = [new FakeQBittorrentFile($"{PackFolder}/Taken (2008).mkv", 28571138185, 1)],
        });
        var before = await Client.ListJobFilesAsync(Saved, PackHash, cancellationToken);
        _qbittorrent.RemoveTorrent(PackHash);

        //Act
        var result = await Client.ListJobFilesAsync(Saved, PackHash, cancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Succeeded, before.Status);
        Assert.Equal(IntegrationCallStatus.NotFound, result.Status);
        Assert.Equal(TorrentGoneError, TestTexts.En(result.Error));
    }

    [Fact]
    public async Task ListJobFiles_whose_version_read_is_404_is_the_wrong_address_not_a_torrent_gone()
    {
        //Arrange
        // Only the files read means "no such torrent" by a 404: on the version read it means no Web API here.
        _qbittorrent.Intercept = request => request.RequestUri!.AbsolutePath == VersionPath ? new HttpResponseMessage(HttpStatusCode.NotFound) : null;

        //Act
        var result = await Client.ListJobFilesAsync(Saved, PackHash, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.UnexpectedResponse, result.Status);
        Assert.Equal(
            "qBittorrent's Web API wasn't found at this address (404 Not Found). Use the address of qBittorrent's Web UI, such as http://192.168.1.10:8080.",
            TestTexts.En(result.Error));
        Assert.DoesNotContain(_harness.QBittorrentHttp.Requests, request => request.Uri.AbsolutePath == FilesPath);
    }

    [Theory]
    [MemberData(nameof(NotFiles))]
    public async Task ListJobFiles_of_a_body_that_is_not_qbittorrents_files_is_an_unexpected_response(string body)
    {
        //Arrange
        // A file without a usable name fails the whole answer rather than being left out: a list missing a file
        // would read as a download with less in it than it has.
        _qbittorrent.Intercept = request => request.RequestUri!.AbsolutePath == FilesPath
            ? VendorResponses.Text(body, HttpStatusCode.OK, "application/json")
            : null;

        //Act
        var result = await Client.ListJobFilesAsync(Saved, PackHash, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.UnexpectedResponse, result.Status);
        Assert.Equal(NotFilesError, TestTexts.En(result.Error));
        Assert.Null(result.Value);
    }

    [Fact]
    public async Task ListJobFiles_keeps_each_path_exactly_as_qbittorrent_names_it()
    {
        //Arrange
        // A path names a real file: double spaces, edge spaces and any script stay, and nothing is cleaned.
        string[] names =
        [
            $"{PackFolder}/Taken (2008) BDRemux 1080p UkrEng  Sub UkrEng [Hurtom].mkv",
            " Фільми 4K /  Заручниця (2008) .mkv ",
            "Season 01/Extras/Sample.mkv",
            "single file.mkv",
        ];
        AnswerFiles([.. names.Select(name => FileEntry(name))]);

        //Act
        var result = await Client.ListJobFilesAsync(Saved, PackHash, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(names, result.Value!.Select(file => file.RelativePath));
    }

    [Fact]
    public async Task ListJobFiles_keeps_a_path_as_long_as_any_path_and_refuses_a_longer_one()
    {
        //Arrange
        var longest = PackFolder + "/" + new string('n', 4096 - PackFolder.Length - 1);
        var cancellationToken = TestContext.Current.CancellationToken;
        AnswerFiles(FileEntry(longest));
        var kept = await Client.ListJobFilesAsync(Saved, PackHash, cancellationToken);
        AnswerFiles(FileEntry(longest + "n"));

        //Act
        var refused = await Client.ListJobFilesAsync(Saved, PackHash, cancellationToken);

        //Assert
        // Cut short it would name another file, so it isn't cut: no path under a torrent's folder is longer
        // than Linux's PATH_MAX.
        Assert.Equal(longest, Assert.Single(kept.Value!).RelativePath);
        Assert.Equal(4096, longest.Length);
        Assert.Equal(IntegrationCallStatus.UnexpectedResponse, refused.Status);
        Assert.Equal(NotFilesError, TestTexts.En(refused.Error));
    }

    [Theory]
    [InlineData("0", 0L)]
    [InlineData("28571138185", 28571138185L)]
    [InlineData("9223372036854775807", long.MaxValue)]
    [InlineData("9223372036854775808", null)]
    [InlineData("-1", null)]
    [InlineData("1.5", null)]
    [InlineData("\"28571138185\"", null)]
    [InlineData("null", null)]
    [InlineData("true", null)]
    public async Task ListJobFiles_reads_the_size_only_as_a_whole_number_of_zero_or_more_bytes(string size, long? expected)
    {
        //Arrange
        var file = FileEntry("Taken (2008).mkv");
        file["size"] = JsonNode.Parse(size);
        AnswerFiles(file);

        //Act
        var result = await Client.ListJobFilesAsync(Saved, PackHash, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(expected, Assert.Single(result.Value!).SizeBytes);
    }

    [Theory]
    [InlineData("0", false)]
    [InlineData("1", true)]
    [InlineData("6", true)]
    [InlineData("7", true)]
    [InlineData("-1", null)]
    [InlineData("1.5", null)]
    [InlineData("4294967296", null)]
    [InlineData("\"1\"", null)]
    [InlineData("null", null)]
    public async Task ListJobFiles_reads_priority_0_as_unwanted_and_any_priority_above_as_wanted(string priority, bool? expected)
    {
        //Arrange
        // qBittorrent's 0 is "do not download"; 1, 6 and 7 are the priorities it downloads at.
        var file = FileEntry("Taken (2008).mkv");
        file["priority"] = JsonNode.Parse(priority);
        AnswerFiles(file);

        //Act
        var result = await Client.ListJobFilesAsync(Saved, PackHash, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(expected, Assert.Single(result.Value!).IsWanted);
    }

    [Theory]
    [InlineData("0", 0.0)]
    [InlineData("0.9997", 0.9997)]
    [InlineData("1", 1.0)]
    [InlineData("-0.01", null)]
    [InlineData("1.0001", null)]
    [InlineData("\"0.5\"", null)]
    [InlineData("null", null)]
    public async Task ListJobFiles_reads_the_progress_only_as_a_number_from_0_to_1(string progress, double? expected)
    {
        //Arrange
        var file = FileEntry("Taken (2008).mkv");
        file["progress"] = JsonNode.Parse(progress);
        AnswerFiles(file);

        //Act
        var result = await Client.ListJobFilesAsync(Saved, PackHash, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(expected, Assert.Single(result.Value!).Progress);
    }

    [Fact]
    public async Task ListJobFiles_reads_a_file_without_size_progress_or_priority_as_unknown()
    {
        //Arrange
        AnswerFiles(new JsonObject { ["name"] = "Taken (2008).mkv" });

        //Act
        var result = await Client.ListJobFilesAsync(Saved, PackHash, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(new ClientJobFile("Taken (2008).mkv", null, null, null), Assert.Single(result.Value!));
    }

    [Theory]
    [InlineData(404, IntegrationCallStatus.NotFound, TorrentGoneError)]
    [InlineData(500, IntegrationCallStatus.ServiceError, "qBittorrent answered 500 Internal Server Error. Its own log says why.")]
    [InlineData(401, IntegrationCallStatus.UnexpectedResponse,
        "qBittorrent answered 401 Unauthorized, which it doesn't send for this request. Check the address.")]
    [InlineData(204, IntegrationCallStatus.UnexpectedResponse,
        "qBittorrent answered 204 No Content, which it doesn't send for this request. Check the address.")]
    public async Task ListJobFiles_answers_a_failed_read_with_its_status_and_sentence(int statusCode, IntegrationCallStatus expectedStatus, string expectedError)
    {
        //Arrange
        _qbittorrent.Intercept = request => request.RequestUri!.AbsolutePath == FilesPath ? new HttpResponseMessage((HttpStatusCode)statusCode) : null;

        //Act
        var result = await Client.ListJobFilesAsync(Saved, PackHash, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(expectedStatus, result.Status);
        Assert.Equal(expectedError, TestTexts.En(result.Error));
        Assert.Null(result.Value);
        Assert.Single(_harness.QBittorrentHttp.Requests, request => request.Uri.AbsolutePath == FilesPath);
    }

    [Fact]
    public async Task ListJobFiles_whose_fresh_session_is_refused_fails_without_signing_in_again()
    {
        //Arrange
        _qbittorrent.Intercept = request => request.RequestUri!.AbsolutePath == FilesPath
            ? VendorResponses.Recorded(FakeQBittorrentServer.ExpiredSessionResponseFile)
            : null;

        //Act
        var result = await Client.ListJobFilesAsync(Saved, PackHash, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.UnexpectedResponse, result.Status);
        Assert.Equal("qBittorrent refused the session it had just opened.", TestTexts.En(result.Error));
        Assert.Equal(
            ["POST /api/v2/auth/login", "GET /api/v2/app/version with session 1", "GET /api/v2/torrents/files with session 1"],
            Describe(_harness.QBittorrentHttp.Requests));
    }

    [Fact]
    public async Task ListJobFiles_whose_kept_session_qbittorrent_forgot_renews_it_once_and_reads_on()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        _qbittorrent.AddTorrent(FakeQBittorrentTorrent.Of(new TorrentInfoHashes(PackHash, null), PackFolder, "Movies") with
        {
            Files = [new FakeQBittorrentFile($"{PackFolder}/Taken (2008).mkv", 28571138185, 1)],
        });
        await Client.ListJobFilesAsync(Saved, PackHash, cancellationToken);
        _qbittorrent.ExpireSessions();

        //Act
        var result = await Client.ListJobFilesAsync(Saved, PackHash, cancellationToken);

        //Assert
        Assert.Equal($"{PackFolder}/Taken (2008).mkv", Assert.Single(result.Value!).RelativePath);
        Assert.Equal(
            [
                "GET /api/v2/app/version with session 1",
                "POST /api/v2/auth/login",
                "GET /api/v2/app/version with session 2",
                "GET /api/v2/torrents/files with session 2",
            ],
            Describe(_harness.QBittorrentHttp.Requests).Skip(3));
    }

    [Theory]
    [InlineData("v4.3.9")]
    [InlineData("v4.4.0")]
    public async Task ListJobFiles_of_a_qbittorrent_older_than_4_4_1_is_an_unsupported_version_and_reads_no_files(string version)
    {
        //Arrange
        _qbittorrent.Version = version;

        //Act
        var result = await Client.ListJobFilesAsync(Saved, PackHash, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.UnsupportedVersion, result.Status);
        Assert.Equal($"This is qBittorrent {version}; the app works with qBittorrent 4.4.1 or later. Update qBittorrent, then test again.", TestTexts.En(result.Error));
        Assert.DoesNotContain(_harness.QBittorrentHttp.Requests, request => request.Uri.AbsolutePath == FilesPath);
    }

    [Theory]
    [MemberData(nameof(TransportFailures))]
    public async Task ListJobFiles_maps_each_transport_error_to_its_sentence_without_the_exception_text(
        HttpRequestError error, IntegrationCallStatus expectedStatus, string expectedError)
    {
        //Arrange
        _qbittorrent.Intercept = request => request.RequestUri!.AbsolutePath == FilesPath
            ? throw new HttpRequestException(error, $"Socket failure on {request.RequestUri} with {FakeQBittorrentServer.DefaultPassword}")
            : null;

        //Act
        var result = await Client.ListJobFilesAsync(Saved, PackHash, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(expectedStatus, result.Status);
        Assert.Equal(expectedError, TestTexts.En(result.Error));
    }

    [Fact]
    public async Task ListJobFiles_that_outlives_the_timeout_is_timed_out()
    {
        //Arrange
        using var harness = new IntegrationAdapterHarness(timeout: TimeSpan.FromMilliseconds(100));
        var qbittorrent = new FakeQBittorrentServer
        {
            Intercept = request => request.RequestUri!.AbsolutePath == FilesPath ? StalledAnswer() : null,
        };
        qbittorrent.Arm(harness.QBittorrentHttp);

        //Act
        var result = await harness.Adapter<IDownloadClient>(QBittorrentAdapter.TypeId)
            .ListJobFilesAsync(Saved, PackHash, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.TimedOut, result.Status);
        Assert.Equal("qBittorrent at http://qbittorrent.test:8080 didn't answer within 0.1 seconds.", TestTexts.En(result.Error));
    }

    [Fact]
    public async Task ListJobFiles_cancelled_by_the_caller_throws()
    {
        //Arrange
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        _qbittorrent.Intercept = request =>
        {
            if (request.RequestUri!.AbsolutePath != FilesPath)
                return null;
            cancellation.Cancel();
            return StalledAnswer();
        };

        //Act
        var exception = await Record.ExceptionAsync(() => Client.ListJobFilesAsync(Saved, PackHash, cancellation.Token));

        //Assert
        Assert.IsAssignableFrom<OperationCanceledException>(exception);
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────────

    /// <summary>A torrents/files entry shaped like 5.2's, with every property it sends.</summary>
    private static JsonObject FileEntry(string name) => new()
    {
        ["availability"] = -1,
        ["index"] = 0,
        ["is_seed"] = false,
        ["name"] = name,
        ["piece_range"] = new JsonArray(0, 3405),
        ["priority"] = 1,
        ["progress"] = 0.5,
        ["size"] = 28571138185,
    };

    private void AnswerFiles(params JsonObject[] files)
    {
        var body = new JsonArray([.. files]).ToJsonString();
        _qbittorrent.Intercept = request => request.RequestUri!.AbsolutePath == FilesPath
            ? VendorResponses.Text(body, HttpStatusCode.OK, "application/json")
            : null;
    }

    // A 200 whose body never finishes arriving: the pipe is never written to or completed.
    private static HttpResponseMessage StalledAnswer() =>
        new(HttpStatusCode.OK) { Content = new StreamContent(new Pipe().Reader.AsStream()) };

    /// <summary>
    /// Each request as its method and path, and which session it carried: 1 for the first the server handed
    /// out, and so on — the fake's session ids are random.
    /// </summary>
    private static List<string> Describe(IEnumerable<RecordedHttpRequest> requests)
    {
        var sessions = new List<string>();
        var described = new List<string>();
        foreach (var request in requests)
        {
            if (request.HeaderValues("Cookie") is not [var cookie])
            {
                described.Add($"{request.Method} {request.Uri.AbsolutePath}");
                continue;
            }

            if (!sessions.Contains(cookie))
                sessions.Add(cookie);
            described.Add($"{request.Method} {request.Uri.AbsolutePath} with session {sessions.IndexOf(cookie) + 1}");
        }

        return described;
    }
}
