using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using MediaAcquisitionManager.Core.Abstractions.Downloads;
using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Downloads;
using MediaAcquisitionManager.Infrastructure.Integrations.Transmission;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;

namespace MediaAcquisitionManager.Infrastructure.Tests.Integrations;

// MAM-30 — the whole listing behind the replacement gates (ADR 0031): one torrent-get of every torrent in every state and
// folder with its files, read as the lookup reads a job and the files read a file list; a magnet without metadata with its
// files unknown, never none; and whole or nothing — a torrent it can't name, or files it can't read, fail the call, since a
// torrent left out could hide a held copy. An answer too big to read says so in its own words.
public sealed class TransmissionAdapterJobsWithFilesTests : IDisposable
{
    private const string NotTorrentsError = "The service at this address answered, but not with Transmission's torrents.";
    private const string NotFilesError = "The service at this address answered, but not with Transmission's files.";

    private static readonly IntegrationConnectionSettings Saved = new(
        Guid.Parse("4d2c6a1e-3b5f-4e7a-9c8d-1f0e2a3b4c5d"),
        FakeTransmissionServer.DefaultBaseUrl,
        FakeTransmissionServer.DefaultUserName,
        FakeTransmissionServer.DefaultPassword,
        null);

    private readonly IntegrationAdapterHarness _harness = new();
    private readonly FakeTransmissionServer _transmission = new();

    public TransmissionAdapterJobsWithFilesTests() => _transmission.Arm(_harness.TransmissionHttp);

    private IDownloadClient Client => _harness.Adapter<IDownloadClient>(TransmissionAdapter.TypeId);

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task ListJobsWithFiles_lists_every_torrent_in_every_state_and_folder_with_its_files()
    {
        //Arrange
        _transmission.AddTorrent(TransmissionSamples.BigBuckBunnyStopped());
        _transmission.AddTorrent(
            new TorrentInfoHashes(Hash(1), null), "Interstellar (2014).mkv", "/downloads/movies",
            files: [new FakeTransmissionFile("Interstellar (2014).mkv", 9_663_676_416, 1_000)]);
        _transmission.UpdateTorrent(Hash(1), torrent => torrent with { Error = 3, Status = 0 });
        _transmission.AddTorrent(TransmissionSamples.MagnetWithoutMetadata());

        //Act
        var result = await Client.ListJobsWithFilesAsync(Saved, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        Assert.Collection(
            result.Value!,
            stopped =>
            {
                Assert.Equal((TransmissionSamples.BigBuckBunnyHash, ClientJobState.Completed, "/scratch"), (stopped.Job.ClientJobId, stopped.Job.State, stopped.Job.ServiceSavePath));
                Assert.Equal(
                    ["Big Buck Bunny/Big Buck Bunny.en.srt", "Big Buck Bunny/Big Buck Bunny.mp4", "Big Buck Bunny/poster.jpg"],
                    stopped.Files!.Select(file => file.RelativePath));
            },
            errored =>
            {
                Assert.Equal((Hash(1), ClientJobState.Errored), (errored.Job.ClientJobId, errored.Job.State));
                Assert.Equal([new ClientJobFile("Interstellar (2014).mkv", 9_663_676_416, 1_000 / 9_663_676_416.0, true)], errored.Files);
            },
            magnet =>
            {
                // Its files unknown, never none: it may still write to a library path.
                Assert.Equal(TransmissionSamples.MagnetHash, magnet.Job.ClientJobId);
                Assert.Null(magnet.Files);
            });
    }

    [Fact]
    public async Task ListJobsWithFiles_sends_one_torrent_get_without_ids_of_the_job_fields_and_the_files()
    {
        //Arrange

        //Act
        var result = await Client.ListJobsWithFilesAsync(Saved, TestContext.Current.CancellationToken);

        //Assert
        Assert.Empty(result.Value!);
        Assert.Equal(["session-get", "torrent-get"], _transmission.Calls.Select(call => call.Method));
        var read = _transmission.Calls[1];
        Assert.Equal(["fields"], read.ArgumentNames);
        Assert.Equal(
            ["hashString", "name", "status", "error", "percentDone", "leftUntilDone", "metadataPercentComplete", "downloadDir", "files", "fileStats"],
            read.Arguments["fields"]!.AsArray().Select(field => (string)field!));
    }

    [Theory]
    [InlineData("a torrent without a hash", NotTorrentsError)]
    [InlineData("files it can't read", NotFilesError)]
    [InlineData("metadata and no files", NotFilesError)]
    [InlineData("no torrents list", NotTorrentsError)]
    public async Task ListJobsWithFiles_fails_whole_rather_than_leave_a_torrent_out(string broken, string error)
    {
        //Arrange
        var good = new JsonObject
        {
            ["hashString"] = Hash(1),
            ["name"] = "Interstellar (2014).mkv",
            ["status"] = 6,
            ["metadataPercentComplete"] = 1.0,
            ["leftUntilDone"] = 0,
            ["downloadDir"] = "/downloads/movies",
            ["files"] = new JsonArray(new JsonObject { ["bytesCompleted"] = 10, ["length"] = 10, ["name"] = "Interstellar (2014).mkv" }),
            ["fileStats"] = new JsonArray(new JsonObject { ["wanted"] = true }),
        };
        var other = good.DeepClone().AsObject();
        other["hashString"] = Hash(2);
        switch (broken)
        {
            case "a torrent without a hash":
                other.Remove("hashString");
                break;
            case "files it can't read":
                other["fileStats"] = new JsonArray();
                break;
            case "metadata and no files":
                other["files"] = new JsonArray();
                other["fileStats"] = new JsonArray();
                break;
        }

        _transmission.RpcIntercept = call => call.Method != "torrent-get"
            ? null
            : broken == "no torrents list"
                ? new JsonObject { ["arguments"] = new JsonObject(), ["result"] = "success" }
                : new JsonObject { ["arguments"] = new JsonObject { ["torrents"] = new JsonArray(good.DeepClone(), other.DeepClone()) }, ["result"] = "success" };

        //Act
        var result = await Client.ListJobsWithFilesAsync(Saved, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.UnexpectedResponse, result.Status);
        Assert.Equal(error, result.Error);
    }

    [Fact]
    public async Task ListJobsWithFiles_whose_answer_outgrows_what_the_app_reads_says_so_in_its_own_words()
    {
        //Arrange
        var huge = new StringBuilder("""{"arguments":{"torrents":[""");
        huge.Append(new string(' ', 5 * 1024 * 1024));
        huge.Append("""]},"result":"success"}""");
        _transmission.Intercept = (_, call) => call?.Method == "torrent-get" && call.SessionId == _transmission.SessionId
            ? VendorResponses.Text(huge.ToString(), HttpStatusCode.OK, "application/json")
            : null;

        //Act
        var result = await Client.ListJobsWithFilesAsync(Saved, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.UnexpectedResponse, result.Status);
        Assert.Equal(
            "Transmission's list of every torrent with its files is larger than the app reads (4 MiB), so the app couldn't tell which torrents hold these copies.",
            result.Error);
    }

    [Theory]
    [InlineData("old version")]
    [InlineData("temporary folder on")]
    public async Task ListJobsWithFiles_refused_by_the_session_it_opens_reads_no_torrent(string refusal)
    {
        //Arrange
        if (refusal == "old version")
            _transmission.Version = FakeTransmissionVersion.V3_00;
        else
            _transmission.IncompleteDirEnabled = true;

        //Act
        var result = await Client.ListJobsWithFilesAsync(Saved, TestContext.Current.CancellationToken);

        //Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(["session-get"], _transmission.Calls.Select(call => call.Method));
    }

    private static string Hash(int number) => number.ToString("x40", CultureInfo.InvariantCulture);
}
