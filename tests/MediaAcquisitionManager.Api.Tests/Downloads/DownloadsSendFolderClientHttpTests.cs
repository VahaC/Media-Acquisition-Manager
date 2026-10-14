using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Abstractions.Downloads;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Paths;
using MediaAcquisitionManager.Infrastructure.Persistence;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Data;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MediaAcquisitionManager.Api.Tests.Downloads;

// MAM-30, ADR 0039 — sending to a client whose destinations are folders: the destination's folder is proposed to the
// client and found by folder (D2, D3), and the one submission carries that folder and the destination's name as the
// torrent's label; the job snapshots that its destination is a folder (D14) and is followed by the client's id. A folder
// the client can't see or use any more, or one that isn't a full path, refuses the send with the app's own sentence
// naming the destination (D5, D13); a torrent that is BitTorrent v2 only is refused before anything is listed or sent
// (D12). Members never see a folder: the Download dialog's options carry none, and a job's folder is the owner's to
// read (D13). The Download dialog's question before Send says nothing is sent to a folder the client can't see (D5).
public sealed class DownloadsSendFolderClientHttpTests : IClassFixture<FolderClientApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/downloads";
    private const string TolokaAddress = "http://prowlarr.test:9696/1/api";
    private const string ApiKey = "planted-torznab-key-0030";
    private const string JellyfinApiKey = "0123456789abcdef0123456789abcdef";
    private const string MoviesFolder = "/downloads/movies";
    private const string ArchiveFolder = "/downloads/archive";
    private const string UnusableError =
        "“Transmission” can't use the folder of the destination “Фільми” any more, so nothing was sent. The owner checks the destination under Destinations in the user menu.";
    private const string V2OnlyError =
        "“Transmission” can't take a torrent that is BitTorrent v2 only, which this release is, so nothing was sent.";

    private readonly FolderClientApiFactory _factory;
    private IReleaseSeeder _releases = null!;
    private AuthorizedUser _owner = null!;
    private AuthorizedUser _member = null!;
    private FakeTorznabServer _feed = null!;
    private IntegrationConnectionEntity _toloka = null!;
    private IntegrationConnectionEntity _client = null!;
    private DestinationMappingEntity _movies = null!;
    private byte[] _content = null!;

    public DownloadsSendFolderClientHttpTests(FolderClientApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        var integrations = new IntegrationConnectionSeeder(_factory.Scope);
        _releases = new ReleaseSeeder(_factory.Scope);
        var users = new UserSeeder(_factory.Scope);
        _owner = await users.CreateAuthorizedOwner("owner");
        _member = await users.CreateAuthorized("member");
        _content = TestTorrent.Bytes((3 * TestTorrent.PieceLength) + 5, seed: 1);
        _feed = new FakeTorznabServer(TolokaAddress, ApiKey) { TorrentFile = TestTorrent.V1("Film.2001.mkv", _content) };
        _feed.Arm(_factory.TorznabHttp);
        _toloka = await integrations.Create("torznab", "Toloka", TolokaAddress, apiKey: ApiKey);
        var jellyfin = await integrations.Create("jellyfin", "Living room", apiKey: JellyfinApiKey);
        await new LibraryLocationSeeder(_factory.Scope).Create(jellyfin, "Фільми", "/media/movies");
        // Transmission's /downloads is the app's /media: the destination Фільми is the library's folder.
        _client = await integrations.Create(FakeFolderDownloadClient.TypeId, "Transmission", pathMappings: [new PathMappingPair("/downloads", "/media")]);
        _movies = await new DestinationMappingSeeder(_factory.Scope).Create(_client, "Фільми", MoviesFolder, isDefault: true);
        await new AllowedRootSeeder(_factory.Scope).Allow("/media");
        _factory.FolderClient.Arm();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    // ── sent ────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Create_sends_the_folder_with_the_destinations_name_as_its_label_once_and_follows_it_by_its_hash()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var release = await FindReleaseAsync(_member);

        //Act
        var response = await SendAsync(_member, release, _movies);

        //Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var job = (await response.Content.ReadFromJsonAsync<DownloadJobDto>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
        var row = await JobRowAsync(cancellationToken);
        Assert.Equal(
            (DownloadJobStatus.Submitted, row.InfoHashV1, (DownloadOverwriteCheck?)DownloadOverwriteCheck.Checked, (DownloadDestinationKind?)DownloadDestinationKind.Folder),
            (job.Status, job.ClientJobId, job.OverwriteCheck, job.DestinationKind));
        // D14 — the row knows its key is a folder, whatever becomes of the client's type.
        Assert.Equal(
            (MoviesFolder, (DownloadDestinationKind?)DownloadDestinationKind.Folder, "Фільми"),
            (row.ClientDestinationKey, row.DestinationKind, row.DestinationName));
        var submission = Assert.Single(_factory.FolderClient.Submissions);
        Assert.Equal((MoviesFolder, (string?)"Фільми", row.InfoHashV1), (submission.DestinationKey, submission.Label, submission.ExpectedInfoHash));
        // The library folder and the stored one are one folder, proposed once; the client's own torrents in it read
        // for the overwrite check, then the one submission.
        Assert.Equal([[MoviesFolder]], _factory.FolderClient.Proposals);
        Assert.Equal(["ListDestinations", "FindJobs", $"ListDestinationJobs {MoviesFolder}", "Submit"], _factory.FolderClient.Calls);
    }

    [Fact]
    public async Task Create_to_a_destination_saved_in_another_spelling_of_the_folder_finds_it_by_the_folder()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // A row of a hand-edited database: the same folder with a trailing slash.
        var films = await new DestinationMappingSeeder(_factory.Scope).Create(_client, "Кіно", "/downloads/movies/");
        var release = await FindReleaseAsync(_owner);

        //Act
        var response = await SendAsync(_owner, release, films);

        //Assert
        // D2 — one folder however it is spelt; sent as the destination keeps it.
        var job = (await response.Content.ReadFromJsonAsync<DownloadJobDto>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
        Assert.Equal(DownloadJobStatus.Submitted, job.Status);
        Assert.Equal("/downloads/movies/", Assert.Single(_factory.FolderClient.Submissions).DestinationKey);
        Assert.Equal([[MoviesFolder]], _factory.FolderClient.Proposals);
    }

    [Fact]
    public async Task Create_to_a_destination_whose_folder_no_library_leads_to_proposes_that_folder_and_sends_there()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // The app's /media/archive, which no library holds — a library that has moved since, say: only this destination
        // names the folder, so only its own proposal can have the client list it.
        var archive = await new DestinationMappingSeeder(_factory.Scope).Create(_client, "Архів", ArchiveFolder);
        var release = await FindReleaseAsync(_owner);

        //Act
        var response = await SendAsync(_owner, release, archive);

        //Assert
        // D3 — proposed, confirmed by the client and checked as itself, never refused as a folder it didn't list.
        var job = (await response.Content.ReadFromJsonAsync<DownloadJobDto>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
        Assert.Equal(
            (DownloadJobStatus.Submitted, (string?)null, (DownloadOverwriteCheck?)DownloadOverwriteCheck.Checked),
            (job.Status, job.Error, job.OverwriteCheck));
        var submission = Assert.Single(_factory.FolderClient.Submissions);
        Assert.Equal((ArchiveFolder, (string?)"Архів"), (submission.DestinationKey, submission.Label));
        Assert.Equal([[MoviesFolder, ArchiveFolder]], _factory.FolderClient.Proposals);
    }

    // ── refused ─────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Create_to_a_folder_the_client_can_not_see_fails_with_the_apps_sentence_naming_the_destination()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        _factory.FolderClient.CantSee(MoviesFolder);
        var release = await FindReleaseAsync(_member);

        //Act
        var response = await SendAsync(_member, release, _movies);

        //Assert
        // D5 — the client would create the folder in its own filesystem. D13 — the member reads the destination's name,
        // never its folder; the app's sentence, so nobody's is swapped for another.
        await AssertRefusedAsync(response, UnusableError, cancellationToken);
        Assert.DoesNotContain(MoviesFolder, UnusableError, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Create_to_a_stored_folder_that_is_not_a_full_path_fails_the_same_way_and_never_proposes_it()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _factory.WithDbAsync(db =>
            db.DestinationMappings.Where(destination => destination.Id == _movies.Id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(destination => destination.ClientDestinationKey, "movies"), cancellationToken));
        var release = await FindReleaseAsync(_owner);

        //Act
        var response = await SendAsync(_owner, release, _movies);

        //Assert
        // D3 — the client still answers for its other folders; this one is this destination's problem alone.
        await AssertRefusedAsync(response, UnusableError, cancellationToken);
        Assert.Equal([[MoviesFolder]], _factory.FolderClient.Proposals);
    }

    [Fact]
    public async Task Create_of_a_torrent_that_is_bittorrent_v2_only_fails_before_the_client_is_asked_anything()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        _feed.TorrentFile = TestTorrent.V2("Film.2001.mkv", [(["Film.2001.mkv"], _content)]);
        var release = await FindReleaseAsync(_member);

        //Act
        var response = await SendAsync(_member, release, _movies);

        //Assert
        // D12 — what the release is, the app's own sentence: nothing listed, looked up or sent.
        await AssertRefusedAsync(response, V2OnlyError, cancellationToken);
        Assert.Empty(_factory.FolderClient.Calls);
    }

    [Fact]
    public async Task Create_of_a_magnet_that_is_bittorrent_v2_only_fails_before_the_client_is_asked_anything()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var release = await _releases.CreateFound(
            _owner.User,
            _toloka,
            "Film (2001) WEBRip",
            $"magnet:?xt=urn:btmh:1220{new string('a', 64)}&dn=Film",
            payloadKind: ReleasePayloadKind.Magnet);

        //Act
        var response = await SendAsync(_owner, release, _movies);

        //Assert
        await AssertRefusedAsync(response, V2OnlyError, cancellationToken);
        Assert.Empty(_factory.FolderClient.Calls);
    }

    // ── what members see ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Options_offer_a_folder_clients_destinations_without_their_folders_to_the_owner_and_to_a_member()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var forOwner = await FindReleaseAsync(_owner);
        var forMember = await FindReleaseAsync(_member);

        //Act
        var owners = await _factory.GetHttpRequest($"{BaseUrl}/options?releaseId={forOwner.Id}", _owner.AccessToken);
        var members = await _factory.GetHttpRequest($"{BaseUrl}/options?releaseId={forMember.Id}", _member.AccessToken);

        //Assert
        // D13 — read as the text came: the key is there, and null.
        foreach (var response in new[] { owners, members })
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            var client = Assert.Single(document.RootElement.GetProperty("clients").EnumerateArray());
            Assert.Equal("Folder", client.GetProperty("destinationKind").GetString());
            var destination = Assert.Single(client.GetProperty("destinations").EnumerateArray());
            Assert.Equal(("Фільми", JsonValueKind.Null), (destination.GetProperty("name").GetString(), destination.GetProperty("clientDestinationKey").ValueKind));
        }
    }

    [Fact]
    public async Task Get_of_a_folder_job_gives_its_folder_to_the_owner_alone()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var membersJob = await PlantJobAsync(_member, DownloadDestinationKind.Folder);
        var ownersJob = await PlantJobAsync(_owner, DownloadDestinationKind.Folder);

        //Act
        var members = await _factory.GetHttpRequest($"{BaseUrl}/{membersJob.Id}", _member.AccessToken);
        var owners = await _factory.GetHttpRequest($"{BaseUrl}/{ownersJob.Id}", _owner.AccessToken);
        var membersList = await _factory.GetHttpRequest(BaseUrl, _member.AccessToken);

        //Assert
        // D13 — the member reads the destination by its name alone, on the job and in the list.
        var member = (await members.Content.ReadFromJsonAsync<DownloadJobDto>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
        var owner = (await owners.Content.ReadFromJsonAsync<DownloadJobDto>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
        var listed = (await membersList.Content.ReadFromJsonAsync<DownloadJobListResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
        Assert.Equal(("Фільми", (string?)null, (DownloadDestinationKind?)DownloadDestinationKind.Folder), (member.DestinationName, member.ClientDestinationKey, member.DestinationKind));
        Assert.Equal(("Фільми", (string?)MoviesFolder, (DownloadDestinationKind?)DownloadDestinationKind.Folder), (owner.DestinationName, owner.ClientDestinationKey, owner.DestinationKind));
        Assert.Null(Assert.Single(listed.Items).ClientDestinationKey);
    }

    [Fact]
    public async Task Get_of_a_category_job_still_gives_a_member_its_category()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var categoryJob = await PlantJobAsync(_member, DownloadDestinationKind.Category);
        var olderJob = await PlantJobAsync(_member, null);

        //Act
        var category = await _factory.GetHttpRequest($"{BaseUrl}/{categoryJob.Id}", _member.AccessToken);
        var older = await _factory.GetHttpRequest($"{BaseUrl}/{olderJob.Id}", _member.AccessToken);

        //Assert
        // A category is no folder, and a job of an older build sent to a category: both as before.
        Assert.Equal(MoviesFolder, (await category.Content.ReadFromJsonAsync<DownloadJobDto>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!.ClientDestinationKey);
        Assert.Equal(MoviesFolder, (await older.Content.ReadFromJsonAsync<DownloadJobDto>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!.ClientDestinationKey);
    }

    // ── the Download dialog's question before Send ──────────────────────────────────────────────

    [Fact]
    public async Task OverwriteCheck_of_a_folder_the_client_sees_inside_the_media_folders_gives_no_reason()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}/overwrite-check?destinationId={_movies.Id}", _member.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(await ReasonAsync(response, cancellationToken));
        Assert.Equal([[MoviesFolder]], _factory.FolderClient.Proposals);
    }

    [Fact]
    public async Task OverwriteCheck_of_a_destinations_folder_no_library_leads_to_proposes_it_and_gives_no_reason()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var archive = await new DestinationMappingSeeder(_factory.Scope).Create(_client, "Архів", ArchiveFolder);

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}/overwrite-check?destinationId={archive.Id}", _member.AccessToken);

        //Assert
        // D3 — the destination's own folder is proposed, so the client lists it and it is a folder to look into, inside
        // the Media folders: never "folder unknown".
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(await ReasonAsync(response, cancellationToken));
        Assert.Equal([[MoviesFolder, ArchiveFolder]], _factory.FolderClient.Proposals);
    }

    [Fact]
    public async Task OverwriteCheck_of_a_folder_the_client_can_not_see_says_so_by_a_reason_of_its_own()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        _factory.FolderClient.CantSee(MoviesFolder);

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}/overwrite-check?destinationId={_movies.Id}", _member.AccessToken);

        //Assert
        // D5 — however its mappings place it, it is no place to look into or send to: the dialog says nothing will be
        // sent, never that the release goes unchecked.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("NotCheckedFolderNotSeen", await ReasonAsync(response, cancellationToken));
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    private Task<ReleaseEntity> FindReleaseAsync(AuthorizedUser user) => _releases.CreateFound(user.User, _toloka, "Film (2001) BDRip 1080p");

    private Task<HttpResponseMessage> SendAsync(AuthorizedUser user, ReleaseEntity release, DestinationMappingEntity destination) =>
        _factory.PostHttpRequest(BaseUrl, user.AccessToken, new SendDownloadRequest(Guid.NewGuid(), release.Id, _client.Id, destination.Id));

    /// <summary>A job <paramref name="user"/> sent to Фільми, its destination's kind as the send snapshotted it.</summary>
    private async Task<DownloadJobEntity> PlantJobAsync(AuthorizedUser user, DownloadDestinationKind? kind)
    {
        var release = await FindReleaseAsync(user);
        var context = _factory.Scope.GetRequiredService<ApplicationDbContext>();
        return await new DownloadJobDataFactory(context, _factory.Time).Create(
            release.Id, _client.Id, _client.Name, _movies.Id, requestedByUserId: user.User.Id, destinationKind: kind);
    }

    private async Task<DownloadJobEntity> JobRowAsync(CancellationToken cancellationToken)
    {
        DownloadJobEntity? row = null;
        await _factory.WithDbAsync(async db => row = await db.DownloadJobs.AsNoTracking().SingleAsync(cancellationToken));
        return row!;
    }

    /// <summary>201 with the job Failed with the app's own <paramref name="error"/>, the same on its row, and nothing
    /// handed to the client.</summary>
    private async Task AssertRefusedAsync(HttpResponseMessage response, string error, CancellationToken cancellationToken)
    {
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var job = (await response.Content.ReadFromJsonAsync<DownloadJobDto>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
        Assert.Equal((DownloadJobStatus.Failed, error), (job.Status, job.Error));
        var row = await JobRowAsync(cancellationToken);
        Assert.Equal((DownloadJobStatus.Failed, error, (DownloadJobErrorOrigin?)DownloadJobErrorOrigin.App), (row.Status, row.Error, row.ErrorOrigin));
        Assert.Empty(_factory.FolderClient.Submissions);
    }

    private static async Task<string?> ReasonAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        return document.RootElement.GetProperty("notCheckedReason").GetString();
    }
}
