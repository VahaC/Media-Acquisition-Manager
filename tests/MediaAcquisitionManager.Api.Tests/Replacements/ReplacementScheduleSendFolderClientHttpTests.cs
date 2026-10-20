using System.Net;
using System.Net.Http.Json;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Abstractions.Downloads;
using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Paths;
using MediaAcquisitionManager.Core.Texts;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Files;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using MediaAcquisitionManager.TestInfrastructure.Texts;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Tests.Replacements;

// MAM-30, ADR 0032, ADR 0039 — a replacement sent in strict mode to a client whose destinations are folders: each
// obstacle refuses the whole send with the app's sentence naming the destination, never its folder (D13) — a folder
// outside every library (the client's own download folder), a file already where the release lands, a client that can't
// list the torrents in that folder — and the replacement fails as refused before it was sent; one the strict check lets
// through goes with the destination's name as the torrent's label. The replacement options say a folder the client can't
// see can't take a replacement, in the client's own sentence, and check a folder no library leads to as itself (D3). The
// torrents are real (TestTorrent).
public sealed class ReplacementScheduleSendFolderClientHttpTests : IClassFixture<FolderClientApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/replacements";
    private const string TolokaAddress = "http://prowlarr.test:9696/1/api";
    private const string TolokaApiKey = "planted-torznab-key-0030";
    private const string JellyfinApiKey = "0123456789abcdef0123456789abcdef";
    private const string AlienImdbId = "tt0078748";
    private const string AlienRelease = "Alien (1979) Director's Cut BDRemux 2160p HDR";
    private const string FilmsFolder = "/media/movies";
    private const string MoviesFolder = "/downloads/movies";
    private const string TheatricalPath = "/media/movies/Alien (1979)/Alien (1979) Theatrical Cut.mkv";
    private const string TorrentName = "Alien.1979.Directors.Cut.2160p.mkv";

    // Sentences the Jobs page and the dialog show, spelled out rather than read from product code.
    private const string RefusedBeforeSentReason = "The download was refused before it was sent, so nothing will be replaced.";
    private const string OutsideLibrariesError =
        "The folder of the destination “Unsorted” isn't inside a library the app knows, so nothing was sent and no copy will be replaced. Pick Keep both to download it, or the owner fixes the destination.";
    private const string AlreadyThereError =
        "“Transmission” would write over “Alien.1979.Directors.Cut.2160p.mkv” in the folder of the destination “Фільми”: a file is already there, and a replacement never lands on a file already there, so nothing was sent.";
    private const string ClientJobsUncheckedError =
        "The app couldn't check every place this release would write in the folder of the destination “Фільми” (the client couldn't list its torrents in that folder, or their files), so nothing was sent and no copy will be replaced. Pick Keep both to download it.";

    private readonly FolderClientApiFactory _factory;
    private IReleaseSeeder _releases = null!;
    private AuthorizedUser _owner = null!;
    private IntegrationConnectionEntity _toloka = null!;
    private IntegrationConnectionEntity _client = null!;
    private DestinationMappingEntity _movies = null!;
    private byte[] _content = null!;
    private Guid _theatrical;

    public ReplacementScheduleSendFolderClientHttpTests(FolderClientApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        var cancellationToken = TestContext.Current.CancellationToken;
        _owner = await new UserSeeder(_factory.Scope).CreateAuthorizedOwner("owner");
        var integrations = new IntegrationConnectionSeeder(_factory.Scope);
        var jellyfin = await integrations.Create("jellyfin", "Living room", apiKey: JellyfinApiKey);
        _content = TestTorrent.Bytes((3 * TestTorrent.PieceLength) + 5, seed: 1);
        new FakeTorznabServer(TolokaAddress, TolokaApiKey) { TorrentFile = TestTorrent.V1(TorrentName, _content) }.Arm(_factory.TorznabHttp);
        _toloka = await integrations.Create("torznab", "Toloka", TolokaAddress, apiKey: TolokaApiKey);
        // Transmission's /downloads is the app's /media: Фільми is the library folder /media/movies.
        _client = await integrations.Create(FakeFolderDownloadClient.TypeId, "Transmission", pathMappings: [new PathMappingPair("/downloads", "/media")]);
        _movies = await new DestinationMappingSeeder(_factory.Scope).Create(_client, "Фільми", MoviesFolder, isDefault: true);
        _releases = new ReleaseSeeder(_factory.Scope);
        var films = await new LibraryLocationSeeder(_factory.Scope).Create(jellyfin, "Фільми", FilmsFolder);
        await new AllowedRootSeeder(_factory.Scope).Allow("/media");
        _factory.FolderClient.Arm();

        // Alien's theatrical cut, inspected, in the ticked Фільми: a copy the owner may tick.
        var works = new MediaWorkSeeder(_factory.Scope);
        var alien = await works.CreateMovie(jellyfin, "Alien", 1979, imdbId: AlienImdbId);
        await works.AddVersion(jellyfin, alien, Guid.NewGuid().ToString("N"), films, "Theatrical Cut", TheatricalPath);
        _factory.Files.AddFile(TheatricalPath, TestMedia.File(inode: 1), TestMedia.Taken());
        await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);
        await new ReplacementSettingsSeeder(_factory.Scope).SwitchOn(FilmsFolder);
        await _factory.WithDbAsync(async db => _theatrical = await db.PhysicalFiles.Where(file => file.ServicePath == TheatricalPath).Select(file => file.Id).SingleAsync(cancellationToken));
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Schedule_through_the_strict_check_sends_the_folder_with_the_destinations_name_as_its_label()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var release = await FindAlienAsync();

        //Act
        var response = await ScheduleAsync(release, _movies);

        //Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var job = (await response.Content.ReadFromJsonAsync<DownloadJobDto>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
        Assert.Equal(
            (DownloadJobStatus.Submitted, (DownloadOverwriteCheck?)DownloadOverwriteCheck.Checked, ReplacementJobState.Submitted),
            (job.Status, job.OverwriteCheck, job.Replacement!.State));
        var submission = Assert.Single(_factory.FolderClient.Submissions);
        Assert.Equal((MoviesFolder, (string?)"Фільми"), (submission.DestinationKey, submission.Label));
    }

    [Fact]
    public async Task Schedule_to_the_clients_download_folder_outside_every_library_fails_naming_the_destination()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // The client's own folder, /media/complete in the app: no library holds it, so nothing could verify a new file.
        var unsorted = await new DestinationMappingSeeder(_factory.Scope).Create(_client, "Unsorted", FakeFolderDownloadClient.DefaultDownloadFolder);
        var release = await FindAlienAsync();

        //Act
        var response = await ScheduleAsync(release, unsorted);

        //Assert
        await AssertRefusedAsync(response, OutsideLibrariesError, cancellationToken);
    }

    [Fact]
    public async Task Schedule_whose_file_is_already_where_it_lands_fails_naming_the_destination()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        _factory.Files.AddContent($"{FilmsFolder}/{TorrentName}", _content);
        var release = await FindAlienAsync();

        //Act
        var response = await ScheduleAsync(release, _movies);

        //Assert
        await AssertRefusedAsync(response, AlreadyThereError, cancellationToken);
        Assert.Empty(_factory.Files.PiecesHashedPaths);
    }

    [Fact]
    public async Task Schedule_when_the_client_can_not_list_the_folders_torrents_fails_saying_that_folder()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        _factory.FolderClient.DestinationJobsFailure = IntegrationCallResult<IReadOnlyList<ClientJob>>.Failed(
            IntegrationCallStatus.TimedOut, Message.Legacy("Transmission didn't answer in time."));
        var release = await FindAlienAsync();

        //Act
        var response = await ScheduleAsync(release, _movies);

        //Assert
        await AssertRefusedAsync(response, ClientJobsUncheckedError, cancellationToken);
    }

    [Fact]
    public async Task Options_for_a_folder_the_client_can_not_see_is_DestinationNotUsable_with_the_clients_sentence()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        _factory.FolderClient.CantSee(MoviesFolder);
        var release = await FindAlienAsync();

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}/options?releaseId={release.Id}&destinationId={_movies.Id}&holdings=true", _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var options = (await response.Content.ReadFromJsonAsync<ReplacementOptionsResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
        Assert.Equal(
            ((ReplacementUnavailableReason?)ReplacementUnavailableReason.DestinationNotUsable, FakeFolderDownloadClient.CantSeeProblem(MoviesFolder)),
            (options.Unavailable, options.DestinationProblem));
        Assert.Empty(options.Works);
        // The folder was proposed and its listing read, and nothing else: no torrent listing, nothing sent.
        Assert.Equal(["ListDestinations"], _factory.FolderClient.Calls);
    }

    [Fact]
    public async Task Options_for_a_destinations_folder_no_library_leads_to_say_it_is_outside_every_library()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // The app's /media/archive, which no library holds: only this destination names the folder.
        var archive = await new DestinationMappingSeeder(_factory.Scope).Create(_client, "Архів", "/downloads/archive");
        var release = await FindAlienAsync();

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}/options?releaseId={release.Id}&destinationId={archive.Id}&holdings=true", _owner.AccessToken);

        //Assert
        // D3 — proposed, so the client lists it and its check is its own: outside the libraries, never "it didn't list
        // the folder".
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var options = (await response.Content.ReadFromJsonAsync<ReplacementOptionsResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
        Assert.Equal(
            ((ReplacementUnavailableReason?)ReplacementUnavailableReason.DestinationNotUsable,
                (string?)"“/media/archive” is outside every media library folder, so automatic replacement stays off for this destination."),
            (options.Unavailable, options.DestinationProblem));
        Assert.Equal([[MoviesFolder, "/downloads/archive"]], _factory.FolderClient.Proposals);
    }

    [Fact]
    public async Task Options_for_a_folder_the_client_sees_in_a_library_lists_the_works()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var release = await FindAlienAsync();

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}/options?releaseId={release.Id}&destinationId={_movies.Id}&holdings=true", _owner.AccessToken);

        //Assert
        var options = (await response.Content.ReadFromJsonAsync<ReplacementOptionsResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
        Assert.Equal(((ReplacementUnavailableReason?)null, (string?)null), (options.Unavailable, options.DestinationProblem));
        Assert.Single(options.Works);
        // Read for its holdings with the other clients, as any download client is.
        Assert.Contains("ListJobsWithFiles", _factory.FolderClient.Calls);
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    private Task<ReleaseEntity> FindAlienAsync() =>
        _releases.CreateFoundBySearch(_owner.User, _toloka, "Alien", 1979, AlienRelease, imdbId: AlienImdbId);

    private Task<HttpResponseMessage> ScheduleAsync(ReleaseEntity release, DestinationMappingEntity destination) =>
        _factory.PostHttpRequest(
            BaseUrl, _owner.AccessToken, new ScheduleReplacementRequest(Guid.NewGuid(), release.Id, _client.Id, destination.Id, [_theatrical]));

    /// <summary>
    /// 201 with the job Failed with the app's own <paramref name="error"/> and no check recorded, its replacement Failed
    /// as refused before it was sent; the same on their rows; and nothing handed to the client.
    /// </summary>
    private async Task AssertRefusedAsync(HttpResponseMessage response, string error, CancellationToken cancellationToken)
    {
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var job = (await response.Content.ReadFromJsonAsync<DownloadJobDto>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
        Assert.Equal((DownloadJobStatus.Failed, error, (DownloadOverwriteCheck?)null), (job.Status, job.Error, job.OverwriteCheck));
        Assert.Equal((ReplacementJobState.Failed, (string?)RefusedBeforeSentReason), (job.Replacement!.State, job.Replacement.Reason));
        await _factory.WithDbAsync(async db =>
        {
            var row = await db.DownloadJobs.AsNoTracking().SingleAsync(cancellationToken);
            Assert.Equal((DownloadJobStatus.Failed, error, (DownloadJobErrorOrigin?)DownloadJobErrorOrigin.App), (row.Status, TestTexts.En(row.Error), row.ErrorOrigin));
            var replacement = await db.ReplacementJobs.AsNoTracking().SingleAsync(cancellationToken);
            Assert.Equal((ReplacementJobState.Failed, (string?)RefusedBeforeSentReason), (replacement.State, TestTexts.En(replacement.Reason)));
        });
        Assert.Empty(_factory.FolderClient.Submissions);
    }
}
