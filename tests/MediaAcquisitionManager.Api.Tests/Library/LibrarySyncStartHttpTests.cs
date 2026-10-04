using System.Net;
using System.Net.Http.Json;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Api.Services.Library;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Data;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MediaAcquisitionManager.Api.Tests.Library;

// MAM-8 — starting a library sync (POST /api/library/sources/{id}/syncs): the owner's alone, it only
// queues a run (202 + run row, Queue/Worker) and reaches nothing else — not Jellyfin, not the snapshot.
// Every refusal writes no run, except a full queue, whose run stays as the failed attempt.
public sealed class LibrarySyncStartHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/library";
    private const string ApiKey = "0123456789abcdef0123456789abcdef";
    private const string OwnerOnlyBody = """{"error":"Only the installation owner can do this."}""";
    private const string QueueFullError = "Too many library syncs are waiting. Try again in a minute.";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private IIntegrationConnectionSeeder _integrations = null!;
    private ILibrarySyncRunSeeder _runs = null!;
    private AuthorizedUser _owner = null!;
    private AuthorizedUser _member = null!;

    public LibrarySyncStartHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _integrations = new IntegrationConnectionSeeder(_factory.Scope);
        _runs = new LibrarySyncRunSeeder(_factory.Scope);
        var users = new UserSeeder(_factory.Scope);
        _owner = await users.CreateAuthorizedOwner("owner");
        _member = await users.CreateAuthorized("member");
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Start_as_the_owner_is_202_with_the_queued_run_and_touches_nothing_else()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var jellyfin = await _integrations.Create("jellyfin", "Living room", apiKey: ApiKey);
        // A working Jellyfin: a request to it would succeed, so none recorded means none was sent.
        new FakeJellyfinServer { ApiKey = ApiKey }.Arm(_factory.JellyfinHttp);
        var now = _factory.Time.GetUtcNow().UtcDateTime;

        //Act
        var response = await _factory.PostHttpRequest(SyncsUrl(jellyfin.Id), _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var run = await response.Content.ReadFromJsonAsync<LibrarySyncRunDto>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken);
        Assert.Equal($"{BaseUrl}/syncs/{run!.Id}", response.Headers.Location!.AbsolutePath);
        Assert.Equal(
            new LibrarySyncRunDto(run.Id, jellyfin.Id, "Living room", LibrarySyncStatus.Queued, "owner", now, null, null, 0, null, null, null, null, null, null),
            run);
        // Only queued: the worker takes it; nothing reached Jellyfin and no snapshot row exists yet.
        Assert.Empty(_factory.JellyfinHttp.Requests);
        Assert.True(_factory.Resolve<LibrarySyncQueue>().Reader.TryPeek(out var queued));
        Assert.Equal(run.Id, queued);
        await _factory.WithDbAsync(async db =>
        {
            var row = await db.LibrarySyncRuns.SingleAsync(cancellationToken);
            Assert.Equal(run.Id, row.Id);
            Assert.Equal((jellyfin.Id, "Living room"), (row.IntegrationConnectionId, row.IntegrationName));
            Assert.Equal((_owner.User.Id, "owner"), (row.RequestedByUserId, row.RequestedByUserName));
            Assert.Equal((LibrarySyncStatus.Queued, now), (row.Status, row.CreatedUtc));
            Assert.Empty((await StoredLibrarySnapshot.ReadAsync(db, cancellationToken)).Rows());
            // The run row records who started it; a sync writes no audit row of its own.
            Assert.Equal(0, await db.AuditEvents.CountAsync(cancellationToken));
        });
    }

    [Fact]
    public async Task Start_as_a_signed_in_non_owner_is_403_with_the_owner_sentence_and_queues_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var jellyfin = await _integrations.Create("jellyfin", "Living room", apiKey: ApiKey);

        //Act
        var response = await _factory.PostHttpRequest(SyncsUrl(jellyfin.Id), _member.AccessToken);

        //Assert
        // A start sends the owner's stored key to Jellyfin and replaces what everyone reads.
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(OwnerOnlyBody, await response.Content.ReadAsStringAsync(cancellationToken));
        await AssertNothingQueuedAsync(cancellationToken);
    }

    [Fact]
    public async Task Start_without_a_token_is_401_and_queues_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var jellyfin = await _integrations.Create("jellyfin", "Living room", apiKey: ApiKey);

        //Act
        var response = await _factory.PostHttpRequest(SyncsUrl(jellyfin.Id));

        //Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        await AssertNothingQueuedAsync(cancellationToken);
    }

    [Fact]
    public async Task Start_of_an_unknown_integration_is_a_bare_404()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _integrations.Create("jellyfin", "Living room", apiKey: ApiKey);

        //Act
        var response = await _factory.PostHttpRequest(SyncsUrl(Guid.NewGuid()), _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsStringAsync(cancellationToken));
        await AssertNothingQueuedAsync(cancellationToken);
    }

    [Theory]
    [InlineData("qbittorrent")]
    [InlineData("torznab")]
    public async Task Start_of_an_integration_that_is_not_a_media_library_is_a_bare_404(string adapterType)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var other = await _integrations.Create(adapterType, "Not a library", apiKey: ApiKey);

        //Act
        var response = await _factory.PostHttpRequest(SyncsUrl(other.Id), _owner.AccessToken);

        //Assert
        // The URL addresses media libraries: anything else is as missing as an unknown id.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsStringAsync(cancellationToken));
        await AssertNothingQueuedAsync(cancellationToken);
    }

    [Fact]
    public async Task Start_of_a_switched_off_integration_is_409_with_its_sentence()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var jellyfin = await _integrations.Create("jellyfin", "Living room", apiKey: ApiKey, isEnabled: false);

        //Act
        var response = await _factory.PostHttpRequest(SyncsUrl(jellyfin.Id), _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(
            "“Living room” is switched off. Switch it on under Integrations to sync it.",
            await response.ReadErrorAsync(cancellationToken));
        await AssertNothingQueuedAsync(cancellationToken);
    }

    // The fake is a media library type that implements no library contract; the other is a type this
    // build doesn't register at all. Either way the registry, not a product name, says "can't read".
    [Theory]
    [InlineData(FakeIntegrationAdapter.TypeId)]
    [InlineData("retired-service")]
    public async Task Start_of_a_media_library_whose_type_cant_read_a_library_is_409_with_its_sentence(string adapterType)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var library = await _integrations.Create(adapterType, "Old library", apiKey: ApiKey, kind: IntegrationKind.MediaLibrary);

        //Act
        var response = await _factory.PostHttpRequest(SyncsUrl(library.Id), _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(
            $"This version of the app can't read a library from “{adapterType}”.",
            await response.ReadErrorAsync(cancellationToken));
        await AssertNothingQueuedAsync(cancellationToken);
        Assert.Empty(_factory.FakeAdapter.Calls);
    }

    [Theory]
    [InlineData(LibrarySyncStatus.Queued)]
    [InlineData(LibrarySyncStatus.Running)]
    public async Task Start_while_a_sync_is_under_way_is_409_and_adds_no_run(LibrarySyncStatus status)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var jellyfin = await _integrations.Create("jellyfin", "Living room", apiKey: ApiKey);
        var active = await _runs.Create(jellyfin, status, _owner.User);

        //Act
        var response = await _factory.PostHttpRequest(SyncsUrl(jellyfin.Id), _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("“Living room” is already being synced.", await response.ReadErrorAsync(cancellationToken));
        Assert.False(_factory.Resolve<LibrarySyncQueue>().Reader.TryPeek(out _));
        await _factory.WithDbAsync(async db =>
        {
            var row = await db.LibrarySyncRuns.SingleAsync(cancellationToken);
            Assert.Equal((active.Id, status), (row.Id, row.Status));
        });
    }

    [Theory]
    [InlineData(LibrarySyncStatus.Succeeded)]
    [InlineData(LibrarySyncStatus.Failed)]
    public async Task Start_after_a_finished_sync_is_202_and_adds_a_second_run(LibrarySyncStatus status)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var jellyfin = await _integrations.Create("jellyfin", "Living room", apiKey: ApiKey);
        var finished = await _runs.Create(jellyfin, status, _owner.User, _factory.Time.GetUtcNow().UtcDateTime.AddHours(-1));

        //Act
        var response = await _factory.PostHttpRequest(SyncsUrl(jellyfin.Id), _owner.AccessToken);

        //Assert
        // Only a queued or running run blocks a start; a finished one is history.
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var run = await response.Content.ReadFromJsonAsync<LibrarySyncRunDto>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken);
        await _factory.WithDbAsync(async db =>
        {
            var rows = await db.LibrarySyncRuns.ToListAsync(cancellationToken);
            Assert.Equal(2, rows.Count);
            Assert.Equal(status, rows.Single(row => row.Id == finished.Id).Status);
            Assert.Equal(LibrarySyncStatus.Queued, rows.Single(row => row.Id == run!.Id).Status);
        });
    }

    [Fact]
    public async Task Start_when_another_start_inserts_its_run_first_is_409_with_one_run()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var jellyfin = await _integrations.Create("jellyfin", "Living room", apiKey: ApiKey);
        LibrarySyncRunEntity? competing = null;
        // Deterministic race: both starts passed the "already under way" check, and the other one's
        // insert lands just before this one's — through a context of its own, like another request.
        _factory.SaveChangesHook.BeforeNextSave(
            context => context.ChangeTracker.Entries<LibrarySyncRunEntity>().Any(entry => entry.State == EntityState.Added),
            async _ =>
            {
                await using var scope = _factory.Services.CreateAsyncScope();
                competing = await new LibrarySyncRunSeeder(scope.ServiceProvider).Create(jellyfin, LibrarySyncStatus.Queued, _owner.User);
            });

        //Act
        var response = await _factory.PostHttpRequest(SyncsUrl(jellyfin.Id), _owner.AccessToken);

        //Assert
        // The partial unique index settles it: one active run, and the loser is a 409, not a 500.
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("“Living room” is already being synced.", await response.ReadErrorAsync(cancellationToken));
        Assert.NotNull(competing);
        Assert.False(_factory.Resolve<LibrarySyncQueue>().Reader.TryPeek(out _));
        await _factory.WithDbAsync(async db =>
            Assert.Equal(competing.Id, (await db.LibrarySyncRuns.SingleAsync(cancellationToken)).Id));
    }

    [Fact]
    public async Task Start_twice_at_once_queues_one_run()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var jellyfin = await _integrations.Create("jellyfin", "Living room", apiKey: ApiKey);

        //Act
        // A double click: whichever way the two interleave, one run is queued.
        var responses = await Task.WhenAll(
            _factory.PostHttpRequest(SyncsUrl(jellyfin.Id), _owner.AccessToken),
            _factory.PostHttpRequest(SyncsUrl(jellyfin.Id), _owner.AccessToken));

        //Assert
        Assert.Equal(
            [HttpStatusCode.Accepted, HttpStatusCode.Conflict],
            responses.Select(response => response.StatusCode).Order().ToArray());
        var accepted = responses.Single(response => response.StatusCode == HttpStatusCode.Accepted);
        var run = await accepted.Content.ReadFromJsonAsync<LibrarySyncRunDto>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken);
        await _factory.WithDbAsync(async db =>
            Assert.Equal(run!.Id, (await db.LibrarySyncRuns.SingleAsync(cancellationToken)).Id));
    }

    [Fact]
    public async Task Start_with_the_queue_full_is_503_and_keeps_the_run_as_failed_with_the_same_sentence()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var jellyfin = await _integrations.Create("jellyfin", "Living room", apiKey: ApiKey);
        var queue = _factory.Resolve<LibrarySyncQueue>();
        for (var index = 0; index < LibrarySyncQueue.Capacity; index++)
            queue.TryEnqueue(Guid.NewGuid());
        var now = _factory.Time.GetUtcNow().UtcDateTime;

        //Act
        var response = await _factory.PostHttpRequest(SyncsUrl(jellyfin.Id), _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(QueueFullError, await response.ReadErrorAsync(cancellationToken));
        await _factory.WithDbAsync(async db =>
        {
            // Failed, not left queued: a queued run no worker will take would block every later start.
            var row = await db.LibrarySyncRuns.SingleAsync(cancellationToken);
            Assert.Equal((LibrarySyncStatus.Failed, QueueFullError, now), (row.Status, row.Error, row.FinishedUtc));
        });
        var warning = Assert.Single(_factory.Logs.Entries, entry => entry.Category == typeof(LibrarySyncQueue).FullName);
        Assert.Equal(LogLevel.Warning, warning.Level);
    }

    private static string SyncsUrl(Guid integrationId) => $"{BaseUrl}/sources/{integrationId}/syncs";

    private async Task AssertNothingQueuedAsync(CancellationToken cancellationToken)
    {
        Assert.False(_factory.Resolve<LibrarySyncQueue>().Reader.TryPeek(out _));
        await _factory.WithDbAsync(async db =>
            Assert.Equal(0, await db.LibrarySyncRuns.CountAsync(cancellationToken)));
    }
}
