using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Api.Mcp.Results;
using MediaAcquisitionManager.Core.Downloads;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Paths;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace MediaAcquisitionManager.Api.Tests.Mcp;

// MAM-45 — get_download_options and send_download (Vasyl's decision d): the web app's Download dialog through MCP, over a
// real Transmission (a FakeTransmissionServer answering as 4.1.3 does). The options are the HTTP API's, with the pick the
// dialog opens on — the client holding the suggested destination, else the only client; its suggested destination, else
// its only one, else none — and the overwrite check of that destination, or of the one asked for, which must be one of the
// options; nothing is checked without a pick, and no client to offer is said in the dialog's own sentence. A send goes as
// its user, Keep both, once per request id: the same id again answers the same job and sends nothing — a failed one too,
// and after the folder went out of sight too —, an id used for another release is refused. A new send to a folder
// Transmission can't see is refused before anything is written, fetched or sent, as the dialog holds Send; a release of
// another user's search is refused like a missing one; a job that failed is the send's answer, its summary saying why.
public sealed class McpDownloadToolsHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string OptionsTool = "get_download_options";
    private const string SendTool = "send_download";
    private const string BaseUrl = "/api/downloads";
    private const string TolokaAddress = "http://prowlarr.test:9696/1/api";
    private const string ApiKey = "planted-torznab-key-0045";
    private const string JellyfinApiKey = "0123456789abcdef0123456789abcdef";
    private const string MoviesFolder = "/downloads/movies";
    private const string ShowsFolder = "/downloads/tv-shows";
    private const string ReleaseTitle = "Film (2001) BDRip 1080p";

    /// <summary>A section of Toloka's own («Фільми HD»), which the destination Фільми is mapped to when a test says so.</summary>
    private const string FilmsSection = "100117";

    // Spelled out rather than read from product code: what the assistant reads and tells its user.
    private const string SendHeldError =
        "This destination's download client can't see its folder, so nothing will be sent there until the owner fixes the destination under Destinations in the user menu.";

    private const string DestinationNotOfferedError =
        "This destination isn't one of this release's download options; give one of the destinations get_download_options lists for it.";

    private const string ReleaseMissingError = "This release isn't available any more: a search keeps what it found for a day. Search again.";
    private const string RequestIdReusedError = "This request id was already used to send a different download. Reload the page and try again.";
    private const string AddRefusedError = "Transmission refused the torrent: invalid or corrupt torrent file.";

    /// <summary>The Download dialog's own sentence for a release no client can take (downloads.json <c>noClients</c>).</summary>
    private const string NoClientsSentence =
        "No download client can take this release yet. The owner sets them up under Integrations and Destinations.";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private IIntegrationConnectionSeeder _integrations = null!;
    private IDestinationMappingSeeder _destinations = null!;
    private IReleaseSeeder _releases = null!;
    private IPersonalAccessTokenSeeder _tokens = null!;
    private AuthorizedUser _owner = null!;
    private AuthorizedUser _member = null!;
    private FakeTransmissionServer _transmission = null!;
    private IntegrationConnectionEntity _toloka = null!;
    private IntegrationConnectionEntity _client = null!;
    private DestinationMappingEntity _movies = null!;
    private string _hash = null!;

    public McpDownloadToolsHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _integrations = new IntegrationConnectionSeeder(_factory.Scope);
        _destinations = new DestinationMappingSeeder(_factory.Scope);
        _releases = new ReleaseSeeder(_factory.Scope);
        _tokens = new PersonalAccessTokenSeeder(_factory.Scope);
        var users = new UserSeeder(_factory.Scope);
        _owner = await users.CreateAuthorizedOwner("owner");
        _member = await users.CreateAuthorized("member");
        var torrent = TestTorrent.V1("Film.2001.mkv", TestTorrent.Bytes((3 * TestTorrent.PieceLength) + 5, seed: 1));
        _hash = TorrentInfoHashReader.FromTorrentFile(torrent).V1!;
        new FakeTorznabServer(TolokaAddress, ApiKey) { TorrentFile = torrent }.Arm(_factory.TorznabHttp);
        _toloka = await _integrations.Create("torznab", "Toloka", TolokaAddress, apiKey: ApiKey);
        var jellyfin = await _integrations.Create("jellyfin", "Living room", apiKey: JellyfinApiKey);
        await new LibraryLocationSeeder(_factory.Scope).Create(jellyfin, "Фільми", "/media/movies");
        _transmission = new FakeTransmissionServer();
        _transmission.AddFolder(MoviesFolder);
        _transmission.Arm(_factory.TransmissionHttp);
        // Transmission's /downloads is the app's /media: the destination Фільми is the library's folder, which is checked.
        _client = await _integrations.Create(
            "transmission",
            "Transmission",
            _transmission.BaseUrl,
            userName: FakeTransmissionServer.DefaultUserName,
            password: FakeTransmissionServer.DefaultPassword,
            pathMappings: [new PathMappingPair("/downloads", "/media")]);
        // Not the default: a test that wants the server's suggestion maps a section to it.
        _movies = await _destinations.Create(_client, "Фільми", MoviesFolder);
        await new AllowedRootSeeder(_factory.Scope).Allow("/media");
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    // ── get_download_options: the dialog's pick ─────────────────────────────────────────────────

    [Fact]
    public async Task GetDownloadOptions_preselects_the_suggested_destination_in_the_client_that_holds_it_and_checks_it()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // qBittorrent comes first by name; the suggestion is Transmission's.
        var qbittorrent = await CreateQBittorrentAsync();
        await _destinations.Create(qbittorrent, "Movies", "Movies");
        await _destinations.Create(qbittorrent, "TV Shows", "TV Shows");
        await _destinations.Create(_client, "Серіали", ShowsFolder);
        await new DestinationSectionSeeder(_factory.Scope).Map(_movies, _toloka, "Фільми HD", true, FilmsSection);
        var release = await _releases.CreateFound(_member.User, _toloka, ReleaseTitle, categoryIds: FilmsSection);
        var page = await OptionsAsync(_member, release, cancellationToken);
        var token = await _tokens.Create(_member.User);
        await using var client = await _factory.ConnectMcpAsync(token.Secret, cancellationToken: cancellationToken);

        //Act
        var result = await CallAsync(client, OptionsTool, new() { ["releaseId"] = release.Id }, cancellationToken);

        //Assert
        Assert.False(result.IsError);
        var answer = result.ReadStructured<DownloadOptionsToolResult>();
        Assert.Equivalent(page, answer.Options, strict: true);
        Assert.Equal(["qBittorrent", "Transmission"], answer.Options.Clients.Select(option => option.Name));
        Assert.Equal(_movies.Id, answer.Options.SuggestedDestinationId);
        Assert.Equal(
            ((Guid?)_client.Id, (Guid?)_movies.Id, (Guid?)_movies.Id, false, (string?)null),
            (answer.PreselectedClientId, answer.PreselectedDestinationId, answer.CheckedDestinationId, answer.SendHeld, answer.SendHeldReason));
        // Its folder is the library's, so a release sent there is checked.
        Assert.Equal(new DownloadOverwriteCheckResponse(null), answer.OverwriteCheck);
        Assert.Equal(
            "2 download clients can take this release; the Download dialog would preselect “Фільми” of “Transmission”, and a release sent there is checked for files it would write over.",
            result.ReadText());
        // Only the destination checked had its client asked, and only read from.
        Assert.Empty(_factory.QBittorrentHttp.Requests);
        Assert.NotEmpty(_transmission.Calls);
        Assert.Empty(_transmission.Writes);
        await AssertNothingWrittenAsync(cancellationToken);
    }

    [Fact]
    public async Task GetDownloadOptions_with_the_only_client_and_its_only_destination_preselects_both_without_a_suggestion()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var release = await _releases.CreateFound(_member.User, _toloka, ReleaseTitle);
        var token = await _tokens.Create(_member.User);
        await using var client = await _factory.ConnectMcpAsync(token.Secret, cancellationToken: cancellationToken);

        //Act
        var result = await CallAsync(client, OptionsTool, new() { ["releaseId"] = release.Id }, cancellationToken);

        //Assert
        var answer = result.ReadStructured<DownloadOptionsToolResult>();
        // No default and no section: the server suggests nothing, and the dialog still opens on the one choice there is.
        Assert.Null(answer.Options.SuggestedDestinationId);
        Assert.Equal(
            ((Guid?)_client.Id, (Guid?)_movies.Id, (Guid?)_movies.Id, false),
            (answer.PreselectedClientId, answer.PreselectedDestinationId, answer.CheckedDestinationId, answer.SendHeld));
        Assert.Equal(new DownloadOverwriteCheckResponse(null), answer.OverwriteCheck);
        Assert.Equal(
            "1 download client can take this release; the Download dialog would preselect “Фільми” of “Transmission”, and a release sent there is checked for files it would write over.",
            result.ReadText());
    }

    [Fact]
    public async Task GetDownloadOptions_with_several_clients_and_no_suggestion_preselects_nothing_and_asks_no_client()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var qbittorrent = await CreateQBittorrentAsync();
        await _destinations.Create(qbittorrent, "Movies", "Movies");
        var release = await _releases.CreateFound(_member.User, _toloka, ReleaseTitle);
        var token = await _tokens.Create(_member.User);
        await using var client = await _factory.ConnectMcpAsync(token.Secret, cancellationToken: cancellationToken);

        //Act
        var result = await CallAsync(client, OptionsTool, new() { ["releaseId"] = release.Id }, cancellationToken);

        //Assert
        // The dialog's placeholders: the user picks, and only then is a folder checked.
        var answer = result.ReadStructured<DownloadOptionsToolResult>();
        Assert.Equal(2, answer.Options.Clients.Count);
        Assert.Equal(
            ((Guid?)null, (Guid?)null, (Guid?)null, (DownloadOverwriteCheckResponse?)null, false, (string?)null),
            (answer.PreselectedClientId, answer.PreselectedDestinationId, answer.CheckedDestinationId, answer.OverwriteCheck, answer.SendHeld, answer.SendHeldReason));
        Assert.Equal("2 download clients can take this release; the Download dialog would preselect no client.", result.ReadText());
        Assert.Empty(_factory.TransmissionHttp.Requests);
        Assert.Empty(_factory.QBittorrentHttp.Requests);
        // The nulls are written, as the output schema lists them.
        Assert.Contains("\"overwriteCheck\":null", JsonTextOf(result), StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetDownloadOptions_with_one_client_of_several_destinations_and_no_suggestion_preselects_the_client_alone()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // Серіали is the default, but the release's two own sections are mapped to the two destinations: a tie, which the
        // server answers with no suggestion rather than fall back to the default (ADR 0020).
        var shows = await _destinations.Create(_client, "Серіали", ShowsFolder, isDefault: true);
        var sections = new DestinationSectionSeeder(_factory.Scope);
        await sections.Map(_movies, _toloka, "Фільми HD", true, FilmsSection);
        await sections.Map(shows, _toloka, "Мультсеріали", true, "100125");
        var release = await _releases.CreateFound(_member.User, _toloka, ReleaseTitle, categoryIds: $"{FilmsSection},100125");
        var token = await _tokens.Create(_member.User);
        await using var client = await _factory.ConnectMcpAsync(token.Secret, cancellationToken: cancellationToken);

        //Act
        var result = await CallAsync(client, OptionsTool, new() { ["releaseId"] = release.Id }, cancellationToken);

        //Assert
        // Never the client's first destination, nor the default by itself: none suggested means the server picked none.
        var answer = result.ReadStructured<DownloadOptionsToolResult>();
        Assert.Null(answer.Options.SuggestedDestinationId);
        Assert.Equal(
            ((Guid?)_client.Id, (Guid?)null, (Guid?)null, (DownloadOverwriteCheckResponse?)null, false),
            (answer.PreselectedClientId, answer.PreselectedDestinationId, answer.CheckedDestinationId, answer.OverwriteCheck, answer.SendHeld));
        Assert.Equal(
            "1 download client can take this release; the Download dialog would preselect “Transmission” with no destination.", result.ReadText());
        Assert.Empty(_factory.TransmissionHttp.Requests);
    }

    [Fact]
    public async Task GetDownloadOptions_with_no_client_that_can_take_the_release_says_so_in_the_dialogs_sentence()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var release = await _releases.CreateFound(_member.User, _toloka, ReleaseTitle);
        // The owner switched the one download client off: the dialog has nothing to offer.
        await _factory.WithDbAsync(db => db.IntegrationConnections
            .Where(connection => connection.Id == _client.Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(connection => connection.IsEnabled, false), cancellationToken));
        var token = await _tokens.Create(_member.User);
        await using var client = await _factory.ConnectMcpAsync(token.Secret, cancellationToken: cancellationToken);

        //Act
        var result = await CallAsync(client, OptionsTool, new() { ["releaseId"] = release.Id }, cancellationToken);

        //Assert
        // Not an error: the answer is that there is nothing to pick, in the words the dialog shows the user.
        Assert.False(result.IsError);
        var answer = result.ReadStructured<DownloadOptionsToolResult>();
        Assert.Empty(answer.Options.Clients);
        Assert.Equal(
            ((Guid?)null, (Guid?)null, (Guid?)null, (DownloadOverwriteCheckResponse?)null, false, (string?)null),
            (answer.PreselectedClientId, answer.PreselectedDestinationId, answer.CheckedDestinationId, answer.OverwriteCheck, answer.SendHeld, answer.SendHeldReason));
        Assert.Equal(NoClientsSentence, result.ReadText());
        Assert.Empty(_factory.TransmissionHttp.Requests);
    }

    // ── get_download_options: the overwrite check ───────────────────────────────────────────────

    [Fact]
    public async Task GetDownloadOptions_with_a_destination_asked_for_checks_that_one_and_holds_send_where_transmission_can_not_see_its_folder()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // Saved while Transmission saw it; unmounted since.
        var shows = await _destinations.Create(_client, "Серіали", ShowsFolder);
        await new DestinationSectionSeeder(_factory.Scope).Map(_movies, _toloka, "Фільми HD", true, FilmsSection);
        var release = await _releases.CreateFound(_member.User, _toloka, ReleaseTitle, categoryIds: FilmsSection);
        var dialogsCheck = await CheckAsync(_member, shows, cancellationToken);
        var token = await _tokens.Create(_member.User);
        await using var client = await _factory.ConnectMcpAsync(token.Secret, cancellationToken: cancellationToken);

        //Act
        var result = await CallAsync(client, OptionsTool, new() { ["releaseId"] = release.Id, ["destinationId"] = shows.Id }, cancellationToken);

        //Assert
        // The pick stays the dialog's; the check is of the destination the user wants instead.
        var answer = result.ReadStructured<DownloadOptionsToolResult>();
        Assert.Equal(
            ((Guid?)_client.Id, (Guid?)_movies.Id, (Guid?)shows.Id),
            (answer.PreselectedClientId, answer.PreselectedDestinationId, answer.CheckedDestinationId));
        Assert.Equal(new DownloadOverwriteCheckResponse(DownloadOverwriteCheck.NotCheckedFolderNotSeen), answer.OverwriteCheck);
        Assert.Equal(dialogsCheck, answer.OverwriteCheck);
        // D5 — where the dialog disables Send, with the dialog's own sentence to tell the user.
        Assert.Equal((true, (string?)SendHeldError), (answer.SendHeld, answer.SendHeldReason));
        Assert.Equal(
            "1 download client can take this release; the Download dialog would preselect “Фільми” of “Transmission”, and nothing can be sent to “Серіали”: its download client can't see the folder.",
            result.ReadText());
        Assert.Empty(_transmission.Writes);
    }

    [Fact]
    public async Task GetDownloadOptions_of_a_preselected_destination_whose_folder_transmission_can_not_see_holds_send()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        _transmission.RemoveFolder(MoviesFolder);
        var release = await _releases.CreateFound(_member.User, _toloka, ReleaseTitle);
        var token = await _tokens.Create(_member.User);
        await using var client = await _factory.ConnectMcpAsync(token.Secret, cancellationToken: cancellationToken);

        //Act
        var result = await CallAsync(client, OptionsTool, new() { ["releaseId"] = release.Id }, cancellationToken);

        //Assert
        var answer = result.ReadStructured<DownloadOptionsToolResult>();
        Assert.Equal(
            ((Guid?)_movies.Id, (DownloadOverwriteCheck?)DownloadOverwriteCheck.NotCheckedFolderNotSeen, true, (string?)SendHeldError),
            (answer.CheckedDestinationId, answer.OverwriteCheck?.NotCheckedReason, answer.SendHeld, answer.SendHeldReason));
        Assert.EndsWith("and nothing can be sent there: its download client can't see the folder.", result.ReadText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetDownloadOptions_with_a_destination_that_is_not_one_of_the_options_is_an_error_and_asks_no_client()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // A destination of a client that is switched off: the dialog doesn't offer it, so it can't be checked for a send.
        var old = await _integrations.Create(
            "transmission",
            "Old Transmission",
            _transmission.BaseUrl,
            userName: FakeTransmissionServer.DefaultUserName,
            password: FakeTransmissionServer.DefaultPassword,
            isEnabled: false);
        var oldMovies = await _destinations.Create(old, "Old films", MoviesFolder);
        var release = await _releases.CreateFound(_member.User, _toloka, ReleaseTitle);
        var token = await _tokens.Create(_member.User);
        await using var client = await _factory.ConnectMcpAsync(token.Secret, cancellationToken: cancellationToken);

        //Act
        var result = await CallAsync(client, OptionsTool, new() { ["releaseId"] = release.Id, ["destinationId"] = oldMovies.Id }, cancellationToken);

        //Assert
        Assert.True(result.IsError);
        Assert.Equal(DestinationNotOfferedError, result.ReadText());
        Assert.Null(result.StructuredContent);
        Assert.Empty(_factory.TransmissionHttp.Requests);
    }

    [Fact]
    public async Task GetDownloadOptions_of_a_release_of_another_users_search_is_the_error_a_missing_one_gets()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var ownersRelease = await _releases.CreateFound(_owner.User, _toloka, ReleaseTitle);
        var token = await _tokens.Create(_member.User);
        await using var client = await _factory.ConnectMcpAsync(token.Secret, cancellationToken: cancellationToken);

        //Act
        var others = await CallAsync(client, OptionsTool, new() { ["releaseId"] = ownersRelease.Id }, cancellationToken);
        var missing = await CallAsync(client, OptionsTool, new() { ["releaseId"] = Guid.NewGuid() }, cancellationToken);

        //Assert
        // Nothing confirms a release exists in someone else's search (ADR 0011).
        Assert.Equal((true, ReleaseMissingError), (others.IsError, others.ReadText()));
        Assert.Equal((true, ReleaseMissingError), (missing.IsError, missing.ReadText()));
        Assert.Empty(_factory.TransmissionHttp.Requests);
    }

    // ── send_download ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SendDownload_sends_the_release_as_its_user_keep_both_and_answers_the_job()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var release = await _releases.CreateFound(_member.User, _toloka, ReleaseTitle);
        var token = await _tokens.Create(_member.User, PersonalAccessTokenAccess.ReadAndDownload);
        var requestId = Guid.NewGuid();
        await using var client = await _factory.ConnectMcpAsync(token.Secret, cancellationToken: cancellationToken);

        //Act
        var result = await CallAsync(client, SendTool, SendArguments(release, _movies, requestId), cancellationToken);

        //Assert
        Assert.False(result.IsError);
        var job = result.ReadStructured<DownloadJobDto>();
        Assert.Equal(
            (release.Id, DownloadJobStatus.Submitted, (string?)_hash, (DownloadOverwriteCheck?)DownloadOverwriteCheck.Checked, (DownloadJobReplacementDto?)null),
            (job.ReleaseId, job.Status, job.ClientJobId, job.OverwriteCheck, job.Replacement));
        // The job the Jobs page shows the member.
        Assert.Equal(await JobAsync(_member, job.Id, cancellationToken), job);
        Assert.Equal(
            "Sent “Film (2001) BDRip 1080p” to “Transmission”, into “Фільми”, and the job is Submitted; list_jobs and get_job follow it.",
            result.ReadText());
        var add = Assert.Single(_transmission.AddRequests);
        Assert.Equal(((string?)MoviesFolder, (bool?)false), (add.DownloadDir, add.Paused));
        Assert.Equal(["Фільми"], add.Labels!);
        await _factory.WithDbAsync(async db =>
        {
            var row = await db.DownloadJobs.AsNoTracking().SingleAsync(cancellationToken);
            Assert.Equal(
                (job.Id, (Guid?)_member.User.Id, (string?)"member", (Guid?)requestId),
                (row.Id, row.RequestedByUserId, row.RequestedByUserName, row.RequestId));
            var requested = await db.AuditEvents.AsNoTracking().SingleAsync(audit => audit.Action == AuditAction.DownloadRequested, cancellationToken);
            Assert.Equal(((Guid?)_member.User.Id, "member", (Guid?)job.Id), (requested.ActorUserId, requested.ActorUserName, requested.TargetId));
            Assert.Equal(0, await db.AuditEvents.CountAsync(audit => audit.Action == AuditAction.PersonalAccessTokenRefused, cancellationToken));
            Assert.Equal(0, await db.ReplacementJobs.CountAsync(cancellationToken));
        });
    }

    [Fact]
    public async Task SendDownload_again_with_the_same_request_id_answers_the_same_job_and_sends_nothing_twice()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var release = await _releases.CreateFound(_member.User, _toloka, ReleaseTitle);
        var token = await _tokens.Create(_member.User, PersonalAccessTokenAccess.ReadAndDownload);
        var requestId = Guid.NewGuid();
        await using var client = await _factory.ConnectMcpAsync(token.Secret, cancellationToken: cancellationToken);
        var first = await CallAsync(client, SendTool, SendArguments(release, _movies, requestId), cancellationToken);

        //Act
        // The assistant's retry after a timeout: the same send, under the same request id (ADR 0013).
        var again = await CallAsync(client, SendTool, SendArguments(release, _movies, requestId), cancellationToken);

        //Assert
        Assert.False(again.IsError);
        Assert.Equal(first.ReadStructured<DownloadJobDto>(), again.ReadStructured<DownloadJobDto>());
        Assert.Equal(
            "This requestId already sent “Film (2001) BDRip 1080p” to “Transmission”, into “Фільми”, so nothing was sent again; the job is Submitted.",
            again.ReadText());
        Assert.Single(_transmission.AddRequests);
        await _factory.WithDbAsync(async db =>
        {
            Assert.Equal(1, await db.DownloadJobs.CountAsync(cancellationToken));
            Assert.Equal(1, await db.AuditEvents.CountAsync(audit => audit.Action == AuditAction.DownloadRequested, cancellationToken));
        });
    }

    [Fact]
    public async Task SendDownload_again_with_the_same_request_id_after_transmission_lost_sight_of_the_folder_answers_the_job_it_made_and_asks_no_client()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var release = await _releases.CreateFound(_member.User, _toloka, ReleaseTitle);
        var token = await _tokens.Create(_member.User, PersonalAccessTokenAccess.ReadAndDownload);
        var requestId = Guid.NewGuid();
        await using var client = await _factory.ConnectMcpAsync(token.Secret, cancellationToken: cancellationToken);
        var first = await CallAsync(client, SendTool, SendArguments(release, _movies, requestId), cancellationToken);
        // Between the first try, which the assistant gave up waiting on, and its retry: the share went away.
        _transmission.RemoveFolder(MoviesFolder);
        var callsBefore = _transmission.Calls.Count;

        //Act
        var again = await CallAsync(client, SendTool, SendArguments(release, _movies, requestId), cancellationToken);

        //Assert
        // The job the first try made, which is downloading — never "nothing will be sent there" (ADR 0013).
        Assert.False(again.IsError);
        Assert.Equal(first.ReadStructured<DownloadJobDto>(), again.ReadStructured<DownloadJobDto>());
        Assert.Equal(
            "This requestId already sent “Film (2001) BDRip 1080p” to “Transmission”, into “Фільми”, so nothing was sent again; the job is Submitted.",
            again.ReadText());
        // Answered from the job alone: Transmission wasn't asked where its folder is, nor sent anything twice.
        Assert.Equal(callsBefore, _transmission.Calls.Count);
        Assert.Single(_transmission.AddRequests);
        await _factory.WithDbAsync(async db =>
            Assert.Equal(1, await db.DownloadJobs.CountAsync(cancellationToken)));
    }

    [Fact]
    public async Task SendDownload_with_a_request_id_that_sent_another_release_is_an_error_and_sends_nothing_more()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var sent = await _releases.CreateFound(_member.User, _toloka, ReleaseTitle);
        var other = await _releases.CreateFound(_member.User, _toloka, "Film (2001) WEB-DL 2160p");
        var token = await _tokens.Create(_member.User, PersonalAccessTokenAccess.ReadAndDownload);
        var requestId = Guid.NewGuid();
        await using var client = await _factory.ConnectMcpAsync(token.Secret, cancellationToken: cancellationToken);
        await CallAsync(client, SendTool, SendArguments(sent, _movies, requestId), cancellationToken);

        //Act
        var result = await CallAsync(client, SendTool, SendArguments(other, _movies, requestId), cancellationToken);

        //Assert
        Assert.True(result.IsError);
        Assert.Equal(RequestIdReusedError, result.ReadText());
        Assert.Null(result.StructuredContent);
        Assert.Single(_transmission.AddRequests);
        await _factory.WithDbAsync(async db =>
            Assert.Equal(sent.Id, (await db.DownloadJobs.AsNoTracking().SingleAsync(cancellationToken)).ReleaseId));
    }

    [Fact]
    public async Task SendDownload_to_a_folder_transmission_can_not_see_is_refused_before_anything_is_written_fetched_or_sent()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var shows = await _destinations.Create(_client, "Серіали", ShowsFolder);
        var release = await _releases.CreateFound(_member.User, _toloka, ReleaseTitle);
        var token = await _tokens.Create(_member.User, PersonalAccessTokenAccess.ReadAndDownload);
        await using var client = await _factory.ConnectMcpAsync(token.Secret, cancellationToken: cancellationToken);

        //Act
        var result = await CallAsync(client, SendTool, SendArguments(release, shows, Guid.NewGuid()), cancellationToken);

        //Assert
        // Where the dialog disables Send: the HTTP API would write a job that fails; the tool writes nothing at all.
        Assert.True(result.IsError);
        Assert.Equal(SendHeldError, result.ReadText());
        Assert.Null(result.StructuredContent);
        Assert.Empty(_transmission.AddRequests);
        Assert.Empty(_transmission.Writes);
        // Not even the .torrent was fetched: the release was found without asking the feed, so it was never asked at all.
        Assert.Empty(_factory.TorznabHttp.Requests);
        await AssertNothingWrittenAsync(cancellationToken);
    }

    [Fact]
    public async Task SendDownload_of_a_release_of_another_users_search_is_the_error_a_missing_one_gets_and_sends_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // The owner's search found it; the member's token may send from the member's own searches only.
        var ownersRelease = await _releases.CreateFound(_owner.User, _toloka, ReleaseTitle);
        var token = await _tokens.Create(_member.User, PersonalAccessTokenAccess.ReadAndDownload);
        await using var client = await _factory.ConnectMcpAsync(token.Secret, cancellationToken: cancellationToken);

        //Act
        var result = await CallAsync(client, SendTool, SendArguments(ownersRelease, _movies, Guid.NewGuid()), cancellationToken);

        //Assert
        Assert.True(result.IsError);
        Assert.Equal(ReleaseMissingError, result.ReadText());
        Assert.Empty(_transmission.AddRequests);
        Assert.Empty(_transmission.Writes);
        await AssertNothingWrittenAsync(cancellationToken);
    }

    [Fact]
    public async Task SendDownload_whose_add_transmission_refuses_answers_the_failed_job_with_why_in_its_summary()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var release = await _releases.CreateFound(_owner.User, _toloka, ReleaseTitle);
        _transmission.RpcIntercept = call =>
            call.Method == "torrent-add" ? new JsonObject { ["arguments"] = new JsonObject(), ["result"] = "invalid or corrupt torrent file" } : null;
        var token = await _tokens.Create(_owner.User, PersonalAccessTokenAccess.ReadAndDownload);
        await using var client = await _factory.ConnectMcpAsync(token.Secret, cancellationToken: cancellationToken);

        //Act
        var result = await CallAsync(client, SendTool, SendArguments(release, _movies, Guid.NewGuid()), cancellationToken);

        //Assert
        // The send's answer, as the HTTP API's 201 is: the job says why, and the summary says it too.
        Assert.False(result.IsError);
        var job = result.ReadStructured<DownloadJobDto>();
        Assert.Equal((DownloadJobStatus.Failed, (string?)AddRefusedError), (job.Status, job.Error));
        Assert.Equal(
            $"Sending “Film (2001) BDRip 1080p” to “Transmission”, into “Фільми”, failed: {AddRefusedError} A new send needs a new requestId.",
            result.ReadText());
        await _factory.WithDbAsync(async db =>
            Assert.Equal(DownloadJobStatus.Failed, (await db.DownloadJobs.AsNoTracking().SingleAsync(cancellationToken)).Status));
    }

    [Fact]
    public async Task SendDownload_again_after_a_failed_send_answers_the_failed_job_and_asks_for_a_new_request_id()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var release = await _releases.CreateFound(_owner.User, _toloka, ReleaseTitle);
        _transmission.RpcIntercept = call =>
            call.Method == "torrent-add" ? new JsonObject { ["arguments"] = new JsonObject(), ["result"] = "invalid or corrupt torrent file" } : null;
        var token = await _tokens.Create(_owner.User, PersonalAccessTokenAccess.ReadAndDownload);
        var requestId = Guid.NewGuid();
        await using var client = await _factory.ConnectMcpAsync(token.Secret, cancellationToken: cancellationToken);
        var first = await CallAsync(client, SendTool, SendArguments(release, _movies, requestId), cancellationToken);

        //Act
        // A retry under the same request id: the failed job is its answer, so trying again takes a new id.
        var again = await CallAsync(client, SendTool, SendArguments(release, _movies, requestId), cancellationToken);

        //Assert
        Assert.False(again.IsError);
        var job = again.ReadStructured<DownloadJobDto>();
        Assert.Equal(first.ReadStructured<DownloadJobDto>(), job);
        Assert.Equal((DownloadJobStatus.Failed, (string?)AddRefusedError), (job.Status, job.Error));
        Assert.Equal(
            $"This requestId already tried to send “Film (2001) BDRip 1080p” to “Transmission”, into “Фільми”, and that send failed: {AddRefusedError} A new send needs a new requestId.",
            again.ReadText());
        // The first try's torrent-add alone: the retry sent nothing.
        Assert.Single(_transmission.AddRequests);
        await _factory.WithDbAsync(async db =>
            Assert.Equal(1, await db.DownloadJobs.CountAsync(cancellationToken)));
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    private static Task<CallToolResult> CallAsync(
        McpClient client, string tool, Dictionary<string, object?> arguments, CancellationToken cancellationToken) =>
        client.CallToolAsync(tool, arguments, cancellationToken: cancellationToken).AsTask();

    private Dictionary<string, object?> SendArguments(ReleaseEntity release, DestinationMappingEntity destination, Guid requestId) => new()
    {
        ["releaseId"] = release.Id,
        ["downloadClientId"] = _client.Id,
        ["destinationId"] = destination.Id,
        ["requestId"] = requestId,
    };

    /// <summary>A second download client, which nothing answers: the options never ask it.</summary>
    private Task<IntegrationConnectionEntity> CreateQBittorrentAsync() => _integrations.Create(
        "qbittorrent", "qBittorrent", FakeQBittorrentServer.DefaultBaseUrl, userName: FakeQBittorrentServer.DefaultUserName, password: "secret");

    /// <summary>The dialog's choices for <paramref name="release"/> as the web app reads them, with <paramref name="user"/>'s
    /// session.</summary>
    private async Task<DownloadOptionsResponse> OptionsAsync(AuthorizedUser user, ReleaseEntity release, CancellationToken cancellationToken)
    {
        var response = await _factory.GetHttpRequest($"{BaseUrl}/options?releaseId={release.Id}", user.AccessToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<DownloadOptionsResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
    }

    /// <summary>The dialog's overwrite check of <paramref name="destination"/>, with <paramref name="user"/>'s session.</summary>
    private async Task<DownloadOverwriteCheckResponse> CheckAsync(AuthorizedUser user, DestinationMappingEntity destination, CancellationToken cancellationToken)
    {
        var response = await _factory.GetHttpRequest($"{BaseUrl}/overwrite-check?destinationId={destination.Id}", user.AccessToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<DownloadOverwriteCheckResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
    }

    /// <summary>The job as the Jobs page reads it, with <paramref name="user"/>'s session.</summary>
    private async Task<DownloadJobDto> JobAsync(AuthorizedUser user, Guid jobId, CancellationToken cancellationToken)
    {
        var response = await _factory.GetHttpRequest($"{BaseUrl}/{jobId}", user.AccessToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<DownloadJobDto>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
    }

    /// <summary>No job, and no audit row of any kind.</summary>
    private Task AssertNothingWrittenAsync(CancellationToken cancellationToken) => _factory.WithDbAsync(async db =>
    {
        Assert.Equal(0, await db.DownloadJobs.CountAsync(cancellationToken));
        Assert.Equal(0, await db.AuditEvents.CountAsync(cancellationToken));
    });

    /// <summary>The JSON text block a success carries beside its summary.</summary>
    private static string JsonTextOf(CallToolResult result) => result.Content.OfType<TextContentBlock>().Last().Text;
}
