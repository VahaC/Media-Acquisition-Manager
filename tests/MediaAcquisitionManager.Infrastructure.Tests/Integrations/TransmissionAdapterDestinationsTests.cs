using System.Net;
using System.Text.Json.Nodes;
using MediaAcquisitionManager.Core.Abstractions.Downloads;
using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Infrastructure.Integrations.Transmission;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;

namespace MediaAcquisitionManager.Infrastructure.Tests.Integrations;

// MAM-30 — Transmission's destinations (ADR 0039, D1, D3, D6, D18): its own download-dir and each folder the app proposes,
// merged when they are the same folder, each confirmed by one free-space or listed with why Transmission can't use it —
// never hidden; only session-get and free-space are sent. And the capabilities the app reads instead of Transmission's
// name. TransmissionAdapterSessionsTests has the session the listing opens.
public sealed class TransmissionAdapterDestinationsTests : IDisposable
{
    private static readonly IntegrationConnectionSettings Saved = new(
        Guid.Parse("4d2c6a1e-3b5f-4e7a-9c8d-1f0e2a3b4c5d"),
        FakeTransmissionServer.DefaultBaseUrl,
        FakeTransmissionServer.DefaultUserName,
        FakeTransmissionServer.DefaultPassword,
        null);

    private readonly IntegrationAdapterHarness _harness = new();
    private readonly FakeTransmissionServer _transmission = new();

    public TransmissionAdapterDestinationsTests() => _transmission.Arm(_harness.TransmissionHttp);

    /// <summary>Proposed folders the app never proposes: none is an absolute path it can parse.</summary>
    public static TheoryData<string> NotFolders => new()
    {
        "",
        "   ",
        "downloads/movies",
        "~/movies",
        "C:movies",
        "/downloads/../etc",
        "/downloads/movies\n",
    };

    private IDownloadClient Client => _harness.Adapter<IDownloadClient>(TransmissionAdapter.TypeId);

    public void Dispose() => _harness.Dispose();

    [Fact]
    public void Capabilities_take_torrents_and_magnets_into_folders_with_a_hash_id_its_own_layout_and_part_files()
    {
        //Arrange

        //Act
        var capabilities = Client.Capabilities;

        //Assert
        // ADR 0039: folders the client confirms; D11: .part; D12: no BitTorrent v2-only torrent; D19: ids never change.
        Assert.Equal([ReleasePayloadKind.TorrentFile, ReleasePayloadKind.Magnet], capabilities.PayloadKinds);
        Assert.True(capabilities.DestinationsHavePaths);
        Assert.True(capabilities.SubmissionReturnsJobId);
        Assert.Equal(DownloadDestinationKind.Folder, capabilities.DestinationKind);
        Assert.Equal(TorrentFilePlacement.OwnLayoutOnly, capabilities.FilePlacement);
        Assert.Equal(".part", capabilities.PartialFileSuffix);
        Assert.False(capabilities.TakesV2OnlyTorrents);
        Assert.False(capabilities.JobIdsMayChange);
    }

    [Fact]
    public async Task ListDestinations_of_nothing_proposed_lists_transmissions_download_folder_confirmed_by_free_space()
    {
        //Arrange

        //Act
        var result = await Client.ListDestinationsAsync(Saved, [], TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        Assert.Equal([new ClientDestination("/downloads", "/downloads", "/downloads", IsClientDefaultFolder: true)], result.Value);
        Assert.Equal(["session-get", "free-space"], _transmission.Calls.Select(call => call.Method));
        var freeSpace = _transmission.Calls[1];
        Assert.Equal(["path"], freeSpace.ArgumentNames);
        Assert.Equal("/downloads", (string?)freeSpace.Arguments["path"]);
    }

    [Theory]
    [InlineData("4.1.3")]
    [InlineData("4.0.6")]
    public async Task ListDestinations_lists_each_proposed_folder_transmission_sees_and_the_one_it_does_not_with_why(string version)
    {
        //Arrange
        // A folder not mounted into Transmission answers like a missing one; each version spells the total its own way.
        _transmission.Version = version == "4.0.6" ? FakeTransmissionVersion.V4_0_6 : FakeTransmissionVersion.V4_1_3;
        _transmission.AddFolder("/downloads/movies");

        //Act
        var result = await Client.ListDestinationsAsync(Saved, ["/downloads/movies", "/downloads/tv-shows"], TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        Assert.Equal(
            [
                new ClientDestination("/downloads", "/downloads", "/downloads", IsClientDefaultFolder: true),
                new ClientDestination("/downloads/movies", "/downloads/movies", "/downloads/movies"),
                new ClientDestination(
                    "/downloads/tv-shows",
                    "/downloads/tv-shows",
                    "/downloads/tv-shows",
                    Problem: "Transmission can't see “/downloads/tv-shows” (No such file or directory). Mount that folder into Transmission, or add a path mapping on this integration that leads there, then look again."),
            ],
            result.Value);
        Assert.Equal(["/downloads", "/downloads/movies", "/downloads/tv-shows"], FreeSpacePaths());
    }

    [Theory]
    [InlineData("/downloads")]
    [InlineData("/downloads/")]
    [InlineData("//downloads//")]
    public async Task ListDestinations_lists_a_proposed_folder_that_is_transmissions_own_once_as_its_download_folder(string proposed)
    {
        //Arrange

        //Act
        var result = await Client.ListDestinationsAsync(Saved, [proposed], TestContext.Current.CancellationToken);

        //Assert
        // D18 — a library folder that is Transmission's download folder is one destination, marked as its folder.
        Assert.Equal([new ClientDestination("/downloads", "/downloads", "/downloads", IsClientDefaultFolder: true)], result.Value);
        Assert.Equal(["/downloads"], FreeSpacePaths());
    }

    [Fact]
    public async Task ListDestinations_asks_once_for_a_folder_proposed_twice_in_two_spellings()
    {
        //Arrange
        _transmission.AddFolder("/downloads/movies");

        //Act
        var result = await Client.ListDestinationsAsync(Saved, ["/downloads/movies", "/downloads//movies/"], TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(2, result.Value!.Count);
        Assert.Equal(["/downloads", "/downloads/movies"], FreeSpacePaths());
    }

    [Fact]
    public async Task ListDestinations_lists_a_proposed_folder_inside_the_download_folder_as_its_own_destination()
    {
        //Arrange
        _transmission.DownloadDir = "/media";
        _transmission.AddFolder("/media");
        _transmission.AddFolder("/media/movies");

        //Act
        var result = await Client.ListDestinationsAsync(Saved, ["/media/movies"], TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(
            [
                new ClientDestination("/media", "/media", "/media", IsClientDefaultFolder: true),
                new ClientDestination("/media/movies", "/media/movies", "/media/movies"),
            ],
            result.Value);
    }

    [Theory]
    [InlineData("/downloads/", "/downloads")]
    [InlineData("///downloads//complete", "/downloads/complete")]
    public async Task ListDestinations_lists_the_download_folder_in_its_canonical_form(string downloadDir, string canonical)
    {
        //Arrange
        _transmission.DownloadDir = downloadDir;
        _transmission.AddFolder(canonical);

        //Act
        var result = await Client.ListDestinationsAsync(Saved, [], TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal([new ClientDestination(canonical, canonical, canonical, IsClientDefaultFolder: true)], result.Value);
        Assert.Equal([canonical], FreeSpacePaths());
    }

    [Theory]
    [InlineData("downloads")]
    [InlineData("//downloads")]
    [InlineData("")]
    public async Task ListDestinations_lists_a_download_folder_that_is_no_path_the_app_can_use_with_why_and_never_asks_about_it(string downloadDir)
    {
        //Arrange
        _transmission.DownloadDir = downloadDir;
        _transmission.AddFolder("/downloads/movies");

        //Act
        var result = await Client.ListDestinationsAsync(Saved, ["/downloads/movies"], TestContext.Current.CancellationToken);

        //Assert
        // "//downloads" is absolute, but POSIX lets the system read a path that starts with exactly two slashes its own way.
        Assert.Equal(
            [
                new ClientDestination(
                    downloadDir,
                    downloadDir,
                    downloadDir,
                    IsClientDefaultFolder: true,
                    Problem: $"Transmission's download folder “{downloadDir}” isn't a full path the app can use. In Transmission's web interface open Edit Preferences, Torrents, give “Download to” a full path such as /downloads, then look again."),
                new ClientDestination("/downloads/movies", "/downloads/movies", "/downloads/movies"),
            ],
            result.Value);
        Assert.Equal(["/downloads/movies"], FreeSpacePaths());
    }

    [Fact]
    public async Task ListDestinations_lists_a_windows_download_folder_by_its_canonical_form_and_transmissions_reason()
    {
        //Arrange
        // The fake is a Linux Transmission, which reads a drive letter as a relative path.
        _transmission.DownloadDir = @"d:/Torrents\";

        //Act
        var result = await Client.ListDestinationsAsync(Saved, [], TestContext.Current.CancellationToken);

        //Assert
        var destination = Assert.Single(result.Value!);
        Assert.Equal(@"D:\Torrents", destination.Key);
        Assert.Equal(
            @"Transmission can't see “D:\Torrents” (directory path is not absolute). Mount that folder into Transmission, or add a path mapping on this integration that leads there, then look again.",
            destination.Problem);
        Assert.Equal([@"D:\Torrents"], FreeSpacePaths());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ListDestinations_lists_a_folder_longer_than_a_destination_holds_with_why_and_never_asks_about_it(bool isDownloadFolder)
    {
        //Arrange
        var longFolder = "/downloads/" + new string('m', 250);
        if (isDownloadFolder)
            _transmission.DownloadDir = longFolder;

        //Act
        var result = await Client.ListDestinationsAsync(Saved, isDownloadFolder ? [] : [longFolder], TestContext.Current.CancellationToken);

        //Assert
        var destination = Assert.Single(result.Value!, listed => listed.Key == longFolder);
        Assert.Equal(isDownloadFolder, destination.IsClientDefaultFolder);
        Assert.Equal(
            $"{(isDownloadFolder ? "Transmission's download folder" : "The folder")} “{longFolder}” is longer than the 256 characters a destination can hold.",
            destination.Problem);
        Assert.DoesNotContain(longFolder, FreeSpacePaths());
    }

    [Fact]
    public async Task ListDestinations_reads_a_success_without_free_bytes_as_a_folder_transmission_could_not_measure()
    {
        //Arrange
        _transmission.RpcIntercept = call => call.Method == "free-space"
            ? new JsonObject { ["arguments"] = new JsonObject { ["path"] = "/downloads", ["size-bytes"] = -1 }, ["result"] = "success" }
            : null;

        //Act
        var result = await Client.ListDestinationsAsync(Saved, [], TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(
            "Transmission can't see “/downloads” (no free space reported). Mount that folder into Transmission, or add a path mapping on this integration that leads there, then look again.",
            Assert.Single(result.Value!).Problem);
    }

    [Fact]
    public async Task ListDestinations_reads_the_free_bytes_written_as_a_fraction_and_never_the_total()
    {
        //Arrange
        // 4.1 writes a whole number as 1.0; the total is left out altogether.
        _transmission.RpcIntercept = call => call.Method == "free-space"
            ? new JsonObject { ["arguments"] = new JsonObject { ["size-bytes"] = JsonNode.Parse("52361691136.0") }, ["result"] = "success" }
            : null;

        //Act
        var result = await Client.ListDestinationsAsync(Saved, [], TestContext.Current.CancellationToken);

        //Assert
        Assert.Null(Assert.Single(result.Value!).Problem);
    }

    [Fact]
    public async Task ListDestinations_masks_the_password_in_transmissions_reason()
    {
        //Arrange
        _transmission.RpcIntercept = call => call.Method == "free-space"
            ? new JsonObject { ["arguments"] = new JsonObject(), ["result"] = $"denied for {FakeTransmissionServer.DefaultPassword}\u202E" }
            : null;

        //Act
        var result = await Client.ListDestinationsAsync(Saved, [], TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(
            "Transmission can't see “/downloads” (denied for ***). Mount that folder into Transmission, or add a path mapping on this integration that leads there, then look again.",
            Assert.Single(result.Value!).Problem);
    }

    [Theory]
    [MemberData(nameof(NotFolders))]
    public async Task ListDestinations_of_a_proposed_folder_that_is_no_absolute_path_throws_and_sends_nothing(string proposed)
    {
        //Arrange

        //Act
        var thrown = await Assert.ThrowsAsync<ArgumentException>(
            () => Client.ListDestinationsAsync(Saved, ["/downloads/movies", proposed], TestContext.Current.CancellationToken));

        //Assert
        Assert.Equal("proposedFolders", thrown.ParamName);
        Assert.Empty(_harness.TransmissionHttp.Requests);
    }

    [Fact]
    public async Task ListDestinations_of_transmission_3_00_is_an_unsupported_version_and_asks_about_no_folder()
    {
        //Arrange
        _transmission.Version = FakeTransmissionVersion.V3_00;

        //Act
        var result = await Client.ListDestinationsAsync(Saved, ["/downloads/movies"], TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.UnsupportedVersion, result.Status);
        Assert.Equal("This is Transmission 3.00; the app needs Transmission 4.0 or newer. Update Transmission, then test again.", result.Error);
        Assert.Equal(["session-get"], _transmission.Calls.Select(call => call.Method));
    }

    [Fact]
    public async Task ListDestinations_with_the_temporary_folder_on_is_refused_with_how_to_turn_it_off()
    {
        //Arrange
        _transmission.IncompleteDirEnabled = true;
        _transmission.IncompleteDir = "/downloads/.incomplete";

        //Act
        var result = await Client.ListDestinationsAsync(Saved, ["/downloads/movies"], TestContext.Current.CancellationToken);

        //Assert
        // D6 — every call, not only the test.
        Assert.Equal(IntegrationCallStatus.Refused, result.Status);
        Assert.Equal(
            "Transmission keeps unfinished downloads in its temporary folder “/downloads/.incomplete”, where the app can't follow them. In Transmission's web interface open Edit Preferences, Torrents, untick “Use temporary folder”, then test again.",
            result.Error);
        Assert.Equal(["session-get"], _transmission.Calls.Select(call => call.Method));
    }

    [Fact]
    public async Task ListDestinations_whose_free_space_fails_on_the_way_fails_the_whole_listing()
    {
        //Arrange
        _transmission.AddFolder("/downloads/movies");
        _transmission.Intercept = (_, call) => call?.Method == "free-space" && (string?)call.Arguments["path"] == "/downloads/movies"
            ? new HttpResponseMessage(HttpStatusCode.BadGateway)
            : null;

        //Act
        var result = await Client.ListDestinationsAsync(Saved, ["/downloads/movies", "/downloads/tv-shows"], TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.ServiceError, result.Status);
        Assert.Equal("Transmission answered 502 Bad Gateway. Its own log says why.", result.Error);
        Assert.Equal(["/downloads"], FreeSpacePaths());
    }

    [Fact]
    public async Task ListDestinations_whose_free_space_answer_is_not_transmissions_fails_the_whole_listing()
    {
        //Arrange
        _transmission.Intercept = (_, call) => call?.Method == "free-space"
            ? VendorResponses.Text(FakeTransmissionServer.WebInterfacePage, HttpStatusCode.OK, "text/html")
            : null;

        //Act
        var result = await Client.ListDestinationsAsync(Saved, [], TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.UnexpectedResponse, result.Status);
        Assert.Equal("The service at this address answered, but not the way Transmission does.", result.Error);
    }

    [Fact]
    public async Task ListDestinations_sends_session_get_and_free_space_only()
    {
        //Arrange
        _transmission.AddFolder("/downloads/movies");

        //Act
        await Client.ListDestinationsAsync(Saved, ["/downloads/movies", "/downloads/tv-shows", "/media/cartoons"], TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(["session-get", "free-space", "free-space", "free-space", "free-space"], _transmission.Calls.Select(call => call.Method));
        Assert.Empty(_transmission.Writes);
    }

    /// <summary>The folders every <c>free-space</c> the server read asked about, in order.</summary>
    private List<string?> FreeSpacePaths() =>
        [.. _transmission.Calls.Where(call => call.Method == "free-space").Select(call => (string?)call.Arguments["path"])];
}
