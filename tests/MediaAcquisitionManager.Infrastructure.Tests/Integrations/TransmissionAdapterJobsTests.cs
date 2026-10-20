using System.Globalization;
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

// MAM-30 — the job lookup: one torrent-get by the v1 hashes (a v2 hash names no Transmission torrent), read as an
// allowlist; each torrent as a job — named by its hashString for good, its state by status and only a local error (D10),
// complete only with its metadata and nothing left, and its content where this Transmission writes it (D20: 4.0 cleans a
// name the portable way, 4.1 on Linux keeps it, 4.1 on Windows replaces control characters too). Checked against the
// torrent-get answers MAM-29 recorded on 4.1.3 and 4.0.6. TransmissionAdapterDestinationJobsTests has one folder's jobs.
public sealed class TransmissionAdapterJobsTests : IDisposable
{
    private static readonly IntegrationConnectionSettings Saved = new(
        Guid.Parse("4d2c6a1e-3b5f-4e7a-9c8d-1f0e2a3b4c5d"),
        FakeTransmissionServer.DefaultBaseUrl,
        FakeTransmissionServer.DefaultUserName,
        FakeTransmissionServer.DefaultPassword,
        null);

    private readonly IntegrationAdapterHarness _harness = new();
    private readonly FakeTransmissionServer _transmission = new();

    public TransmissionAdapterJobsTests() => _transmission.Arm(_harness.TransmissionHttp);

    /// <summary>D10 — Transmission's status and error, whether the torrent is complete, and the core's state.</summary>
    public static TheoryData<int, int, bool, ClientJobState> States => new()
    {
        { 0, 0, true, ClientJobState.Completed },
        { 0, 0, false, ClientJobState.Paused },
        { 1, 0, false, ClientJobState.Queued },
        { 2, 0, false, ClientJobState.Queued },
        // A verify of a complete torrent is no download done.
        { 2, 0, true, ClientJobState.Queued },
        { 3, 0, false, ClientJobState.Queued },
        { 4, 0, false, ClientJobState.Downloading },
        { 5, 0, true, ClientJobState.Seeding },
        { 6, 0, true, ClientJobState.Seeding },
        // A tracker's warning or error leaves the download going.
        { 4, 1, false, ClientJobState.Downloading },
        { 6, 2, true, ClientJobState.Seeding },
        // A local error — a full disk, a permission, files gone — whatever the status.
        { 4, 3, false, ClientJobState.Errored },
        { 0, 3, true, ClientJobState.Errored },
        { 6, 3, true, ClientJobState.Errored },
        { 7, 0, true, ClientJobState.Unknown },
        { -1, 0, false, ClientJobState.Unknown },
    };

    /// <summary>
    /// D20 — Transmission's version, its download folder, the torrent's folder and name, and where its content is; null when
    /// it can't be told.
    /// </summary>
    public static TheoryData<string, string, string, string, string?> ContentPaths => new()
    {
        { "4.0.6", "/downloads", "/downloads/movies", "Mission: Impossible (1996)", "/downloads/movies/Mission_ Impossible (1996)" },
        { "4.1.3", "/downloads", "/downloads/movies", "Mission: Impossible (1996)", "/downloads/movies/Mission: Impossible (1996)" },
        { "4.0.6", "/downloads", "/downloads", "  Movie (2020) . ", "/downloads/Movie (2020) " },
        { "4.1.3", "/downloads", "/downloads", "  Movie (2020) . ", "/downloads/  Movie (2020) . " },
        { "4.0.6", "/downloads", "/downloads", "con.mkv", "/downloads/_con.mkv" },
        { "4.1.3", "/downloads", "/downloads", "con.mkv", "/downloads/con.mkv" },
        { "4.0.6", "/downloads", "/downloads", "Season 1/Episode 1.mkv", "/downloads/Season 1/Episode 1.mkv" },
        { "4.1.3", "/downloads", "/downloads/", "Season 1/Episode 1.mkv", "/downloads/Season 1/Episode 1.mkv" },
        // A name that cleans to nothing writes its files straight into the folder, which the name alone can't tell.
        { "4.0.6", "/downloads", "/downloads", "...", null },
        { "4.1.3", "/downloads", "/downloads", "..", "/downloads/_.." },
        { "4.1.3", "/downloads", "/downloads", "/", null },
        { "4.1.3", @"D:\Torrents", @"D:\Torrents\Movies", "Movie\u0001: Part 1", @"D:\Torrents\Movies\Movie__ Part 1" },
        { "4.0.6", @"D:\Torrents", @"D:\Torrents\Movies", "Movie\u0001: Part 1", "D:\\Torrents\\Movies\\Movie\u0001_ Part 1" },
        // 4.1 with a download folder that tells no system: unknown, never guessed.
        { "4.1.3", "downloads", "/downloads/movies", "Movie", null },
        { "4.0.6", "/downloads", "movies", "Movie", null },
    };

    private IDownloadClient Client => _harness.Adapter<IDownloadClient>(TransmissionAdapter.TypeId);

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task FindJobs_sends_one_torrent_get_of_the_v1_hashes_lower_case_once_each_and_of_the_job_fields_only()
    {
        //Arrange
        var hybrid = new TorrentInfoHashes(Hash(1), new string('b', 64));

        //Act
        var result = await Client.FindJobsAsync(Saved, [Hash(1).ToUpperInvariant(), hybrid.V2!, Hash(1), Hash(2)], TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        Assert.Empty(result.Value!);
        var read = _transmission.Calls[^1];
        Assert.Equal("torrent-get", read.Method);
        Assert.Equal(["fields", "ids"], read.ArgumentNames.Order());
        Assert.Equal([Hash(1), Hash(2)], read.Arguments["ids"]!.AsArray().Select(id => (string)id!));
        // Never id, labels, trackers, the magnet link or errorString.
        Assert.Equal(
            ["hashString", "name", "status", "error", "percentDone", "leftUntilDone", "metadataPercentComplete", "downloadDir"],
            read.Arguments["fields"]!.AsArray().Select(field => (string)field!));
    }

    [Fact]
    public async Task FindJobs_of_no_hashes_or_v2_hashes_only_asks_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var none = await Client.FindJobsAsync(Saved, [], cancellationToken);
        var v2Only = await Client.FindJobsAsync(Saved, [new string('c', 64)], cancellationToken);

        //Assert
        Assert.Empty(none.Value!);
        Assert.Empty(v2Only.Value!);
        Assert.Empty(_harness.TransmissionHttp.Requests);
    }

    [Theory]
    [InlineData("")]
    [InlineData("4a3c1931")]
    [InlineData("4a3c1931c85c9c568c39a8eb947ed382824b1cdz")]
    [InlineData("٤a3c1931c85c9c568c39a8eb947ed382824b1cdd")]
    public async Task FindJobs_of_a_hash_that_is_no_info_hash_throws_before_anything_is_sent(string hash)
    {
        //Arrange

        //Act
        var thrown = await Assert.ThrowsAsync<ArgumentException>(() => Client.FindJobsAsync(Saved, [Hash(1), hash], TestContext.Current.CancellationToken));

        //Assert
        Assert.Equal("infoHashes", thrown.ParamName);
        Assert.Empty(_harness.TransmissionHttp.Requests);
    }

    [Fact]
    public async Task FindJobs_reads_a_downloading_torrent_as_a_job_named_by_its_hash_in_its_folder()
    {
        //Arrange
        var planted = _transmission.AddTorrent(TransmissionSamples.BigBuckBunnyDownloading());

        //Act
        var result = await Client.FindJobsAsync(Saved, [TransmissionSamples.BigBuckBunnyHash], TestContext.Current.CancellationToken);

        //Assert
        Assert.Equivalent(
            new ClientJob(
                TransmissionSamples.BigBuckBunnyHash,
                "Big Buck Bunny",
                ClientJobState.Downloading,
                planted.PercentDone,
                "/scratch",
                "/scratch/Big Buck Bunny",
                [TransmissionSamples.BigBuckBunnyHash],
                "/scratch"),
            Assert.Single(result.Value!),
            strict: true);
    }

    [Theory]
    [InlineData(TransmissionSamples.TorrentGetDownloadingFile, ClientJobState.Downloading, 0.00894926581531763, "/scratch/Big Buck Bunny")]
    [InlineData(TransmissionSamples.TorrentGetVerifying406File, ClientJobState.Queued, 0.0, "/scratch/Big Buck Bunny")]
    [InlineData(TransmissionSamples.TorrentGetSeedingFile, ClientJobState.Seeding, 1.0, "/scratch/Big Buck Bunny")]
    [InlineData(TransmissionSamples.TorrentGetStoppedFile, ClientJobState.Completed, 1.0, "/scratch/Big Buck Bunny")]
    [InlineData(TransmissionSamples.TorrentGetMagnetNoMetadataFile, ClientJobState.Downloading, 0.0, null)]
    public async Task FindJobs_reads_each_recorded_torrent_get(string file, ClientJobState state, double progress, string? contentPath)
    {
        //Arrange
        // The recorded bodies as Transmission wrote them — 4.0.6's 1 and 4.1.3's 1.0, and every field the app never reads.
        if (file == TransmissionSamples.TorrentGetVerifying406File)
            _transmission.Version = FakeTransmissionVersion.V4_0_6;
        _transmission.Intercept = (_, call) => call?.Method == "torrent-get" && call.SessionId == _transmission.SessionId
            ? VendorResponses.Sample(file, "application/json; charset=UTF-8")
            : null;
        var hash = file == TransmissionSamples.TorrentGetMagnetNoMetadataFile ? TransmissionSamples.MagnetHash : TransmissionSamples.BigBuckBunnyHash;

        //Act
        var result = await Client.FindJobsAsync(Saved, [hash], TestContext.Current.CancellationToken);

        //Assert
        var job = Assert.Single(result.Value!);
        Assert.Equal(hash, job.ClientJobId);
        Assert.Equal(state, job.State);
        Assert.Equal(progress, job.Progress);
        Assert.Equal(contentPath, job.ServiceContentPath);
        Assert.Equal("/scratch", job.ServiceSavePath);
        Assert.Equal("/scratch", job.DestinationKey);
        Assert.Equal([hash], job.InfoHashes);
    }

    [Theory]
    [MemberData(nameof(States))]
    public async Task FindJobs_maps_transmissions_status_and_a_local_error_only_to_the_cores_state(int status, int error, bool complete, ClientJobState expected)
    {
        //Arrange
        _transmission.AddTorrent(new TorrentInfoHashes(Hash(1), null), "Interstellar (2014)");
        _transmission.UpdateTorrent(Hash(1), torrent => torrent with
        {
            Status = status,
            Error = error,
            LeftUntilDone = complete ? 0 : 1_000,
            PercentDone = complete ? 1 : 0.5,
        });

        //Act
        var result = await Client.FindJobsAsync(Saved, [Hash(1)], TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(expected, Assert.Single(result.Value!).State);
    }

    [Fact]
    public async Task FindJobs_reads_a_magnet_without_metadata_as_not_complete_though_nothing_is_left()
    {
        //Arrange
        // MAM-29: leftUntilDone 0 at 0 %, and stopped it is paused, never completed.
        _transmission.AddTorrent(TransmissionSamples.MagnetWithoutMetadata() with { Status = 0 });

        //Act
        var result = await Client.FindJobsAsync(Saved, [TransmissionSamples.MagnetHash], TestContext.Current.CancellationToken);

        //Assert
        var job = Assert.Single(result.Value!);
        Assert.Equal(ClientJobState.Paused, job.State);
        Assert.Equal(0, job.Progress);
        Assert.Null(job.ServiceContentPath);
    }

    [Theory]
    [InlineData(0.5, 1_000L, 0.5)]
    [InlineData(0.0, 1_000L, 0.0)]
    // A rounding, not a download done: something is left.
    [InlineData(1.0, 1L, null)]
    [InlineData(-0.1, 1_000L, null)]
    [InlineData(1.5, 1_000L, null)]
    // Complete is 1 whatever percentDone says.
    [InlineData(0.99, 0L, 1.0)]
    public async Task FindJobs_reads_progress_only_from_0_to_1_and_1_only_once_complete(double percentDone, long leftUntilDone, double? progress)
    {
        //Arrange
        _transmission.AddTorrent(new TorrentInfoHashes(Hash(1), null), "Interstellar (2014)");
        _transmission.UpdateTorrent(Hash(1), torrent => torrent with { PercentDone = percentDone, LeftUntilDone = leftUntilDone });

        //Act
        var result = await Client.FindJobsAsync(Saved, [Hash(1)], TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(progress, Assert.Single(result.Value!).Progress);
    }

    [Theory]
    [MemberData(nameof(ContentPaths))]
    public async Task FindJobs_puts_the_content_where_this_transmission_writes_it(
        string version, string downloadDir, string torrentFolder, string name, string? contentPath)
    {
        //Arrange
        _transmission.Version = version == "4.0.6" ? FakeTransmissionVersion.V4_0_6 : FakeTransmissionVersion.V4_1_3;
        _transmission.DownloadDir = downloadDir;
        _transmission.AddTorrent(new TorrentInfoHashes(Hash(1), null), name, torrentFolder);

        //Act
        var result = await Client.FindJobsAsync(Saved, [Hash(1)], TestContext.Current.CancellationToken);

        //Assert
        var job = Assert.Single(result.Value!);
        Assert.Equal(contentPath, job.ServiceContentPath);
        // The folder it saves to stays exactly as Transmission sent it.
        Assert.Equal(torrentFolder, job.ServiceSavePath);
    }

    [Fact]
    public async Task FindJobs_puts_a_queued_4_0_6_torrents_content_where_transmission_will_write_its_files()
    {
        //Arrange
        // D20 — the overwrite check reads the content's first folder to tell which files the torrent may write: 4.0.6 writes
        // "Mission_ Impossible (1996)/…", as its files say, never the name as sent.
        _transmission.Version = FakeTransmissionVersion.V4_0_6;
        var torrent = TestTorrent.V1(
            "Mission: Impossible (1996)", [(["Mission: Impossible (1996).mkv"], TestTorrent.Bytes(40_000, 3)), (["Extras", "Trailer.mkv"], TestTorrent.Bytes(500, 4))]);
        _transmission.AddFolder("/downloads/movies");
        await Client.SubmitAsync(Saved, new DownloadSubmission(new ReleasePayload(ReleasePayloadKind.TorrentFile, torrent, null), "/downloads/movies", null, "Movies"), TestContext.Current.CancellationToken);
        var hash = TorrentInfoHashReader.FromTorrentFile(torrent).V1!;
        _transmission.UpdateTorrent(hash, queued => queued with { Status = 3 });

        //Act
        var result = await Client.FindJobsAsync(Saved, [hash], TestContext.Current.CancellationToken);

        //Assert
        var job = Assert.Single(result.Value!);
        Assert.Equal(ClientJobState.Queued, job.State);
        Assert.Equal("/downloads/movies/Mission_ Impossible (1996)", job.ServiceContentPath);
        Assert.All(_transmission.Torrents[0].Files, file => Assert.StartsWith("Mission_ Impossible (1996)/", file.Name, StringComparison.Ordinal));
    }

    [Fact]
    public async Task FindJobs_finds_none_of_hashes_transmission_does_not_have()
    {
        //Arrange
        _transmission.AddTorrent(new TorrentInfoHashes(Hash(1), null), "Interstellar (2014)");

        //Act
        var result = await Client.FindJobsAsync(Saved, [Hash(2)], TestContext.Current.CancellationToken);

        //Assert
        Assert.Empty(result.Value!);
    }

    [Fact]
    public async Task FindJobs_leaves_out_a_torrent_without_a_usable_hash()
    {
        //Arrange
        _transmission.RpcIntercept = call => call.Method == "torrent-get"
            ? Torrents(
                new JsonObject { ["hashString"] = "not-a-hash", ["name"] = "Broken", ["status"] = 4 },
                new JsonObject { ["name"] = "No hash", ["status"] = 4 },
                new JsonObject { ["hashString"] = Hash(1).ToUpperInvariant(), ["name"] = "Interstellar (2014)", ["status"] = 6 })
            : null;

        //Act
        var result = await Client.FindJobsAsync(Saved, [Hash(1)], TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(Hash(1), Assert.Single(result.Value!).ClientJobId);
    }

    [Fact]
    public async Task FindJobs_cleans_a_name_and_masks_the_password_in_it_but_puts_the_content_by_the_name_as_sent()
    {
        //Arrange
        var name = $"Interstellar\u202E (2014)\n{FakeTransmissionServer.DefaultPassword}";
        _transmission.Version = FakeTransmissionVersion.V4_1_3;
        _transmission.AddTorrent(new TorrentInfoHashes(Hash(1), null), name);

        //Act
        var result = await Client.FindJobsAsync(Saved, [Hash(1)], TestContext.Current.CancellationToken);

        //Assert
        var job = Assert.Single(result.Value!);
        Assert.Equal("Interstellar (2014) ***", job.Name);
        Assert.Equal($"/downloads/{name}", job.ServiceContentPath);
    }

    [Theory]
    [InlineData("""{"arguments":{},"result":"success"}""")]
    [InlineData("""{"arguments":{"torrents":{}},"result":"success"}""")]
    [InlineData("""{"arguments":{"torrents":[1]},"result":"success"}""")]
    public async Task FindJobs_of_an_answer_without_a_list_of_torrents_is_an_unexpected_response(string body)
    {
        //Arrange
        _transmission.Intercept = (_, call) => call?.Method == "torrent-get" && call.SessionId == _transmission.SessionId
            ? VendorResponses.Text(body, HttpStatusCode.OK, "application/json")
            : null;

        //Act
        var result = await Client.FindJobsAsync(Saved, [Hash(1)], TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.UnexpectedResponse, result.Status);
        Assert.Equal("The service at this address answered, but not with Transmission's torrents.", TestTexts.En(result.Error));
    }

    [Fact]
    public async Task FindJobs_that_transmission_refuses_quotes_its_result()
    {
        //Arrange
        _transmission.RpcIntercept = call => call.Method == "torrent-get"
            ? new JsonObject { ["arguments"] = new JsonObject(), ["result"] = "no fields specified" }
            : null;

        //Act
        var result = await Client.FindJobsAsync(Saved, [Hash(1)], TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Refused, result.Status);
        Assert.Equal("Transmission refused the request: no fields specified.", TestTexts.En(result.Error));
    }

    [Fact]
    public async Task FindJobs_of_an_unreachable_transmission_is_unreachable()
    {
        //Arrange
        _transmission.Unreachable = true;

        //Act
        var result = await Client.FindJobsAsync(Saved, [Hash(1)], TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Unreachable, result.Status);
    }

    private static string Hash(int number) => number.ToString("x40", CultureInfo.InvariantCulture);

    private static JsonObject Torrents(params JsonObject[] torrents) =>
        new() { ["arguments"] = new JsonObject { ["torrents"] = new JsonArray([.. torrents]) }, ["result"] = "success" };
}
