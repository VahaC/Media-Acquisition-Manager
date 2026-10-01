using System.Net;
using System.Net.Http.Json;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Tests.Settings;

// MAM-11, decision 2 — one default destination for the whole installation, never replaced automatically:
// the first destination is the default unasked, marking another moves it in the same change (one audit
// row, for the destination the owner edited), un-marking or removing it leaves none, and two saves that
// each take it never leave two.
public sealed class DestinationsDefaultHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/settings/destinations";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private IIntegrationConnectionSeeder _integrations = null!;
    private IDestinationMappingSeeder _destinations = null!;
    private AuthorizedUser _owner = null!;
    private IntegrationConnectionEntity _client = null!;

    public DestinationsDefaultHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _integrations = new IntegrationConnectionSeeder(_factory.Scope);
        _destinations = new DestinationMappingSeeder(_factory.Scope);
        _owner = await new UserSeeder(_factory.Scope).CreateAuthorizedOwner("owner");
        new FakeQBittorrentServer().Arm(_factory.QBittorrentHttp);
        _client = await _integrations.Create(
            "qbittorrent", "qBittorrent", FakeQBittorrentServer.DefaultBaseUrl, FakeQBittorrentServer.DefaultUserName, FakeQBittorrentServer.DefaultPassword);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Theory]
    [InlineData(null)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Create_of_the_first_destination_makes_it_the_default_whatever_it_asks(bool? isDefault)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _owner.AccessToken, new CreateDestinationRequest("Movies", _client.Id, "Movies", isDefault));

        //Assert
        // So there is one as soon as there is anything to send downloads to.
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<DestinationDto>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken);
        Assert.True(body!.IsDefault);
        Assert.Equal([("Movies", true)], await DefaultsAsync(cancellationToken));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(false)]
    public async Task Create_of_a_later_destination_not_asking_for_it_leaves_the_default_where_it_is(bool? isDefault)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _destinations.Create(_client, "Movies", "Movies", isDefault: true);

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _owner.AccessToken, new CreateDestinationRequest("Cartoons", _client.Id, "Cartoons", isDefault));

        //Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal([("Cartoons", false), ("Movies", true)], await DefaultsAsync(cancellationToken));
    }

    [Fact]
    public async Task Create_of_a_later_destination_while_none_is_the_default_does_not_make_it_one_unasked()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // The owner un-marked or removed the default: picking the next one is theirs.
        await _destinations.Create(_client, "Movies", "Movies");

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _owner.AccessToken, new CreateDestinationRequest("Cartoons", _client.Id, "Cartoons"));

        //Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal([("Cartoons", false), ("Movies", false)], await DefaultsAsync(cancellationToken));
    }

    [Fact]
    public async Task Create_marked_default_takes_it_over_from_the_one_that_was_with_one_audit_row()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _destinations.Create(_client, "Movies", "Movies", isDefault: true);

        //Act
        var response = await _factory.PostHttpRequest(
            BaseUrl, _owner.AccessToken, new CreateDestinationRequest("Cartoons", _client.Id, "Cartoons", IsDefault: true));

        //Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal([("Cartoons", true), ("Movies", false)], await DefaultsAsync(cancellationToken));
        await _factory.WithDbAsync(async db =>
        {
            // The default taken off Movies is part of adding Cartoons, not a change of its own.
            var audit = await db.AuditEvents.SingleAsync(cancellationToken);
            Assert.Equal(AuditAction.DestinationCreated, audit.Action);
            Assert.Equal("Cartoons", audit.TargetName);
        });
    }

    [Fact]
    public async Task Update_marking_another_default_moves_it_without_asking_the_client_with_one_audit_row()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _destinations.Create(_client, "Movies", "Movies", isDefault: true);
        var cartoons = await _destinations.Create(_client, "Cartoons", "Cartoons");

        //Act
        var response = await _factory.PutHttpRequest(
            $"{BaseUrl}/{cartoons.Id}", _owner.AccessToken, new UpdateDestinationRequest("Cartoons", _client.Id, "Cartoons", IsDefault: true));

        //Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal([("Cartoons", true), ("Movies", false)], await DefaultsAsync(cancellationToken));
        // Which destination is the default doesn't depend on what the client has (decision 3).
        Assert.Empty(_factory.QBittorrentHttp.Requests);
        await _factory.WithDbAsync(async db =>
        {
            var audit = await db.AuditEvents.SingleAsync(cancellationToken);
            Assert.Equal(AuditAction.DestinationUpdated, audit.Action);
            Assert.Equal(cartoons.Id, audit.TargetId);
        });
    }

    [Fact]
    public async Task Update_un_marking_the_default_leaves_the_installation_without_one()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var movies = await _destinations.Create(_client, "Movies", "Movies", isDefault: true);
        await _destinations.Create(_client, "Cartoons", "Cartoons");

        //Act
        var response = await _factory.PutHttpRequest(
            $"{BaseUrl}/{movies.Id}", _owner.AccessToken, new UpdateDestinationRequest("Movies", _client.Id, "Movies", IsDefault: false));

        //Assert
        // Never replaced automatically: "the next one" would be a guess.
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal([("Cartoons", false), ("Movies", false)], await DefaultsAsync(cancellationToken));
        await _factory.WithDbAsync(async db => Assert.Equal(AuditAction.DestinationUpdated, (await db.AuditEvents.SingleAsync(cancellationToken)).Action));
    }

    [Fact]
    public async Task Update_leaving_the_default_out_keeps_it()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var movies = await _destinations.Create(_client, "Movies", "Movies", isDefault: true);

        //Act
        var response = await _factory.PutHttpRequest(
            $"{BaseUrl}/{movies.Id}", _owner.AccessToken, new { name = "Films", downloadClientId = _client.Id, clientDestinationKey = "Movies" });

        //Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal([("Films", true)], await DefaultsAsync(cancellationToken));
    }

    [Fact]
    public async Task Update_marking_the_default_default_again_changes_nothing_and_records_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var movies = await _destinations.Create(_client, "Movies", "Movies", isDefault: true);

        //Act
        var response = await _factory.PutHttpRequest(
            $"{BaseUrl}/{movies.Id}", _owner.AccessToken, new UpdateDestinationRequest("Movies", _client.Id, "Movies", IsDefault: true));

        //Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal([("Movies", true)], await DefaultsAsync(cancellationToken));
        await _factory.WithDbAsync(async db => Assert.Equal(0, await db.AuditEvents.CountAsync(cancellationToken)));
    }

    [Fact]
    public async Task Delete_of_the_default_leaves_the_installation_without_one()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var movies = await _destinations.Create(_client, "Movies", "Movies", isDefault: true);
        await _destinations.Create(_client, "Cartoons", "Cartoons");
        await _destinations.Create(_client, "TV Shows", "TV Shows");

        //Act
        var response = await _factory.DeleteHttpRequest($"{BaseUrl}/{movies.Id}", _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal([("Cartoons", false), ("TV Shows", false)], await DefaultsAsync(cancellationToken));
    }

    [Fact]
    public async Task Two_saves_that_each_take_the_default_at_once_leave_exactly_one()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _destinations.Create(_client, "Movies", "Movies", isDefault: true);
        var cartoons = await _destinations.Create(_client, "Cartoons", "Cartoons");
        Task<HttpResponseMessage>? create = null;
        // Deterministic overlap: an edit taking the default from Movies has cleared it inside its
        // transaction, which holds SQLite's write lock, and waits there while a create that takes the
        // default too is sent and runs up to the transaction of its own save. Without the edit's
        // transaction the create would instead run to its end and commit its default in the gap.
        _factory.SaveChangesHook.BeforeNextSave(
            context => context.ChangeTracker.Entries<DestinationMappingEntity>().Any(entry => entry.State == EntityState.Modified),
            async _ =>
            {
                var createReachedItsSave = new TaskCompletionSource();
                _factory.TransactionHook.BeforeNextTransaction(
                    context => context.ChangeTracker.Entries<DestinationMappingEntity>().Any(entry => entry.State == EntityState.Added),
                    _ =>
                    {
                        createReachedItsSave.TrySetResult();
                        return Task.CompletedTask;
                    });
                create = _factory.PostHttpRequest(
                    BaseUrl, _owner.AccessToken, new CreateDestinationRequest("TV Shows", _client.Id, "TV Shows", IsDefault: true));
                await Task.WhenAny(create, createReachedItsSave.Task);
            });

        //Act
        var edit = await _factory.PutHttpRequest(
            $"{BaseUrl}/{cartoons.Id}", _owner.AccessToken, new UpdateDestinationRequest("Cartoons", _client.Id, "Cartoons", IsDefault: true));
        var created = create is null ? null : await create;

        //Assert
        // The partial unique index allows one default, and each save clears the others first inside its
        // transaction: neither fails, the create's clearing waits for the edit's commit and takes the
        // default from Cartoons, and there are never two, nor none.
        Assert.Equal(HttpStatusCode.NoContent, edit.StatusCode);
        Assert.Equal(HttpStatusCode.Created, created?.StatusCode);
        Assert.Equal([("Cartoons", false), ("Movies", false), ("TV Shows", true)], await DefaultsAsync(cancellationToken));
    }

    // Every destination's name and whether it is the default, by name.
    private async Task<List<(string Name, bool IsDefault)>> DefaultsAsync(CancellationToken cancellationToken)
    {
        var rows = new List<(string Name, bool IsDefault)>();
        await _factory.WithDbAsync(async db => rows.AddRange(
            (await db.DestinationMappings.OrderBy(destination => destination.NormalizedName).ToListAsync(cancellationToken))
                .Select(destination => (destination.Name, destination.IsDefault))));
        return rows;
    }
}
