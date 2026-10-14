using System.Globalization;
using MediaAcquisitionManager.Core.Abstractions.Downloads;
using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Downloads;
using MediaAcquisitionManager.Infrastructure.Integrations.Transmission;
using MediaAcquisitionManager.TestInfrastructure.Integrations;

namespace MediaAcquisitionManager.Infrastructure.Tests.Integrations;

// MAM-30 — one folder destination's jobs, for the overwrite check (ADR 0034): one torrent-get of every torrent without ids,
// as Transmission filters by no folder, keeping those whose downloadDir is the folder, compared folder name by folder name
// (D2) — never by the destination's name as a label (ADR 0039). TransmissionAdapterJobsTests has how each job reads.
public sealed class TransmissionAdapterDestinationJobsTests : IDisposable
{
    private static readonly IntegrationConnectionSettings Saved = new(
        Guid.Parse("4d2c6a1e-3b5f-4e7a-9c8d-1f0e2a3b4c5d"),
        FakeTransmissionServer.DefaultBaseUrl,
        FakeTransmissionServer.DefaultUserName,
        FakeTransmissionServer.DefaultPassword,
        null);

    private readonly IntegrationAdapterHarness _harness = new();
    private readonly FakeTransmissionServer _transmission = new();

    public TransmissionAdapterDestinationJobsTests() => _transmission.Arm(_harness.TransmissionHttp);

    private IDownloadClient Client => _harness.Adapter<IDownloadClient>(TransmissionAdapter.TypeId);

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task ListDestinationJobs_lists_the_torrents_whose_folder_is_the_destinations_and_no_other()
    {
        //Arrange
        _transmission.AddTorrent(new TorrentInfoHashes(Hash(1), null), "Interstellar (2014)", "/downloads/movies");
        // The same folder written another way.
        _transmission.AddTorrent(new TorrentInfoHashes(Hash(2), null), "Arrival (2016)", "/downloads//movies/");
        // A folder whose name starts the same, its parent, one inside it, and the destination's name as a label elsewhere.
        _transmission.AddTorrent(new TorrentInfoHashes(Hash(3), null), "Dune (2021)", "/downloads/movies2");
        _transmission.AddTorrent(new TorrentInfoHashes(Hash(4), null), "Movies", "/downloads");
        _transmission.AddTorrent(new TorrentInfoHashes(Hash(5), null), "Extras", "/downloads/movies/extras");
        _transmission.AddTorrent(new TorrentInfoHashes(Hash(6), null), "Tenet (2020)", "/downloads/tv-shows", ["Movies"]);
        // Letter case counts in a Linux folder.
        _transmission.AddTorrent(new TorrentInfoHashes(Hash(7), null), "Heat (1995)", "/downloads/Movies");

        //Act
        var result = await Client.ListDestinationJobsAsync(Saved, "/downloads/movies", TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        Assert.Equal([Hash(1), Hash(2)], result.Value!.Select(job => job.ClientJobId));
        Assert.Equal(["/downloads/movies", "/downloads//movies/"], result.Value!.Select(job => job.ServiceSavePath));
    }

    [Fact]
    public async Task ListDestinationJobs_reads_every_torrent_once_of_the_job_fields_only()
    {
        //Arrange

        //Act
        await Client.ListDestinationJobsAsync(Saved, "/downloads/movies", TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(["session-get", "torrent-get"], _transmission.Calls.Select(call => call.Method));
        var read = _transmission.Calls[1];
        Assert.Equal(["fields"], read.ArgumentNames);
        Assert.Equal(
            ["hashString", "name", "status", "error", "percentDone", "leftUntilDone", "metadataPercentComplete", "downloadDir"],
            read.Arguments["fields"]!.AsArray().Select(field => (string)field!));
    }

    [Fact]
    public async Task ListDestinationJobs_of_a_windows_folder_ignores_letter_case_and_separators()
    {
        //Arrange
        _transmission.AddTorrent(new TorrentInfoHashes(Hash(1), null), "Interstellar (2014)", @"d:/torrents\MOVIES");

        //Act
        var result = await Client.ListDestinationJobsAsync(Saved, @"D:\Torrents\Movies", TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(Hash(1), Assert.Single(result.Value!).ClientJobId);
    }

    [Fact]
    public async Task ListDestinationJobs_leaves_out_a_torrent_whose_folder_is_no_path()
    {
        //Arrange
        _transmission.AddTorrent(new TorrentInfoHashes(Hash(1), null), "Interstellar (2014)", "movies");
        _transmission.AddTorrent(new TorrentInfoHashes(Hash(2), null), "Arrival (2016)", "");

        //Act
        var result = await Client.ListDestinationJobsAsync(Saved, "/downloads/movies", TestContext.Current.CancellationToken);

        //Assert
        Assert.Empty(result.Value!);
    }

    [Fact]
    public async Task ListDestinationJobs_of_a_folder_without_torrents_is_empty()
    {
        //Arrange
        _transmission.AddTorrent(new TorrentInfoHashes(Hash(1), null), "Interstellar (2014)", "/downloads/tv-shows");

        //Act
        var result = await Client.ListDestinationJobsAsync(Saved, "/downloads/movies", TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        Assert.Empty(result.Value!);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("Movies")]
    [InlineData("downloads/movies")]
    [InlineData("/downloads/../movies")]
    public async Task ListDestinationJobs_of_a_key_that_is_no_absolute_folder_throws_before_anything_is_sent(string key)
    {
        //Arrange

        //Act
        var thrown = await Assert.ThrowsAsync<ArgumentException>(() => Client.ListDestinationJobsAsync(Saved, key, TestContext.Current.CancellationToken));

        //Assert
        Assert.Equal("destinationKey", thrown.ParamName);
        Assert.Empty(_harness.TransmissionHttp.Requests);
    }

    [Fact]
    public async Task ListDestinationJobs_of_a_transmission_with_its_temporary_folder_on_is_refused()
    {
        //Arrange
        _transmission.IncompleteDirEnabled = true;

        //Act
        var result = await Client.ListDestinationJobsAsync(Saved, "/downloads/movies", TestContext.Current.CancellationToken);

        //Assert
        // D6 — the overwrite check would look in the wrong folder for an unfinished torrent's files.
        Assert.Equal(IntegrationCallStatus.Refused, result.Status);
        Assert.Equal(["session-get"], _transmission.Calls.Select(call => call.Method));
    }

    private static string Hash(int number) => number.ToString("x40", CultureInfo.InvariantCulture);
}
