using System.Net;
using System.Text.Json.Nodes;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Tests.LibraryLinks;

// MAM-34 (0.2.1, ADR 0026) — removing links (DELETE /api/library/works/{id}/tmdb, and DELETE /api/library/tmdb/links/{id}
// for one link) forgets them in the app and nothing else: the library keeps what its Identify set (there is no undo), is
// never called, and needs no integration — a library switched off or removed no longer stands in the way. The link goes
// with its file identities and one audit row; a link still Queued simply goes and the worker passes it over; a link being
// identified (Writing) stays, 409, as deleting it under the worker would orphan its audit target. A removal queues a
// recommendation run; one that keeps the link doesn't.
public sealed class LibraryLinkRemoveHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/library";
    private const string ApiKey = "0123456789abcdef0123456789abcdef";
    private const string ShortId = JellyfinSamples.LandingStriplingId;
    private const string FilmId = JellyfinSamples.LandingStriplingTmdbId;
    private const string FilmImdbId = JellyfinSamples.LandingStriplingImdbId;
    private const string PlanetEarthId = "1044";
    private const string WrongTmdbId = "100";
    private const string UnderWayError = "A write to the library is under way for this work; try again in a moment.";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private IIntegrationConnectionSeeder _integrations = null!;
    private ILibraryItemLinkSeeder _links = null!;
    private AuthorizedUser _owner = null!;
    private FakeJellyfinServer _jellyfin = null!;
    private IntegrationConnectionEntity _jellyfinIntegration = null!;

    public LibraryLinkRemoveHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _integrations = new IntegrationConnectionSeeder(_factory.Scope);
        _links = new LibraryItemLinkSeeder(_factory.Scope);
        _owner = await new UserSeeder(_factory.Scope).CreateAuthorizedOwner("owner");
        _jellyfin = new FakeJellyfinServer { ApiKey = ApiKey };
        _jellyfin.Movies.Clear();
        _jellyfin.AddFilm(FilmId, "Landing Stripling", 1962, FilmImdbId);
        _jellyfin.Arm(_factory.JellyfinHttp);
        var tmdb = FakeTmdbServer.Arm(_factory.TmdbHttp);
        tmdb.AddWork(new FakeTmdbWork(MediaWorkKind.Movie, int.Parse(FilmId), "Посадкова смуга", ImdbId: FilmImdbId));
        tmdb.AddWork(new FakeTmdbWork(MediaWorkKind.Series, int.Parse(PlanetEarthId), "Планета Земля", ImdbId: "tt0795176"));
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Remove_forgets_the_link_with_its_audit_row_and_sends_nothing_to_jellyfin()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var workId = await SyncShortAsync(tmdbId: WrongTmdbId, imdbId: null);
        await SetAndIdentifyAsync(workId, new LibraryLinkRequest("Movie", FilmId));
        var link = await _factory.SingleLinkAsync(cancellationToken);
        var sent = _factory.JellyfinHttp.Requests.Count;

        //Act
        var response = await _factory.DeleteHttpRequest(LinkUrl(workId), _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.ReadAsync<LibraryLinkRemoveResponse>(cancellationToken);
        Assert.Null(Assert.Single(body.Dialog.Items).LinkId);
        await _factory.WithDbAsync(async db =>
        {
            Assert.False(await db.LibraryItemLinks.AnyAsync(cancellationToken));
            Assert.False(await db.LibraryItemLinkFiles.AnyAsync(cancellationToken));
        });
        // The link's ids and nothing after: no restore row, no row for a call that was never made.
        var audit = await _factory.AuditAsync(cancellationToken);
        Assert.Equal(
            [
                (AuditAction.LibraryLinkRemoved, "Before Imdb tt0056164, Before Tmdb 223021"),
            ],
            audit.Skip(2).Select(row => (row.Action, row.Identifiers)));
        Assert.All(audit.Skip(2), row => Assert.Equal(((Guid?)link.Id, "owner"), (row.TargetId, row.ActorUserName)));
        Assert.Equal(sent, _factory.JellyfinHttp.Requests.Count);
    }

    [Fact]
    public async Task Remove_of_an_identified_link_leaves_what_identify_set_in_jellyfin()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var workId = await SyncShortAsync(tmdbId: WrongTmdbId, imdbId: null);
        await SetAndIdentifyAsync(workId, new LibraryLinkRequest("Movie", FilmId));
        var identified = Movie(ShortId).DeepClone();

        //Act
        var response = await _factory.DeleteHttpRequest(LinkUrl(workId), _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // No undo: Jellyfin keeps TMDb's name, ids and images; the owner changes them in Jellyfin if he wants them back.
        Assert.True(JsonNode.DeepEquals(identified, Movie(ShortId)));
        Assert.Equal(
            new Dictionary<string, string> { ["Tmdb"] = FilmId, ["Imdb"] = FilmImdbId },
            Movie(ShortId)["ProviderIds"]!.AsObject().ToDictionary(pair => pair.Key, pair => (string)pair.Value!));
        Assert.Single(_jellyfin.Applied);
    }

    [Fact]
    public async Task Remove_while_jellyfin_is_switched_off_still_deletes_the_link()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var workId = await SyncShortAsync(tmdbId: null, imdbId: null);
        await SetAndIdentifyAsync(workId, new LibraryLinkRequest("Movie", FilmId));
        await _factory.WithDbAsync(db => db.IntegrationConnections
            .Where(integration => integration.Id == _jellyfinIntegration.Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(integration => integration.IsEnabled, false), cancellationToken));
        var sent = _factory.JellyfinHttp.Requests.Count;

        //Act
        var response = await _factory.DeleteHttpRequest(LinkUrl(workId), _owner.AccessToken);

        //Assert
        // Nothing is put back, so a library that is off no longer keeps the link (it was a 409).
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await _factory.WithDbAsync(async db => Assert.False(await db.LibraryItemLinks.AnyAsync(cancellationToken)));
        Assert.Equal(sent, _factory.JellyfinHttp.Requests.Count);
    }

    [Fact]
    public async Task Remove_of_a_queued_link_deletes_it_and_the_worker_passes_it_over()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var workId = await SyncShortAsync(tmdbId: null, imdbId: null);
        var response = await _factory.PutHttpRequest(LinkUrl(workId), _owner.AccessToken, new LibraryLinkRequest("Movie", FilmId));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(LibraryLinkWriteState.Queued, (await _factory.SingleLinkAsync(cancellationToken)).WriteState);
        var sent = _factory.JellyfinHttp.Requests.Count;

        //Act
        var removed = await _factory.DeleteHttpRequest(LinkUrl(workId), _owner.AccessToken);
        await _factory.IdentifyQueuedLinksAsync(cancellationToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, removed.StatusCode);
        await _factory.WithDbAsync(async db => Assert.False(await db.LibraryItemLinks.AnyAsync(cancellationToken)));
        // The worker found nothing to claim: nothing was asked of Jellyfin and nothing applied.
        Assert.Equal(sent, _factory.JellyfinHttp.Requests.Count);
        Assert.Empty(_jellyfin.Applied);
        Assert.Equal([AuditAction.LibraryLinkSet, AuditAction.LibraryLinkRemoved], await _factory.AuditActionsAsync(cancellationToken));
    }

    [Fact]
    public async Task Remove_of_an_episode_link_kept_in_the_app_sends_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var workId = await SyncShortAsync(tmdbId: WrongTmdbId, imdbId: null);
        await SetAsync(workId, new LibraryLinkRequest("Series", PlanetEarthId, SeasonNumber: "1", EpisodeNumber: "8"));
        var sent = _factory.JellyfinHttp.Requests.Count;

        //Act
        var response = await _factory.DeleteHttpRequest(LinkUrl(workId), _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(sent, _factory.JellyfinHttp.Requests.Count);
        await _factory.WithDbAsync(async db => Assert.False(await db.LibraryItemLinks.AnyAsync(cancellationToken)));
        Assert.Equal([AuditAction.LibraryLinkSet, AuditAction.LibraryLinkRemoved], await _factory.AuditActionsAsync(cancellationToken));
    }

    [Fact]
    public async Task Remove_queues_a_recommendation_run_for_the_removed_links()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var workId = await SyncShortAsync(tmdbId: null, imdbId: null);
        await _links.Create(_jellyfinIntegration, ShortId, workId, MediaIdentifierScheme.Tmdb, FilmId);

        //Act
        var response = await _factory.DeleteHttpRequest(LinkUrl(workId), _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal([(RecommendationRunStatus.Queued, RecommendationRunTrigger.LinkChanged)], await RecommendationRunsAsync(cancellationToken));
    }

    [Fact]
    public async Task RemoveLink_queues_a_recommendation_run_for_the_removed_link()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await SyncShortAsync(tmdbId: null, imdbId: null);
        var link = await _links.Create(_jellyfinIntegration, "f6f6f6f6f6f6f6f6f6f6f6f6f6f6f6f6", null, MediaIdentifierScheme.Tmdb, FilmId);

        //Act
        var response = await _factory.DeleteHttpRequest($"{BaseUrl}/tmdb/links/{link.Id}", _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal([(RecommendationRunStatus.Queued, RecommendationRunTrigger.LinkChanged)], await RecommendationRunsAsync(cancellationToken));
    }

    [Fact]
    public async Task Remove_that_keeps_the_link_queues_no_recommendation_run()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var workId = await SyncShortAsync(tmdbId: null, imdbId: null);
        await _links.Create(_jellyfinIntegration, ShortId, workId, MediaIdentifierScheme.Tmdb, FilmId, writeState: LibraryLinkWriteState.Writing);

        //Act
        var response = await _factory.DeleteHttpRequest(LinkUrl(workId), _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Empty(await RecommendationRunsAsync(cancellationToken));
    }

    [Fact]
    public async Task Remove_of_a_work_with_no_link_is_204()
    {
        //Arrange
        var workId = await SyncShortAsync(tmdbId: null, imdbId: null);

        //Act
        var response = await _factory.DeleteHttpRequest(LinkUrl(workId), _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Remove_of_an_unknown_work_is_a_bare_404()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var response = await _factory.DeleteHttpRequest(LinkUrl(Guid.NewGuid()), _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsStringAsync(cancellationToken));
    }

    [Fact]
    public async Task Remove_while_a_link_is_being_identified_is_409_and_keeps_the_link()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var workId = await SyncShortAsync(tmdbId: null, imdbId: null);
        await _links.Create(_jellyfinIntegration, ShortId, workId, MediaIdentifierScheme.Tmdb, FilmId, writeState: LibraryLinkWriteState.Writing);

        //Act
        var response = await _factory.DeleteHttpRequest(LinkUrl(workId), _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(UnderWayError, await response.ReadErrorAsync(cancellationToken));
        Assert.Equal(LibraryLinkWriteState.Writing, (await _factory.SingleLinkAsync(cancellationToken)).WriteState);
        Assert.Empty(await _factory.AuditAsync(cancellationToken));
    }

    [Fact]
    public async Task Remove_of_a_link_whose_worker_claim_wins_the_race_is_409_and_keeps_it()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var workId = await SyncShortAsync(tmdbId: null, imdbId: null);
        var link = await _links.Create(_jellyfinIntegration, ShortId, workId, MediaIdentifierScheme.Tmdb, FilmId, writeState: LibraryLinkWriteState.Queued);
        // The removal has read the link as Queued; the worker claims it before the delete runs.
        _factory.TransactionHook.BeforeNextTransaction(
            _ => true,
            _ => _factory.WithDbAsync(db => db.LibraryItemLinks
                .Where(row => row.Id == link.Id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.WriteState, LibraryLinkWriteState.Writing), cancellationToken)));

        //Act
        var response = await _factory.DeleteHttpRequest(LinkUrl(workId), _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(UnderWayError, await response.ReadErrorAsync(cancellationToken));
        Assert.Equal(LibraryLinkWriteState.Writing, (await _factory.SingleLinkAsync(cancellationToken)).WriteState);
        Assert.Empty(await _factory.AuditAsync(cancellationToken));
        Assert.Empty(await RecommendationRunsAsync(cancellationToken));
    }

    [Fact]
    public async Task RemoveLink_of_an_unattached_link_deletes_it_and_is_204()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await SyncShortAsync(tmdbId: null, imdbId: null);
        // Its item is gone from Jellyfin's list: there is nothing to call, whatever the link says.
        var link = await _links.Create(
            _jellyfinIntegration, "f6f6f6f6f6f6f6f6f6f6f6f6f6f6f6f6", null, MediaIdentifierScheme.Tmdb, FilmId, writeState: LibraryLinkWriteState.Written,
            configure: row => row.ItemTitle = "Calypso Cat");
        _factory.Time.Advance(TimeSpan.FromMinutes(1));
        var sent = _factory.JellyfinHttp.Requests.Count;

        //Act
        var response = await _factory.DeleteHttpRequest($"{BaseUrl}/tmdb/links/{link.Id}", _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(sent, _factory.JellyfinHttp.Requests.Count);
        await _factory.WithDbAsync(async db => Assert.False(await db.LibraryItemLinks.AnyAsync(cancellationToken)));
        var removed = (await _factory.AuditAsync(cancellationToken)).Last();
        Assert.Equal((AuditAction.LibraryLinkRemoved, "Calypso Cat", (Guid?)link.Id), (removed.Action, removed.TargetName, removed.TargetId));
    }

    [Fact]
    public async Task RemoveLink_of_an_unknown_link_is_a_bare_404()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var response = await _factory.DeleteHttpRequest($"{BaseUrl}/tmdb/links/{Guid.NewGuid()}", _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsStringAsync(cancellationToken));
    }

    [Fact]
    public async Task RemoveLink_of_a_link_being_identified_is_409_and_keeps_it()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var workId = await SyncShortAsync(tmdbId: null, imdbId: null);
        var link = await _links.Create(_jellyfinIntegration, ShortId, workId, MediaIdentifierScheme.Tmdb, FilmId, writeState: LibraryLinkWriteState.Writing);

        //Act
        var response = await _factory.DeleteHttpRequest($"{BaseUrl}/tmdb/links/{link.Id}", _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(UnderWayError, await response.ReadErrorAsync(cancellationToken));
        Assert.Equal(LibraryLinkWriteState.Writing, (await _factory.SingleLinkAsync(cancellationToken)).WriteState);
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    private static string LinkUrl(Guid workId) => $"{BaseUrl}/works/{workId}/tmdb";

    private async Task SetAsync(Guid workId, LibraryLinkRequest request)
    {
        var response = await _factory.PutHttpRequest(LinkUrl(workId), _owner.AccessToken, request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        _factory.Time.Advance(TimeSpan.FromSeconds(1));
    }

    // The owner's Save and the identification it queued, with a minute passing after them.
    private async Task SetAndIdentifyAsync(Guid workId, LibraryLinkRequest request)
    {
        var response = await _factory.PutHttpRequest(LinkUrl(workId), _owner.AccessToken, request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await _factory.IdentifyQueuedLinksAsync(TestContext.Current.CancellationToken);
        _factory.Time.Advance(TimeSpan.FromMinutes(1));
    }

    private async Task<Guid> SyncShortAsync(string? tmdbId, string? imdbId)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        _jellyfinIntegration = await _integrations.Create("jellyfin", "Living room", apiKey: ApiKey);
        _jellyfin.Movies.Add(JellyfinSamples.MovieWith(
            ShortId, "Landing Stripling", null, 1962, $"{JellyfinSamples.CartoonsRoot}/Tom & Jerry/120.mkv", imdbId, tmdbId));
        await _factory.SyncLibraryAsync(_jellyfinIntegration.Id, _owner.AccessToken, cancellationToken);
        await _integrations.Create("tmdb", "TMDb", FakeTmdbServer.BaseUrl, apiKey: FakeTmdbServer.Token);
        return await _factory.WorkOfItemAsync(ShortId, cancellationToken);
    }

    private async Task<List<(RecommendationRunStatus Status, RecommendationRunTrigger Trigger)>> RecommendationRunsAsync(CancellationToken cancellationToken)
    {
        var runs = new List<(RecommendationRunStatus, RecommendationRunTrigger)>();
        await _factory.WithDbAsync(async db => runs.AddRange(
            (await db.RecommendationRuns.AsNoTracking().ToListAsync(cancellationToken)).Select(run => (run.Status, run.Trigger))));
        return runs;
    }

    private JsonObject Movie(string itemId) => _jellyfin.Movies.Single(movie => (string?)movie["Id"] == itemId);
}
