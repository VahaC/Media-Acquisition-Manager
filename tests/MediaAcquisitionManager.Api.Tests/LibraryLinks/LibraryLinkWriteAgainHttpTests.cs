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

namespace MediaAcquisitionManager.Api.Tests.LibraryLinks;

// MAM-34 — Retry and Write again (POST /api/library/works/{id}/tmdb/write for a work, POST
// /api/library/tmdb/links/{id}/write for one link): only the owner's click sends a link into Jellyfin again — after a
// refused write, an unconfirmed one, or Jellyfin losing it — read first, sent once, recorded, settled, and Jellyfin asked
// to look the item up again (LibraryLinkLookupHttpTests); a series' link the first rules kept in the app is written like
// a film's (Vasyl's final decision of 2026-10-06). Nothing to write (an episode's link without its numbers, an episode
// item's, a library type that can't set ids, an item gone), a library switched off (R12) or a write under way (R9) is a
// 409 that sends nothing.
public sealed class LibraryLinkWriteAgainHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/library";
    private const string ApiKey = "0123456789abcdef0123456789abcdef";
    private const string ShortId = JellyfinSamples.LandingStriplingId;
    private const string FilmId = JellyfinSamples.LandingStriplingTmdbId;
    private const string FilmImdbId = JellyfinSamples.LandingStriplingImdbId;

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private IIntegrationConnectionSeeder _integrations = null!;
    private ILibraryItemLinkSeeder _links = null!;
    private AuthorizedUser _owner = null!;
    private FakeJellyfinServer _jellyfin = null!;
    private IntegrationConnectionEntity _jellyfinIntegration = null!;

    public LibraryLinkWriteAgainHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _integrations = new IntegrationConnectionSeeder(_factory.Scope);
        _links = new LibraryItemLinkSeeder(_factory.Scope);
        _owner = await new UserSeeder(_factory.Scope).CreateAuthorizedOwner("owner");
        _jellyfin = new FakeJellyfinServer { ApiKey = ApiKey };
        _jellyfin.Movies.Clear();
        _jellyfin.Arm(_factory.JellyfinHttp);
        FakeTmdbServer.Arm(_factory.TmdbHttp).AddWork(new FakeTmdbWork(MediaWorkKind.Movie, int.Parse(FilmId), "Посадкова смуга", ImdbId: FilmImdbId));
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Write_after_a_refused_write_sends_the_link_again_and_settles_it_written()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var workId = await SyncShortAsync();
        _jellyfin.WriteRefusal = HttpStatusCode.Forbidden;
        await _factory.PutHttpRequest(WorkUrl(workId), _owner.AccessToken, new LibraryLinkRequest("Movie", FilmId));
        var refused = await SingleLinkAsync(cancellationToken);
        _jellyfin.WriteRefusal = null;
        _factory.Time.Advance(TimeSpan.FromMinutes(1));

        //Act
        var response = await _factory.PostHttpRequest($"{WorkUrl(workId)}/write", _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dialog = await response.Content.ReadFromJsonAsync<LibraryLinkDialogResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken);
        Assert.Equal(LibraryLinkWriteState.Written, Assert.Single(dialog!.Items).WriteState);
        Assert.Equal(LibraryLinkWriteState.NotWritten, refused.WriteState);
        var link = await SingleLinkAsync(cancellationToken);
        Assert.Equal((LibraryLinkWriteState.Written, (string?)null, _factory.Time.GetUtcNow().UtcDateTime), (link.WriteState, link.WriteError, link.WriteStateUtc!.Value));
        Assert.Equal(FilmId, (string?)Movie(ShortId)["ProviderIds"]!["Tmdb"]);
        // The refused write sent nothing and asked for no lookup; the one that landed did both.
        Assert.Equal(
            [AuditAction.LibraryLinkSet, AuditAction.LibraryIdsWritten, AuditAction.LibraryMetadataLookupRequested],
            await AuditActionsAsync(cancellationToken));
        Assert.Equal([ShortId], _jellyfin.Lookups);
    }

    [Fact]
    public async Task WriteLink_of_a_link_jellyfin_lost_writes_it_again_and_answers_its_works_dialog()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var workId = await SyncShortAsync();
        var link = await _links.Create(
            _jellyfinIntegration, ShortId, workId, MediaIdentifierScheme.Tmdb, FilmId, writeState: LibraryLinkWriteState.Lost,
            configure: row => row.ImdbId = FilmImdbId);

        //Act
        var response = await _factory.PostHttpRequest($"{BaseUrl}/tmdb/links/{link.Id}/write", _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dialog = await response.Content.ReadFromJsonAsync<LibraryLinkDialogResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken);
        Assert.Equal((workId, (Guid?)link.Id), (dialog!.Work.Id, Assert.Single(dialog.Items).LinkId));
        Assert.Equal(LibraryLinkWriteState.Written, (await SingleLinkAsync(cancellationToken)).WriteState);
        Assert.Equal((FilmId, FilmImdbId), ((string?)Movie(ShortId)["ProviderIds"]!["Tmdb"], (string?)Movie(ShortId)["ProviderIds"]!["Imdb"]));
    }

    [Fact]
    public async Task Write_while_jellyfin_is_switched_off_is_409_with_the_sentence_and_sends_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var workId = await SyncShortAsync();
        await _links.Create(_jellyfinIntegration, ShortId, workId, MediaIdentifierScheme.Tmdb, FilmId, writeState: LibraryLinkWriteState.NotWritten);
        await _factory.WithDbAsync(db => db.IntegrationConnections
            .Where(integration => integration.Id == _jellyfinIntegration.Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(integration => integration.IsEnabled, false), cancellationToken));
        var sent = _factory.JellyfinHttp.Requests.Count;

        //Act
        var response = await _factory.PostHttpRequest($"{WorkUrl(workId)}/write", _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("“Living room” is switched off. Switch it on under Integrations, then retry.", await response.ReadErrorAsync(cancellationToken));
        Assert.Equal(sent, _factory.JellyfinHttp.Requests.Count);
        Assert.Equal(LibraryLinkWriteState.NotWritten, (await SingleLinkAsync(cancellationToken)).WriteState);
    }

    [Fact]
    public async Task Write_of_an_episode_link_is_409_as_there_is_nothing_to_write()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var workId = await SyncShortAsync();
        await _links.Create(_jellyfinIntegration, ShortId, workId, MediaIdentifierScheme.TmdbTv, "1044", target: LibraryLinkTarget.ShowEpisode);

        //Act
        var response = await _factory.PostHttpRequest($"{WorkUrl(workId)}/write", _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(
            "There is nothing to write: Without a season and an episode the app has no id of this episode to write.",
            await response.ReadErrorAsync(cancellationToken));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Write_of_a_series_link_the_first_rules_kept_in_the_app_writes_it_into_jellyfin_and_asks_it_to_look_the_series_up(bool oneLink)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        const string PenguinsId = "d4d4d4d4d4d4d4d4d4d4d4d4d4d4d4d4";
        _jellyfinIntegration = await _integrations.Create("jellyfin", "Living room", apiKey: ApiKey);
        _jellyfin.Series.Add(JellyfinSamples.SeriesWith(PenguinsId, "The Penguins Of Madagascar", 2008, "/media/tv/Penguins"));
        _jellyfin.Episodes.Add(JellyfinSamples.EpisodeWith("e5e5e5e5e5e5e5e5e5e5e5e5e5e5e5e5", "Gone in a Flash", PenguinsId, 1, 1, "/media/tv/Penguins/S01E01.mkv"));
        await _factory.SyncLibraryAsync(_jellyfinIntegration.Id, _owner.AccessToken, cancellationToken);
        var workId = Guid.Empty;
        await _factory.WithDbAsync(async db => workId = await db.MediaWorkTitles
            .Where(title => title.SourceItemId == PenguinsId)
            .Select(title => title.MediaWorkId)
            .FirstAsync(cancellationToken));
        // Kept in the app when series weren't written; written like a film's now (Vasyl's final decision of 2026-10-06).
        var link = await _links.Create(
            _jellyfinIntegration, PenguinsId, workId, MediaIdentifierScheme.TmdbTv, JellyfinSamples.PenguinsTmdbTvId, configure: row => row.ImdbId = "tt0892700");
        _factory.Time.Advance(TimeSpan.FromMinutes(1));

        //Act
        var response = await _factory.PostHttpRequest(oneLink ? $"{BaseUrl}/tmdb/links/{link.Id}/write" : $"{WorkUrl(workId)}/write", _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dialog = await response.Content.ReadFromJsonAsync<LibraryLinkDialogResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken);
        var item = Assert.Single(dialog!.Items);
        Assert.Equal(((LibraryLinkWriteState?)LibraryLinkWriteState.Written, true, (string?)null), (item.WriteState, item.CanWrite, item.CannotWriteReason));
        var stored = await SingleLinkAsync(cancellationToken);
        var now = _factory.Time.GetUtcNow().UtcDateTime;
        Assert.Equal(
            (LibraryLinkWriteState.Written, (string?)null, (DateTime?)now, (DateTime?)now),
            (stored.WriteState, stored.WriteError, stored.WriteStateUtc, stored.LookupRequestedUtc));
        Assert.Equal("""{"Tmdb":"7869","Imdb":"tt0892700"}""", _jellyfin.Series.Single()["ProviderIds"]!.ToJsonString());
        Assert.Equal([PenguinsId], _jellyfin.Lookups);
        Assert.Equal([AuditAction.LibraryIdsWritten, AuditAction.LibraryMetadataLookupRequested], await AuditActionsAsync(cancellationToken));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Write_of_a_link_on_an_episode_item_is_409_with_jellyfins_sentence_and_sends_nothing(bool oneLink)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        const string KidsId = "c3c3c3c3c3c3c3c3c3c3c3c3c3c3c3c3";
        _jellyfinIntegration = await _integrations.Create("jellyfin", "Living room", apiKey: ApiKey);
        // An episode no series holds (Tom & Jerry Kids): its link names the show, whose ids no item of it holds.
        _jellyfin.Episodes.Add(JellyfinSamples.EpisodeWith(KidsId, "Tom & Jerry Kids 01", null, null, null, "/media/cartoons/Tom & Jerry Kids/01.mkv"));
        await _factory.SyncLibraryAsync(_jellyfinIntegration.Id, _owner.AccessToken, cancellationToken);
        var workId = Guid.Empty;
        await _factory.WithDbAsync(async db => workId = await db.MediaWorkTitles
            .Where(title => title.SourceItemId == KidsId)
            .Select(title => title.MediaWorkId)
            .FirstAsync(cancellationToken));
        var link = await _links.Create(
            _jellyfinIntegration, KidsId, workId, MediaIdentifierScheme.TmdbTv, "1044", target: LibraryLinkTarget.ShowEpisode,
            configure: row => (row.ItemKind, row.SeasonNumber, row.EpisodeNumber) = (MediaWorkKind.Episode, 1, 1));
        var sent = _factory.JellyfinHttp.Requests.Count;

        //Act
        var response = await _factory.PostHttpRequest(oneLink ? $"{BaseUrl}/tmdb/links/{link.Id}/write" : $"{WorkUrl(workId)}/write", _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(
            "There is nothing to write: Jellyfin's ids are written on movie and series items only, so a link on an episode stays in the app.",
            await response.ReadErrorAsync(cancellationToken));
        Assert.Equal(sent, _factory.JellyfinHttp.Requests.Count);
        var stored = await SingleLinkAsync(cancellationToken);
        Assert.Equal((LibraryLinkWriteState.KeptInApp, link.WriteStateUtc), (stored.WriteState, stored.WriteStateUtc));
    }

    [Fact]
    public async Task Write_of_a_work_with_no_link_is_409()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var workId = await SyncShortAsync();

        //Act
        var response = await _factory.PostHttpRequest($"{WorkUrl(workId)}/write", _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("There is nothing to write: no item of this work is linked.", await response.ReadErrorAsync(cancellationToken));
    }

    [Fact]
    public async Task Write_with_a_library_type_that_cannot_set_ids_is_409()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var library = await _integrations.Create(name: "Old library");
        var work = await new MediaWorkSeeder(_factory.Scope).Create(library);
        await _links.Create(library, MediaWorkSeeder.SourceItemId, work.Id, MediaIdentifierScheme.Tmdb, FilmId);

        //Act
        var response = await _factory.PostHttpRequest($"{WorkUrl(work.Id)}/write", _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("There is nothing to write: “Old library” can't set ids, so a link stays in the app.", await response.ReadErrorAsync(cancellationToken));
        Assert.Empty(_factory.FakeAdapter.Calls);
    }

    [Fact]
    public async Task Write_while_a_write_to_the_work_is_under_way_is_409()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var workId = await SyncShortAsync();
        await _links.Create(_jellyfinIntegration, ShortId, workId, MediaIdentifierScheme.Tmdb, FilmId, writeState: LibraryLinkWriteState.Writing);

        //Act
        var response = await _factory.PostHttpRequest($"{WorkUrl(workId)}/write", _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("A write to the library is under way for this work; try again in a moment.", await response.ReadErrorAsync(cancellationToken));
    }

    [Fact]
    public async Task WriteLink_of_a_link_whose_item_is_gone_is_409()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await SyncShortAsync();
        var link = await _links.Create(_jellyfinIntegration, "f6f6f6f6f6f6f6f6f6f6f6f6f6f6f6f6", null, MediaIdentifierScheme.Tmdb, FilmId, writeState: LibraryLinkWriteState.NotWritten);

        //Act
        var response = await _factory.PostHttpRequest($"{BaseUrl}/tmdb/links/{link.Id}/write", _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("There is nothing to write: the library no longer reports this item.", await response.ReadErrorAsync(cancellationToken));
    }

    [Fact]
    public async Task WriteLink_of_an_unknown_link_is_a_bare_404()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var response = await _factory.PostHttpRequest($"{BaseUrl}/tmdb/links/{Guid.NewGuid()}/write", _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsStringAsync(cancellationToken));
    }

    [Fact]
    public async Task Write_again_of_a_link_jellyfin_lost_puts_back_the_imdb_id_the_app_added_too()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var workId = await SyncShortAsync();
        await SetAsync(workId);
        // A refresh replaced the item's metadata: both ids the app wrote are gone.
        Movie(ShortId)["ProviderIds"] = new System.Text.Json.Nodes.JsonObject();
        await MarkAsync(LibraryLinkWriteState.Lost, cancellationToken);

        //Act
        var response = await _factory.PostHttpRequest($"{WorkUrl(workId)}/write", _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new Dictionary<string, string> { ["Tmdb"] = FilmId, ["Imdb"] = FilmImdbId }, ProviderIdsOf(ShortId));
        Assert.Equal(LibraryLinkWriteState.Written, (await SingleLinkAsync(cancellationToken)).WriteState);
    }

    [Fact]
    public async Task Retry_after_a_write_jellyfin_failed_on_adds_the_imdb_id_too()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var workId = await SyncShortAsync();
        // Jellyfin failed on the change and saved nothing: sent, not confirmed, recorded as what it meant to leave.
        _jellyfin.Intercept = request => request.Method == HttpMethod.Post ? new HttpResponseMessage(HttpStatusCode.InternalServerError) : null;
        await SetAsync(workId);
        var failed = await SingleLinkAsync(cancellationToken);
        _jellyfin.Intercept = null;

        //Act
        var response = await _factory.PostHttpRequest($"{WorkUrl(workId)}/write", _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(LibraryLinkWriteState.NotWritten, failed.WriteState);
        Assert.Equal(new Dictionary<string, string> { ["Tmdb"] = FilmId, ["Imdb"] = FilmImdbId }, ProviderIdsOf(ShortId));
        Assert.Equal(LibraryLinkWriteState.Written, (await SingleLinkAsync(cancellationToken)).WriteState);
    }

    [Fact]
    public async Task Write_again_after_someone_changed_the_imdb_id_leaves_it_and_so_does_a_removal_after_it()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var workId = await SyncShortAsync();
        await SetAsync(workId);
        // A refresh dropped the TMDb id, and someone set another IMDb id over the one the app added.
        Movie(ShortId)["ProviderIds"] = new System.Text.Json.Nodes.JsonObject { ["Imdb"] = "tt7777777" };
        await MarkAsync(LibraryLinkWriteState.Lost, cancellationToken);

        //Act
        var written = await _factory.PostHttpRequest($"{WorkUrl(workId)}/write", _owner.AccessToken);
        var afterWrite = ProviderIdsOf(ShortId);
        _factory.Time.Advance(TimeSpan.FromMinutes(1));
        var removed = await _factory.DeleteHttpRequest(WorkUrl(workId), _owner.AccessToken);

        //Assert
        Assert.Equal((HttpStatusCode.OK, HttpStatusCode.OK), (written.StatusCode, removed.StatusCode));
        Assert.Equal(new Dictionary<string, string> { ["Imdb"] = "tt7777777", ["Tmdb"] = FilmId }, afterWrite);
        // Decision 13 — the read after the write found tt7777777, which the app never wrote: it isn't taken away.
        Assert.Equal(new Dictionary<string, string> { ["Imdb"] = "tt7777777" }, ProviderIdsOf(ShortId));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Write_whose_request_is_cancelled_before_the_claim_leaves_the_link_as_it_was(bool oneLink)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var workId = await SyncShortAsync();
        var link = await _links.Create(_jellyfinIntegration, ShortId, workId, MediaIdentifierScheme.Tmdb, FilmId, writeState: LibraryLinkWriteState.NotWritten);
        // The browser goes while the owner's name is read for the audit row.
        _factory.DbCommandHook.BeforeNextCommand(
            command => command.CommandText.Contains("\"UserName\"", StringComparison.Ordinal)
                && command.CommandText.Contains("FROM \"Users\"", StringComparison.Ordinal),
            _ => throw new OperationCanceledException());

        //Act
        var response = await _factory.PostHttpRequest(oneLink ? $"{BaseUrl}/tmdb/links/{link.Id}/write" : $"{WorkUrl(workId)}/write", _owner.AccessToken);

        //Assert
        // R9 — nothing claimed before the last read that takes the request's token: the link isn't left Writing, which
        // would refuse every change of the work until a restart.
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var row = await SingleLinkAsync(cancellationToken);
        Assert.Equal((LibraryLinkWriteState.NotWritten, link.WriteAttemptId), (row.WriteState, row.WriteAttemptId));
        Assert.DoesNotContain(_factory.JellyfinHttp.Requests, request => request.Method == HttpMethod.Post);
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    private static string WorkUrl(Guid workId) => $"{BaseUrl}/works/{workId}/tmdb";

    private async Task<Guid> SyncShortAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        _jellyfinIntegration = await _integrations.Create("jellyfin", "Living room", apiKey: ApiKey);
        _jellyfin.Movies.Add(JellyfinSamples.MovieWith(ShortId, "Landing Stripling", null, 1962, "/media/cartoons/Tom & Jerry/120.mkv"));
        await _factory.SyncLibraryAsync(_jellyfinIntegration.Id, _owner.AccessToken, cancellationToken);
        await _integrations.Create("tmdb", "TMDb", FakeTmdbServer.BaseUrl, apiKey: FakeTmdbServer.Token);
        var workId = Guid.Empty;
        await _factory.WithDbAsync(async db => workId = await db.MediaWorkTitles
            .Where(title => title.SourceItemId == ShortId)
            .Select(title => title.MediaWorkId)
            .FirstAsync(cancellationToken));
        return workId;
    }

    // The owner's Save of the film link, which writes it: its TMDb id, and the IMDb id TMDb gave where Jellyfin has none.
    private async Task SetAsync(Guid workId)
    {
        var response = await _factory.PutHttpRequest(WorkUrl(workId), _owner.AccessToken, new LibraryLinkRequest("Movie", FilmId));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        _factory.Time.Advance(TimeSpan.FromMinutes(1));
    }

    // What a sync that read the item says of the link, as it says it.
    private Task MarkAsync(LibraryLinkWriteState state, CancellationToken cancellationToken) =>
        _factory.WithDbAsync(db => db.LibraryItemLinks.ExecuteUpdateAsync(
            setters => setters.SetProperty(row => row.WriteState, state).SetProperty(row => row.WriteStateUtc, _factory.Time.GetUtcNow().UtcDateTime),
            cancellationToken));

    private Dictionary<string, string> ProviderIdsOf(string itemId) =>
        Movie(itemId)["ProviderIds"]!.AsObject().ToDictionary(pair => pair.Key, pair => (string)pair.Value!);

    private System.Text.Json.Nodes.JsonObject Movie(string itemId) => _jellyfin.Movies.Single(movie => (string?)movie["Id"] == itemId);

    private async Task<LibraryItemLinkEntity> SingleLinkAsync(CancellationToken cancellationToken)
    {
        LibraryItemLinkEntity? link = null;
        await _factory.WithDbAsync(async db => link = await db.LibraryItemLinks.AsNoTracking().SingleAsync(cancellationToken));
        return link!;
    }

    private async Task<List<AuditAction>> AuditActionsAsync(CancellationToken cancellationToken)
    {
        var actions = new List<AuditAction>();
        await _factory.WithDbAsync(async db => actions.AddRange((await db.AuditEvents.AsNoTracking().ToListAsync(cancellationToken))
            .OrderBy(auditEvent => auditEvent.CreatedUtc)
            .ThenBy(auditEvent => auditEvent.Action)
            .Select(auditEvent => auditEvent.Action)));
        return actions;
    }
}
