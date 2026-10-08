using System.Globalization;
using System.IO.Pipelines;
using System.Net;
using System.Text.Json.Nodes;
using MediaAcquisitionManager.Core.Abstractions.Downloads;
using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Abstractions.Search;
using MediaAcquisitionManager.Core.Downloads;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Infrastructure.Integrations.QBittorrent;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;

namespace MediaAcquisitionManager.Infrastructure.Tests.Integrations;

// MAM-13 — the torrents qBittorrent lists under the info hashes the app worked out from a payload: the
// session and version read first, then one GET api/v2/torrents/info?hashes=… of TorrentIDs (a v2 hash cut to
// 40 characters) joined by | and escaped; the answer read as an allowlist against the real one of the
// reference installation (VendorSamples/QBittorrent/torrents-info.json, 5.2.3), each torrent with every hash
// qBittorrent lists for it, its state mapped, its name cleaned and masked, and every failure answered like the
// destinations answer it.
public sealed class QBittorrentAdapterJobsTests : IDisposable
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

    /// <summary>A v2 info hash: 64 hexadecimal characters, named by qBittorrent by its first 40.</summary>
    private const string V2Hash = "e7f2a1c5b3d94e8f0a6c2b1d4e3f5a7c9b8d0e1f2a3b4c5d6e7f8a9b0c1d2e3f";

    private const string V2TorrentId = "e7f2a1c5b3d94e8f0a6c2b1d4e3f5a7c9b8d0e1f";

    private static readonly IntegrationConnectionSettings Saved = new(
        Guid.Parse("3c9e1a7b-2f4d-4b6a-8e1c-5d7f9a0b2c4e"), BaseUrl, FakeQBittorrentServer.DefaultUserName, FakeQBittorrentServer.DefaultPassword, null);

    private readonly IntegrationAdapterHarness _harness = new();
    private readonly FakeQBittorrentServer _qbittorrent = new();

    public QBittorrentAdapterJobsTests() => _qbittorrent.Arm(_harness.QBittorrentHttp);

    /// <summary>Bodies that answer 200 to torrents/info but are not qBittorrent's array of torrents.</summary>
    public static TheoryData<string> NotTorrents =>
    [
        "",
        "<!DOCTYPE html><html><body>qBittorrent Web UI</body></html>",
        "[{",
        "null",
        "{}",
        """{"1ec461458feffe9f27ae17e30d0a804e2ab0a88f":{"name":"Taken"}}""",
        "\"torrents\"",
        "[1]",
        "[null]",
        "[[]]",
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
        {
            HttpRequestError.ResponseEnded, IntegrationCallStatus.Unreachable,
            "Couldn't reach qBittorrent at http://qbittorrent.test:8080: the connection failed before it answered."
        },
    };

    private IDownloadClient Client => _harness.Adapter<IDownloadClient>(QBittorrentAdapter.TypeId);

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task FindJobs_of_no_hashes_finds_none_and_sends_nothing()
    {
        //Arrange

        //Act
        var result = await Client.FindJobsAsync(Saved, [], TestContext.Current.CancellationToken);

        //Assert
        // Never torrents/info without hashes: that lists every torrent of the client.
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        Assert.Empty(result.Value!);
        Assert.Empty(_harness.QBittorrentHttp.Requests);
    }

    [Theory]
    [InlineData("")]
    [InlineData("1ec461458feffe9f27ae17e30d0a804e2ab0a88")]
    [InlineData("1ec461458feffe9f27ae17e30d0a804e2ab0a88f0")]
    [InlineData("e7f2a1c5b3d94e8f0a6c2b1d4e3f5a7c9b8d0e1f2a3b4c5d6e7f8a9b0c1d2e3")]
    [InlineData("e7f2a1c5b3d94e8f0a6c2b1d4e3f5a7c9b8d0e1f2a3b4c5d6e7f8a9b0c1d2e3f0")]
    [InlineData("1ec461458feffe9f27ae17e30d0a804e2ab0a88g")]
    [InlineData(" 1ec461458feffe9f27ae17e30d0a804e2ab0a88")]
    [InlineData("١ec461458feffe9f27ae17e30d0a804e2ab0a88f")]
    [InlineData("1ec461458feffe9f27ae17e30d0a804e2ab0a88f|4a3c1931c85c9c568c39a8eb947ed382824b1cd")]
    [InlineData(null)]
    public async Task FindJobs_with_a_hash_that_is_not_an_info_hash_throws_and_sends_nothing(string? hash)
    {
        //Arrange
        // ASCII hexadecimal only: an Arabic-Indic digit is a digit, and a | would add a hash of its own.
        IReadOnlyCollection<string> hashes = [MovieHash, hash!];

        //Act
        var exception = await Record.ExceptionAsync(() => Client.FindJobsAsync(Saved, hashes, TestContext.Current.CancellationToken));

        //Assert
        Assert.IsType<ArgumentException>(exception);
        Assert.Empty(_harness.QBittorrentHttp.Requests);
    }

    [Fact]
    public async Task FindJobs_signs_in_reads_the_version_and_asks_for_the_hashes_with_the_session()
    {
        //Arrange
        _qbittorrent.AddTorrent(new TorrentInfoHashes(PackHash, null), "Taken. Trilogy", "Movies");

        //Act
        var result = await Client.FindJobsAsync(Saved, [PackHash, MovieHash], TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        Assert.Equal(PackHash, Assert.Single(result.Value!).ClientJobId);
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
            info =>
            {
                Assert.Equal(HttpMethod.Get, info.Method);
                // TorrentIDs joined by | — escaped, like every query value the app sends.
                Assert.Equal($"{BaseUrl}{InfoPath}?hashes={PackHash}%7C{MovieHash}", info.Uri.AbsoluteUri);
                Assert.Equal(_harness.QBittorrentHttp.Requests[1].HeaderValues("Cookie"), info.HeaderValues("Cookie"));
                Assert.Null(info.Body);
            });
    }

    [Fact]
    public async Task FindJobs_sends_each_torrent_id_once_in_lower_case_and_a_v2_hash_cut_to_40_characters()
    {
        //Arrange
        IReadOnlyCollection<string> hashes = [PackHash.ToUpperInvariant(), V2Hash, PackHash, V2Hash.ToUpperInvariant(), V2TorrentId];

        //Act
        var result = await Client.FindJobsAsync(Saved, hashes, TestContext.Current.CancellationToken);

        //Assert
        // qBittorrent reads hashes as TorrentIDs: a 64-character hash would name no torrent at all.
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        var info = Assert.Single(_harness.QBittorrentHttp.Requests, request => request.Uri.AbsolutePath == InfoPath);
        Assert.Equal($"?hashes={PackHash}%7C{V2TorrentId}", info.Uri.Query);
    }

    [Fact]
    public async Task FindJobs_reads_the_four_torrents_of_the_sample_with_their_states_mapped()
    {
        //Arrange
        _qbittorrent.Intercept = request => request.RequestUri!.AbsolutePath == InfoPath
            ? VendorResponses.Sample("QBittorrent/torrents-info.json", "application/json")
            : null;

        //Act
        var result = await Client.FindJobsAsync(Saved, [PackHash], TestContext.Current.CancellationToken);

        //Assert
        // stalledUP and queuedUP are both seeding; the trackers, comment and magnet link go no further.
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        Assert.Null(result.Error);
        Assert.Equal(
            [
                "1ec461458feffe9f27ae17e30d0a804e2ab0a88f | Taken. Trilogy [Extended] (2008-2014) BDRemux 1080p [Hurtom] | Seeding | 1 | Movies"
                + " | /downloads/movies/Taken. Trilogy [Extended] (2008-2014) BDRemux 1080p [Hurtom] | 1ec461458feffe9f27ae17e30d0a804e2ab0a88f",
                "4a3c1931c85c9c568c39a8eb947ed382824b1cdd | Im Westen nichts Neues (2022) BDRip 1080p H.265 [Ukr, Ger][ExTerminus].mkv | Seeding | 1 | Movies"
                + " | /downloads/movies/Im Westen nichts Neues (2022) BDRip 1080p H.265 [Ukr, Ger][ExTerminus].mkv | 4a3c1931c85c9c568c39a8eb947ed382824b1cdd",
                "e40455cf36e8520a156762368034a98b91ddad5c | the.grand.tour.s05e03.1080p V odne rilo.mkv | Seeding | 1 | TV Shows"
                + " | /downloads/tv-shows/the.grand.tour.s05e03.1080p V odne rilo.mkv | e40455cf36e8520a156762368034a98b91ddad5c",
                "93df2b76a73841bb9d0bedab5512733af2c37c5d | Monsters, Inc. (2001) BDRip 1080p x265 [UKR_ENG] [Hurtom].mkv | Seeding | 1 | Cartoons"
                + " | /downloads/cartoons/Monsters, Inc. (2001) BDRip 1080p x265 [UKR_ENG] [Hurtom].mkv | 93df2b76a73841bb9d0bedab5512733af2c37c5d",
            ],
            Describe(result.Value!));
    }

    [Fact]
    public async Task FindJobs_finds_a_v1_a_v2_and_a_hybrid_torrent_each_with_every_hash_qbittorrent_lists()
    {
        //Arrange
        const string hybridV1 = "93df2b76a73841bb9d0bedab5512733af2c37c5d";
        const string hybridV2 = "0a1b2c3d4e5f60718293a4b5c6d7e8f90a1b2c3d4e5f60718293a4b5c6d7e8f9";
        _qbittorrent.AddTorrent(new TorrentInfoHashes(PackHash, null), "v1", "Movies");
        _qbittorrent.AddTorrent(new TorrentInfoHashes(null, V2Hash), "v2", "Movies");
        _qbittorrent.AddTorrent(new TorrentInfoHashes(hybridV1, hybridV2), "hybrid", "Movies");
        _qbittorrent.AddTorrent(new TorrentInfoHashes(MovieHash, null), "someone else's", "Movies");

        //Act
        // Every hash the payload has, as the caller passes them: qBittorrent names a hybrid by its v2 hash cut
        // to 40 characters on libtorrent 2, by its v1 hash on 1.2.
        var result = await Client.FindJobsAsync(Saved, [PackHash, V2Hash, hybridV1, hybridV2], TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        Assert.Equal(
            [
                $"{PackHash} | {PackHash}",
                $"{V2TorrentId} | {V2TorrentId},{V2Hash}",
                $"{hybridV2[..40]} | {hybridV2[..40]},{hybridV1},{hybridV2}",
            ],
            result.Value!.Select(job => $"{job.ClientJobId} | {string.Join(',', job.InfoHashes)}"));
        Assert.Equal(
            $"?hashes={PackHash}%7C{V2TorrentId}%7C{hybridV1}%7C{hybridV2[..40]}",
            _harness.QBittorrentHttp.Requests[^1].Uri.Query);
    }

    [Fact]
    public async Task FindJobs_of_hashes_qbittorrent_does_not_have_finds_none()
    {
        //Arrange
        _qbittorrent.AddTorrent(new TorrentInfoHashes(MovieHash, null), "Im Westen nichts Neues", "Movies");

        //Act
        var result = await Client.FindJobsAsync(Saved, [PackHash], TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        Assert.Empty(result.Value!);
    }

    [Fact]
    public async Task FindJobs_after_a_submission_qbittorrent_has_not_listed_yet_finds_it_on_a_later_lookup()
    {
        //Arrange
        // qBittorrent answers the add before libtorrent has taken the torrent: the first lookup may miss it.
        var cancellationToken = TestContext.Current.CancellationToken;
        _qbittorrent.AddedTorrentsUnlistedForLookups = 1;
        var torrent = TorznabSamples.TorrentFile();
        var hashes = TorrentInfoHashReader.FromTorrentFile(torrent);
        await Client.SubmitAsync(
            Saved, new DownloadSubmission(new ReleasePayload(ReleasePayloadKind.TorrentFile, torrent, null), "Movies", hashes.Primary), cancellationToken);

        //Act
        var first = await Client.FindJobsAsync(Saved, hashes.All, cancellationToken);
        var second = await Client.FindJobsAsync(Saved, hashes.All, cancellationToken);

        //Assert
        Assert.Empty(first.Value!);
        var job = Assert.Single(second.Value!);
        Assert.Equal(hashes.V1, job.ClientJobId);
        Assert.Equal(ClientJobState.Downloading, job.State);
        Assert.Equal(0d, job.Progress);
        Assert.Equal("Movies", job.DestinationKey);
        Assert.Equal([hashes.V1!], job.InfoHashes);
    }

    [Theory]
    [InlineData("error", ClientJobState.Errored)]
    [InlineData("missingFiles", ClientJobState.Errored)]
    [InlineData("uploading", ClientJobState.Seeding)]
    [InlineData("stalledUP", ClientJobState.Seeding)]
    [InlineData("forcedUP", ClientJobState.Seeding)]
    [InlineData("queuedUP", ClientJobState.Seeding)]
    [InlineData("checkingUP", ClientJobState.Seeding)]
    [InlineData("pausedUP", ClientJobState.Completed)]
    [InlineData("stoppedUP", ClientJobState.Completed)]
    [InlineData("downloading", ClientJobState.Downloading)]
    [InlineData("stalledDL", ClientJobState.Downloading)]
    [InlineData("forcedDL", ClientJobState.Downloading)]
    [InlineData("metaDL", ClientJobState.Downloading)]
    [InlineData("forcedMetaDL", ClientJobState.Downloading)]
    [InlineData("queuedDL", ClientJobState.Queued)]
    [InlineData("checkingDL", ClientJobState.Queued)]
    [InlineData("allocating", ClientJobState.Queued)]
    [InlineData("checkingResumeData", ClientJobState.Queued)]
    [InlineData("pausedDL", ClientJobState.Paused)]
    [InlineData("stoppedDL", ClientJobState.Paused)]
    [InlineData("moving", ClientJobState.Unknown)]
    [InlineData("unknown", ClientJobState.Unknown)]
    [InlineData("StalledUP", ClientJobState.Unknown)]
    [InlineData("", ClientJobState.Unknown)]
    public async Task FindJobs_maps_each_qbittorrent_state_to_the_cores(string state, ClientJobState expected)
    {
        //Arrange
        AnswerTorrents(Torrent(PackHash, state: state));

        //Act
        var result = await Client.FindJobsAsync(Saved, [PackHash], TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(expected, Assert.Single(result.Value!).State);
    }

    [Fact]
    public async Task FindJobs_of_a_torrent_without_a_state_as_text_is_unknown()
    {
        //Arrange
        var torrent = Torrent(PackHash);
        torrent["state"] = 5;
        AnswerTorrents(torrent, Torrent(MovieHash, state: null));

        //Act
        var result = await Client.FindJobsAsync(Saved, [PackHash, MovieHash], TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal([ClientJobState.Unknown, ClientJobState.Unknown], result.Value!.Select(job => job.State));
    }

    [Theory]
    [MemberData(nameof(NotTorrents))]
    public async Task FindJobs_of_a_body_that_is_not_qbittorrents_torrents_is_an_unexpected_response(string body)
    {
        //Arrange
        _qbittorrent.Intercept = request => request.RequestUri!.AbsolutePath == InfoPath
            ? VendorResponses.Text(body, HttpStatusCode.OK, "application/json")
            : null;

        //Act
        var result = await Client.FindJobsAsync(Saved, [PackHash], TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.UnexpectedResponse, result.Status);
        Assert.Equal(NotTorrentsError, result.Error);
        Assert.Null(result.Value);
    }

    [Fact]
    public async Task FindJobs_leaves_out_a_torrent_without_a_usable_hash()
    {
        //Arrange
        var numberHash = Torrent(PackHash);
        numberHash["hash"] = 42;
        var noHash = Torrent(PackHash);
        noHash.Remove("hash");
        AnswerTorrents(
            noHash,
            numberHash,
            Torrent(""),
            Torrent(PackHash[..39]),
            Torrent(PackHash + "0"),
            Torrent("1ec461458feffe9f27ae17e30d0a804e2ab0a88g"),
            Torrent(MovieHash.ToUpperInvariant(), name: "Kept"));

        //Act
        var result = await Client.FindJobsAsync(Saved, [PackHash, MovieHash], TestContext.Current.CancellationToken);

        //Assert
        // The rest stays usable: a job is followed by its id, and one without can't be. Lower-case, as the
        // core compares hashes.
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        var job = Assert.Single(result.Value!);
        Assert.Equal(MovieHash, job.ClientJobId);
        Assert.Equal("Kept", job.Name);
    }

    [Fact]
    public async Task FindJobs_takes_a_64_character_hash_as_the_id_qbittorrent_gave()
    {
        //Arrange
        AnswerTorrents(Torrent(V2Hash, v1: "", v2: V2Hash));

        //Act
        var result = await Client.FindJobsAsync(Saved, [V2Hash], TestContext.Current.CancellationToken);

        //Assert
        var job = Assert.Single(result.Value!);
        Assert.Equal(V2Hash, job.ClientJobId);
        Assert.Equal([V2Hash], job.InfoHashes);
    }

    [Theory]
    [InlineData("", "", "1ec461458feffe9f27ae17e30d0a804e2ab0a88f")]
    [InlineData("1EC461458FEFFE9F27AE17E30D0A804E2AB0A88F", "", "1ec461458feffe9f27ae17e30d0a804e2ab0a88f")]
    [InlineData("4a3c1931c85c9c568c39a8eb947ed382824b1cdd", "", "1ec461458feffe9f27ae17e30d0a804e2ab0a88f,4a3c1931c85c9c568c39a8eb947ed382824b1cdd")]
    [InlineData("1ec461458feffe9f27ae17e30d0a804e2ab0a88", "1ec461458feffe9f27ae17e30d0a804e2ab0a88f", "1ec461458feffe9f27ae17e30d0a804e2ab0a88f")]
    [InlineData("zz", "e7f2a1c5b3d94e8f0a6c2b1d4e3f5a7c9b8d0e1f2a3b4c5d6e7f8a9b0c1d2e3g", "1ec461458feffe9f27ae17e30d0a804e2ab0a88f")]
    [InlineData("", "E7F2A1C5B3D94E8F0A6C2B1D4E3F5A7C9B8D0E1F2A3B4C5D6E7F8A9B0C1D2E3F",
        "1ec461458feffe9f27ae17e30d0a804e2ab0a88f,e7f2a1c5b3d94e8f0a6c2b1d4e3f5a7c9b8d0e1f2a3b4c5d6e7f8a9b0c1d2e3f")]
    public async Task FindJobs_lists_each_hash_once_in_lower_case_and_only_a_well_formed_one(string v1, string v2, string expectedHashes)
    {
        //Arrange
        // 5.2 lists a hash a torrent doesn't have as "".
        AnswerTorrents(Torrent(PackHash, v1: v1, v2: v2));

        //Act
        var result = await Client.FindJobsAsync(Saved, [PackHash], TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(expectedHashes, string.Join(',', Assert.Single(result.Value!).InfoHashes));
    }

    [Fact]
    public async Task FindJobs_cleans_the_name_and_masks_the_password_and_the_session_in_it()
    {
        //Arrange
        // A right-to-left override, a bell, a line break with indentation: none reach a sentence or a row.
        _qbittorrent.Intercept = request => request.RequestUri!.AbsolutePath == InfoPath
            ? Answer(Torrent(PackHash, name: $"Movie {FakeQBittorrentServer.DefaultPassword} ‮{SessionOf(request)}\u0007\r\n  part 2"))
            : null;

        //Act
        var result = await Client.FindJobsAsync(Saved, [PackHash], TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal("Movie *** *** part 2", Assert.Single(result.Value!).Name);
    }

    [Fact]
    public async Task FindJobs_caps_a_long_name_and_reads_a_missing_one_as_empty()
    {
        //Arrange
        var noName = Torrent(MovieHash);
        noName.Remove("name");
        AnswerTorrents(Torrent(PackHash, name: new string('n', 1500)), noName);

        //Act
        var result = await Client.FindJobsAsync(Saved, [PackHash, MovieHash], TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal([new string('n', 999) + "…", ""], result.Value!.Select(job => job.Name));
    }

    [Theory]
    [InlineData("0", 0.0)]
    [InlineData("0.25", 0.25)]
    [InlineData("1", 1.0)]
    [InlineData("-0.01", null)]
    [InlineData("1.0001", null)]
    [InlineData("1e400", null)]
    [InlineData("\"0.5\"", null)]
    [InlineData("null", null)]
    [InlineData("true", null)]
    public async Task FindJobs_reads_the_progress_only_as_a_number_from_0_to_1(string progress, double? expected)
    {
        //Arrange
        var torrent = Torrent(PackHash);
        torrent["progress"] = JsonNode.Parse(progress);
        AnswerTorrents(torrent);

        //Act
        var result = await Client.FindJobsAsync(Saved, [PackHash], TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(expected, Assert.Single(result.Value!).Progress);
    }

    [Fact]
    public async Task FindJobs_reads_the_category_and_the_content_path_exactly_and_an_empty_one_as_none()
    {
        //Arrange
        var noCategory = Torrent(MovieHash, category: "", contentPath: "");
        noCategory.Remove("content_path");
        AnswerTorrents(
            Torrent(PackHash, category: " Фільми 4K ", contentPath: "/downloads/movies-4k/Taken (2008) "),
            noCategory,
            Torrent("e40455cf36e8520a156762368034a98b91ddad5c", category: "", contentPath: ""));

        //Act
        var result = await Client.FindJobsAsync(
            Saved, [PackHash, MovieHash, "e40455cf36e8520a156762368034a98b91ddad5c"], TestContext.Current.CancellationToken);

        //Assert
        // The category is matched against a destination's key, and the path names a folder: neither is
        // trimmed. "" is qBittorrent's "no category", and the content path before a magnet's metadata arrives.
        Assert.Equal(
            [
                " Фільми 4K  | /downloads/movies-4k/Taken (2008) ",
                "(none) | (none)",
                "(none) | (none)",
            ],
            result.Value!.Select(job => $"{job.DestinationKey ?? "(none)"} | {job.ServiceContentPath ?? "(none)"}"));
    }

    [Fact]
    public async Task FindJobs_reads_the_save_path_exactly_apart_from_the_content_path_and_an_empty_or_missing_one_as_none()
    {
        //Arrange
        var noSavePath = Torrent(MovieHash);
        noSavePath.Remove("save_path");
        AnswerTorrents(
            Torrent(PackHash, contentPath: "/downloads/incomplete/Taken (2008)", savePath: "/downloads/movies-4k "),
            noSavePath,
            Torrent("e40455cf36e8520a156762368034a98b91ddad5c", savePath: ""));

        //Act
        var result = await Client.FindJobsAsync(
            Saved, [PackHash, MovieHash, "e40455cf36e8520a156762368034a98b91ddad5c"], TestContext.Current.CancellationToken);

        //Assert
        // MAM-38 — where the files end up, while the content path follows an unfinished torrent into the folder for
        // incomplete torrents; it names a folder, so it isn't trimmed either.
        Assert.Equal(
            [
                "/downloads/incomplete/Taken (2008) | /downloads/movies-4k ",
                "/downloads/movies/Taken (2008) | (none)",
                "/downloads/movies/Taken (2008) | (none)",
            ],
            result.Value!.Select(job => $"{job.ServiceContentPath ?? "(none)"} | {job.ServiceSavePath ?? "(none)"}"));
    }

    [Theory]
    [InlineData(404, IntegrationCallStatus.UnexpectedResponse,
        "qBittorrent's Web API wasn't found at this address (404 Not Found). Use the address of qBittorrent's Web UI, such as http://192.168.1.10:8080.")]
    [InlineData(500, IntegrationCallStatus.ServiceError, "qBittorrent answered 500 Internal Server Error. Its own log says why.")]
    [InlineData(401, IntegrationCallStatus.UnexpectedResponse,
        "qBittorrent answered 401 Unauthorized, which it doesn't send for this request. Check the address.")]
    [InlineData(204, IntegrationCallStatus.UnexpectedResponse,
        "qBittorrent answered 204 No Content, which it doesn't send for this request. Check the address.")]
    public async Task FindJobs_answers_a_failed_read_with_its_status_and_sentence(int statusCode, IntegrationCallStatus expectedStatus, string expectedError)
    {
        //Arrange
        _qbittorrent.Intercept = request => request.RequestUri!.AbsolutePath == InfoPath ? new HttpResponseMessage((HttpStatusCode)statusCode) : null;

        //Act
        var result = await Client.FindJobsAsync(Saved, [PackHash], TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(expectedStatus, result.Status);
        Assert.Equal(expectedError, result.Error);
        Assert.Null(result.Value);
        Assert.Single(_harness.QBittorrentHttp.Requests, request => request.Uri.AbsolutePath == InfoPath);
    }

    [Fact]
    public async Task FindJobs_whose_fresh_session_is_refused_fails_without_signing_in_again()
    {
        //Arrange
        _qbittorrent.Intercept = request => request.RequestUri!.AbsolutePath == InfoPath
            ? VendorResponses.Recorded(FakeQBittorrentServer.ExpiredSessionResponseFile)
            : null;

        //Act
        var result = await Client.FindJobsAsync(Saved, [PackHash], TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.UnexpectedResponse, result.Status);
        Assert.Equal("qBittorrent refused the session it had just opened.", result.Error);
        Assert.Equal(
            ["POST /api/v2/auth/login", "GET /api/v2/app/version with session 1", "GET /api/v2/torrents/info with session 1"],
            Describe(_harness.QBittorrentHttp.Requests));
    }

    [Fact]
    public async Task FindJobs_whose_kept_session_qbittorrent_forgot_renews_it_once_and_reads_on()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        _qbittorrent.AddTorrent(new TorrentInfoHashes(PackHash, null), "Taken. Trilogy", "Movies");
        await Client.FindJobsAsync(Saved, [PackHash], cancellationToken);
        _qbittorrent.ExpireSessions();

        //Act
        var result = await Client.FindJobsAsync(Saved, [PackHash], cancellationToken);

        //Assert
        Assert.Equal(PackHash, Assert.Single(result.Value!).ClientJobId);
        Assert.Equal(
            [
                "GET /api/v2/app/version with session 1",
                "POST /api/v2/auth/login",
                "GET /api/v2/app/version with session 2",
                "GET /api/v2/torrents/info with session 2",
            ],
            Describe(_harness.QBittorrentHttp.Requests).Skip(3));
    }

    [Theory]
    [InlineData("v4.3.9")]
    [InlineData("v4.4.0")]
    public async Task FindJobs_of_a_qbittorrent_older_than_4_4_1_is_an_unsupported_version_and_reads_no_torrents(string version)
    {
        //Arrange
        _qbittorrent.Version = version;

        //Act
        var result = await Client.FindJobsAsync(Saved, [PackHash], TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.UnsupportedVersion, result.Status);
        Assert.Equal($"This is qBittorrent {version}; the app works with qBittorrent 4.4.1 or later. Update qBittorrent, then test again.", result.Error);
        Assert.DoesNotContain(_harness.QBittorrentHttp.Requests, request => request.Uri.AbsolutePath == InfoPath);
    }

    [Theory]
    [MemberData(nameof(TransportFailures))]
    public async Task FindJobs_maps_each_transport_error_to_its_sentence_without_the_exception_text(
        HttpRequestError error, IntegrationCallStatus expectedStatus, string expectedError)
    {
        //Arrange
        _qbittorrent.Intercept = request => request.RequestUri!.AbsolutePath == InfoPath
            ? throw new HttpRequestException(error, $"Socket failure on {request.RequestUri} with {FakeQBittorrentServer.DefaultPassword}")
            : null;

        //Act
        var result = await Client.FindJobsAsync(Saved, [PackHash], TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(expectedStatus, result.Status);
        Assert.Equal(expectedError, result.Error);
    }

    [Fact]
    public async Task FindJobs_that_outlives_the_timeout_is_timed_out()
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
            .FindJobsAsync(Saved, [PackHash], TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.TimedOut, result.Status);
        Assert.Equal("qBittorrent at http://qbittorrent.test:8080 didn't answer within 0.1 seconds.", result.Error);
    }

    [Fact]
    public async Task FindJobs_cancelled_by_the_caller_throws()
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
        var exception = await Record.ExceptionAsync(() => Client.FindJobsAsync(Saved, [PackHash], cancellation.Token));

        //Assert
        Assert.IsAssignableFrom<OperationCanceledException>(exception);
    }

    /// <summary>A torrents/info entry shaped like 5.2's, with the properties the adapter reads.</summary>
    private static JsonObject Torrent(
        string hash,
        string? name = "Taken (2008)",
        string? state = "stalledUP",
        string? v1 = null,
        string? v2 = "",
        string category = "Movies",
        string contentPath = "/downloads/movies/Taken (2008)",
        string savePath = "/downloads/movies") => new()
    {
        ["auto_tmm"] = true,
        ["category"] = category,
        ["comment"] = "https://toloka.to/p1151579",
        ["content_path"] = contentPath,
        ["hash"] = hash,
        ["infohash_v1"] = v1 ?? hash,
        ["infohash_v2"] = v2,
        ["magnet_uri"] = $"magnet:?xt=urn:btih:{hash}",
        ["name"] = name,
        ["progress"] = 1,
        ["save_path"] = savePath,
        ["state"] = state,
        ["tracker"] = "http://bt.toloka.test/REDACTED",
    };

    private void AnswerTorrents(params JsonObject[] torrents)
    {
        var body = new JsonArray([.. torrents]).ToJsonString();
        _qbittorrent.Intercept = request => request.RequestUri!.AbsolutePath == InfoPath
            ? VendorResponses.Text(body, HttpStatusCode.OK, "application/json")
            : null;
    }

    private static HttpResponseMessage Answer(params JsonObject[] torrents) =>
        VendorResponses.Text(new JsonArray([.. torrents]).ToJsonString(), HttpStatusCode.OK, "application/json");

    // A 200 whose body never finishes arriving: the pipe is never written to or completed.
    private static HttpResponseMessage StalledAnswer() =>
        new(HttpStatusCode.OK) { Content = new StreamContent(new Pipe().Reader.AsStream()) };

    /// <summary>The session id the request carries.</summary>
    private static string SessionOf(HttpRequestMessage request) =>
        request.Headers.TryGetValues("Cookie", out var values) ? values.Single()[(FakeQBittorrentServer.CookieName.Length + 1)..] : "";

    private static List<string> Describe(IEnumerable<ClientJob> jobs) =>
    [
        .. jobs.Select(job => string.Join(
            " | ",
            job.ClientJobId,
            job.Name,
            job.State,
            job.Progress?.ToString(CultureInfo.InvariantCulture) ?? "(unknown)",
            job.DestinationKey ?? "(none)",
            job.ServiceContentPath ?? "(none)",
            string.Join(',', job.InfoHashes))),
    ];

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
