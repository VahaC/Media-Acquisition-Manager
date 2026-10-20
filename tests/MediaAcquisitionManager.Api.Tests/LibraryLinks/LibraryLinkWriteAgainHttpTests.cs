using System.Net;
using System.Text.Json.Nodes;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Api.Services.LibraryLinks;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Texts;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using MediaAcquisitionManager.TestInfrastructure.Texts;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Tests.LibraryLinks;

// MAM-34 (0.2.1, ADR 0026) — Retry and Write again (POST /api/library/works/{id}/tmdb/write for a work, POST
// /api/library/tmdb/links/{id}/write for one link): the owner's click queues a link for identification again — after a
// refusal, a failure or Jellyfin losing it — and never waits for the library: the request answers 200 with the dialog, the
// item Queued, and the background writer reads the item first, so one that already shows the match is found so and sent
// nothing. The click is the owner's word: it clears the batch a bulk click put on the link, which lifts the protection of
// the library's own ids. Nothing to identify (an episode's link without its numbers, an episode item's, a library type that
// can't identify, an item gone), a library switched off (R12) or an identification under way (R9) is a 409 that queues
// nothing.
public sealed class LibraryLinkWriteAgainHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/library";
    private const string ApiKey = "0123456789abcdef0123456789abcdef";
    private const string ShortId = JellyfinSamples.LandingStriplingId;
    private const string FilmId = JellyfinSamples.LandingStriplingTmdbId;
    private const string FilmImdbId = JellyfinSamples.LandingStriplingImdbId;
    private const string UnderWayError = "A write to the library is under way for this work; try again in a moment.";

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
        _jellyfin.AddFilm(FilmId, "Landing Stripling", 1962, FilmImdbId);
        _jellyfin.AddShow(JellyfinSamples.PenguinsTmdbTvId, "The Penguins of Madagascar", 2008, "tt0892700");
        _jellyfin.Arm(_factory.JellyfinHttp);
        FakeTmdbServer.Arm(_factory.TmdbHttp).AddWork(new FakeTmdbWork(MediaWorkKind.Movie, int.Parse(FilmId), "Посадкова смуга", ImdbId: FilmImdbId));
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Write_queues_the_works_links_and_the_worker_identifies_them_without_the_request_waiting()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var workId = await SyncShortAsync();
        var link = await _links.Create(
            _jellyfinIntegration, ShortId, workId, MediaIdentifierScheme.Tmdb, FilmId, writeState: LibraryLinkWriteState.NotWritten, setBy: _owner.User,
            configure: row => (row.ImdbId, row.WriteError) = (FilmImdbId, Message.Of("jellyfin.identify.search-refused")));
        var sent = _factory.JellyfinHttp.Requests.Count;

        //Act
        var response = await _factory.PostHttpRequest($"{WorkUrl(workId)}/write", _owner.AccessToken);
        var queued = await _factory.SingleLinkAsync(cancellationToken);
        var sentByTheRequest = _factory.JellyfinHttp.Requests.Count - sent;
        var woken = _factory.Resolve<LibraryLinkWriteQueue>().Reader.TryPeek(out _);
        await _factory.IdentifyQueuedLinksAsync(cancellationToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var item = Assert.Single((await response.ReadDialogAsync(cancellationToken)).Items);
        Assert.Equal(((LibraryLinkWriteState?)LibraryLinkWriteState.Queued, (string?)null), (item.WriteState, item.WriteError));
        Assert.Equal((link.Id, LibraryLinkWriteState.Queued, (string?)null, (Guid?)null), (queued.Id, queued.WriteState, TestTexts.En(queued.WriteError), queued.BatchId));
        // The request only queued: the library was asked nothing, and the worker was woken.
        Assert.Equal(0, sentByTheRequest);
        Assert.True(woken);
        var identified = await _factory.SingleLinkAsync(cancellationToken);
        Assert.Equal((LibraryLinkWriteState.Written, (string?)null), (identified.WriteState, TestTexts.En(identified.WriteError)));
        Assert.NotNull(identified.IdentifiedUtc);
        Assert.Equal(FilmId, (string?)Movie(ShortId)["ProviderIds"]!["Tmdb"]);
    }

    [Fact]
    public async Task Write_after_a_refused_identify_queues_the_link_again_clears_its_error_and_settles_it_written()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var workId = await SyncShortAsync();
        _jellyfin.SearchRefusal = HttpStatusCode.Forbidden;
        await _factory.PutHttpRequest(WorkUrl(workId), _owner.AccessToken, new LibraryLinkRequest("Movie", FilmId));
        await _factory.IdentifyQueuedLinksAsync(cancellationToken);
        var refused = await _factory.SingleLinkAsync(cancellationToken);
        _jellyfin.SearchRefusal = null;
        _factory.Time.Advance(TimeSpan.FromMinutes(1));

        //Act
        var response = await _factory.PostHttpRequest($"{WorkUrl(workId)}/write", _owner.AccessToken);
        await _factory.IdentifyQueuedLinksAsync(cancellationToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            (LibraryLinkWriteState.NotWritten, "Jellyfin refused the search for the match. Check that the API key belongs to an administrator.", (DateTime?)null),
            (refused.WriteState, TestTexts.En(refused.WriteError), refused.IdentifiedUtc));
        var link = await _factory.SingleLinkAsync(cancellationToken);
        Assert.Equal((LibraryLinkWriteState.Written, (string?)null, _factory.Time.GetUtcNow().UtcDateTime), (link.WriteState, TestTexts.En(link.WriteError), link.WriteStateUtc!.Value));
        Assert.Equal(FilmId, (string?)Movie(ShortId)["ProviderIds"]!["Tmdb"]);
        // The refused one sent no Apply and so recorded nothing; the one that landed did.
        Assert.Equal([AuditAction.LibraryLinkSet, AuditAction.LibraryItemIdentified], await _factory.AuditActionsAsync(cancellationToken));
        Assert.Single(_jellyfin.Applied);
    }

    [Fact]
    public async Task Write_of_an_already_identified_link_finds_the_item_showing_the_match_and_sends_no_apply()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var workId = await SyncShortAsync();
        await _factory.PutHttpRequest(WorkUrl(workId), _owner.AccessToken, new LibraryLinkRequest("Movie", FilmId));
        await _factory.IdentifyQueuedLinksAsync(cancellationToken);
        var first = await _factory.SingleLinkAsync(cancellationToken);
        _factory.Time.Advance(TimeSpan.FromMinutes(1));
        var recorded = (await _factory.AuditAsync(cancellationToken)).Count;

        //Act
        var response = await _factory.PostHttpRequest($"{WorkUrl(workId)}/write", _owner.AccessToken);
        await _factory.IdentifyQueuedLinksAsync(cancellationToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var link = await _factory.SingleLinkAsync(cancellationToken);
        // Read first: the item shows TMDb's name, year, id, poster and overview, so nothing is sent (an Apply deletes the
        // item's images first) and no audit row is written, but the app has seen it identified again.
        Assert.Equal((LibraryLinkWriteState.Written, (string?)null), (link.WriteState, TestTexts.En(link.WriteError)));
        Assert.Equal(_factory.Time.GetUtcNow().UtcDateTime, link.IdentifiedUtc);
        Assert.NotEqual(first.IdentifiedUtc, link.IdentifiedUtc);
        Assert.Single(_jellyfin.Applied);
        Assert.Equal(recorded, (await _factory.AuditAsync(cancellationToken)).Count);
    }

    [Fact]
    public async Task Write_clears_the_batch_so_a_link_the_guard_refused_is_identified_anyway()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // Jellyfin holds the ids of another film: a bulk click's identification refused to replace them.
        var workId = await SyncShortAsync(tmdbId: "100", imdbId: "tt9999999");
        var link = await _links.Create(
            _jellyfinIntegration, ShortId, workId, MediaIdentifierScheme.Tmdb, FilmId, writeState: LibraryLinkWriteState.NotWritten, setBy: _owner.User,
            configure: row => (row.BatchId, row.ImdbId, row.WriteError) =
                (Guid.NewGuid(), FilmImdbId, Message.Of("jellyfin.identify.conflicting-id.tmdb", ("held", "100"), ("wanted", FilmId))));

        //Act
        var response = await _factory.PostHttpRequest($"{WorkUrl(workId)}/write", _owner.AccessToken);
        var queued = await _factory.SingleLinkAsync(cancellationToken);
        await _factory.IdentifyQueuedLinksAsync(cancellationToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal((link.Id, (Guid?)null), (queued.Id, queued.BatchId));
        var identified = await _factory.SingleLinkAsync(cancellationToken);
        Assert.Equal((LibraryLinkWriteState.Written, (string?)null), (identified.WriteState, TestTexts.En(identified.WriteError)));
        Assert.Equal(
            new Dictionary<string, string> { ["Tmdb"] = FilmId, ["Imdb"] = FilmImdbId },
            Movie(ShortId)["ProviderIds"]!.AsObject().ToDictionary(pair => pair.Key, pair => (string)pair.Value!));
    }

    [Fact]
    public async Task Write_of_a_queued_link_leaves_it_as_it_is_and_is_not_an_error()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var workId = await SyncShortAsync();
        var batchId = Guid.NewGuid();
        var link = await _links.Create(
            _jellyfinIntegration, ShortId, workId, MediaIdentifierScheme.Tmdb, FilmId, writeState: LibraryLinkWriteState.Queued, configure: row => row.BatchId = batchId);

        //Act
        var response = await _factory.PostHttpRequest($"{WorkUrl(workId)}/write", _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var stored = await _factory.SingleLinkAsync(cancellationToken);
        Assert.Equal((link.Id, LibraryLinkWriteState.Queued, (Guid?)batchId, link.WriteAttemptId), (stored.Id, stored.WriteState, stored.BatchId, stored.WriteAttemptId));
        Assert.True(_factory.Resolve<LibraryLinkWriteQueue>().Reader.TryPeek(out _));
    }

    [Fact]
    public async Task WriteLink_of_a_link_jellyfin_lost_identifies_it_again_and_answers_its_works_dialog()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var workId = await SyncShortAsync();
        var link = await _links.Create(
            _jellyfinIntegration, ShortId, workId, MediaIdentifierScheme.Tmdb, FilmId, writeState: LibraryLinkWriteState.Lost,
            configure: row => row.ImdbId = FilmImdbId);

        //Act
        var response = await _factory.PostHttpRequest($"{BaseUrl}/tmdb/links/{link.Id}/write", _owner.AccessToken);
        await _factory.IdentifyQueuedLinksAsync(cancellationToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dialog = await response.ReadDialogAsync(cancellationToken);
        Assert.Equal((workId, (Guid?)link.Id), (dialog.Work.Id, Assert.Single(dialog.Items).LinkId));
        Assert.Equal(LibraryLinkWriteState.Queued, Assert.Single(dialog.Items).WriteState);
        Assert.Equal(LibraryLinkWriteState.Written, (await _factory.SingleLinkAsync(cancellationToken)).WriteState);
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
        Assert.Equal(LibraryLinkWriteState.NotWritten, (await _factory.SingleLinkAsync(cancellationToken)).WriteState);
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
            "There is nothing to write: Without a season and an episode the app has no id of this episode to identify it by.",
            await response.ReadErrorAsync(cancellationToken));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Write_of_a_series_link_the_first_rules_kept_in_the_app_queues_it_and_the_worker_identifies_the_series(bool oneLink)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        const string PenguinsId = "d4d4d4d4d4d4d4d4d4d4d4d4d4d4d4d4";
        _jellyfinIntegration = await _integrations.Create("jellyfin", "Living room", apiKey: ApiKey);
        _jellyfin.Series.Add(JellyfinSamples.SeriesWith(PenguinsId, "The Penguins Of Madagascar", 2008, $"{JellyfinSamples.TvShowsRoot}/Penguins"));
        _jellyfin.Episodes.Add(JellyfinSamples.EpisodeWith("e5e5e5e5e5e5e5e5e5e5e5e5e5e5e5e5", "Gone in a Flash", PenguinsId, 1, 1, $"{JellyfinSamples.TvShowsRoot}/Penguins/S01E01.mkv"));
        await _factory.SyncLibraryAsync(_jellyfinIntegration.Id, _owner.AccessToken, cancellationToken);
        var workId = await _factory.WorkOfItemAsync(PenguinsId, cancellationToken);
        // Kept in the app when series weren't identified; identified like a film's now.
        var link = await _links.Create(
            _jellyfinIntegration, PenguinsId, workId, MediaIdentifierScheme.TmdbTv, JellyfinSamples.PenguinsTmdbTvId, configure: row => row.ImdbId = "tt0892700");
        _factory.Time.Advance(TimeSpan.FromMinutes(1));

        //Act
        var response = await _factory.PostHttpRequest(oneLink ? $"{BaseUrl}/tmdb/links/{link.Id}/write" : $"{WorkUrl(workId)}/write", _owner.AccessToken);
        var item = Assert.Single((await response.ReadDialogAsync(cancellationToken)).Items);
        await _factory.IdentifyQueuedLinksAsync(cancellationToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(((LibraryLinkWriteState?)LibraryLinkWriteState.Queued, true, (string?)null), (item.WriteState, item.CanWrite, item.CannotWriteReason));
        var stored = await _factory.SingleLinkAsync(cancellationToken);
        var now = _factory.Time.GetUtcNow().UtcDateTime;
        Assert.Equal((LibraryLinkWriteState.Written, (string?)null, (DateTime?)now, (DateTime?)now), (stored.WriteState, TestTexts.En(stored.WriteError), stored.WriteStateUtc, stored.IdentifiedUtc));
        Assert.Equal("""{"Tmdb":"7869","Imdb":"tt0892700"}""", _jellyfin.Series.Single()["ProviderIds"]!.ToJsonString());
        Assert.Equal(["Series"], _jellyfin.RemoteSearches.Select(search => search.Endpoint));
        Assert.Equal([AuditAction.LibraryItemIdentified], await _factory.AuditActionsAsync(cancellationToken));
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
        var workId = await _factory.WorkOfItemAsync(KidsId, cancellationToken);
        var link = await _links.Create(
            _jellyfinIntegration, KidsId, workId, MediaIdentifierScheme.TmdbTv, "1044", target: LibraryLinkTarget.ShowEpisode,
            configure: row => (row.ItemKind, row.SeasonNumber, row.EpisodeNumber) = (MediaWorkKind.Episode, 1, 1));
        var sent = _factory.JellyfinHttp.Requests.Count;

        //Act
        var response = await _factory.PostHttpRequest(oneLink ? $"{BaseUrl}/tmdb/links/{link.Id}/write" : $"{WorkUrl(workId)}/write", _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(
            "There is nothing to write: Jellyfin identifies movie and series items only, so a link on an episode stays in the app.",
            await response.ReadErrorAsync(cancellationToken));
        Assert.Equal(sent, _factory.JellyfinHttp.Requests.Count);
        var stored = await _factory.SingleLinkAsync(cancellationToken);
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
    public async Task Write_with_a_library_type_that_cannot_identify_is_409()
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
        Assert.Equal("There is nothing to write: “Old library” can't identify items, so a link stays in the app.", await response.ReadErrorAsync(cancellationToken));
        Assert.Empty(_factory.FakeAdapter.Calls);
    }

    [Fact]
    public async Task Write_while_an_identify_is_under_way_is_409()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var workId = await SyncShortAsync();
        await _links.Create(_jellyfinIntegration, ShortId, workId, MediaIdentifierScheme.Tmdb, FilmId, writeState: LibraryLinkWriteState.Writing);

        //Act
        var response = await _factory.PostHttpRequest($"{WorkUrl(workId)}/write", _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(UnderWayError, await response.ReadErrorAsync(cancellationToken));
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Write_whose_request_is_cancelled_before_it_queues_leaves_the_link_as_it_was(bool oneLink)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var workId = await SyncShortAsync();
        var link = await _links.Create(_jellyfinIntegration, ShortId, workId, MediaIdentifierScheme.Tmdb, FilmId, writeState: LibraryLinkWriteState.NotWritten);
        // The browser goes while the library's integration is read, the last read that takes the request's token.
        _factory.DbCommandHook.BeforeNextCommand(
            command => command.CommandText.Contains("FROM \"IntegrationConnections\"", StringComparison.Ordinal),
            _ => throw new OperationCanceledException());

        //Act
        var response = await _factory.PostHttpRequest(oneLink ? $"{BaseUrl}/tmdb/links/{link.Id}/write" : $"{WorkUrl(workId)}/write", _owner.AccessToken);

        //Assert
        // R9 — nothing is claimed before the last read that takes the request's token: the link isn't left Queued or Writing.
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var row = await _factory.SingleLinkAsync(cancellationToken);
        Assert.Equal((LibraryLinkWriteState.NotWritten, link.WriteAttemptId), (row.WriteState, row.WriteAttemptId));
        Assert.DoesNotContain(_factory.JellyfinHttp.Requests, request => request.Method == HttpMethod.Post);
        Assert.False(_factory.Resolve<LibraryLinkWriteQueue>().Reader.TryPeek(out _));
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    private static string WorkUrl(Guid workId) => $"{BaseUrl}/works/{workId}/tmdb";

    private async Task<Guid> SyncShortAsync(string? tmdbId = null, string? imdbId = null)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        _jellyfinIntegration = await _integrations.Create("jellyfin", "Living room", apiKey: ApiKey);
        _jellyfin.Movies.Add(JellyfinSamples.MovieWith(
            ShortId, "Landing Stripling", null, 1962, $"{JellyfinSamples.CartoonsRoot}/Tom & Jerry/120.mkv", imdbId, tmdbId));
        await _factory.SyncLibraryAsync(_jellyfinIntegration.Id, _owner.AccessToken, cancellationToken);
        await _integrations.Create("tmdb", "TMDb", FakeTmdbServer.BaseUrl, apiKey: FakeTmdbServer.Token);
        return await _factory.WorkOfItemAsync(ShortId, cancellationToken);
    }

    private JsonObject Movie(string itemId) => _jellyfin.Movies.Single(movie => (string?)movie["Id"] == itemId);
}
