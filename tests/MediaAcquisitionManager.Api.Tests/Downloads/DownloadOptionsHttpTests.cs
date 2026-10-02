using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Paths;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Tests.Downloads;

// MAM-13 — the Download dialog's choices for one release: every switched-on download client of a type this
// build sends to that takes the release's payload and has a destination, by name, each with its
// destinations by name and which is the installation's default. For every signed-in user (decision 1), so
// names only — no address, credential, folder or path mapping — and read from the database alone: no client
// is asked. The release must be one of the caller's own searches' from the last day, as a send's must.
public sealed class DownloadOptionsHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/downloads";
    private const string TolokaAddress = "http://prowlarr.test:9696/1/api";
    private const string ApiKey = "planted-torznab-key-0001";
    private const string JellyfinApiKey = "0123456789abcdef0123456789abcdef";
    private const string SeedboxAddress = "http://seedbox.test:8080";

    // Spelled out rather than read from the product code: these are what the Download dialog shows.
    private const string ReleaseMissingError = "Pick the release to send.";
    private const string ReleaseGoneError = "This release isn't available any more: a search keeps what it found for a day. Search again.";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private IIntegrationConnectionSeeder _integrations = null!;
    private IDestinationMappingSeeder _destinations = null!;
    private IReleaseSeeder _releases = null!;
    private IUserSeeder _users = null!;
    private AuthorizedUser _owner = null!;
    private AuthorizedUser _member = null!;
    private FakeQBittorrentServer _qbittorrent = null!;
    private IntegrationConnectionEntity _toloka = null!;

    public DownloadOptionsHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _integrations = new IntegrationConnectionSeeder(_factory.Scope);
        _destinations = new DestinationMappingSeeder(_factory.Scope);
        _releases = new ReleaseSeeder(_factory.Scope);
        _users = new UserSeeder(_factory.Scope);
        _owner = await _users.CreateAuthorizedOwner("owner");
        _member = await _users.CreateAuthorized("member");
        new FakeTorznabServer(TolokaAddress, ApiKey).Arm(_factory.TorznabHttp);
        // Armed, so a client the listing asked would be recorded and answer as a working qBittorrent.
        _qbittorrent = new FakeQBittorrentServer();
        _qbittorrent.Arm(_factory.QBittorrentHttp);
        _toloka = await _integrations.Create("torznab", "Toloka", TolokaAddress, apiKey: ApiKey);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Options_lists_the_switched_on_clients_that_take_the_release_with_their_destinations()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // Made out of name order, so the answer's order is the names', in any letter case.
        var seedbox = await CreateClientAsync("Seedbox", SeedboxAddress);
        var client = await CreateClientAsync("qBittorrent", _qbittorrent.BaseUrl);
        var tvShows = await _destinations.Create(client, "TV Shows", "TV Shows");
        var movies = await _destinations.Create(client, "Movies", "Movies", isDefault: true);
        var cartoons = await _destinations.Create(client, "cartoons", "Cartoons");
        var films = await _destinations.Create(seedbox, "Films", "Movies");
        var release = await _releases.CreateFound(_member.User, _toloka);

        //Act
        var response = await GetOptionsAsync(_member, release.Id);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<DownloadOptionsResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken);
        Assert.Equivalent(
            new DownloadOptionsResponse(
            [
                new DownloadClientOptionDto(
                    client.Id,
                    "qBittorrent",
                    "qBittorrent",
                    [
                        new DownloadDestinationOptionDto(cartoons.Id, "cartoons", "Cartoons", IsDefault: false),
                        new DownloadDestinationOptionDto(movies.Id, "Movies", "Movies", IsDefault: true),
                        new DownloadDestinationOptionDto(tvShows.Id, "TV Shows", "TV Shows", IsDefault: false),
                    ]),
                // The type's name, whatever the owner named the client.
                new DownloadClientOptionDto(seedbox.Id, "Seedbox", "qBittorrent", [new DownloadDestinationOptionDto(films.Id, "Films", "Movies", IsDefault: false)]),
            ]),
            body,
            strict: true);
        Assert.Equal(["qBittorrent", "Seedbox"], body!.Clients.Select(option => option.Name));
        Assert.Equal(["cartoons", "Movies", "TV Shows"], body.Clients[0].Destinations.Select(destination => destination.Name));
        // From the database alone: the category is confirmed with the client when the release is sent.
        await AssertNothingWrittenOrAskedAsync(cancellationToken);
    }

    [Theory]
    [InlineData("switched off")]
    [InlineData("a type this version can't send to")]
    [InlineData("no destination")]
    [InlineData("a media library")]
    [InlineData("a release source")]
    public async Task Options_leaves_out_a_client_that_can_not_be_sent_to(string other)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var client = await CreateClientAsync("qBittorrent", _qbittorrent.BaseUrl);
        var movies = await _destinations.Create(client, "Movies", "Movies", isDefault: true);
        switch (other)
        {
            case "switched off":
                await _destinations.Create(await CreateClientAsync("Seedbox", SeedboxAddress, isEnabled: false), "Movies", "Movies");
                break;
            case "a type this version can't send to":
                await _destinations.Create(await _integrations.Create("retired-client", "Old client", kind: IntegrationKind.DownloadClient), "Movies", "Movies");
                break;
            case "no destination":
                await CreateClientAsync("Seedbox", SeedboxAddress);
                break;
            // Each with a destination planted on it, which the API would refuse: what isn't a download client
            // is never one, whatever rows point at it.
            case "a media library":
                await _destinations.Create(await _integrations.Create("jellyfin", "Living room", apiKey: JellyfinApiKey), "Movies", "Movies");
                break;
            case "a release source":
                await _destinations.Create(_toloka, "Movies", "Movies");
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(other), other, null);
        }

        var release = await _releases.CreateFound(_member.User, _toloka);

        //Act
        var response = await GetOptionsAsync(_member, release.Id);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<DownloadOptionsResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken);
        var listed = Assert.Single(body!.Clients);
        Assert.Equal(client.Id, listed.Id);
        Assert.Equal([movies.Id], listed.Destinations.Select(destination => destination.Id));
        await AssertNothingWrittenOrAskedAsync(cancellationToken);
    }

    // A release whose kind the listing didn't state is a gateway's link, a .torrent or a magnet once fetched:
    // a client taking either may get it, and the fetched kind is checked again before anything is sent.
    [Theory]
    [InlineData(ReleasePayloadKind.TorrentFile)]
    [InlineData(ReleasePayloadKind.Magnet)]
    [InlineData(null)]
    public async Task Options_for_a_torrent_release_lists_qbittorrent(ReleasePayloadKind? kind)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var client = await CreateClientAsync("qBittorrent", _qbittorrent.BaseUrl);
        await _destinations.Create(client, "Movies", "Movies", isDefault: true);
        var locator = kind == ReleasePayloadKind.Magnet
            ? "magnet:?xt=urn:btih:72d6069eff259a0800f7afdb2500d5de591a9f01&dn=x&tr=http%3A%2F%2Ftracker.test%2Fannounce%2Fplanted-passkey-0001"
            : null;
        var release = await _releases.CreateFound(_member.User, _toloka, locator: locator, payloadKind: kind);

        //Act
        var response = await GetOptionsAsync(_member, release.Id);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<DownloadOptionsResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken);
        Assert.Equal(client.Id, Assert.Single(body!.Clients).Id);
        await AssertNothingWrittenOrAskedAsync(cancellationToken);
    }

    [Fact]
    public async Task Options_for_an_nzb_release_lists_no_client()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var client = await CreateClientAsync("qBittorrent", _qbittorrent.BaseUrl);
        await _destinations.Create(client, "Movies", "Movies", isDefault: true);
        var release = await _releases.CreateFound(_member.User, _toloka, payloadKind: ReleasePayloadKind.Nzb);

        //Act
        var response = await GetOptionsAsync(_member, release.Id);

        //Assert
        // An answer, not a refusal: the dialog says there is nothing that takes this release.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<DownloadOptionsResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken);
        Assert.Empty(body!.Clients);
        await AssertNothingWrittenOrAskedAsync(cancellationToken);
    }

    [Fact]
    public async Task Options_answers_a_member_with_names_only()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var client = await CreateClientAsync("qBittorrent", _qbittorrent.BaseUrl);
        await _destinations.Create(client, "Movies", "Movies", isDefault: true);
        var release = await _releases.CreateFound(_member.User, _toloka);

        //Act
        var response = await GetOptionsAsync(_member, release.Id);

        //Assert
        // Decision 1 — everyone may send, so the owner's settings stay the owner's: exactly these properties,
        // and nothing of the client's address, sign-in or folders anywhere in the text.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        using var document = JsonDocument.Parse(text);
        Assert.Equal(["clients"], PropertyNames(document.RootElement));
        var option = Assert.Single(document.RootElement.GetProperty("clients").EnumerateArray().ToList());
        Assert.Equal(["id", "name", "typeName", "destinations"], PropertyNames(option));
        var destination = Assert.Single(option.GetProperty("destinations").EnumerateArray().ToList());
        Assert.Equal(["id", "name", "clientDestinationKey", "isDefault"], PropertyNames(destination));
        Assert.DoesNotContain("qbittorrent.test", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(FakeQBittorrentServer.DefaultUserName, text, StringComparison.Ordinal);
        Assert.DoesNotContain(FakeQBittorrentServer.DefaultPassword, text, StringComparison.Ordinal);
        Assert.DoesNotContain("/downloads", text, StringComparison.Ordinal);
        Assert.DoesNotContain("/media", text, StringComparison.Ordinal);
        Assert.DoesNotContain("prowlarr.test", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(ApiKey, text, StringComparison.Ordinal);
        await AssertNothingWrittenOrAskedAsync(cancellationToken);
    }

    [Fact]
    public async Task Options_for_a_release_whose_search_is_a_day_old_still_lists_the_clients()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var client = await CreateClientAsync("qBittorrent", _qbittorrent.BaseUrl);
        await _destinations.Create(client, "Movies", "Movies", isDefault: true);
        var release = await _releases.CreateFound(_member.User, _toloka);
        _factory.Time.Advance(TimeSpan.FromHours(24));
        var member = await _users.CreateSession(_member.User);

        //Act
        var response = await GetOptionsAsync(member, release.Id);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<DownloadOptionsResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken);
        Assert.Equal(client.Id, Assert.Single(body!.Clients).Id);
        await AssertNothingWrittenOrAskedAsync(cancellationToken);
    }

    // A string in the query, so a missing or malformed id gets the rule's sentence, not a validation body.
    [Theory]
    [InlineData("")]
    [InlineData("?releaseId=")]
    [InlineData("?releaseId=abc")]
    [InlineData("?releaseId=00000000-0000-0000-0000-000000000000")]
    [InlineData("?release=0f8fad5b-d9cb-469f-a165-70867728950e")]
    public async Task Options_without_a_usable_release_id_is_400_with_the_rule(string query)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var client = await CreateClientAsync("qBittorrent", _qbittorrent.BaseUrl);
        await _destinations.Create(client, "Movies", "Movies", isDefault: true);

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}/options{query}", _member.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ReleaseMissingError, await response.ReadErrorAsync(cancellationToken));
        await AssertNothingWrittenOrAskedAsync(cancellationToken);
    }

    [Theory]
    [InlineData("another user's, read by the installation owner")]
    [InlineData("from a search over a day old")]
    [InlineData("found by a plain release search")]
    [InlineData("never was")]
    public async Task Options_for_a_release_that_is_not_one_of_the_callers_searches_is_404_with_the_sentence(string release)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var client = await CreateClientAsync("qBittorrent", _qbittorrent.BaseUrl);
        await _destinations.Create(client, "Movies", "Movies", isDefault: true);
        var caller = _member;
        Guid releaseId;
        switch (release)
        {
            case "another user's, read by the installation owner":
                releaseId = (await _releases.CreateFound(_member.User, _toloka)).Id;
                caller = _owner;
                break;
            case "from a search over a day old":
                releaseId = (await _releases.CreateFound(_member.User, _toloka)).Id;
                _factory.Time.Advance(TimeSpan.FromHours(24) + TimeSpan.FromSeconds(1));
                caller = await _users.CreateSession(_member.User);
                break;
            case "found by a plain release search":
                var searched = await _factory.PostHttpRequest("/api/releases/search", _member.AccessToken, new ReleaseSearchRequest("Interstellar"));
                var found = await searched.Content.ReadFromJsonAsync<ReleaseSearchResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken);
                releaseId = found!.Sources.Single().Releases[0].Id;
                break;
            case "never was":
                releaseId = Guid.NewGuid();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(release), release, null);
        }

        // The plain release search's own requests are the only ones the feed may have had.
        var feedRequests = _factory.TorznabHttp.Requests.Count;

        //Act
        var response = await GetOptionsAsync(caller, releaseId);

        //Assert
        // A release id in the query, not the addressed row: the dialog gets the sentence, which says the same
        // for someone else's release as for a forgotten one.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(ReleaseGoneError, await response.ReadErrorAsync(cancellationToken));
        await AssertNothingWrittenOrAskedAsync(cancellationToken, feedRequests);
    }

    [Fact]
    public async Task Options_without_a_token_is_401()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var client = await CreateClientAsync("qBittorrent", _qbittorrent.BaseUrl);
        await _destinations.Create(client, "Movies", "Movies", isDefault: true);
        var release = await _releases.CreateFound(_member.User, _toloka);

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}/options?releaseId={release.Id}");

        //Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        await AssertNothingWrittenOrAskedAsync(cancellationToken);
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    // A qBittorrent that signs in with the fake server's credentials and maps its /downloads to the app's
    // /media, as ADR 0003's reference installation does.
    private Task<IntegrationConnectionEntity> CreateClientAsync(string name, string baseUrl, bool isEnabled = true) =>
        _integrations.Create(
            "qbittorrent",
            name,
            baseUrl,
            userName: FakeQBittorrentServer.DefaultUserName,
            password: FakeQBittorrentServer.DefaultPassword,
            isEnabled: isEnabled,
            pathMappings: [new PathMappingPair("/downloads", "/media")]);

    private Task<HttpResponseMessage> GetOptionsAsync(AuthorizedUser user, Guid releaseId) =>
        _factory.GetHttpRequest($"{BaseUrl}/options?releaseId={releaseId}", user.AccessToken);

    /// <summary>A read of the database alone: no job or audit row written, no client asked, and nothing
    /// fetched from the feed beyond its first <paramref name="feedRequests"/> requests.</summary>
    private async Task AssertNothingWrittenOrAskedAsync(CancellationToken cancellationToken, int feedRequests = 0)
    {
        await _factory.WithDbAsync(async db =>
        {
            Assert.Equal(0, await db.DownloadJobs.CountAsync(cancellationToken));
            Assert.Equal(0, await db.AuditEvents.CountAsync(cancellationToken));
        });
        Assert.Empty(_factory.QBittorrentHttp.Requests);
        Assert.Equal(feedRequests, _factory.TorznabHttp.Requests.Count);
    }

    private static List<string> PropertyNames(JsonElement element) => [.. element.EnumerateObject().Select(property => property.Name)];
}
