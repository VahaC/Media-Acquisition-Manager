using System.IO.Pipelines;
using System.Net;
using System.Text;
using MediaAcquisitionManager.Core.Abstractions.Downloads;
using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Abstractions.Search;
using MediaAcquisitionManager.Core.Downloads;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Infrastructure.Integrations.QBittorrent;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;

namespace MediaAcquisitionManager.Infrastructure.Tests.Integrations;

// MAM-13 — a release handed to qBittorrent: the session and version read first, then exactly one POST
// api/v2/torrents/add of multipart/form-data with the .torrent (or the magnet link), the destination's key as
// the category, unchanged, and autoTMM=true — never a save path or any other field. Every answer qBittorrent
// gives from 4.4.1 to 5.2 is told apart by status and body shape (Ok., Fails., 5.2's JSON, 202, 409, 415), a
// 403 is never answered by sending the write again, and every error masks the password, the session, the
// magnet link and its trackers' addresses. Against FakeQBittorrentServer, which answers torrents/add the way
// qBittorrent's source does.
public sealed class QBittorrentAdapterSubmitTests : IDisposable
{
    private const string BaseUrl = FakeQBittorrentServer.DefaultBaseUrl;
    private const string SignInPath = "/api/v2/auth/login";
    private const string VersionPath = "/api/v2/app/version";
    private const string AddPath = "/api/v2/torrents/add";

    /// <summary>The v1 info hash of <see cref="TorznabSamples.TorrentFile"/>: SHA-1 of its info dictionary.</summary>
    private const string SampleHash = "72d6069eff259a0800f7afdb2500d5de591a9f01";

    private const string Passkey = "a1b2c3d4e5f6a7b8c9d0e1f2";

    /// <summary>The magnet's tracker, as a tracker that knows the owner by a passkey in its path spells it.</summary>
    private const string TrackerAddress = "http://bt.toloka.test/" + Passkey + "/announce";

    private const string Magnet =
        "magnet:?xt=urn:btih:4a3c1931c85c9c568c39a8eb947ed382824b1cdd&dn=Im%20Westen%20nichts%20Neues%20%282022%29&tr=http%3A%2F%2Fbt.toloka.test%2F" + Passkey + "%2Fannounce";

    private const string MagnetHash = "4a3c1931c85c9c568c39a8eb947ed382824b1cdd";

    private const string AddFailedError =
        "qBittorrent didn't add the release: it already has this torrent, or it couldn't use what it was sent.";

    private const string AddedNothingError =
        "qBittorrent added nothing: it already has this torrent, or it couldn't use what it was sent.";

    private const string UnreadableTorrentError = "qBittorrent couldn't read the .torrent file it was sent (415 Unsupported Media Type).";

    private const string NotAnAddAnswerError = "The service at this address answered, but not the way qBittorrent answers a new torrent.";

    private const string SessionRefusedError = "qBittorrent refused the session it had just opened.";

    private const string SignInNeededError =
        "qBittorrent asks for a user name and password. Enter them, or let this app's address skip authentication in qBittorrent's Web UI options.";

    private static readonly IntegrationConnectionSettings Saved = new(
        Guid.Parse("6a1f3c2e-9b4d-4e8a-b7c5-2d1e0f3a4b5c"), BaseUrl, FakeQBittorrentServer.DefaultUserName, FakeQBittorrentServer.DefaultPassword, null);

    private static readonly IntegrationConnectionSettings Anonymous = Saved with { UserName = null, Password = null };

    private readonly IntegrationAdapterHarness _harness = new();
    private readonly FakeQBittorrentServer _qbittorrent = new();

    public QBittorrentAdapterSubmitTests() => _qbittorrent.Arm(_harness.QBittorrentHttp);

    /// <summary>Bodies of a 200 to torrents/add that are neither 4.x's text nor 5.2's JSON object.</summary>
    public static TheoryData<string> NotAddAnswers =>
    [
        "",
        "   ",
        "OK",
        "Ok",
        "ok.",
        "Fails",
        "\"Ok.\"",
        "<!DOCTYPE html><html><body>qBittorrent Web UI</body></html>",
        "[]",
        """["72d6069eff259a0800f7afdb2500d5de591a9f01"]""",
        "null",
        "1",
        "{",
    ];

    /// <summary>5.2's JSON answers, and the receipt each one gives: an id only when exactly one well-formed
    /// TorrentID is listed.</summary>
    public static TheoryData<string, string?> AddedAnswers => new()
    {
        { """{"success_count":1,"failure_count":0,"pending_count":0,"added_torrent_ids":["72d6069eff259a0800f7afdb2500d5de591a9f01"]}""", SampleHash },
        { """{"added_torrent_ids":["72D6069EFF259A0800F7AFDB2500D5DE591A9F01"]}""", SampleHash },
        { """{"success_count":2,"failure_count":0,"pending_count":0,"added_torrent_ids":["72d6069eff259a0800f7afdb2500d5de591a9f01","14673d44802b315c5e3f0c5d6288a7e4914fe113"]}""", null },
        { """{"success_count":0,"failure_count":0,"pending_count":1,"added_torrent_ids":[]}""", null },
        { """{"success_count":1,"failure_count":0,"pending_count":0}""", null },
        { """{"added_torrent_ids":"72d6069eff259a0800f7afdb2500d5de591a9f01"}""", null },
        { """{"added_torrent_ids":[null]}""", null },
        { """{"added_torrent_ids":[42]}""", null },
        { """{"added_torrent_ids":["72d6069eff259a0800f7afdb2500d5de591a9f0"]}""", null },
        { """{"added_torrent_ids":["72d6069eff259a0800f7afdb2500d5de591a9f01aa"]}""", null },
        { """{"added_torrent_ids":["72d6069eff259a0800f7afdb2500d5de591a9g01"]}""", null },
        { """{"added_torrent_ids":["٧٢d6069eff259a0800f7afdb2500d5de591a9f01"]}""", null },
        { """{"added_torrent_ids":["e7f2a1c5b3d94e8f0a6c2b1d4e3f5a7c9b8d0e1f2a3b4c5d6e7f8a9b0c1d2e3f"]}""", null },
        { "{}", null },
    };

    /// <summary>The sentence for each status the adapter answers itself or leaves to the base class.</summary>
    public static TheoryData<int, IntegrationCallStatus, string> FailedStatuses => new()
    {
        { 409, IntegrationCallStatus.Refused, AddedNothingError },
        { 415, IntegrationCallStatus.UnexpectedResponse, UnreadableTorrentError },
        {
            404, IntegrationCallStatus.UnexpectedResponse,
            "qBittorrent's Web API wasn't found at this address (404 Not Found). Use the address of qBittorrent's Web UI, such as http://192.168.1.10:8080."
        },
        { 500, IntegrationCallStatus.ServiceError, "qBittorrent answered 500 Internal Server Error. Its own log says why." },
        { 503, IntegrationCallStatus.ServiceError, "qBittorrent answered 503 Service Unavailable. Its own log says why." },
        { 400, IntegrationCallStatus.UnexpectedResponse, "qBittorrent answered 400 Bad Request, which it doesn't send for this request. Check the address." },
        { 401, IntegrationCallStatus.UnexpectedResponse, "qBittorrent answered 401 Unauthorized, which it doesn't send for this request. Check the address." },
        { 204, IntegrationCallStatus.UnexpectedResponse, "qBittorrent answered 204 No Content, which it doesn't send for this request. Check the address." },
        { 405, IntegrationCallStatus.UnexpectedResponse, "qBittorrent answered 405 Method Not Allowed, which it doesn't send for this request. Check the address." },
    };

    /// <summary>qBittorrent's sentence for each transport failure at the POST, and the status it maps to.</summary>
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
            HttpRequestError.SecureConnectionError, IntegrationCallStatus.Unreachable,
            "Couldn't reach qBittorrent at http://qbittorrent.test:8080: the secure (HTTPS) connection failed, for example because its certificate isn't trusted."
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

    /// <summary>Submissions that are the caller's bug: a payload qBittorrent doesn't take or that is missing
    /// what it sends, or a key no destination can hold.</summary>
    public static TheoryData<string> CallerBugs =>
    [
        "nzb",
        "torrent file without bytes",
        "torrent file with no bytes",
        "magnet without a link",
        "blank magnet",
        "web link as a magnet",
        "magnet with a line break",
        "unknown payload kind",
        "empty key",
        "blank key",
        "key with a control character",
        "key too long",
    ];

    private IDownloadClient Client => _harness.Adapter<IDownloadClient>(QBittorrentAdapter.TypeId);

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task Submit_signs_in_reads_the_version_and_posts_the_torrent_once_with_the_session()
    {
        //Arrange

        //Act
        var result = await Client.SubmitAsync(Saved, TorrentSubmission("Movies"), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        Assert.Null(result.Error);
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
            add =>
            {
                Assert.Equal(HttpMethod.Post, add.Method);
                // No query: everything travels in the body.
                Assert.Equal(BaseUrl + AddPath, add.Uri.AbsoluteUri);
                Assert.Equal(_harness.QBittorrentHttp.Requests[1].HeaderValues("Cookie"), add.HeaderValues("Cookie"));
                Assert.StartsWith("multipart/form-data; boundary=", Assert.Single(add.HeaderValues("Content-Type")), StringComparison.Ordinal);
                // By qBittorrent's source, a Referer or Origin is compared with Host when present.
                Assert.Empty(add.HeaderValues("Referer"));
                Assert.Empty(add.HeaderValues("Origin"));
            });
    }

    [Fact]
    public async Task Submit_of_a_torrent_file_sends_the_file_part_the_category_and_autotmm_and_no_other_field()
    {
        //Arrange
        var torrent = TorznabSamples.TorrentFile();

        //Act
        await Client.SubmitAsync(Saved, new DownloadSubmission(TorrentPayload(torrent), "Movies", SampleHash), TestContext.Current.CancellationToken);

        //Assert
        // Never savepath, downloadPath, useDownloadPath, rename, tags, paused or stopped: the category's folder
        // decides where it lands (specification §3.2), and the rest are the owner's qBittorrent defaults.
        var add = Assert.Single(_qbittorrent.AddRequests);
        Assert.Equal(["torrents", "category", "autoTMM"], add.FieldNames);
        Assert.Equal(ReleasePayloadKind.TorrentFile, add.Kind);
        Assert.Equal(torrent, add.TorrentFile);
        Assert.Equal("release.torrent", add.TorrentFileName);
        Assert.Equal("application/x-bittorrent", add.TorrentContentType);
        Assert.Equal("Movies", add.Category);
        Assert.Equal("true", add.AutoTmm);
        Assert.Null(add.Urls);
    }

    [Fact]
    public async Task Submit_of_a_magnet_sends_it_as_urls_with_the_category_and_autotmm_and_no_other_field()
    {
        //Arrange

        //Act
        var result = await Client.SubmitAsync(Saved, MagnetSubmission("TV Shows"), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        Assert.Equal(MagnetHash, result.Value!.ClientJobId);
        var add = Assert.Single(_qbittorrent.AddRequests);
        Assert.Equal(["urls", "category", "autoTMM"], add.FieldNames);
        Assert.Equal(ReleasePayloadKind.Magnet, add.Kind);
        Assert.Equal(Magnet, add.Urls);
        Assert.Equal("TV Shows", add.Category);
        Assert.Equal("true", add.AutoTmm);
        Assert.Null(add.TorrentFile);
    }

    [Fact]
    public async Task Submit_sends_the_category_exactly_as_stored_surrounding_spaces_included()
    {
        //Arrange
        // A key qBittorrent has with spaces around it; trimmed, it would name a category qBittorrent doesn't
        // have, which it would create in its default folder.
        const string key = "  Фільми 4K ";
        _qbittorrent.AddCategory(key, "/downloads/movies-4k");

        //Act
        var result = await Client.SubmitAsync(Saved, TorrentSubmission(key), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        Assert.Equal(key, Assert.Single(_qbittorrent.AddRequests).Category);
        var torrent = Assert.Single(_qbittorrent.Torrents);
        Assert.Equal(key, torrent.Category);
        Assert.Equal("/downloads/movies-4k", torrent.SavePath);
        Assert.True(torrent.AutoTmm);
    }

    [Fact]
    public async Task Submit_to_a_category_qbittorrent_does_not_have_is_added_and_the_category_created_silently()
    {
        //Arrange
        // What qBittorrent does with an unknown category, which is why the service checks the destination's
        // key against the client's list first: the release would land in the default folder.

        //Act
        var result = await Client.SubmitAsync(Saved, TorrentSubmission("Unsorted"), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        Assert.Equal("/downloads/Unsorted", Assert.Single(_qbittorrent.Torrents).SavePath);
        Assert.True(_qbittorrent.Categories.ContainsKey("Unsorted"));
    }

    [Fact]
    public async Task Submit_to_qbittorrent_5_2_answers_the_id_it_added()
    {
        //Arrange

        //Act
        var result = await Client.SubmitAsync(Saved, TorrentSubmission("Movies"), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        Assert.Equal(new SubmissionReceipt(SampleHash), result.Value);
        var torrent = Assert.Single(_qbittorrent.Torrents);
        Assert.Equal(SampleHash, torrent.Hash);
        Assert.Equal("/downloads/movies", torrent.SavePath);
    }

    [Theory]
    [InlineData("v4.4.1")]
    [InlineData("v4.6.7")]
    [InlineData("v5.0.5")]
    [InlineData("v5.1.4")]
    public async Task Submit_to_qbittorrent_before_5_2_succeeds_on_ok_without_an_id(string version)
    {
        //Arrange
        // 4.4.1 to 5.1 answer "Ok." and nothing more: the caller finds the job by its info hash.
        _qbittorrent.Version = version;

        //Act
        var result = await Client.SubmitAsync(Saved, TorrentSubmission("Movies"), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        Assert.Equal(new SubmissionReceipt(null), result.Value);
        Assert.Equal(SampleHash, Assert.Single(_qbittorrent.Torrents).Hash);
    }

    [Theory]
    [InlineData("v5.2.3", AddedNothingError)]
    [InlineData("v5.1.4", AddFailedError)]
    [InlineData("v4.4.1", AddFailedError)]
    public async Task Submit_of_a_torrent_qbittorrent_already_has_is_refused_with_its_sentence(string version, string expectedError)
    {
        //Arrange
        // 5.2 answers 409 when nothing was added, earlier versions 200 "Fails.".
        _qbittorrent.Version = version;
        _qbittorrent.AddTorrent(new TorrentInfoHashes(SampleHash, null), "x.mkv", "Movies");

        //Act
        var result = await Client.SubmitAsync(Saved, TorrentSubmission("Movies"), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Refused, result.Status);
        Assert.Equal(expectedError, result.Error);
        Assert.Null(result.Value);
        Assert.Single(_qbittorrent.Torrents);
        Assert.Single(_harness.QBittorrentHttp.Requests, request => request.Uri.AbsolutePath == AddPath);
    }

    [Fact]
    public async Task Submit_of_a_torrent_file_qbittorrent_can_not_read_is_an_unexpected_response()
    {
        //Arrange
        var notATorrent = Encoding.ASCII.GetBytes("<!DOCTYPE html><html><body>Please log in</body></html>");

        //Act
        var result = await Client.SubmitAsync(
            Saved, new DownloadSubmission(TorrentPayload(notATorrent), "Movies", null), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.UnexpectedResponse, result.Status);
        Assert.Equal(UnreadableTorrentError, result.Error);
        Assert.Empty(_qbittorrent.Torrents);
    }

    [Theory]
    [MemberData(nameof(AddedAnswers))]
    public async Task Submit_reads_an_id_from_5_2s_answer_only_when_it_lists_exactly_one(string body, string? expectedId)
    {
        //Arrange
        _qbittorrent.Intercept = request => request.RequestUri!.AbsolutePath == AddPath
            ? VendorResponses.Text(body, HttpStatusCode.OK, "application/json")
            : null;

        //Act
        var result = await Client.SubmitAsync(Saved, TorrentSubmission("Movies"), TestContext.Current.CancellationToken);

        //Assert
        // An answer in 5.2's shape means something was added (5.2 answers 409 when nothing was): without one
        // id, the caller finds the job by its info hash rather than failing a release qBittorrent took.
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        Assert.Equal(new SubmissionReceipt(expectedId), result.Value);
    }

    [Fact]
    public async Task Submit_answered_202_accepted_succeeds_without_an_id()
    {
        //Arrange
        // 5.2 answers 202 while something it was sent is still pending.
        _qbittorrent.Intercept = request => request.RequestUri!.AbsolutePath == AddPath
            ? VendorResponses.Text(
                """{"success_count":0,"failure_count":0,"pending_count":1,"added_torrent_ids":[]}""", HttpStatusCode.Accepted, "application/json")
            : null;

        //Act
        var result = await Client.SubmitAsync(Saved, MagnetSubmission("Movies"), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        Assert.Equal(new SubmissionReceipt(null), result.Value);
    }

    [Theory]
    [InlineData("Ok.", IntegrationCallStatus.Succeeded, null)]
    [InlineData("Ok.\n", IntegrationCallStatus.Succeeded, null)]
    [InlineData("Fails.", IntegrationCallStatus.Refused, AddFailedError)]
    [InlineData(" Fails. ", IntegrationCallStatus.Refused, AddFailedError)]
    public async Task Submit_reads_the_text_answers_of_4_4_1_to_5_1(string body, IntegrationCallStatus expectedStatus, string? expectedError)
    {
        //Arrange
        _qbittorrent.Intercept = request => request.RequestUri!.AbsolutePath == AddPath
            ? VendorResponses.Text(body, HttpStatusCode.OK)
            : null;

        //Act
        var result = await Client.SubmitAsync(Saved, TorrentSubmission("Movies"), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(expectedStatus, result.Status);
        Assert.Equal(expectedError, result.Error);
        Assert.Equal(expectedStatus == IntegrationCallStatus.Succeeded ? new SubmissionReceipt(null) : null, result.Value);
    }

    [Theory]
    [MemberData(nameof(NotAddAnswers))]
    public async Task Submit_whose_200_answer_is_not_qbittorrents_is_an_unexpected_response(string body)
    {
        //Arrange
        _qbittorrent.Intercept = request => request.RequestUri!.AbsolutePath == AddPath
            ? VendorResponses.Text(body, HttpStatusCode.OK, "application/json")
            : null;

        //Act
        var result = await Client.SubmitAsync(Saved, TorrentSubmission("Movies"), TestContext.Current.CancellationToken);

        //Assert
        // Maybe sent: the caller looks the hash up rather than taking it as added or as refused.
        Assert.Equal(IntegrationCallStatus.UnexpectedResponse, result.Status);
        Assert.Equal(NotAnAddAnswerError, result.Error);
        Assert.Null(result.Value);
    }

    [Theory]
    [MemberData(nameof(FailedStatuses))]
    public async Task Submit_answers_each_failed_status_with_its_sentence_and_sends_it_once(
        int statusCode, IntegrationCallStatus expectedStatus, string expectedError)
    {
        //Arrange
        _qbittorrent.Intercept = request => request.RequestUri!.AbsolutePath == AddPath
            ? new HttpResponseMessage((HttpStatusCode)statusCode)
            : null;

        //Act
        var result = await Client.SubmitAsync(Saved, TorrentSubmission("Movies"), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(expectedStatus, result.Status);
        Assert.Equal(expectedError, result.Error);
        Assert.Null(result.Value);
        Assert.Equal(
            [$"POST {SignInPath}", $"GET {VersionPath}", $"POST {AddPath}"],
            _harness.QBittorrentHttp.Requests.Select(request => $"{request.Method} {request.Uri.AbsolutePath}"));
    }

    [Fact]
    public async Task Submit_whose_post_is_refused_with_403_on_a_kept_session_sends_it_once_and_drops_the_session()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await Client.ListDestinationsAsync(Saved, [], cancellationToken);
        // The version read just confirmed the session; qBittorrent refuses the write all the same.
        _qbittorrent.Intercept = request => request.RequestUri!.AbsolutePath == AddPath
            ? VendorResponses.Recorded(FakeQBittorrentServer.ExpiredSessionResponseFile)
            : null;

        //Act
        var result = await Client.SubmitAsync(Saved, TorrentSubmission("Movies"), cancellationToken);
        _qbittorrent.Intercept = null;
        var next = await Client.ListDestinationsAsync(Saved, [], cancellationToken);

        //Assert
        // Never signed in for and sent again: a write sent twice may act twice (house rule 8). The refused
        // session is dropped, so the next call signs in rather than sending it again.
        Assert.Equal(IntegrationCallStatus.UnexpectedResponse, result.Status);
        Assert.Equal(SessionRefusedError, result.Error);
        Assert.True(next.IsSuccess);
        Assert.Equal(
            [
                "GET /api/v2/app/version with session 1",
                "POST /api/v2/torrents/add with session 1",
                "POST /api/v2/auth/login",
                "GET /api/v2/app/version with session 2",
                "GET /api/v2/torrents/categories with session 2",
            ],
            Describe(_harness.QBittorrentHttp.Requests).Skip(3));
        Assert.Empty(_qbittorrent.Torrents);
    }

    [Fact]
    public async Task Submit_whose_post_is_refused_with_403_on_a_fresh_session_sends_it_once_without_signing_in_again()
    {
        //Arrange
        _qbittorrent.Intercept = request => request.RequestUri!.AbsolutePath == AddPath
            ? VendorResponses.Recorded(FakeQBittorrentServer.ExpiredSessionResponseFile)
            : null;

        //Act
        var result = await Client.SubmitAsync(Saved, TorrentSubmission("Movies"), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.UnexpectedResponse, result.Status);
        Assert.Equal(SessionRefusedError, result.Error);
        Assert.Equal(
            ["POST /api/v2/auth/login", "GET /api/v2/app/version with session 1", "POST /api/v2/torrents/add with session 1"],
            Describe(_harness.QBittorrentHttp.Requests));
    }

    [Fact]
    public async Task Submit_without_a_user_name_whose_post_is_refused_asks_for_one()
    {
        //Arrange
        // qBittorrent let this address read without signing in, then refused the write.
        _qbittorrent.Intercept = request => request.RequestUri!.AbsolutePath switch
        {
            VersionPath => VendorResponses.Text("v5.2.3", HttpStatusCode.OK),
            AddPath => VendorResponses.Recorded(FakeQBittorrentServer.ExpiredSessionResponseFile),
            _ => null,
        };

        //Act
        var result = await Client.SubmitAsync(Anonymous, TorrentSubmission("Movies"), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.AuthenticationFailed, result.Status);
        Assert.Equal(SignInNeededError, result.Error);
        Assert.Equal(["GET /api/v2/app/version", "POST /api/v2/torrents/add"], Describe(_harness.QBittorrentHttp.Requests));
    }

    [Fact]
    public async Task Submit_without_a_user_name_where_qbittorrent_lets_the_address_in_sends_no_sign_in_and_no_cookie()
    {
        //Arrange
        _qbittorrent.Intercept = request => request.RequestUri!.AbsolutePath switch
        {
            VersionPath => VendorResponses.Text("v4.6.7", HttpStatusCode.OK),
            AddPath => VendorResponses.Text("Ok.", HttpStatusCode.OK),
            _ => null,
        };

        //Act
        var result = await Client.SubmitAsync(Anonymous, TorrentSubmission("Movies"), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        Assert.Equal(["GET /api/v2/app/version", "POST /api/v2/torrents/add"], Describe(_harness.QBittorrentHttp.Requests));
    }

    [Fact]
    public async Task Submit_whose_kept_session_qbittorrent_forgot_renews_it_at_the_version_read_and_posts_once()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await Client.ListDestinationsAsync(Saved, [], cancellationToken);
        // qBittorrent restarted, or the session sat idle past its timeout.
        _qbittorrent.ExpireSessions();

        //Act
        var result = await Client.SubmitAsync(Saved, TorrentSubmission("Movies"), cancellationToken);

        //Assert
        // The read is sent again on the renewed session; the write goes out once, on a session qBittorrent has
        // just accepted.
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        Assert.Equal(SampleHash, result.Value!.ClientJobId);
        Assert.Equal(
            [
                "GET /api/v2/app/version with session 1",
                "POST /api/v2/auth/login",
                "GET /api/v2/app/version with session 2",
                "POST /api/v2/torrents/add with session 2",
            ],
            Describe(_harness.QBittorrentHttp.Requests).Skip(3));
    }

    [Theory]
    [InlineData("v4.3.9")]
    [InlineData("v4.4.0")]
    [InlineData("v4.1.9.1")]
    public async Task Submit_to_a_qbittorrent_older_than_4_4_1_is_an_unsupported_version_and_posts_nothing(string version)
    {
        //Arrange
        _qbittorrent.Version = version;

        //Act
        var result = await Client.SubmitAsync(Saved, TorrentSubmission("Movies"), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.UnsupportedVersion, result.Status);
        Assert.Equal($"This is qBittorrent {version}; the app works with qBittorrent 4.4.1 or later. Update qBittorrent, then test again.", result.Error);
        Assert.DoesNotContain(_harness.QBittorrentHttp.Requests, request => request.Uri.AbsolutePath == AddPath);
        Assert.Empty(_qbittorrent.Torrents);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Forbidden")]
    [InlineData("<!DOCTYPE html><html><body>qBittorrent Web UI</body></html>")]
    public async Task Submit_whose_version_answer_is_not_a_version_is_an_unexpected_response_and_posts_nothing(string body)
    {
        //Arrange
        _qbittorrent.Intercept = request => request.RequestUri!.AbsolutePath == VersionPath
            ? VendorResponses.Text(body, HttpStatusCode.OK)
            : null;

        //Act
        var result = await Client.SubmitAsync(Saved, TorrentSubmission("Movies"), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.UnexpectedResponse, result.Status);
        Assert.Equal("The service at this address answered, but not with a qBittorrent version.", result.Error);
        Assert.DoesNotContain(_harness.QBittorrentHttp.Requests, request => request.Uri.AbsolutePath == AddPath);
    }

    [Fact]
    public async Task Submit_whose_sign_in_is_refused_posts_nothing()
    {
        //Arrange
        _qbittorrent.Password = "changed-in-qbittorrent";

        //Act
        var result = await Client.SubmitAsync(Saved, TorrentSubmission("Movies"), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.AuthenticationFailed, result.Status);
        Assert.Equal("qBittorrent refused the user name or password.", result.Error);
        Assert.Equal(["POST /api/v2/auth/login"], Describe(_harness.QBittorrentHttp.Requests));
    }

    [Theory]
    [MemberData(nameof(TransportFailures))]
    public async Task Submit_maps_each_transport_error_at_the_post_to_its_sentence_without_the_exception_text(
        HttpRequestError error, IntegrationCallStatus expectedStatus, string expectedMessage)
    {
        //Arrange
        _qbittorrent.Intercept = request => request.RequestUri!.AbsolutePath == AddPath
            ? throw new HttpRequestException(error, $"Socket failure on {request.RequestUri} with {FakeQBittorrentServer.DefaultPassword} and {Magnet}")
            : null;

        //Act
        var result = await Client.SubmitAsync(Saved, MagnetSubmission("Movies"), TestContext.Current.CancellationToken);

        //Assert
        // Maybe sent (the connection may have failed after qBittorrent took it): never sent again here, the
        // caller looks the hash up.
        Assert.Equal(expectedStatus, result.Status);
        Assert.Equal(expectedMessage, result.Error);
        Assert.Single(_harness.QBittorrentHttp.Requests, request => request.Uri.AbsolutePath == AddPath);
    }

    [Fact]
    public async Task Submit_whose_post_outlives_the_timeout_is_timed_out_and_not_sent_again()
    {
        //Arrange
        using var harness = new IntegrationAdapterHarness(timeout: TimeSpan.FromMilliseconds(100));
        var qbittorrent = new FakeQBittorrentServer
        {
            // The headers arrive, the body never does: only the client's timeout ends the wait.
            Intercept = request => request.RequestUri!.AbsolutePath == AddPath ? StalledAnswer() : null,
        };
        qbittorrent.Arm(harness.QBittorrentHttp);

        //Act
        var result = await harness.Adapter<IDownloadClient>(QBittorrentAdapter.TypeId)
            .SubmitAsync(Saved, TorrentSubmission("Movies"), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.TimedOut, result.Status);
        Assert.Equal("qBittorrent at http://qbittorrent.test:8080 didn't answer within 0.1 seconds.", result.Error);
        Assert.Single(harness.QBittorrentHttp.Requests, request => request.Uri.AbsolutePath == AddPath);
    }

    [Fact]
    public async Task Submit_cancelled_by_the_caller_during_the_post_throws()
    {
        //Arrange
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        _qbittorrent.Intercept = request =>
        {
            if (request.RequestUri!.AbsolutePath != AddPath)
                return null;
            // The caller goes away while the answer is still arriving.
            cancellation.Cancel();
            return StalledAnswer();
        };

        //Act
        var exception = await Record.ExceptionAsync(() => Client.SubmitAsync(Saved, TorrentSubmission("Movies"), cancellation.Token));

        //Assert
        // Not a "timed out" result: nobody is waiting for one.
        Assert.IsAssignableFrom<OperationCanceledException>(exception);
        Assert.Single(_harness.QBittorrentHttp.Requests, request => request.Uri.AbsolutePath == AddPath);
    }

    [Fact]
    public async Task Submit_masks_the_password_and_the_session_in_its_error()
    {
        //Arrange
        // A redirect whose path repeats both: the one sentence of a submission that carries remote text.
        _qbittorrent.Intercept = request => request.RequestUri!.AbsolutePath == AddPath
            ? VendorResponses.Redirect($"{BaseUrl}/session/{SessionOf(request)}/{FakeQBittorrentServer.DefaultPassword}/?next=%2F")
            : null;

        //Act
        var result = await Client.SubmitAsync(Saved, TorrentSubmission("Movies"), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.UnexpectedResponse, result.Status);
        Assert.Equal(
            "The address answered with a redirect to http://qbittorrent.test:8080/session/***/***/. Enter the address the service is really at: the app doesn't follow redirects, so your credentials never go anywhere else.",
            result.Error);
    }

    [Fact]
    public async Task Submit_of_a_magnet_masks_its_trackers_address_in_its_error()
    {
        //Arrange
        // A redirect to the magnet's tracker, passkey and all: the link's addresses are masked like a session.
        _qbittorrent.Intercept = request => request.RequestUri!.AbsolutePath == AddPath
            ? VendorResponses.Redirect(TrackerAddress)
            : null;

        //Act
        var result = await Client.SubmitAsync(Saved, MagnetSubmission("Movies"), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.UnexpectedResponse, result.Status);
        Assert.Equal(
            "The address answered with a redirect to ***. Enter the address the service is really at: the app doesn't follow redirects, so your credentials never go anywhere else.",
            result.Error);
    }

    [Theory]
    [InlineData("redirect with the link")]
    [InlineData("transport failure")]
    [InlineData("server error")]
    [InlineData("refused")]
    public async Task Submit_of_a_magnet_never_puts_the_link_or_its_trackers_passkey_into_an_error(string failure)
    {
        //Arrange
        // A redirect drops its query, a status's sentence is the app's own, and an exception's text is never used.
        _qbittorrent.Intercept = request => request.RequestUri!.AbsolutePath != AddPath
            ? null
            : failure switch
            {
                "redirect with the link" => VendorResponses.Redirect($"{BaseUrl}/added?link={Uri.EscapeDataString(Magnet)}"),
                "transport failure" => throw new HttpRequestException(HttpRequestError.ConnectionError, $"Failed adding {Magnet} ({Passkey})"),
                "server error" => VendorResponses.Text($"Couldn't add {Magnet}", HttpStatusCode.InternalServerError),
                _ => VendorResponses.Text(Magnet, HttpStatusCode.Conflict),
            };

        //Act
        var result = await Client.SubmitAsync(Saved, MagnetSubmission("Movies"), TestContext.Current.CancellationToken);

        //Assert
        Assert.False(result.IsSuccess);
        Assert.DoesNotContain(Magnet, result.Error!, StringComparison.Ordinal);
        Assert.DoesNotContain(Passkey, result.Error!, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(CallerBugs))]
    public async Task Submit_of_what_qbittorrent_can_not_be_sent_throws_and_sends_nothing(string bug)
    {
        //Arrange
        var torrent = TorznabSamples.TorrentFile();
        var submission = bug switch
        {
            "nzb" => new DownloadSubmission(new ReleasePayload(ReleasePayloadKind.Nzb, torrent, null), "Movies", null),
            "torrent file without bytes" => new DownloadSubmission(new ReleasePayload(ReleasePayloadKind.TorrentFile, null, Magnet), "Movies", null),
            "torrent file with no bytes" => new DownloadSubmission(new ReleasePayload(ReleasePayloadKind.TorrentFile, [], null), "Movies", null),
            "magnet without a link" => new DownloadSubmission(new ReleasePayload(ReleasePayloadKind.Magnet, torrent, null), "Movies", null),
            "blank magnet" => new DownloadSubmission(new ReleasePayload(ReleasePayloadKind.Magnet, null, "  "), "Movies", null),
            "web link as a magnet" => new DownloadSubmission(
                new ReleasePayload(ReleasePayloadKind.Magnet, null, "http://tracker.test/download/1.torrent"), "Movies", null),
            // urls is one link per line: this would hand qBittorrent a second link.
            "magnet with a line break" => new DownloadSubmission(
                new ReleasePayload(ReleasePayloadKind.Magnet, null, Magnet + "\nhttp://tracker.test/other.torrent"), "Movies", null),
            "unknown payload kind" => new DownloadSubmission(new ReleasePayload((ReleasePayloadKind)99, torrent, Magnet), "Movies", null),
            "empty key" => TorrentSubmission(""),
            "blank key" => TorrentSubmission("   "),
            "key with a control character" => TorrentSubmission("Movies\n"),
            _ => TorrentSubmission(new string('k', 257)),
        };

        //Act
        var exception = await Record.ExceptionAsync(() => Client.SubmitAsync(Saved, submission, TestContext.Current.CancellationToken));

        //Assert
        Assert.IsType<ArgumentException>(exception);
        Assert.Empty(_harness.QBittorrentHttp.Requests);
    }

    [Fact]
    public async Task Submit_twice_at_once_adds_the_release_once_and_refuses_the_other()
    {
        //Arrange
        // Two clicks racing: qBittorrent adds a torrent once, whichever arrives second is refused as a duplicate.
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var results = await Task.WhenAll(
            Client.SubmitAsync(Saved, TorrentSubmission("Movies"), cancellationToken),
            Client.SubmitAsync(Saved, TorrentSubmission("Movies"), cancellationToken));

        //Assert
        Assert.Single(results, result => result.Status == IntegrationCallStatus.Succeeded && result.Value!.ClientJobId == SampleHash);
        Assert.Single(results, result => result.Status == IntegrationCallStatus.Refused && result.Error == AddedNothingError);
        Assert.Single(_qbittorrent.Torrents);
        Assert.Equal(2, _qbittorrent.AddRequests.Count);
    }

    private static DownloadSubmission TorrentSubmission(string destinationKey) =>
        new(TorrentPayload(TorznabSamples.TorrentFile()), destinationKey, SampleHash);

    private static DownloadSubmission MagnetSubmission(string destinationKey) =>
        new(new ReleasePayload(ReleasePayloadKind.Magnet, null, Magnet), destinationKey, MagnetHash);

    private static ReleasePayload TorrentPayload(byte[] torrent) => new(ReleasePayloadKind.TorrentFile, torrent, null);

    // A 200 whose body never finishes arriving: the pipe is never written to or completed.
    private static HttpResponseMessage StalledAnswer() =>
        new(HttpStatusCode.OK) { Content = new StreamContent(new Pipe().Reader.AsStream()) };

    /// <summary>The session id the request carries.</summary>
    private static string SessionOf(HttpRequestMessage request) =>
        request.Headers.TryGetValues("Cookie", out var values) ? values.Single()[(FakeQBittorrentServer.CookieName.Length + 1)..] : "";

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
