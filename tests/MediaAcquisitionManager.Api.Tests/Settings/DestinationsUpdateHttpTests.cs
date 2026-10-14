using System.Net;
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

namespace MediaAcquisitionManager.Api.Tests.Settings;

// MAM-11 — changing a destination: what is stored and audited, the create's rules, decision 3 on an
// edit — the client is asked only about a new client or a new category, so a rename saves while the
// client is switched off or away — and the races an edit can lose between its checks and its save. The
// default's rules are DestinationsDefaultHttpTests.
public sealed class DestinationsUpdateHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/settings/destinations";
    private const string SecondClientAddress = "http://qbittorrent-2.test:8080";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private IIntegrationConnectionSeeder _integrations = null!;
    private IDestinationMappingSeeder _destinations = null!;
    private AuthorizedUser _owner = null!;
    private FakeQBittorrentServer _qbittorrent = null!;
    private FakeQBittorrentServer _secondQBittorrent = null!;

    public DestinationsUpdateHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _integrations = new IntegrationConnectionSeeder(_factory.Scope);
        _destinations = new DestinationMappingSeeder(_factory.Scope);
        _owner = await new UserSeeder(_factory.Scope).CreateAuthorizedOwner("owner");
        _qbittorrent = new FakeQBittorrentServer();
        _secondQBittorrent = new FakeQBittorrentServer(SecondClientAddress);
        FakeQBittorrentServer.Arm(_factory.QBittorrentHttp, _qbittorrent, _secondQBittorrent);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Update_to_another_category_is_204_once_the_client_confirms_it_and_persists_the_change()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var client = await CreateClientAsync();
        var destination = await _destinations.Create(client, "Movies", "Movies", isDefault: true);

        //Act
        var response = await _factory.PutHttpRequest(
            $"{BaseUrl}/{destination.Id}", _owner.AccessToken, new UpdateDestinationRequest("  Мультфільми  ", client.Id, "Cartoons", IsDefault: true));

        //Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsStringAsync(cancellationToken));
        Assert.Equal(
            ["POST /api/v2/auth/login", "GET /api/v2/app/version", "GET /api/v2/torrents/categories"],
            RequestsTo(_qbittorrent.BaseUrl));
        await _factory.WithDbAsync(async db =>
        {
            var row = await db.DestinationMappings.SingleAsync(cancellationToken);
            Assert.Equal("Мультфільми", row.Name);
            Assert.Equal("МУЛЬТФІЛЬМИ", row.NormalizedName);
            Assert.Equal("Cartoons", row.ClientDestinationKey);
            Assert.Equal(client.Id, row.DownloadClientConnectionId);
            Assert.True(row.IsDefault);
            Assert.Equal(destination.CreatedUtc, row.CreatedUtc);
        });
    }

    [Fact]
    public async Task Update_records_a_destination_updated_audit_row_with_the_new_name()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var now = _factory.Time.GetUtcNow().UtcDateTime;
        var client = await CreateClientAsync();
        var destination = await _destinations.Create(client);

        //Act
        await _factory.PutHttpRequest($"{BaseUrl}/{destination.Id}", _owner.AccessToken, new UpdateDestinationRequest("Films", client.Id, "Movies"));

        //Assert
        await _factory.WithDbAsync(async db =>
        {
            var audit = await db.AuditEvents.SingleAsync(cancellationToken);
            Assert.Equal(AuditAction.DestinationUpdated, audit.Action);
            Assert.Equal(_owner.User.Id, audit.ActorUserId);
            Assert.Equal("owner", audit.ActorUserName);
            Assert.Equal(AuditTargetKind.Destination, audit.TargetKind);
            Assert.Equal(destination.Id, audit.TargetId);
            Assert.Equal("Films", audit.TargetName);
            Assert.Equal(now, audit.CreatedUtc);
        });
    }

    [Theory]
    [InlineData("unreachable")]
    [InlineData("switched off")]
    public async Task Update_of_the_name_only_saves_without_asking_the_client(string state)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var client = await CreateClientAsync(isEnabled: state != "switched off");
        _qbittorrent.Unreachable = state == "unreachable";
        var destination = await _destinations.Create(client);

        //Act
        var response = await _factory.PutHttpRequest(
            $"{BaseUrl}/{destination.Id}", _owner.AccessToken, new UpdateDestinationRequest("Films", client.Id, "Movies"));

        //Assert
        // A new name doesn't depend on what the client has, so the client's state doesn't hold it back.
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Empty(_factory.QBittorrentHttp.Requests);
        await _factory.WithDbAsync(async db => Assert.Equal("Films", (await db.DestinationMappings.SingleAsync(cancellationToken)).Name));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(false)]
    public async Task Update_that_changes_nothing_is_204_and_records_nothing(bool? isDefault)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var client = await CreateClientAsync();
        var destination = await _destinations.Create(client, "Movies", "Movies");

        //Act
        // The name as stored, with spaces around it that the save trims away, and the default left out or
        // false, as it is.
        var response = await _factory.PutHttpRequest(
            $"{BaseUrl}/{destination.Id}", _owner.AccessToken, new UpdateDestinationRequest(" Movies ", client.Id, "Movies", isDefault));

        //Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await _factory.WithDbAsync(async db => Assert.Empty(await db.AuditEvents.ToListAsync(cancellationToken)));
        Assert.Empty(_factory.QBittorrentHttp.Requests);
    }

    [Fact]
    public async Task Update_to_its_own_name_in_another_letter_case_is_204()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var client = await CreateClientAsync();
        var destination = await _destinations.Create(client, "Movies", "Movies");

        //Act
        var response = await _factory.PutHttpRequest(
            $"{BaseUrl}/{destination.Id}", _owner.AccessToken, new UpdateDestinationRequest("MOVIES", client.Id, "Movies"));

        //Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await _factory.WithDbAsync(async db => Assert.Equal("MOVIES", (await db.DestinationMappings.SingleAsync(cancellationToken)).Name));
    }

    [Fact]
    public async Task Update_to_a_name_another_destination_of_the_client_has_is_409_and_changes_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var client = await CreateClientAsync();
        await _destinations.Create(client, "Movies", "Movies");
        var cartoons = await _destinations.Create(client, "Cartoons", "Cartoons");

        //Act
        var response = await _factory.PutHttpRequest(
            $"{BaseUrl}/{cartoons.Id}", _owner.AccessToken, new UpdateDestinationRequest("movies", client.Id, "Cartoons"));

        //Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("“qBittorrent” already has a destination named “movies”.", await response.ReadErrorAsync(cancellationToken));
        await AssertUnchangedAsync(cartoons, cancellationToken);
    }

    [Fact]
    public async Task Update_to_another_client_asks_that_client_and_not_the_old_one()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var client = await CreateClientAsync();
        var second = await CreateClientAsync("Second", _secondQBittorrent);
        var destination = await _destinations.Create(client);

        //Act
        var response = await _factory.PutHttpRequest(
            $"{BaseUrl}/{destination.Id}", _owner.AccessToken, new UpdateDestinationRequest("Movies", second.Id, "Movies"));

        //Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Empty(RequestsTo(_qbittorrent.BaseUrl));
        Assert.Equal(
            ["POST /api/v2/auth/login", "GET /api/v2/app/version", "GET /api/v2/torrents/categories"],
            RequestsTo(_secondQBittorrent.BaseUrl));
        await _factory.WithDbAsync(async db =>
            Assert.Equal(second.Id, (await db.DestinationMappings.SingleAsync(cancellationToken)).DownloadClientConnectionId));
    }

    [Fact]
    public async Task Update_to_a_category_the_client_has_not_got_is_409_and_changes_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var client = await CreateClientAsync();
        var destination = await _destinations.Create(client);

        //Act
        var response = await _factory.PutHttpRequest(
            $"{BaseUrl}/{destination.Id}", _owner.AccessToken, new UpdateDestinationRequest("Movies", client.Id, "Movies 4K"));

        //Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(
            "“qBittorrent” has no category “Movies 4K”. Create it in the client first, or pick one it has.",
            await response.ReadErrorAsync(cancellationToken));
        await AssertUnchangedAsync(destination, cancellationToken);
    }

    [Fact]
    public async Task Update_to_another_category_of_a_switched_off_client_is_409_and_asks_it_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var client = await CreateClientAsync(isEnabled: false);
        var destination = await _destinations.Create(client);

        //Act
        var response = await _factory.PutHttpRequest(
            $"{BaseUrl}/{destination.Id}", _owner.AccessToken, new UpdateDestinationRequest("Movies", client.Id, "Cartoons"));

        //Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(
            "“qBittorrent” is switched off. Switch it on under Integrations to use it for destinations.",
            await response.ReadErrorAsync(cancellationToken));
        await AssertUnchangedAsync(destination, cancellationToken);
        Assert.Empty(_factory.QBittorrentHttp.Requests);
    }

    [Fact]
    public async Task Update_to_another_category_while_the_client_is_unreachable_is_502_with_its_sentence()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var client = await CreateClientAsync();
        var destination = await _destinations.Create(client);
        _qbittorrent.Unreachable = true;

        //Act
        var response = await _factory.PutHttpRequest(
            $"{BaseUrl}/{destination.Id}", _owner.AccessToken, new UpdateDestinationRequest("Movies", client.Id, "Cartoons"));

        //Assert
        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal(
            "Couldn't reach qBittorrent at http://qbittorrent.test:8080: nothing accepted the connection. Check the address and port, and that qBittorrent is running.",
            await response.ReadErrorAsync(cancellationToken));
        await AssertUnchangedAsync(destination, cancellationToken);
    }

    [Theory]
    [InlineData("empty name", "A destination needs a name.")]
    [InlineData("missing client", "Pick the download client this destination belongs to.")]
    // MAM-31 — in the words of the client's kind, by its name.
    [InlineData("missing category", "Pick the category of “qBittorrent” this destination saves to.")]
    public async Task Update_with_a_blank_or_missing_field_is_400_with_the_rule(string blank, string expectedError)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var client = await CreateClientAsync();
        var destination = await _destinations.Create(client);
        object body = blank switch
        {
            "empty name" => new UpdateDestinationRequest("", client.Id, "Movies"),
            "missing client" => new { name = "Movies", clientDestinationKey = "Movies" },
            "missing category" => new { name = "Movies", downloadClientId = client.Id },
            _ => throw new ArgumentOutOfRangeException(nameof(blank), blank, null),
        };

        //Act
        var response = await _factory.PutHttpRequest($"{BaseUrl}/{destination.Id}", _owner.AccessToken, body);

        //Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(expectedError, await response.ReadErrorAsync(cancellationToken));
        await AssertUnchangedAsync(destination, cancellationToken);
    }

    [Fact]
    public async Task Update_to_an_id_that_is_not_a_download_client_is_404_with_the_sentence()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var client = await CreateClientAsync();
        var destination = await _destinations.Create(client);

        //Act
        var response = await _factory.PutHttpRequest(
            $"{BaseUrl}/{destination.Id}", _owner.AccessToken, new UpdateDestinationRequest("Movies", Guid.NewGuid(), "Movies"));

        //Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(
            "The download client you picked no longer exists. Reload the page and pick one again.",
            await response.ReadErrorAsync(cancellationToken));
        await AssertUnchangedAsync(destination, cancellationToken);
    }

    [Fact]
    public async Task Update_of_a_destination_that_does_not_exist_is_a_bare_404()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var client = await CreateClientAsync();

        //Act
        var response = await _factory.PutHttpRequest(
            $"{BaseUrl}/{Guid.NewGuid()}", _owner.AccessToken, new UpdateDestinationRequest("Movies", client.Id, "Movies"));

        //Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsStringAsync(cancellationToken));
        await _factory.WithDbAsync(async db => Assert.Equal(0, await db.AuditEvents.CountAsync(cancellationToken)));
        Assert.Empty(_factory.QBittorrentHttp.Requests);
    }

    [Fact]
    public async Task Update_of_a_destination_another_request_removes_before_the_save_is_a_bare_404_and_changes_nothing_else()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var client = await CreateClientAsync();
        var movies = await _destinations.Create(client, "Movies", "Movies", isDefault: true);
        var cartoons = await _destinations.Create(client, "Cartoons", "Cartoons");
        // Deterministic race: this edit read the destination and passed every rule, and another request
        // removes it just before the save.
        _factory.SaveChangesHook.BeforeNextSave(IsEditingADestination, token => RemoveDestinationAsync(cartoons.Id, token));

        //Act
        var response = await _factory.PutHttpRequest(
            $"{BaseUrl}/{cartoons.Id}", _owner.AccessToken, new UpdateDestinationRequest("Films", client.Id, "Cartoons"));

        //Assert
        // Gone either way: the same bare 404 as a destination that was never there, not a 500, and no
        // audit row for a change that wasn't saved.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsStringAsync(cancellationToken));
        await _factory.WithDbAsync(async db => Assert.False(await db.DestinationMappings.AnyAsync(row => row.Id == cartoons.Id, cancellationToken)));
        await AssertUnchangedAsync(movies, cancellationToken);
    }

    [Fact]
    public async Task Update_to_a_name_another_request_gives_a_destination_of_the_client_first_is_409_and_changes_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var client = await CreateClientAsync();
        var cartoons = await _destinations.Create(client, "Cartoons", "Cartoons");
        DestinationMappingEntity? competing = null;
        // The edit passed the "name is free" check, and another request's destination of the same client
        // takes the name just before the save.
        _factory.SaveChangesHook.BeforeNextSave(
            IsEditingADestination,
            async _ => competing = await InAnotherRequestAsync(services => new DestinationMappingSeeder(services).Create(client, "FILMS", "Movies")));

        //Act
        var response = await _factory.PutHttpRequest(
            $"{BaseUrl}/{cartoons.Id}", _owner.AccessToken, new UpdateDestinationRequest("Films", client.Id, "Cartoons"));

        //Assert
        // The unique index settles it: the loser is a 409, not a 500, and keeps its old name.
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("“qBittorrent” already has a destination named “Films”.", await response.ReadErrorAsync(cancellationToken));
        Assert.NotNull(competing);
        await AssertUnchangedAsync(cartoons, cancellationToken);
        await _factory.WithDbAsync(async db =>
            Assert.Equal(competing.Id, (await db.DestinationMappings.SingleAsync(row => row.NormalizedName == "FILMS", cancellationToken)).Id));
    }

    [Fact]
    public async Task Update_to_another_client_that_is_removed_before_the_save_is_404_with_the_sentence_and_changes_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var client = await CreateClientAsync();
        var second = await CreateClientAsync("Second", _secondQBittorrent);
        var destination = await _destinations.Create(client);
        // The second client confirmed the category, then another request removed that integration just
        // before the save.
        _factory.SaveChangesHook.BeforeNextSave(IsEditingADestination, token => RemoveClientAsync(second.Id, token));

        //Act
        var response = await _factory.PutHttpRequest(
            $"{BaseUrl}/{destination.Id}", _owner.AccessToken, new UpdateDestinationRequest("Movies", second.Id, "Movies"));

        //Assert
        // The foreign key settles it: the client is gone, which the dialog says, rather than a 500.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(
            "The download client you picked no longer exists. Reload the page and pick one again.",
            await response.ReadErrorAsync(cancellationToken));
        await AssertUnchangedAsync(destination, cancellationToken);
    }

    // MAM-11, decision 2 — an edit taking the default clears the old one first, inside its transaction;
    // losing a race after that must roll the clearing back with the rest, or the installation is left
    // with no default at all.
    [Theory]
    [InlineData("removed", HttpStatusCode.NotFound)]
    [InlineData("name taken", HttpStatusCode.Conflict)]
    [InlineData("client removed", HttpStatusCode.NotFound)]
    public async Task Update_taking_the_default_that_loses_a_race_leaves_the_old_default_where_it_was(string race, HttpStatusCode expectedStatus)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var client = await CreateClientAsync();
        var second = await CreateClientAsync("Second", _secondQBittorrent);
        var movies = await _destinations.Create(client, "Movies", "Movies", isDefault: true);
        var cartoons = await _destinations.Create(client, "Cartoons", "Cartoons");
        var request = race switch
        {
            "removed" => new UpdateDestinationRequest("Cartoons", client.Id, "Cartoons", IsDefault: true),
            "name taken" => new UpdateDestinationRequest("Films", client.Id, "Cartoons", IsDefault: true),
            "client removed" => new UpdateDestinationRequest("Cartoons", second.Id, "Cartoons", IsDefault: true),
            _ => throw new ArgumentOutOfRangeException(nameof(race), race, null),
        };
        // Deterministic race: the edit passed every rule, and the other request's write lands just before
        // the transaction that clears Movies' default begins — not inside it, where the edit holds SQLite's
        // write lock and the other write would wait for it.
        _factory.TransactionHook.BeforeNextTransaction(IsEditingADestination, token => race switch
        {
            "removed" => RemoveDestinationAsync(cartoons.Id, token),
            "name taken" => InAnotherRequestAsync(services => new DestinationMappingSeeder(services).Create(client, "FILMS", "Movies")),
            _ => RemoveClientAsync(second.Id, token),
        });

        //Act
        var response = await _factory.PutHttpRequest($"{BaseUrl}/{cartoons.Id}", _owner.AccessToken, request);

        //Assert
        Assert.Equal(expectedStatus, response.StatusCode);
        // Movies is still the default — the clearing rolled back — and nothing was recorded.
        await AssertUnchangedAsync(movies, cancellationToken);
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    // The edit's save, or the transaction around it: the destination changed in its context.
    private static bool IsEditingADestination(DbContext context) =>
        context.ChangeTracker.Entries<DestinationMappingEntity>().Any(entry => entry.State == EntityState.Modified);

    // What another request does: through a scope and a context of its own.
    private async Task<T> InAnotherRequestAsync<T>(Func<IServiceProvider, Task<T>> action)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider);
    }

    private Task<int> RemoveDestinationAsync(Guid destinationId, CancellationToken cancellationToken) =>
        InAnotherRequestAsync(services => services.GetRequiredService<ApplicationDbContext>().DestinationMappings
            .Where(destination => destination.Id == destinationId)
            .ExecuteDeleteAsync(cancellationToken));

    private Task<int> RemoveClientAsync(Guid clientId, CancellationToken cancellationToken) =>
        InAnotherRequestAsync(services => services.GetRequiredService<ApplicationDbContext>().IntegrationConnections
            .Where(integration => integration.Id == clientId)
            .ExecuteDeleteAsync(cancellationToken));

    private Task<IntegrationConnectionEntity> CreateClientAsync(string name = "qBittorrent", FakeQBittorrentServer? server = null, bool isEnabled = true) =>
        _integrations.Create(
            "qbittorrent",
            name,
            (server ?? _qbittorrent).BaseUrl,
            userName: FakeQBittorrentServer.DefaultUserName,
            password: FakeQBittorrentServer.DefaultPassword,
            isEnabled: isEnabled,
            pathMappings: [new PathMappingPair("/downloads", "/media")]);

    private List<string> RequestsTo(string baseUrl) =>
        [.. _factory.QBittorrentHttp.Requests
            .Where(request => request.Uri.AbsoluteUri.StartsWith(baseUrl, StringComparison.Ordinal))
            .Select(request => $"{request.Method} {request.Uri.AbsolutePath}")];

    private Task AssertUnchangedAsync(DestinationMappingEntity seeded, CancellationToken cancellationToken) => _factory.WithDbAsync(async db =>
    {
        var row = await db.DestinationMappings.SingleAsync(destination => destination.Id == seeded.Id, cancellationToken);
        Assert.Equal(seeded.Name, row.Name);
        Assert.Equal(seeded.NormalizedName, row.NormalizedName);
        Assert.Equal(seeded.ClientDestinationKey, row.ClientDestinationKey);
        Assert.Equal(seeded.DownloadClientConnectionId, row.DownloadClientConnectionId);
        Assert.Equal(seeded.IsDefault, row.IsDefault);
        Assert.Equal(0, await db.AuditEvents.CountAsync(cancellationToken));
    });
}
