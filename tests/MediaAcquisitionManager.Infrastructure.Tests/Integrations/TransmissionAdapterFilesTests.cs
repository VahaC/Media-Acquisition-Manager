using System.Globalization;
using System.Net;
using System.Text.Json.Nodes;
using MediaAcquisitionManager.Core.Abstractions.Downloads;
using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Downloads;
using MediaAcquisitionManager.Infrastructure.Integrations.Transmission;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;

namespace MediaAcquisitionManager.Infrastructure.Tests.Integrations;

// MAM-30 — a torrent's files: one torrent-get of its hashString, metadataPercentComplete, files and fileStats, read as an
// allowlist — each path exactly as Transmission names it (its folder first, never .part), its size, how much is there and
// whether it is wanted, 4.1's begin_piece and end_piece ignored; none while a magnet fetches its metadata; NotFound for a
// torrent Transmission no longer has; a broken answer failing whole. Checked against MAM-29's recorded answers.
public sealed class TransmissionAdapterFilesTests : IDisposable
{
    private const string NotFilesError = "The service at this address answered, but not with Transmission's files.";

    private static readonly IntegrationConnectionSettings Saved = new(
        Guid.Parse("4d2c6a1e-3b5f-4e7a-9c8d-1f0e2a3b4c5d"),
        FakeTransmissionServer.DefaultBaseUrl,
        FakeTransmissionServer.DefaultUserName,
        FakeTransmissionServer.DefaultPassword,
        null);

    private readonly IntegrationAdapterHarness _harness = new();
    private readonly FakeTransmissionServer _transmission = new();

    public TransmissionAdapterFilesTests() => _transmission.Arm(_harness.TransmissionHttp);

    /// <summary>A torrent of one hash whose files or fileStats aren't Transmission's: each row names what is wrong.</summary>
    public static TheoryData<string> BrokenFiles => new()
    {
        "files not a list",
        "no fileStats",
        "fewer fileStats than files",
        "a file without a name",
        "a file with an empty name",
        "a file named longer than any path",
        "a file that isn't an object",
        "another torrent",
        "two torrents",
        "no torrents list",
    };

    private IDownloadClient Client => _harness.Adapter<IDownloadClient>(TransmissionAdapter.TypeId);

    public void Dispose() => _harness.Dispose();

    [Theory]
    [InlineData("4.1.3")]
    [InlineData("4.0.6")]
    public async Task ListJobFiles_lists_each_file_with_its_folder_size_progress_and_whether_it_is_wanted(string version)
    {
        //Arrange
        _transmission.Version = version == "4.0.6" ? FakeTransmissionVersion.V4_0_6 : FakeTransmissionVersion.V4_1_3;
        _transmission.AddTorrent(TransmissionSamples.BigBuckBunnyDownloading());

        //Act
        var result = await Client.ListJobFilesAsync(Saved, TransmissionSamples.BigBuckBunnyHash, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(
            [
                new ClientJobFile("Big Buck Bunny/Big Buck Bunny.en.srt", 140, 1, true),
                new ClientJobFile("Big Buck Bunny/Big Buck Bunny.mp4", 276_134_947, 2_473_844 / 276_134_947.0, true),
                new ClientJobFile("Big Buck Bunny/poster.jpg", 310_380, 0, true),
            ],
            result.Value);
        var read = _transmission.Calls[^1];
        Assert.Equal("torrent-get", read.Method);
        Assert.Equal([TransmissionSamples.BigBuckBunnyHash], read.Arguments["ids"]!.AsArray().Select(id => (string)id!));
        Assert.Equal(["hashString", "metadataPercentComplete", "files", "fileStats"], read.Arguments["fields"]!.AsArray().Select(field => (string)field!));
    }

    [Theory]
    [InlineData(TransmissionSamples.TorrentGetSeedingFile, 1.0)]
    [InlineData(TransmissionSamples.TorrentGetVerifying406File, 0.0)]
    public async Task ListJobFiles_reads_the_recorded_answers(string file, double progress)
    {
        //Arrange
        _transmission.Intercept = (_, call) => call?.Method == "torrent-get" && call.SessionId == _transmission.SessionId
            ? VendorResponses.Sample(file, "application/json; charset=UTF-8")
            : null;

        //Act
        var result = await Client.ListJobFilesAsync(Saved, TransmissionSamples.BigBuckBunnyHash, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(
            ["Big Buck Bunny/Big Buck Bunny.en.srt", "Big Buck Bunny/Big Buck Bunny.mp4", "Big Buck Bunny/poster.jpg"],
            result.Value!.Select(listed => listed.RelativePath));
        Assert.Equal([140L, 276_134_947L, 310_380L], result.Value!.Select(listed => listed.SizeBytes!.Value));
        Assert.All(result.Value!, listed => Assert.Equal((progress, true), (listed.Progress!.Value, listed.IsWanted!.Value)));
    }

    [Fact]
    public async Task ListJobFiles_keeps_each_name_exactly_reads_an_unwanted_file_and_counts_an_empty_file_whole()
    {
        //Arrange
        _transmission.AddTorrent(
            new TorrentInfoHashes(Hash(1), null),
            "Season 1",
            files:
            [
                new FakeTransmissionFile("Season 1/UkrEng  Sub/E01.mkv", 1_000, 250),
                new FakeTransmissionFile("Season 1/Sample.mkv", 100, 0, Wanted: false),
                new FakeTransmissionFile("Season 1/empty.nfo", 0, 0),
            ]);

        //Act
        var result = await Client.ListJobFilesAsync(Saved, Hash(1), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(
            [
                new ClientJobFile("Season 1/UkrEng  Sub/E01.mkv", 1_000, 0.25, true),
                new ClientJobFile("Season 1/Sample.mkv", 100, 0, false),
                new ClientJobFile("Season 1/empty.nfo", 0, 1, true),
            ],
            result.Value);
    }

    [Fact]
    public async Task ListJobFiles_of_a_magnet_still_fetching_its_metadata_lists_none()
    {
        //Arrange
        _transmission.AddTorrent(TransmissionSamples.MagnetWithoutMetadata());

        //Act
        var result = await Client.ListJobFilesAsync(Saved, TransmissionSamples.MagnetHash, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        Assert.Empty(result.Value!);
    }

    [Fact]
    public async Task ListJobFiles_of_a_torrent_transmission_no_longer_has_is_not_found()
    {
        //Arrange

        //Act
        var result = await Client.ListJobFilesAsync(Saved, Hash(1), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.NotFound, result.Status);
        Assert.Equal("Transmission doesn't have this torrent any more.", result.Error);
    }

    [Fact]
    public async Task ListJobFiles_of_a_v2_id_is_not_found_without_a_request()
    {
        //Arrange
        // Transmission names every torrent by its v1 hash, which is the id it gives a job: a v2 id names none of them.

        //Act
        var result = await Client.ListJobFilesAsync(Saved, new string('b', 64), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.NotFound, result.Status);
        Assert.Empty(_harness.TransmissionHttp.Requests);
    }

    [Fact]
    public async Task ListJobFiles_asks_by_the_id_lower_case()
    {
        //Arrange
        _transmission.AddTorrent(new TorrentInfoHashes(Hash(10), null), "Interstellar (2014)", files: [new FakeTransmissionFile("Interstellar (2014).mkv", 10, 10)]);

        //Act
        var result = await Client.ListJobFilesAsync(Saved, Hash(10).ToUpperInvariant(), TestContext.Current.CancellationToken);

        //Assert
        Assert.Single(result.Value!);
        Assert.Equal([Hash(10)], _transmission.Calls[^1].Arguments["ids"]!.AsArray().Select(id => (string)id!));
    }

    [Theory]
    [InlineData("")]
    [InlineData("4a3c1931")]
    [InlineData("4a3c1931c85c9c568c39a8eb947ed382824b1cdz")]
    public async Task ListJobFiles_of_an_id_that_is_no_info_hash_throws_before_anything_is_sent(string id)
    {
        //Arrange

        //Act
        var thrown = await Assert.ThrowsAsync<ArgumentException>(() => Client.ListJobFilesAsync(Saved, id, TestContext.Current.CancellationToken));

        //Assert
        Assert.Equal("clientJobId", thrown.ParamName);
        Assert.Empty(_harness.TransmissionHttp.Requests);
    }

    [Theory]
    [MemberData(nameof(BrokenFiles))]
    public async Task ListJobFiles_of_files_that_are_not_transmissions_fails_whole(string broken)
    {
        //Arrange
        var files = new JsonArray(
            new JsonObject { ["bytesCompleted"] = 10, ["length"] = 10, ["name"] = "Interstellar (2014)/Interstellar (2014).mkv" },
            new JsonObject { ["bytesCompleted"] = 1, ["length"] = 1, ["name"] = "Interstellar (2014)/uk.srt" });
        var fileStats = new JsonArray(
            new JsonObject { ["bytesCompleted"] = 10, ["priority"] = 0, ["wanted"] = true },
            new JsonObject { ["bytesCompleted"] = 1, ["priority"] = 0, ["wanted"] = true });
        var torrent = new JsonObject { ["hashString"] = Hash(1), ["metadataPercentComplete"] = 1, ["files"] = files, ["fileStats"] = fileStats };
        JsonNode torrents = new JsonArray(torrent);
        switch (broken)
        {
            case "files not a list":
                torrent["files"] = new JsonObject();
                break;
            case "no fileStats":
                torrent.Remove("fileStats");
                break;
            case "fewer fileStats than files":
                fileStats.RemoveAt(1);
                break;
            case "a file without a name":
                files[1]!.AsObject().Remove("name");
                break;
            case "a file with an empty name":
                files[1]!["name"] = "";
                break;
            case "a file named longer than any path":
                files[1]!["name"] = new string('n', 4097);
                break;
            case "a file that isn't an object":
                files[1] = "uk.srt";
                break;
            case "another torrent":
                torrent["hashString"] = Hash(2);
                break;
            case "two torrents":
                torrents.AsArray().Add(torrent.DeepClone());
                break;
            default:
                torrents = new JsonObject();
                break;
        }

        _transmission.RpcIntercept = call => call.Method == "torrent-get"
            ? new JsonObject { ["arguments"] = new JsonObject { ["torrents"] = torrents.DeepClone() }, ["result"] = "success" }
            : null;

        //Act
        var result = await Client.ListJobFilesAsync(Saved, Hash(1), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.UnexpectedResponse, result.Status);
        Assert.Equal(NotFilesError, result.Error);
    }

    [Fact]
    public async Task ListJobFiles_reads_a_size_or_progress_it_can_not_read_as_unknown()
    {
        //Arrange
        _transmission.RpcIntercept = call => call.Method == "torrent-get"
            ? new JsonObject
            {
                ["arguments"] = new JsonObject
                {
                    ["torrents"] = new JsonArray(new JsonObject
                    {
                        ["hashString"] = Hash(1),
                        ["metadataPercentComplete"] = 1.0,
                        ["files"] = new JsonArray(
                            new JsonObject { ["bytesCompleted"] = 5, ["length"] = "ten", ["name"] = "a.mkv" },
                            new JsonObject { ["length"] = 10, ["name"] = "b.mkv" },
                            new JsonObject { ["bytesCompleted"] = 20.0, ["length"] = 10.0, ["name"] = "c.mkv" }),
                        ["fileStats"] = new JsonArray(
                            new JsonObject { ["wanted"] = 1 },
                            new JsonObject { ["wanted"] = false },
                            new JsonObject()),
                    }),
                },
                ["result"] = "success",
            }
            : null;

        //Act
        var result = await Client.ListJobFilesAsync(Saved, Hash(1), TestContext.Current.CancellationToken);

        //Assert
        // 4.1 writes a whole number as 1.0; more completed than the length is whole, never more.
        Assert.Equal(
            [
                new ClientJobFile("a.mkv", null, null, null),
                new ClientJobFile("b.mkv", 10, null, false),
                new ClientJobFile("c.mkv", 10, 1, null),
            ],
            result.Value);
    }

    [Fact]
    public async Task ListJobFiles_that_transmission_refuses_quotes_its_result()
    {
        //Arrange
        _transmission.RpcIntercept = call => call.Method == "torrent-get"
            ? new JsonObject { ["arguments"] = new JsonObject(), ["result"] = "no fields specified" }
            : null;

        //Act
        var result = await Client.ListJobFilesAsync(Saved, Hash(1), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Refused, result.Status);
        Assert.Equal("Transmission refused the request: no fields specified.", result.Error);
    }

    [Fact]
    public async Task ListJobFiles_whose_read_fails_is_unknown_never_not_found()
    {
        //Arrange
        _transmission.Intercept = (_, call) => call?.Method == "torrent-get" ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) : null;

        //Act
        var result = await Client.ListJobFilesAsync(Saved, Hash(1), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.ServiceError, result.Status);
    }

    private static string Hash(int number) => number.ToString("x40", CultureInfo.InvariantCulture);
}
