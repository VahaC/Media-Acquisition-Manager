using System.Net;
using System.Text.Json.Nodes;
using MediaAcquisitionManager.Core.Abstractions.Downloads;
using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Abstractions.Search;
using MediaAcquisitionManager.Core.Downloads;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Infrastructure.Integrations.Transmission;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Texts;

namespace MediaAcquisitionManager.Infrastructure.Tests.Integrations;

// MAM-30 — the one write (Vasyl's decision 4, ADR 0013): one torrent-add with exactly metainfo or filename, download-dir,
// labels [the destination's name] and paused false, on a session a session-get has just confirmed; sent once more only
// after a 409 (D8), never after anything Transmission read; a duplicate refused whatever the read after it gives (D9); a
// BitTorrent v2-only release refused without a request (D12); the caller's bugs thrown before anything is sent; every
// refusal of Transmission's quoted and masked. TransmissionAdapterJobsTests has the lookup a sender confirms with.
public sealed class TransmissionAdapterSubmitTests : IDisposable
{
    private const string MoviesFolder = "/downloads/movies";
    private const string Label = "Фільми";

    private const string DuplicateUnreadError = "Transmission already has this torrent, so it added nothing.";
    private const string SessionIdRefusedError = "Transmission refused the session id it had just sent.";
    private const string V2OnlyError = "Transmission can't take a torrent that is BitTorrent v2 only, which this release is, so nothing was sent.";

    /// <summary>A magnet whose tracker address carries the owner's passkey.</summary>
    private const string Magnet =
        "magnet:?xt=urn:btih:4a3c1931c85c9c568c39a8eb947ed382824b1cdd&dn=Interstellar.2014.1080p&tr=http%3A%2F%2Fbt.toloka.test%2Fa1b2c3d4e5f6a7b8%2Fannounce";

    private const string MagnetHash = "4a3c1931c85c9c568c39a8eb947ed382824b1cdd";
    private const string Passkey = "a1b2c3d4e5f6a7b8";

    private static readonly IntegrationConnectionSettings Saved = new(
        Guid.Parse("4d2c6a1e-3b5f-4e7a-9c8d-1f0e2a3b4c5d"),
        FakeTransmissionServer.DefaultBaseUrl,
        FakeTransmissionServer.DefaultUserName,
        FakeTransmissionServer.DefaultPassword,
        null);

    private static readonly byte[] Torrent = TestTorrent.V1(
        "Interstellar (2014)", [(["Interstellar (2014).mkv"], TestTorrent.Bytes(40_000, 7)), (["Subs", "uk.srt"], TestTorrent.Bytes(900, 9))]);

    private static readonly string TorrentHash = TorrentInfoHashReader.FromTorrentFile(Torrent).V1!;

    private readonly IntegrationAdapterHarness _harness = new();
    private readonly FakeTransmissionServer _transmission = new();

    public TransmissionAdapterSubmitTests()
    {
        _transmission.AddFolder(MoviesFolder);
        _transmission.Arm(_harness.TransmissionHttp);
    }

    /// <summary>Submissions the caller must never make: each row names what is wrong.</summary>
    public static TheoryData<string> CallerBugs => new()
    {
        "an nzb",
        "a torrent without bytes",
        "a magnet that is no magnet link",
        "a magnet with a line break",
        "a blank key",
        "a relative key",
        "a key with a line break",
        "a key longer than a destination holds",
        "no label",
        "a blank label",
        "a label with a comma",
    };

    /// <summary>Refusals Transmission answers a torrent-add with, HTTP 200 all the same.</summary>
    public static TheoryData<string> AddRefusals => new()
    {
        "unrecognized info",
        "download directory path is not absolute",
        "labels cannot contain comma (,) character",
        "No space left on device",
    };

    /// <summary>How the read after a duplicate may fail: each row names it. The last two fail on the way, as an exception
    /// the adapter must catch: passed on, the call would answer Unreachable or UnexpectedResponse, which the sender reads as
    /// "may have been taken".</summary>
    public static TheoryData<string> DuplicateReadFailures => new()
    {
        "503",
        "refused",
        "lists nothing",
        "lists another torrent",
        "no download folder",
        "not an envelope",
        "connection lost",
        "larger than the app reads",
    };

    private IDownloadClient Client => _harness.Adapter<IDownloadClient>(TransmissionAdapter.TypeId);

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task Submit_of_a_torrent_file_sends_one_torrent_add_with_exactly_the_metainfo_folder_label_and_paused_false()
    {
        //Arrange

        //Act
        var result = await Client.SubmitAsync(Saved, Submission(TorrentFile()), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        Assert.Equal(TorrentHash, result.Value!.ClientJobId);
        Assert.Equal(["session-get", "session-get", "torrent-add"], _harness.TransmissionHttp.Requests.Select(FakeTransmissionServer.Describe));
        var add = Assert.Single(_transmission.AddRequests);
        Assert.Equal(["metainfo", "download-dir", "labels", "paused"], add.ArgumentNames);
        Assert.Equal(ReleasePayloadKind.TorrentFile, add.Kind);
        Assert.Equal(Torrent, add.Metainfo);
        Assert.Equal(MoviesFolder, add.DownloadDir);
        Assert.Equal([Label], add.Labels);
        Assert.False(add.Paused);
        Assert.Equal(["torrent-add"], _transmission.Writes.Select(call => call.Method));
        var torrent = Assert.Single(_transmission.Torrents);
        Assert.Equal((MoviesFolder, 4), (torrent.DownloadDir, torrent.Status));
        Assert.Equal([Label], torrent.Labels);
    }

    [Fact]
    public async Task Submit_of_a_torrent_file_is_added_on_4_0_6_too()
    {
        //Arrange
        _transmission.Version = FakeTransmissionVersion.V4_0_6;

        //Act
        var result = await Client.SubmitAsync(Saved, Submission(TorrentFile()), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(TorrentHash, result.Value!.ClientJobId);
        Assert.Equal([Label], Assert.Single(_transmission.Torrents).Labels);
    }

    [Fact]
    public async Task Submit_of_a_magnet_sends_it_as_the_filename_and_names_the_job_by_its_hash()
    {
        //Arrange

        //Act
        var result = await Client.SubmitAsync(Saved, Submission(new ReleasePayload(ReleasePayloadKind.Magnet, null, Magnet)), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(MagnetHash, result.Value!.ClientJobId);
        var add = Assert.Single(_transmission.AddRequests);
        Assert.Equal(["filename", "download-dir", "labels", "paused"], add.ArgumentNames);
        Assert.Equal(ReleasePayloadKind.Magnet, add.Kind);
        Assert.Equal(Magnet, add.Filename);
    }

    [Fact]
    public async Task Submit_sends_the_folder_and_label_exactly_as_given()
    {
        //Arrange
        // A folder name with spaces, a label of the owner's script with spaces Transmission would trim itself.
        const string folder = "/downloads/Мультфільми  (4K)";
        _transmission.AddFolder(folder);

        //Act
        await Client.SubmitAsync(Saved, Submission(TorrentFile(), folder, " Мультфільми (4K) "), TestContext.Current.CancellationToken);

        //Assert
        var add = Assert.Single(_transmission.AddRequests);
        Assert.Equal(folder, add.DownloadDir);
        Assert.Equal([" Мультфільми (4K) "], add.Labels);
    }

    [Fact]
    public async Task Submit_whose_session_rotated_right_before_the_add_sends_the_add_once_more_with_the_new_id()
    {
        //Arrange
        // D8 — the 409 is answered before Transmission reads the body: the first add added nothing.
        _transmission.RpcIntercept = call =>
        {
            if (call.Method == "session-get")
                _transmission.RotateSession();
            return null;
        };

        //Act
        var result = await Client.SubmitAsync(Saved, Submission(TorrentFile()), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(TorrentHash, result.Value!.ClientJobId);
        Assert.Equal(["session-get", "session-get", "torrent-add", "torrent-add"], _harness.TransmissionHttp.Requests.Select(FakeTransmissionServer.Describe));
        Assert.Single(_transmission.AddRequests);
        Assert.Single(_transmission.Torrents);
    }

    [Fact]
    public async Task Submit_whose_add_is_answered_409_twice_is_refused_and_the_add_is_sent_no_third_time()
    {
        //Arrange
        _transmission.Intercept = (_, call) =>
        {
            if (call?.Method == "torrent-add")
                _transmission.RotateSession();
            return null;
        };

        //Act
        var result = await Client.SubmitAsync(Saved, Submission(TorrentFile()), TestContext.Current.CancellationToken);

        //Assert
        // Refused, never a status the sender reads as "may have been taken": Transmission read neither add.
        Assert.Equal(IntegrationCallStatus.Refused, result.Status);
        Assert.Equal(SessionIdRefusedError, TestTexts.En(result.Error));
        Assert.Equal(2, _harness.TransmissionHttp.Requests.Count(request => FakeTransmissionServer.Describe(request) == "torrent-add"));
        Assert.Empty(_transmission.AddRequests);
        Assert.Empty(_transmission.Torrents);
    }

    [Fact]
    public async Task Submit_whose_add_is_answered_409_without_a_usable_id_is_refused_and_not_sent_again()
    {
        //Arrange
        _transmission.Intercept = (_, call) => call?.Method == "torrent-add"
            ? new HttpResponseMessage(HttpStatusCode.Conflict)
            : null;

        //Act
        var result = await Client.SubmitAsync(Saved, Submission(TorrentFile()), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Refused, result.Status);
        Assert.Equal(SessionIdRefusedError, TestTexts.En(result.Error));
        Assert.Equal(1, _harness.TransmissionHttp.Requests.Count(request => FakeTransmissionServer.Describe(request) == "torrent-add"));
    }

    [Fact]
    public async Task Submit_of_a_torrent_transmission_already_has_is_refused_with_where_it_is_and_changes_nothing()
    {
        //Arrange
        // D9 — Transmission applies nothing of the add to the torrent it has (tr_torrentNew).
        var held = _transmission.AddTorrent(new TorrentInfoHashes(TorrentHash, null), "Interstellar (2014)", "/downloads/tv-shows", ["Серіали", "4K"]);

        //Act
        var result = await Client.SubmitAsync(Saved, Submission(TorrentFile()), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Refused, result.Status);
        Assert.Null(result.Value);
        Assert.Equal("Transmission already has this torrent, in “/downloads/tv-shows” with the label “Серіали, 4K”, so it added nothing.", TestTexts.En(result.Error));
        Assert.Equal(held, Assert.Single(_transmission.Torrents));
        var read = _transmission.Calls[^1];
        Assert.Equal("torrent-get", read.Method);
        Assert.Equal([TorrentHash], read.Arguments["ids"]!.AsArray().Select(id => (string)id!));
        Assert.Equal(["hashString", "downloadDir", "labels"], read.Arguments["fields"]!.AsArray().Select(field => (string)field!));
        Assert.Equal(["torrent-add"], _transmission.Writes.Select(call => call.Method));
    }

    [Fact]
    public async Task Submit_of_a_torrent_transmission_already_has_without_a_label_says_so()
    {
        //Arrange
        _transmission.AddTorrent(new TorrentInfoHashes(TorrentHash, null), "Interstellar (2014)");

        //Act
        var result = await Client.SubmitAsync(Saved, Submission(TorrentFile()), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal("Transmission already has this torrent, in “/downloads” with no label, so it added nothing.", TestTexts.En(result.Error));
    }

    [Theory]
    [MemberData(nameof(DuplicateReadFailures))]
    public async Task Submit_of_a_duplicate_whose_read_after_fails_is_still_refused_in_fewer_words(string failure)
    {
        //Arrange
        // D9 — whatever follows the duplicate, the add is refused: never a status the sender reads as "may have been taken",
        // which would adopt a torrent the app didn't add.
        _transmission.AddTorrent(new TorrentInfoHashes(TorrentHash, null), "Interstellar (2014)", "/downloads/tv-shows");
        _transmission.Intercept = (_, call) => call?.Method != "torrent-get" ? null : failure switch
        {
            "503" => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
            "not an envelope" => VendorResponses.Text("<html></html>", HttpStatusCode.OK, "text/html"),
            // Transmission went away right after it answered the add.
            "connection lost" => throw new HttpRequestException(HttpRequestError.ConnectionError, "No connection could be made because the target machine actively refused it."),
            _ => null,
        };
        _transmission.RpcIntercept = call => call.Method != "torrent-get" ? null : failure switch
        {
            "refused" => new JsonObject { ["arguments"] = new JsonObject(), ["result"] = "no fields specified" },
            "lists nothing" => new JsonObject { ["arguments"] = new JsonObject { ["torrents"] = new JsonArray() }, ["result"] = "success" },
            "lists another torrent" => TorrentsAnswer(new JsonObject { ["hashString"] = MagnetHash, ["downloadDir"] = "/downloads", ["labels"] = new JsonArray() }),
            "no download folder" => TorrentsAnswer(new JsonObject { ["hashString"] = TorrentHash, ["labels"] = new JsonArray() }),
            // Past the client's 4 MiB buffer limit, which ends the read in an exception before anything of it is read.
            "larger than the app reads" => TorrentsAnswer(new JsonObject
            {
                ["hashString"] = TorrentHash, ["downloadDir"] = "/downloads/tv-shows", ["labels"] = new JsonArray(new string('x', 5 * 1024 * 1024)),
            }),
            _ => null,
        };

        //Act
        var result = await Client.SubmitAsync(Saved, Submission(TorrentFile()), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Refused, result.Status);
        Assert.Equal(DuplicateUnreadError, TestTexts.En(result.Error));
        Assert.Single(_transmission.AddRequests);
    }

    [Fact]
    public async Task Submit_of_a_duplicate_whose_read_after_never_answers_is_refused_and_the_add_is_not_sent_again()
    {
        //Arrange
        using var harness = new IntegrationAdapterHarness(timeout: TimeSpan.FromMilliseconds(100));
        var transmission = new FakeTransmissionServer();
        transmission.AddFolder(MoviesFolder);
        transmission.AddTorrent(new TorrentInfoHashes(TorrentHash, null), "Interstellar (2014)");
        transmission.Intercept = (_, call) => call?.Method == "torrent-get" ? VendorResponses.Stalled() : null;
        transmission.Arm(harness.TransmissionHttp);

        //Act
        var result = await harness.Adapter<IDownloadClient>(TransmissionAdapter.TypeId)
            .SubmitAsync(Saved, Submission(TorrentFile()), TestContext.Current.CancellationToken);

        //Assert
        // Never TimedOut, which the sender reads as "may have been taken" and would adopt the torrent Transmission already had.
        Assert.Equal(IntegrationCallStatus.Refused, result.Status);
        Assert.Equal(DuplicateUnreadError, TestTexts.En(result.Error));
        Assert.Single(transmission.AddRequests);
    }

    [Theory]
    [MemberData(nameof(AddRefusals))]
    public async Task Submit_that_transmission_refuses_quotes_its_result_and_added_nothing(string refusal)
    {
        //Arrange
        _transmission.RpcIntercept = call => call.Method == "torrent-add"
            ? new JsonObject { ["arguments"] = new JsonObject(), ["result"] = refusal }
            : null;

        //Act
        var result = await Client.SubmitAsync(Saved, Submission(TorrentFile()), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Refused, result.Status);
        Assert.Equal($"Transmission refused the torrent: {refusal}.", TestTexts.En(result.Error));
        Assert.Single(_transmission.AddRequests);
    }

    [Fact]
    public async Task Submit_of_bytes_that_are_no_torrent_is_refused_as_transmission_says()
    {
        //Arrange
        // The recorded answer to the base64 of "not a torrent".
        var payload = new ReleasePayload(ReleasePayloadKind.TorrentFile, "not a torrent"u8.ToArray(), null);

        //Act
        var result = await Client.SubmitAsync(Saved, Submission(payload), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Refused, result.Status);
        Assert.Equal("Transmission refused the torrent: unrecognized info.", TestTexts.En(result.Error));
        Assert.Empty(_transmission.Torrents);
    }

    [Fact]
    public async Task Submit_of_a_torrent_that_is_bittorrent_v2_only_is_refused_without_a_request()
    {
        //Arrange
        // D12 — Transmission 4.0 and 4.1 can't take one ("unrecognized info"); the sender refuses it first, in its own words.
        var torrent = TestTorrent.V2("Interstellar (2014)", [(["Interstellar (2014).mkv"], TestTorrent.Bytes(40_000, 7))]);

        //Act
        var result = await Client.SubmitAsync(
            Saved, Submission(new ReleasePayload(ReleasePayloadKind.TorrentFile, torrent, null)), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Refused, result.Status);
        Assert.Equal(V2OnlyError, TestTexts.En(result.Error));
        Assert.Empty(_harness.TransmissionHttp.Requests);
    }

    [Fact]
    public async Task Submit_of_a_magnet_that_names_only_a_v2_hash_is_refused_without_a_request()
    {
        //Arrange
        const string magnet = "magnet:?xt=urn:btmh:1220caf1e1c30e81cb361b9ee167c4aa64228a7fa4fa9f6105232b28ad099f3a302e&dn=x";

        //Act
        var result = await Client.SubmitAsync(Saved, Submission(new ReleasePayload(ReleasePayloadKind.Magnet, null, magnet)), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(V2OnlyError, TestTexts.En(result.Error));
        Assert.Empty(_harness.TransmissionHttp.Requests);
    }

    [Fact]
    public async Task Submit_of_a_hybrid_magnet_is_sent_and_named_by_its_v1_hash()
    {
        //Arrange
        const string magnet = $"magnet:?xt=urn:btih:{MagnetHash}&xt=urn:btmh:1220caf1e1c30e81cb361b9ee167c4aa64228a7fa4fa9f6105232b28ad099f3a302e";

        //Act
        var result = await Client.SubmitAsync(Saved, Submission(new ReleasePayload(ReleasePayloadKind.Magnet, null, magnet)), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(MagnetHash, result.Value!.ClientJobId);
    }

    [Theory]
    [MemberData(nameof(CallerBugs))]
    public async Task Submit_of_what_the_caller_must_never_send_throws_before_anything_is_sent(string bug)
    {
        //Arrange
        var submission = bug switch
        {
            "an nzb" => Submission(new ReleasePayload(ReleasePayloadKind.Nzb, [1, 2, 3], null)),
            "a torrent without bytes" => Submission(new ReleasePayload(ReleasePayloadKind.TorrentFile, [], null)),
            "a magnet that is no magnet link" => Submission(new ReleasePayload(ReleasePayloadKind.Magnet, null, "/config/watch/release.torrent")),
            "a magnet with a line break" => Submission(new ReleasePayload(ReleasePayloadKind.Magnet, null, Magnet + "\nmagnet:?xt=urn:btih:" + new string('0', 40))),
            "a blank key" => Submission(TorrentFile(), " "),
            "a relative key" => Submission(TorrentFile(), "downloads/movies"),
            "a key with a line break" => Submission(TorrentFile(), "/downloads/movies\n"),
            "a key longer than a destination holds" => Submission(TorrentFile(), "/" + new string('m', 256)),
            "no label" => Submission(TorrentFile()) with { Label = null },
            "a blank label" => Submission(TorrentFile(), label: "  "),
            _ => Submission(TorrentFile(), label: "Movies, 4K"),
        };

        //Act
        var thrown = await Record.ExceptionAsync(() => Client.SubmitAsync(Saved, submission, TestContext.Current.CancellationToken));

        //Assert
        Assert.Equal("submission", Assert.IsType<ArgumentException>(thrown).ParamName);
        Assert.Empty(_harness.TransmissionHttp.Requests);
    }

    [Fact]
    public async Task Submit_masks_the_magnet_and_its_trackers_passkey_in_what_transmission_echoes()
    {
        //Arrange
        _transmission.RpcIntercept = call => call.Method == "torrent-add"
            ? new JsonObject { ["arguments"] = new JsonObject(), ["result"] = $"couldn't fetch {Magnet} from http://bt.toloka.test/{Passkey}/announce" }
            : null;

        //Act
        var result = await Client.SubmitAsync(Saved, Submission(new ReleasePayload(ReleasePayloadKind.Magnet, null, Magnet)), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal("Transmission refused the torrent: couldn't fetch *** from ***.", TestTexts.En(result.Error));
        Assert.DoesNotContain(Passkey, TestTexts.En(result.Error), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Submit_whose_answer_never_comes_is_timed_out_and_not_sent_again()
    {
        //Arrange
        using var harness = new IntegrationAdapterHarness(timeout: TimeSpan.FromMilliseconds(100));
        var transmission = new FakeTransmissionServer();
        transmission.Intercept = (_, call) => call?.Method == "torrent-add" ? VendorResponses.Stalled() : null;
        transmission.Arm(harness.TransmissionHttp);

        //Act
        var result = await harness.Adapter<IDownloadClient>(TransmissionAdapter.TypeId)
            .SubmitAsync(Saved, Submission(TorrentFile()), TestContext.Current.CancellationToken);

        //Assert
        // Transmission may have added it all the same: the sender looks the hash up rather than sending it twice.
        Assert.Equal(IntegrationCallStatus.TimedOut, result.Status);
        Assert.Equal("Transmission at http://transmission.test:9091 didn't answer within 0.1 seconds.", TestTexts.En(result.Error));
        Assert.Equal(1, harness.TransmissionHttp.Requests.Count(request => FakeTransmissionServer.Describe(request) == "torrent-add"));
    }

    [Fact]
    public async Task Submit_cancelled_by_the_caller_throws_and_is_not_sent_again()
    {
        //Arrange
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        _transmission.Intercept = (_, call) =>
        {
            if (call?.Method != "torrent-add")
                return null;
            cancellation.CancelAfter(TimeSpan.FromMilliseconds(50));
            return VendorResponses.Stalled();
        };

        //Act
        var thrown = await Record.ExceptionAsync(() => Client.SubmitAsync(Saved, Submission(TorrentFile()), cancellation.Token));

        //Assert
        Assert.IsAssignableFrom<OperationCanceledException>(thrown);
        Assert.Equal(1, _harness.TransmissionHttp.Requests.Count(request => FakeTransmissionServer.Describe(request) == "torrent-add"));
    }

    [Fact]
    public async Task Submit_whose_success_names_neither_an_added_torrent_nor_a_duplicate_is_an_unexpected_response()
    {
        //Arrange
        // It may have been added all the same: the sender looks the hash up.
        _transmission.RpcIntercept = call => call.Method == "torrent-add"
            ? new JsonObject { ["arguments"] = new JsonObject(), ["result"] = "success" }
            : null;

        //Act
        var result = await Client.SubmitAsync(Saved, Submission(TorrentFile()), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.UnexpectedResponse, result.Status);
        Assert.Equal("The service at this address answered, but not the way Transmission answers a new torrent.", TestTexts.En(result.Error));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("4a3c1931")]
    [InlineData("4A3C1931C85C9C568C39A8EB947ED382824B1CDD")]
    public async Task Submit_takes_the_added_torrents_hash_lower_case_or_answers_without_an_id(string? hashString)
    {
        //Arrange
        _transmission.RpcIntercept = call => call.Method == "torrent-add"
            ? new JsonObject
            {
                ["arguments"] = new JsonObject { ["torrent-added"] = new JsonObject { ["hashString"] = hashString, ["id"] = 1, ["name"] = "x" } },
                ["result"] = "success",
            }
            : null;

        //Act
        var result = await Client.SubmitAsync(Saved, Submission(TorrentFile()), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        Assert.Equal(hashString?.Length == 40 ? MagnetHash : null, result.Value!.ClientJobId);
    }

    [Theory]
    [InlineData("old version")]
    [InlineData("temporary folder on")]
    public async Task Submit_refused_by_the_session_it_opens_sends_no_add(string refusal)
    {
        //Arrange
        if (refusal == "old version")
            _transmission.Version = FakeTransmissionVersion.V3_00;
        else
            _transmission.IncompleteDirEnabled = true;

        //Act
        var result = await Client.SubmitAsync(Saved, Submission(TorrentFile()), TestContext.Current.CancellationToken);

        //Assert
        Assert.False(result.IsSuccess);
        Assert.Empty(_transmission.AddRequests);
    }

    [Fact]
    public async Task Submit_whose_credentials_were_refused_before_sends_nothing()
    {
        //Arrange
        var wrong = Saved with { Password = "wrong-horse-battery" };
        await Client.FindJobsAsync(wrong, [MagnetHash], TestContext.Current.CancellationToken);

        //Act
        var result = await Client.SubmitAsync(wrong, Submission(TorrentFile()), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.AuthenticationFailed, result.Status);
        Assert.Single(_harness.TransmissionHttp.Requests);
    }

    private static ReleasePayload TorrentFile() => new(ReleasePayloadKind.TorrentFile, Torrent, null);

    private static DownloadSubmission Submission(ReleasePayload payload, string folder = MoviesFolder, string label = Label) =>
        new(payload, folder, null, label);

    private static JsonObject TorrentsAnswer(JsonObject torrent) =>
        new() { ["arguments"] = new JsonObject { ["torrents"] = new JsonArray(torrent) }, ["result"] = "success" };
}
