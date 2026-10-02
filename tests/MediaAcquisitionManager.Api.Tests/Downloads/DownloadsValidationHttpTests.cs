using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Paths;
using MediaAcquisitionManager.Infrastructure.Persistence;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MediaAcquisitionManager.Api.Tests.Downloads;

// MAM-13 — every refusal of a send, in the order the service checks: the ids present (the request id
// first); the release one of the caller's own searches' from the last day; the client and the destination
// there and belonging together; the client switched on, of a type this build sends to and taking the
// release's payload; the release's source still there, switched on and of a type this build fetches from.
// Each answers its own sentence — the first rule broken only — and leaves nothing behind: no job, no audit
// row, and not one request to the feed or to qBittorrent. A request id sent again and a release already sent
// are DownloadsIdempotencyHttpTests'.
public sealed class DownloadsValidationHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/downloads";
    private const string TolokaAddress = "http://prowlarr.test:9696/1/api";
    private const string ApiKey = "planted-torznab-key-0001";
    private const string JellyfinApiKey = "0123456789abcdef0123456789abcdef";

    // Spelled out rather than read from the product code: these are what the Download dialog shows.
    private const string RequestIdMissingError =
        "The download has no request id, which keeps a retry from sending it twice. Reload the page and try again.";
    private const string ReleaseMissingError = "Pick the release to send.";
    private const string ClientMissingError = "Pick the download client to send it to.";
    private const string DestinationMissingError = "Pick the destination to send it to.";
    private const string ReleaseGoneError = "This release isn't available any more: a search keeps what it found for a day. Search again.";
    private const string ClientGoneError = "The download client you picked no longer exists. Reload the page and pick one again.";
    private const string DestinationGoneError = "The destination you picked no longer exists. Reload the page and pick one again.";
    private const string OtherClientsDestinationError = "“Movies” is a destination of another download client. Pick one of “qBittorrent”’s.";
    private const string NzbRefusedError = "“qBittorrent” can't take NZB files, which this release is.";
    private const string ClientSwitchedOffError = "“qBittorrent” is switched off. The owner can switch it on under Integrations.";
    private const string UnregisteredClientError = "This version of the app can't send downloads to “retired-client”.";
    private const string SourceRemovedError = "The source that listed this release has been removed, so the release can't be fetched. Search again.";
    private const string SourceSwitchedOffError =
        "“Toloka” is switched off, so its releases can't be fetched. The owner can switch it on under Integrations.";
    private const string UnregisteredSourceError = "This version of the app can't fetch releases from “retired-source”.";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private IIntegrationConnectionSeeder _integrations = null!;
    private IDestinationMappingSeeder _destinations = null!;
    private IReleaseSeeder _releases = null!;
    private IUserSeeder _users = null!;
    private AuthorizedUser _owner = null!;
    private AuthorizedUser _member = null!;
    private AuthorizedUser _otherMember = null!;
    private FakeQBittorrentServer _qbittorrent = null!;
    private IntegrationConnectionEntity _toloka = null!;
    private IntegrationConnectionEntity _client = null!;
    private DestinationMappingEntity _movies = null!;

    public DownloadsValidationHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _integrations = new IntegrationConnectionSeeder(_factory.Scope);
        _destinations = new DestinationMappingSeeder(_factory.Scope);
        _releases = new ReleaseSeeder(_factory.Scope);
        _users = new UserSeeder(_factory.Scope);
        _owner = await _users.CreateAuthorizedOwner("owner");
        _member = await _users.CreateAuthorized("member");
        _otherMember = await _users.CreateAuthorized("other");
        // Both armed, so a request that reached either would be recorded and answered as a working service.
        new FakeTorznabServer(TolokaAddress, ApiKey).Arm(_factory.TorznabHttp);
        _qbittorrent = new FakeQBittorrentServer();
        _qbittorrent.Arm(_factory.QBittorrentHttp);
        _toloka = await _integrations.Create("torznab", "Toloka", TolokaAddress, apiKey: ApiKey);
        _client = await CreateClientAsync("qBittorrent", _qbittorrent.BaseUrl);
        _movies = await _destinations.Create(_client, "Movies", "Movies", isDefault: true);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    // MAM-13 — a missing or empty id reaches the rules and gets their sentence, which the dialog shows as is;
    // a framework validation body has no "error" for it to show. The request id is checked first: a request
    // without one couldn't be told from its own retry.
    [Theory]
    [InlineData("missing request id", RequestIdMissingError)]
    [InlineData("empty request id", RequestIdMissingError)]
    [InlineData("only a destination", RequestIdMissingError)]
    [InlineData("missing release", ReleaseMissingError)]
    [InlineData("empty release", ReleaseMissingError)]
    [InlineData("missing release and client", ReleaseMissingError)]
    [InlineData("missing client", ClientMissingError)]
    [InlineData("empty client", ClientMissingError)]
    [InlineData("missing client and destination", ClientMissingError)]
    [InlineData("missing destination", DestinationMissingError)]
    [InlineData("empty destination", DestinationMissingError)]
    [InlineData("empty object", RequestIdMissingError)]
    // A JSON null and no content at all: the controller lets both through to the rules.
    [InlineData("null body", RequestIdMissingError)]
    [InlineData("no body", RequestIdMissingError)]
    public async Task Create_with_a_missing_id_is_400_with_the_rule_and_asks_nothing(string missing, string expectedError)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var release = await _releases.CreateFound(_member.User, _toloka);
        var requestId = Guid.NewGuid();
        object? body = missing switch
        {
            "missing request id" => new { releaseId = release.Id, downloadClientId = _client.Id, destinationId = _movies.Id },
            "empty request id" => new SendDownloadRequest(Guid.Empty, release.Id, _client.Id, _movies.Id),
            "only a destination" => new { destinationId = _movies.Id },
            "missing release" => new { requestId, downloadClientId = _client.Id, destinationId = _movies.Id },
            "empty release" => new SendDownloadRequest(requestId, Guid.Empty, _client.Id, _movies.Id),
            "missing release and client" => new { requestId, destinationId = _movies.Id },
            "missing client" => new { requestId, releaseId = release.Id, destinationId = _movies.Id },
            "empty client" => new SendDownloadRequest(requestId, release.Id, Guid.Empty, _movies.Id),
            "missing client and destination" => new { requestId, releaseId = release.Id },
            "missing destination" => new { requestId, releaseId = release.Id, downloadClientId = _client.Id },
            "empty destination" => new SendDownloadRequest(requestId, release.Id, _client.Id, Guid.Empty),
            "empty object" => new { },
            "null body" or "no body" => null,
            _ => throw new ArgumentOutOfRangeException(nameof(missing), missing, null),
        };

        //Act
        var response = missing switch
        {
            "null body" => await PostJsonTextAsync("null"),
            _ => await _factory.PostHttpRequest(BaseUrl, _member.AccessToken, body),
        };

        //Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(expectedError, await response.ReadErrorAsync(cancellationToken));
        await AssertNothingWrittenOrSentAsync(cancellationToken);
    }

    [Fact]
    public async Task Create_of_another_users_release_is_404_with_the_sentence_even_for_the_installation_owner()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var release = await _releases.CreateFound(_member.User, _toloka);

        //Act
        var forOtherMember = await SendAsync(_otherMember, release.Id);
        var forOwner = await SendAsync(_owner, release.Id);

        //Assert
        // The sentence a forgotten release gets: nothing confirms a release is in someone else's search.
        Assert.Equal(HttpStatusCode.NotFound, forOtherMember.StatusCode);
        Assert.Equal(ReleaseGoneError, await forOtherMember.ReadErrorAsync(cancellationToken));
        Assert.Equal(HttpStatusCode.NotFound, forOwner.StatusCode);
        Assert.Equal(ReleaseGoneError, await forOwner.ReadErrorAsync(cancellationToken));
        await AssertNothingWrittenOrSentAsync(cancellationToken);
    }

    [Fact]
    public async Task Create_of_a_release_whose_search_is_over_a_day_old_is_404_with_the_sentence()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var release = await _releases.CreateFound(_member.User, _toloka);
        _factory.Time.Advance(TimeSpan.FromHours(24) + TimeSpan.FromSeconds(1));
        // A day later the morning's access token has long expired: the member signs in again.
        var member = await _users.CreateSession(_member.User);

        //Act
        var response = await SendAsync(member, release.Id);

        //Assert
        // Gone as its search is, whether or not a later search pruned the row yet.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(ReleaseGoneError, await response.ReadErrorAsync(cancellationToken));
        await _factory.WithDbAsync(async db => Assert.True(await db.Releases.AnyAsync(row => row.Id == release.Id, cancellationToken)));
        await AssertNothingWrittenOrSentAsync(cancellationToken);
    }

    [Fact]
    public async Task Create_of_a_release_whose_search_is_a_day_old_is_still_sent()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var release = await _releases.CreateFound(_member.User, _toloka);
        _factory.Time.Advance(TimeSpan.FromHours(24));
        var member = await _users.CreateSession(_member.User);

        //Act
        var response = await SendAsync(member, release.Id);

        //Assert
        // The day is inclusive, as the search's own reads are (SearchesAccessHttpTests).
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var job = await response.Content.ReadFromJsonAsync<DownloadJobDto>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken);
        Assert.Equal(DownloadJobStatus.Submitted, job!.Status);
        await _factory.WithDbAsync(async db =>
        {
            Assert.Equal(job.Id, (await db.DownloadJobs.AsNoTracking().SingleAsync(cancellationToken)).Id);
            Assert.Equal([AuditAction.DownloadRequested], await db.AuditEvents.Select(audit => audit.Action).ToListAsync(cancellationToken));
        });
        Assert.Single(_factory.TorznabHttp.Requests);
        Assert.Single(_qbittorrent.AddRequests);
    }

    [Fact]
    public async Task Create_of_a_release_a_plain_release_search_found_is_404_with_the_sentence()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // MAM-10's search: its releases are stored, but belong to no search of anyone's.
        var searched = await _factory.PostHttpRequest("/api/releases/search", _member.AccessToken, new ReleaseSearchRequest("Interstellar"));
        Assert.Equal(HttpStatusCode.OK, searched.StatusCode);
        var found = await searched.Content.ReadFromJsonAsync<ReleaseSearchResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken);
        var releaseId = found!.Sources.Single().Releases[0].Id;
        var feedRequests = _factory.TorznabHttp.Requests.Count;

        //Act
        var response = await SendAsync(_member, releaseId);

        //Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(ReleaseGoneError, await response.ReadErrorAsync(cancellationToken));
        await _factory.WithDbAsync(async db =>
        {
            Assert.True(await db.Releases.AnyAsync(row => row.Id == releaseId && row.SearchSourceId == null, cancellationToken));
            Assert.Equal(0, await db.DownloadJobs.CountAsync(cancellationToken));
            Assert.Equal(0, await db.AuditEvents.CountAsync(cancellationToken));
        });
        // Nothing after the search itself: the release's link was never fetched.
        Assert.Equal(feedRequests, _factory.TorznabHttp.Requests.Count);
        Assert.Empty(_factory.QBittorrentHttp.Requests);
    }

    [Fact]
    public async Task Create_of_a_release_that_never_was_is_404_with_the_sentence()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _releases.CreateFound(_member.User, _toloka);

        //Act
        var response = await SendAsync(_member, Guid.NewGuid());

        //Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(ReleaseGoneError, await response.ReadErrorAsync(cancellationToken));
        await AssertNothingWrittenOrSentAsync(cancellationToken);
    }

    [Theory]
    [InlineData("no integration")]
    [InlineData("a media library")]
    [InlineData("a release source")]
    public async Task Create_to_an_id_that_is_not_a_download_client_is_404_and_asks_nothing(string target)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var release = await _releases.CreateFound(_member.User, _toloka);
        var clientId = target switch
        {
            "no integration" => Guid.NewGuid(),
            "a media library" => (await _integrations.Create("jellyfin", "Living room", apiKey: JellyfinApiKey)).Id,
            "a release source" => _toloka.Id,
            _ => throw new ArgumentOutOfRangeException(nameof(target), target, null),
        };

        //Act
        var response = await SendAsync(_member, release.Id, clientId, _movies.Id);

        //Assert
        // An id in the body, not the addressed row: the dialog says what happened.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(ClientGoneError, await response.ReadErrorAsync(cancellationToken));
        await AssertNothingWrittenOrSentAsync(cancellationToken);
    }

    [Fact]
    public async Task Create_to_an_unknown_destination_is_404_and_asks_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var release = await _releases.CreateFound(_member.User, _toloka);

        //Act
        var response = await SendAsync(_member, release.Id, _client.Id, Guid.NewGuid());

        //Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(DestinationGoneError, await response.ReadErrorAsync(cancellationToken));
        await AssertNothingWrittenOrSentAsync(cancellationToken);
    }

    [Fact]
    public async Task Create_to_a_destination_of_another_client_is_400_and_asks_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var release = await _releases.CreateFound(_member.User, _toloka);
        var seedbox = await CreateClientAsync("Seedbox", "http://seedbox.test:8080");
        // The same name as the picked client's own: the destination is told by its id, never its name.
        var seedboxMovies = await _destinations.Create(seedbox, "Movies", "Movies");

        //Act
        var response = await SendAsync(_member, release.Id, _client.Id, seedboxMovies.Id);

        //Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(OtherClientsDestinationError, await response.ReadErrorAsync(cancellationToken));
        await AssertNothingWrittenOrSentAsync(cancellationToken);
    }

    [Fact]
    public async Task Create_to_a_client_that_can_not_take_the_release_is_400_and_asks_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var release = await _releases.CreateFound(_member.User, _toloka, payloadKind: ReleasePayloadKind.Nzb);

        //Act
        var response = await SendAsync(_member, release.Id);

        //Assert
        // Decided from the listing, before anything is fetched: qBittorrent takes torrents only.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(NzbRefusedError, await response.ReadErrorAsync(cancellationToken));
        await AssertNothingWrittenOrSentAsync(cancellationToken);
    }

    [Fact]
    public async Task Create_to_a_switched_off_client_is_409_and_asks_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var release = await _releases.CreateFound(_member.User, _toloka);
        // Switched off after the dialog listed it.
        await SwitchOffAsync(_client.Id);

        //Act
        var response = await SendAsync(_member, release.Id);

        //Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(ClientSwitchedOffError, await response.ReadErrorAsync(cancellationToken));
        await AssertNothingWrittenOrSentAsync(cancellationToken);
    }

    [Fact]
    public async Task Create_to_a_client_of_a_type_this_version_can_not_send_to_is_409_and_asks_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var release = await _releases.CreateFound(_member.User, _toloka);
        var retired = await _integrations.Create("retired-client", "Old client", kind: IntegrationKind.DownloadClient);
        var destination = await _destinations.Create(retired, "Movies", "Movies");

        //Act
        var response = await SendAsync(_member, release.Id, retired.Id, destination.Id);

        //Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(UnregisteredClientError, await response.ReadErrorAsync(cancellationToken));
        await AssertNothingWrittenOrSentAsync(cancellationToken);
    }

    [Fact]
    public async Task Create_of_a_release_whose_source_was_removed_is_409_and_fetches_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var release = await _releases.CreateFound(_member.User, _toloka);
        // Removed by the owner after the search: the release stays in the member's search, unlinked from it.
        var removed = await _factory.DeleteHttpRequest($"/api/settings/integrations/{_toloka.Id}", _owner.AccessToken);
        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);

        //Act
        var response = await SendAsync(_member, release.Id);

        //Assert
        // ADR 0007 — the key its link needs went with the integration.
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(SourceRemovedError, await response.ReadErrorAsync(cancellationToken));
        await _factory.WithDbAsync(async db =>
        {
            Assert.Null((await db.Releases.AsNoTracking().SingleAsync(row => row.Id == release.Id, cancellationToken)).SourceConnectionId);
            Assert.Equal(0, await db.DownloadJobs.CountAsync(cancellationToken));
            // The removal's own row only.
            Assert.Equal([AuditAction.IntegrationDeleted], await db.AuditEvents.Select(audit => audit.Action).ToListAsync(cancellationToken));
        });
        Assert.Empty(_factory.TorznabHttp.Requests);
        Assert.Empty(_factory.QBittorrentHttp.Requests);
    }

    [Fact]
    public async Task Create_of_a_release_whose_source_is_switched_off_is_409_and_fetches_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var release = await _releases.CreateFound(_member.User, _toloka);
        await SwitchOffAsync(_toloka.Id);

        //Act
        var response = await SendAsync(_member, release.Id);

        //Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(SourceSwitchedOffError, await response.ReadErrorAsync(cancellationToken));
        await AssertNothingWrittenOrSentAsync(cancellationToken);
    }

    [Fact]
    public async Task Create_of_a_release_from_a_source_type_this_version_can_not_fetch_from_is_409_and_fetches_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var retired = await _integrations.Create("retired-source", "Old source", TolokaAddress, apiKey: ApiKey, kind: IntegrationKind.ReleaseSearch);
        var release = await _releases.CreateFound(_member.User, retired);

        //Act
        var response = await SendAsync(_member, release.Id);

        //Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(UnregisteredSourceError, await response.ReadErrorAsync(cancellationToken));
        await AssertNothingWrittenOrSentAsync(cancellationToken);
    }

    // MAM-13 — one sentence per answer, the first rule broken in the service's order; the later ones are
    // never reached, so nothing about them leaks either.
    [Theory]
    [InlineData("another user's release to an unknown client", HttpStatusCode.NotFound, ReleaseGoneError)]
    [InlineData("an unknown client and an unknown destination", HttpStatusCode.NotFound, ClientGoneError)]
    [InlineData("an unknown destination of a switched-off client", HttpStatusCode.NotFound, DestinationGoneError)]
    [InlineData("another client's destination on a switched-off client", HttpStatusCode.BadRequest, OtherClientsDestinationError)]
    [InlineData("an NZB release to a switched-off client", HttpStatusCode.Conflict, ClientSwitchedOffError)]
    [InlineData("an NZB release from a switched-off source", HttpStatusCode.BadRequest, NzbRefusedError)]
    public async Task Create_with_several_rules_broken_answers_the_first_in_order(string scenario, HttpStatusCode expectedStatus, string expectedError)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var searcher = scenario == "another user's release to an unknown client" ? _otherMember : _member;
        var kind = scenario.StartsWith("an NZB release", StringComparison.Ordinal) ? ReleasePayloadKind.Nzb : (ReleasePayloadKind?)null;
        var release = await _releases.CreateFound(searcher.User, _toloka, payloadKind: kind);
        var clientId = _client.Id;
        var destinationId = _movies.Id;
        switch (scenario)
        {
            case "another user's release to an unknown client":
                clientId = Guid.NewGuid();
                break;
            case "an unknown client and an unknown destination":
                clientId = Guid.NewGuid();
                destinationId = Guid.NewGuid();
                break;
            case "an unknown destination of a switched-off client":
                await SwitchOffAsync(_client.Id);
                destinationId = Guid.NewGuid();
                break;
            case "another client's destination on a switched-off client":
                await SwitchOffAsync(_client.Id);
                destinationId = (await _destinations.Create(await CreateClientAsync("Seedbox", "http://seedbox.test:8080"), "Movies", "Movies")).Id;
                break;
            case "an NZB release to a switched-off client":
                await SwitchOffAsync(_client.Id);
                break;
            case "an NZB release from a switched-off source":
                await SwitchOffAsync(_toloka.Id);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(scenario), scenario, null);
        }

        //Act
        var response = await SendAsync(_member, release.Id, clientId, destinationId);

        //Assert
        Assert.Equal(expectedStatus, response.StatusCode);
        Assert.Equal(expectedError, await response.ReadErrorAsync(cancellationToken));
        await AssertNothingWrittenOrSentAsync(cancellationToken);
    }

    // MAM-13 — every check passed and the job is about to be saved when another request removes what it
    // points at: the foreign key settles it, and the send answers the sentence the dialog would have got a
    // moment later rather than a 500 — its job and audit row never saved, nothing fetched or sent.
    [Theory]
    [InlineData("the client", ClientGoneError)]
    [InlineData("the destination", DestinationGoneError)]
    [InlineData("the release", ReleaseGoneError)]
    public async Task Create_when_what_it_names_is_removed_before_the_save_is_404_with_the_sentence_and_sends_nothing(string removed, string expectedError)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var release = await _releases.CreateFound(_member.User, _toloka);
        _factory.SaveChangesHook.BeforeNextSave(
            context => context.ChangeTracker.Entries<DownloadJobEntity>().Any(entry => entry.State == EntityState.Added),
            async token =>
            {
                await using var scope = _factory.Services.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var deleted = removed switch
                {
                    // The client's destinations go with it.
                    "the client" => await db.IntegrationConnections.Where(row => row.Id == _client.Id).ExecuteDeleteAsync(token),
                    "the destination" => await db.DestinationMappings.Where(row => row.Id == _movies.Id).ExecuteDeleteAsync(token),
                    // The prune's doing, once its search is a day old.
                    "the release" => await db.Releases.Where(row => row.Id == release.Id).ExecuteDeleteAsync(token),
                    _ => throw new ArgumentOutOfRangeException(nameof(removed), removed, null),
                };
                Assert.Equal(1, deleted);
            });

        //Act
        var response = await SendAsync(_member, release.Id);

        //Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(expectedError, await response.ReadErrorAsync(cancellationToken));
        await AssertNothingWrittenOrSentAsync(cancellationToken);
    }

    [Fact]
    public async Task Create_whose_save_fails_for_another_reason_is_500_and_sends_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var release = await _releases.CreateFound(_member.User, _toloka);
        // Not a race: nothing the job points at went, so the failure is the database's (a full disk).
        _factory.DbCommandHook.BeforeNextCommand(
            command => command.CommandText.Contains("INSERT INTO \"DownloadJobs\"", StringComparison.Ordinal),
            _ => throw new InvalidOperationException("The disk is full."));

        //Act
        var response = await SendAsync(_member, release.Id);

        //Assert
        // A failure, not a refusal: nothing was written, so nothing may be sent either.
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.StartsWith("The server failed to process this request.", await response.ReadErrorAsync(cancellationToken), StringComparison.Ordinal);
        await AssertNothingWrittenOrSentAsync(cancellationToken);
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    // A qBittorrent that signs in with the fake server's credentials and maps its /downloads to the app's
    // /media, as ADR 0003's reference installation does.
    private Task<IntegrationConnectionEntity> CreateClientAsync(string name, string baseUrl) =>
        _integrations.Create(
            "qbittorrent",
            name,
            baseUrl,
            userName: FakeQBittorrentServer.DefaultUserName,
            password: FakeQBittorrentServer.DefaultPassword,
            pathMappings: [new PathMappingPair("/downloads", "/media")]);

    /// <summary>A send of <paramref name="releaseId"/> by <paramref name="user"/> under a new request id, to
    /// the test's qBittorrent and its Movies destination unless told otherwise.</summary>
    private Task<HttpResponseMessage> SendAsync(AuthorizedUser user, Guid releaseId, Guid? clientId = null, Guid? destinationId = null) =>
        _factory.PostHttpRequest(BaseUrl, user.AccessToken, new SendDownloadRequest(Guid.NewGuid(), releaseId, clientId ?? _client.Id, destinationId ?? _movies.Id));

    /// <summary>A send whose body is <paramref name="json"/> as it is — JSON the helpers can't write, such as
    /// a bare <c>null</c>.</summary>
    private Task<HttpResponseMessage> PostJsonTextAsync(string json)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, BaseUrl) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _member.AccessToken);
        return _factory.SendHttpRequest(request);
    }

    // Straight to the column, as the owner's switch on the Integrations page leaves it.
    private Task SwitchOffAsync(Guid integrationId) => _factory.WithDbAsync(db =>
        db.IntegrationConnections
            .Where(integration => integration.Id == integrationId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(integration => integration.IsEnabled, false)));

    private async Task AssertNothingWrittenOrSentAsync(CancellationToken cancellationToken)
    {
        await _factory.WithDbAsync(async db =>
        {
            Assert.Equal(0, await db.DownloadJobs.CountAsync(cancellationToken));
            Assert.Equal(0, await db.AuditEvents.CountAsync(cancellationToken));
        });
        Assert.Empty(_factory.TorznabHttp.Requests);
        Assert.Empty(_factory.QBittorrentHttp.Requests);
    }
}
