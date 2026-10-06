using System.Net;
using System.Net.Http.Json;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Api.Services.Audit;
using MediaAcquisitionManager.Api.Services.LibraryLinks;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Tests.LibraryLinks;

// MAM-34, R11, decision 13 — removing links (DELETE /api/library/works/{id}/tmdb, and DELETE /api/library/tmdb/links/{id}
// for one link): the library item put back first, the link deleted after with its audit rows. Jellyfin's own ids come back
// and the IMDb id the app added goes, but only where Jellyfin still holds what the app wrote — an id someone changed since
// stays —, a series' show id as a film's; an episode's link takes back only the episode's IMDb id it wrote
// (LibraryLinkEpisodeHttpTests); a link never sent, kept in the app (an episode's without its numbers, a series' set
// before series were written), or whose item is gone sends nothing; a restore
// Jellyfin refuses keeps the link (502) — as does one whose item the administrator Jellyfin reads it as can no longer see
// — and one switched off can't be changed back (409). R10 — a write a restart cut is put back from the row the reconciler recorded for it, whatever a
// sync or a Retry made of the link since. A removal queues a recommendation run; one that keeps the link doesn't.
public sealed class LibraryLinkRemoveHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/library";
    private const string ApiKey = "0123456789abcdef0123456789abcdef";
    private const string ShortId = JellyfinSamples.LandingStriplingId;
    private const string FilmId = JellyfinSamples.LandingStriplingTmdbId;
    private const string FilmImdbId = JellyfinSamples.LandingStriplingImdbId;
    private const string PlanetEarthId = "1044";
    private const string WrongTmdbId = "100";
    private const string RestartedError = "The app restarted while writing the ids into the library. Retry.";
    private const string PenguinsId = "d4d4d4d4d4d4d4d4d4d4d4d4d4d4d4d4";

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
        _jellyfin.Arm(_factory.JellyfinHttp);
        var tmdb = FakeTmdbServer.Arm(_factory.TmdbHttp);
        tmdb.AddWork(new FakeTmdbWork(MediaWorkKind.Movie, int.Parse(FilmId), "Посадкова смуга", ImdbId: FilmImdbId));
        tmdb.AddWork(new FakeTmdbWork(MediaWorkKind.Series, int.Parse(PlanetEarthId), "Планета Земля", ImdbId: "tt0795176"));
        tmdb.AddWork(new FakeTmdbWork(MediaWorkKind.Series, int.Parse(JellyfinSamples.PenguinsTmdbTvId), "Пінгвіни з Мадагаскару", ImdbId: "tt0892700"));
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Remove_puts_back_the_ids_jellyfin_held_then_deletes_the_link_with_its_audit_rows()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var workId = await SyncShortAsync(tmdbId: WrongTmdbId, imdbId: null);
        await SetAsync(workId, new LibraryLinkRequest("Movie", FilmId));
        var link = await SingleLinkAsync(cancellationToken);
        var written = ProviderIdsOf(ShortId);
        _factory.Time.Advance(TimeSpan.FromMinutes(1));

        //Act
        var response = await _factory.DeleteHttpRequest(LinkUrl(workId), _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<LibraryLinkRemoveResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken);
        Assert.Null(Assert.Single(body!.Dialog.Items).LinkId);
        Assert.Equal(new Dictionary<string, string> { ["Imdb"] = FilmImdbId, ["Tmdb"] = FilmId }, written);
        // Jellyfin's own (wrong) film id is back, and the IMDb id the app added is gone.
        Assert.Equal(new Dictionary<string, string> { ["Tmdb"] = WrongTmdbId }, ProviderIdsOf(ShortId));
        await _factory.WithDbAsync(async db => Assert.False(await db.LibraryItemLinks.AnyAsync(cancellationToken)));
        var audit = await AuditAsync(cancellationToken);
        Assert.Equal(
            [
                (AuditAction.LibraryLinkRemoved, "Before Imdb tt0056164, Before Tmdb 223021"),
                (AuditAction.LibraryIdsRestored, "Before Imdb tt0056164, Before Tmdb 223021, After Tmdb 100"),
            ],
            audit.Skip(3).Select(row => (row.Action, row.Identifiers)));
        Assert.All(audit.Skip(3), row => Assert.Equal(((Guid?)link.Id, "owner"), (row.TargetId, row.ActorUserName)));
        // A removal asks Jellyfin for no lookup: the one the write asked for is all.
        Assert.Equal([ShortId], _jellyfin.Lookups);
    }

    [Fact]
    public async Task Remove_leaves_an_id_someone_changed_in_jellyfin_since_the_app_wrote_it()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var workId = await SyncShortAsync(tmdbId: null, imdbId: "tt9999999");
        await SetAsync(workId, new LibraryLinkRequest("Movie", FilmId));
        // Someone set another film id in Jellyfin since; the IMDb id was Jellyfin's own all along.
        Movie(ShortId)["ProviderIds"]!["Tmdb"] = "555";
        var sent = _factory.JellyfinHttp.Requests.Count;

        //Act
        var response = await _factory.DeleteHttpRequest(LinkUrl(workId), _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new Dictionary<string, string> { ["Imdb"] = "tt9999999", ["Tmdb"] = "555" }, ProviderIdsOf(ShortId));
        // Read, found not the app's to change, and nothing sent.
        Assert.DoesNotContain(_factory.JellyfinHttp.Requests.Skip(sent), request => request.Method == HttpMethod.Post);
        await _factory.WithDbAsync(async db => Assert.False(await db.LibraryItemLinks.AnyAsync(cancellationToken)));
        Assert.Equal(AuditAction.LibraryLinkRemoved, (await AuditAsync(cancellationToken)).Last().Action);
    }

    [Fact]
    public async Task Remove_that_jellyfin_refuses_keeps_the_link_with_the_sentence_and_is_502()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var workId = await SyncShortAsync(tmdbId: null, imdbId: null);
        await SetAsync(workId, new LibraryLinkRequest("Movie", FilmId));
        var recorded = (await AuditAsync(cancellationToken)).Count;
        _jellyfin.WriteRefusal = HttpStatusCode.Forbidden;

        //Act
        var response = await _factory.DeleteHttpRequest(LinkUrl(workId), _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        const string refused = "Jellyfin refused to change the item's ids. Check that the API key belongs to an administrator.";
        Assert.Equal($"“Living room” couldn't be changed back, so the link stays: {refused}", await response.ReadErrorAsync(cancellationToken));
        var link = await SingleLinkAsync(cancellationToken);
        Assert.Equal((LibraryLinkWriteState.Written, refused), (link.WriteState, link.WriteError));
        Assert.Equal(recorded, (await AuditAsync(cancellationToken)).Count);
    }

    [Fact]
    public async Task Remove_while_jellyfin_is_switched_off_is_409_and_keeps_the_link()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var workId = await SyncShortAsync(tmdbId: null, imdbId: null);
        await SetAsync(workId, new LibraryLinkRequest("Movie", FilmId));
        await _factory.WithDbAsync(db => db.IntegrationConnections
            .Where(integration => integration.Id == _jellyfinIntegration.Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(integration => integration.IsEnabled, false), cancellationToken));
        var sent = _factory.JellyfinHttp.Requests.Count;

        //Act
        var response = await _factory.DeleteHttpRequest(LinkUrl(workId), _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("“Living room” is switched off. Switch it on under Integrations to remove the link.", await response.ReadErrorAsync(cancellationToken));
        Assert.Equal(LibraryLinkWriteState.Written, (await SingleLinkAsync(cancellationToken)).WriteState);
        Assert.Equal(sent, _factory.JellyfinHttp.Requests.Count);
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
        Assert.Equal([AuditAction.LibraryLinkSet, AuditAction.LibraryLinkRemoved], (await AuditAsync(cancellationToken)).Select(row => row.Action));
    }

    [Fact]
    public async Task Remove_of_a_written_series_link_puts_back_the_show_id_jellyfin_held_and_takes_back_the_imdb_id_the_app_added()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var workId = await SyncPenguinsAsync(cancellationToken);
        // Written like a film's (Vasyl's final decision of 2026-10-06): the show's id over Jellyfin's wrong one, and its
        // IMDb id where the series had none.
        await SetAsync(workId, new LibraryLinkRequest("Series", JellyfinSamples.PenguinsTmdbTvId));
        var link = await SingleLinkAsync(cancellationToken);
        var written = _jellyfin.Series.Single()["ProviderIds"]!.ToJsonString();
        _factory.Time.Advance(TimeSpan.FromMinutes(1));

        //Act
        var response = await _factory.DeleteHttpRequest(LinkUrl(workId), _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("""{"Tmdb":"7869","Imdb":"tt0892700"}""", written);
        Assert.Equal("""{"Tmdb":"100"}""", _jellyfin.Series.Single()["ProviderIds"]!.ToJsonString());
        await _factory.WithDbAsync(async db => Assert.False(await db.LibraryItemLinks.AnyAsync(cancellationToken)));
        var audit = await AuditAsync(cancellationToken);
        Assert.Equal(
            [
                (AuditAction.LibraryLinkRemoved, "Before Imdb tt0892700, Before TmdbTv 7869"),
                (AuditAction.LibraryIdsRestored, "Before Imdb tt0892700, Before TmdbTv 7869, After TmdbTv 100"),
            ],
            audit.Skip(3).Select(row => (row.Action, row.Identifiers)));
        Assert.All(audit.Skip(3), row => Assert.Equal((Guid?)link.Id, row.TargetId));
    }

    [Fact]
    public async Task Remove_of_a_series_link_set_before_series_were_written_sends_nothing_even_while_jellyfin_is_switched_off()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var workId = await SyncPenguinsAsync(cancellationToken);
        // Kept in the app by the first rules, so nothing of it was ever sent.
        await _links.Create(_jellyfinIntegration, PenguinsId, workId, MediaIdentifierScheme.TmdbTv, JellyfinSamples.PenguinsTmdbTvId);
        await _factory.WithDbAsync(db => db.IntegrationConnections
            .Where(integration => integration.Id == _jellyfinIntegration.Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(integration => integration.IsEnabled, false), cancellationToken));
        var sent = _factory.JellyfinHttp.Requests.Count;

        //Act
        var response = await _factory.DeleteHttpRequest(LinkUrl(workId), _owner.AccessToken);

        //Assert
        // Nothing to put back, so a library switched off doesn't keep the link (as it would a written one's, R11).
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(sent, _factory.JellyfinHttp.Requests.Count);
        Assert.Equal(WrongTmdbId, (string?)_jellyfin.Series.Single()["ProviderIds"]!["Tmdb"]);
        await _factory.WithDbAsync(async db => Assert.False(await db.LibraryItemLinks.AnyAsync(cancellationToken)));
        Assert.Equal([AuditAction.LibraryLinkRemoved], (await AuditAsync(cancellationToken)).Select(row => row.Action));
    }

    [Fact]
    public async Task Remove_of_a_link_whose_write_a_restart_cut_puts_back_what_the_reconciler_recorded_it_sent()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var workId = await SyncShortAsync(tmdbId: WrongTmdbId, imdbId: null);
        var link = await CutWriteAsync(workId);

        //Act
        await _factory.Resolve<LibraryLinkWriteReconciler>().ReconcileAsync(cancellationToken);
        var reconciled = await SingleLinkAsync(cancellationToken);
        _factory.Time.Advance(TimeSpan.FromMinutes(1));
        var response = await _factory.DeleteHttpRequest(LinkUrl(workId), _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal((LibraryLinkWriteState.NotWritten, RestartedError), (reconciled.WriteState, reconciled.WriteError));
        Assert.Equal(new Dictionary<string, string> { ["Tmdb"] = WrongTmdbId }, ProviderIdsOf(ShortId));
        // R10 — the cut write recorded as what it meant to leave, in the name of whoever set the link.
        var audit = await AuditAsync(cancellationToken);
        Assert.Equal(
            [
                (AuditAction.LibraryLinkSet, "Before Tmdb 100, After Imdb tt0056164, After Tmdb 223021"),
                (AuditAction.LibraryIdsWritten, "Before Tmdb 100, After Imdb tt0056164, After Tmdb 223021"),
                (AuditAction.LibraryLinkRemoved, "Before Imdb tt0056164, Before Tmdb 223021"),
                (AuditAction.LibraryIdsRestored, "Before Imdb tt0056164, Before Tmdb 223021, After Tmdb 100"),
            ],
            audit.Select(row => (row.Action, row.Identifiers)));
        Assert.Equal(((Guid?)link.Id, "owner"), (audit[1].TargetId, audit[1].ActorUserName));
    }

    [Fact]
    public async Task Remove_of_a_link_whose_cut_write_a_sync_then_found_in_jellyfin_still_puts_back_jellyfins_own_ids()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var workId = await SyncShortAsync(tmdbId: WrongTmdbId, imdbId: null);
        await CutWriteAsync(workId);
        await _factory.Resolve<LibraryLinkWriteReconciler>().ReconcileAsync(cancellationToken);
        // The routine sync after the restart reads the landed id: the restart's sentence is gone from the link.
        _factory.Time.Advance(TimeSpan.FromMinutes(1));
        await _factory.SyncLibraryAsync(_jellyfinIntegration.Id, _owner.AccessToken, cancellationToken);
        var synced = await SingleLinkAsync(cancellationToken);

        //Act
        var response = await _factory.DeleteHttpRequest(LinkUrl(synced.MediaWorkId!.Value), _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal((LibraryLinkWriteState.InLibrary, (string?)null), (synced.WriteState, synced.WriteError));
        Assert.Equal(new Dictionary<string, string> { ["Tmdb"] = WrongTmdbId }, ProviderIdsOf(ShortId));
        await _factory.WithDbAsync(async db => Assert.False(await db.LibraryItemLinks.AnyAsync(cancellationToken)));
    }

    [Fact]
    public async Task Remove_of_a_link_whose_cut_write_a_retry_found_landed_still_puts_back_jellyfins_own_ids()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var workId = await SyncShortAsync(tmdbId: WrongTmdbId, imdbId: null);
        await CutWriteAsync(workId);
        await _factory.Resolve<LibraryLinkWriteReconciler>().ReconcileAsync(cancellationToken);
        // The owner's Retry finds Jellyfin holding the ids already: nothing is sent, and the link settles written.
        var retried = await _factory.PostHttpRequest($"{LinkUrl(workId)}/write", _owner.AccessToken);
        var settled = await SingleLinkAsync(cancellationToken);

        //Act
        var response = await _factory.DeleteHttpRequest(LinkUrl(workId), _owner.AccessToken);

        //Assert
        Assert.Equal((HttpStatusCode.OK, HttpStatusCode.OK), (retried.StatusCode, response.StatusCode));
        Assert.Equal((LibraryLinkWriteState.Written, (string?)null), (settled.WriteState, settled.WriteError));
        Assert.Equal(new Dictionary<string, string> { ["Tmdb"] = WrongTmdbId }, ProviderIdsOf(ShortId));
    }

    [Fact]
    public async Task Remove_of_a_link_whose_item_the_administrator_can_no_longer_see_keeps_the_link_and_is_502()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var workId = await SyncShortAsync(tmdbId: WrongTmdbId, imdbId: null);
        await SetAsync(workId, new LibraryLinkRequest("Movie", FilmId));
        var recorded = (await AuditAsync(cancellationToken)).Count;
        // A parental limit now hides the item from the administrator Jellyfin reads it as; the sync's list still has it.
        _jellyfin.HiddenFromAdministrator.Add(ShortId);

        //Act
        var response = await _factory.DeleteHttpRequest(LinkUrl(workId), _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        const string hidden =
            "Jellyfin's administrator account can't see this item (a parental limit, a blocked tag or no access to its library), so the app can't read it as that account.";
        Assert.Equal($"“Living room” couldn't be changed back, so the link stays: {hidden}", await response.ReadErrorAsync(cancellationToken));
        var link = await SingleLinkAsync(cancellationToken);
        Assert.Equal((LibraryLinkWriteState.Written, hidden), (link.WriteState, link.WriteError));
        // The app's ids stay in Jellyfin until the owner can remove the link, and nothing was recorded.
        Assert.Equal(new Dictionary<string, string> { ["Imdb"] = FilmImdbId, ["Tmdb"] = FilmId }, ProviderIdsOf(ShortId));
        Assert.Equal(recorded, (await AuditAsync(cancellationToken)).Count);
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

    [Theory]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.Conflict)]
    public async Task Remove_that_keeps_the_link_queues_no_recommendation_run(HttpStatusCode expected)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var workId = await SyncShortAsync(tmdbId: null, imdbId: null);
        await SetAsync(workId, new LibraryLinkRequest("Movie", FilmId));
        // The set's own run is done with: only what the removal queues is left to see.
        await _factory.WithDbAsync(db => db.RecommendationRuns.ExecuteDeleteAsync(cancellationToken));
        if (expected == HttpStatusCode.BadGateway)
            _jellyfin.WriteRefusal = HttpStatusCode.Forbidden;
        else
            await _factory.WithDbAsync(db => db.LibraryItemLinks.ExecuteUpdateAsync(setters => setters.SetProperty(row => row.WriteState, LibraryLinkWriteState.Writing), cancellationToken));

        //Act
        var response = await _factory.DeleteHttpRequest(LinkUrl(workId), _owner.AccessToken);

        //Assert
        Assert.Equal(expected, response.StatusCode);
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
    public async Task Remove_while_a_write_to_the_work_is_under_way_is_409_and_keeps_the_link()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var workId = await SyncShortAsync(tmdbId: null, imdbId: null);
        await _links.Create(_jellyfinIntegration, ShortId, workId, MediaIdentifierScheme.Tmdb, FilmId, writeState: LibraryLinkWriteState.Writing);

        //Act
        var response = await _factory.DeleteHttpRequest(LinkUrl(workId), _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("A write to the library is under way for this work; try again in a moment.", await response.ReadErrorAsync(cancellationToken));
        Assert.Equal(LibraryLinkWriteState.Writing, (await SingleLinkAsync(cancellationToken)).WriteState);
    }

    [Fact]
    public async Task RemoveLink_of_an_unattached_link_deletes_it_without_calling_jellyfin_and_is_204()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await SyncShortAsync(tmdbId: null, imdbId: null);
        // Its item is gone from Jellyfin's list, so there is nothing to put back (R11), whatever was written.
        var link = await _links.Create(
            _jellyfinIntegration, "f6f6f6f6f6f6f6f6f6f6f6f6f6f6f6f6", null, MediaIdentifierScheme.Tmdb, FilmId, writeState: LibraryLinkWriteState.Written,
            configure: row => row.ItemTitle = "Calypso Cat");
        await new AuditEventSeeder(_factory.Scope).CreateForLink(
            AuditAction.LibraryIdsWritten, link, "Calypso Cat", [new AuditIdentifier(AuditIdentifierRole.After, MediaIdentifierScheme.Tmdb, FilmId)], _owner.User);
        _factory.Time.Advance(TimeSpan.FromMinutes(1));
        var sent = _factory.JellyfinHttp.Requests.Count;

        //Act
        var response = await _factory.DeleteHttpRequest($"{BaseUrl}/tmdb/links/{link.Id}", _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(sent, _factory.JellyfinHttp.Requests.Count);
        await _factory.WithDbAsync(async db => Assert.False(await db.LibraryItemLinks.AnyAsync(cancellationToken)));
        var removed = (await AuditAsync(cancellationToken)).Last();
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

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    private static string LinkUrl(Guid workId) => $"{BaseUrl}/works/{workId}/tmdb";

    private async Task SetAsync(Guid workId, LibraryLinkRequest request)
    {
        var response = await _factory.PutHttpRequest(LinkUrl(workId), _owner.AccessToken, request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        _factory.Time.Advance(TimeSpan.FromSeconds(1));
    }

    private async Task<Guid> SyncShortAsync(string? tmdbId, string? imdbId)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        _jellyfinIntegration = await _integrations.Create("jellyfin", "Living room", apiKey: ApiKey);
        _jellyfin.Movies.Add(JellyfinSamples.MovieWith(ShortId, "Landing Stripling", null, 1962, "/media/cartoons/Tom & Jerry/120.mkv", imdbId, tmdbId));
        await _factory.SyncLibraryAsync(_jellyfinIntegration.Id, _owner.AccessToken, cancellationToken);
        await _integrations.Create("tmdb", "TMDb", FakeTmdbServer.BaseUrl, apiKey: FakeTmdbServer.Token);
        var workId = Guid.Empty;
        await _factory.WithDbAsync(async db => workId = await db.MediaWorkTitles
            .Where(title => title.SourceItemId == ShortId)
            .Select(title => title.MediaWorkId)
            .FirstAsync(cancellationToken));
        return workId;
    }

    // The Penguins series with one episode, given a wrong show id by Jellyfin, synced, with TMDb connected.
    private async Task<Guid> SyncPenguinsAsync(CancellationToken cancellationToken)
    {
        _jellyfinIntegration = await _integrations.Create("jellyfin", "Living room", apiKey: ApiKey);
        _jellyfin.Series.Add(JellyfinSamples.SeriesWith(PenguinsId, "The Penguins Of Madagascar", 2008, "/media/tv/Penguins", tmdbId: WrongTmdbId));
        _jellyfin.Episodes.Add(JellyfinSamples.EpisodeWith("e5e5e5e5e5e5e5e5e5e5e5e5e5e5e5e5", "Gone in a Flash", PenguinsId, 1, 1, "/media/tv/Penguins/S01E01.mkv"));
        await _factory.SyncLibraryAsync(_jellyfinIntegration.Id, _owner.AccessToken, cancellationToken);
        await _integrations.Create("tmdb", "TMDb", FakeTmdbServer.BaseUrl, apiKey: FakeTmdbServer.Token);
        var workId = Guid.Empty;
        await _factory.WithDbAsync(async db => workId = await db.MediaWorkTitles
            .Where(title => title.SourceItemId == PenguinsId)
            .Select(title => title.MediaWorkId)
            .FirstAsync(cancellationToken));
        return workId;
    }

    // A film link whose write landed in Jellyfin — its TMDb id and the IMDb id it added — before a restart cut it: only its
    // set row was recorded, and the link is still Writing for the reconciler.
    private async Task<LibraryItemLinkEntity> CutWriteAsync(Guid workId)
    {
        Movie(ShortId)["ProviderIds"] = new System.Text.Json.Nodes.JsonObject { ["Tmdb"] = FilmId, ["Imdb"] = FilmImdbId };
        var link = await _links.Create(
            _jellyfinIntegration, ShortId, workId, MediaIdentifierScheme.Tmdb, FilmId, writeState: LibraryLinkWriteState.Writing, setBy: _owner.User,
            configure: row => row.ImdbId = FilmImdbId);
        await new AuditEventSeeder(_factory.Scope).CreateForLink(
            AuditAction.LibraryLinkSet, link, "Landing Stripling",
            [
                new AuditIdentifier(AuditIdentifierRole.Before, MediaIdentifierScheme.Tmdb, WrongTmdbId),
                new AuditIdentifier(AuditIdentifierRole.After, MediaIdentifierScheme.Tmdb, FilmId),
                new AuditIdentifier(AuditIdentifierRole.After, MediaIdentifierScheme.Imdb, FilmImdbId),
            ],
            _owner.User);
        _factory.Time.Advance(TimeSpan.FromSeconds(1));
        return link;
    }

    private async Task<List<(RecommendationRunStatus Status, RecommendationRunTrigger Trigger)>> RecommendationRunsAsync(CancellationToken cancellationToken)
    {
        var runs = new List<(RecommendationRunStatus, RecommendationRunTrigger)>();
        await _factory.WithDbAsync(async db => runs.AddRange(
            (await db.RecommendationRuns.AsNoTracking().ToListAsync(cancellationToken)).Select(run => (run.Status, run.Trigger))));
        return runs;
    }

    private System.Text.Json.Nodes.JsonObject Movie(string itemId) => _jellyfin.Movies.Single(movie => (string?)movie["Id"] == itemId);

    private Dictionary<string, string> ProviderIdsOf(string itemId) =>
        Movie(itemId)["ProviderIds"]!.AsObject().ToDictionary(pair => pair.Key, pair => (string)pair.Value!);

    private async Task<LibraryItemLinkEntity> SingleLinkAsync(CancellationToken cancellationToken)
    {
        LibraryItemLinkEntity? link = null;
        await _factory.WithDbAsync(async db => link = await db.LibraryItemLinks.AsNoTracking().SingleAsync(cancellationToken));
        return link!;
    }

    // The audit rows in the order they were recorded, each with its ids as "Role Scheme Value".
    private async Task<List<(AuditAction Action, string? ActorUserName, Guid? TargetId, string? TargetName, string Identifiers)>> AuditAsync(CancellationToken cancellationToken)
    {
        var rows = new List<(AuditAction, string?, Guid?, string?, string)>();
        await _factory.WithDbAsync(async db =>
        {
            var identifiers = (await db.AuditEventIdentifiers.AsNoTracking().ToListAsync(cancellationToken)).ToLookup(identifier => identifier.AuditEventId);
            rows.AddRange((await db.AuditEvents.AsNoTracking().ToListAsync(cancellationToken))
                .OrderBy(auditEvent => auditEvent.CreatedUtc)
                .ThenBy(auditEvent => auditEvent.Action)
                .Select(auditEvent => (
                    auditEvent.Action,
                    auditEvent.ActorUserName,
                    auditEvent.TargetId,
                    auditEvent.TargetName,
                    string.Join(", ", identifiers[auditEvent.Id]
                        .OrderBy(identifier => identifier.Role)
                        .ThenBy(identifier => identifier.Scheme)
                        .Select(identifier => $"{identifier.Role} {identifier.Scheme} {identifier.Value}")))));
        });
        return rows;
    }
}
