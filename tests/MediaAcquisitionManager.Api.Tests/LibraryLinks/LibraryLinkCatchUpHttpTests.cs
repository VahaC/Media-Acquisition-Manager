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

// MAM-34 (0.2.1, ADR 0026; Vasyl's decisions of 2026-10-06) — the repair, "Update Jellyfin" on the TMDb page (GET/POST
// /api/library/tmdb/catch-up, the route keeps its name): it counts exactly the attached links, in libraries switched on,
// that are identified by rule and were never confirmed identified (IdentifiedUtc null) — a refused or failed one only when
// no Apply was ever sent for its current setting (the audit row says), so a link whose Apply was sent and not confirmed
// waits for the owner's own Retry and no click ever deletes a series' images twice. Nothing happens without the click, which
// queues them (202) under one new batch for the background writer and answers with the progress, "done of total". The writer
// identifies each with its own audit row, an item that already shows the match is found so with nothing sent, and a bulk
// click protects the library's own ids: the four evident mistakes of the owner's library are refused and reported, and no
// sync erases the refusal. Owner-only by its namespace (LibraryLinksAreOwnerOnlyHttpTests).
public sealed class LibraryLinkCatchUpHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string CatchUpUrl = "/api/library/tmdb/catch-up";
    private const string ApiKey = "0123456789abcdef0123456789abcdef";
    private const string FilmItemId = JellyfinSamples.LandingStriplingId;
    private const string OtherFilmItemId = "b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2";
    private const string EpisodeItemId = "c3c3c3c3c3c3c3c3c3c3c3c3c3c3c3c3";
    private const string PenguinsId = "d4d4d4d4d4d4d4d4d4d4d4d4d4d4d4d4";
    private const string FilmId = JellyfinSamples.LandingStriplingTmdbId;
    private const string FilmImdbId = JellyfinSamples.LandingStriplingImdbId;
    private const string OtherFilmId = "223022";
    private const string ShowId = TmdbSamples.TvEpisodeWwiiShowId;
    private const string EpisodeImdbId = TmdbSamples.TvEpisodeWwiiImdbId;

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private IIntegrationConnectionSeeder _integrations = null!;
    private ILibraryItemLinkSeeder _links = null!;
    private AuthorizedUser _owner = null!;
    private AuthorizedUser _member = null!;
    private FakeJellyfinServer _jellyfin = null!;
    private FakeTmdbServer _tmdb = null!;
    private IntegrationConnectionEntity _jellyfinIntegration = null!;

    public LibraryLinkCatchUpHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _integrations = new IntegrationConnectionSeeder(_factory.Scope);
        _links = new LibraryItemLinkSeeder(_factory.Scope);
        var users = new UserSeeder(_factory.Scope);
        _owner = await users.CreateAuthorizedOwner("owner");
        _member = await users.CreateAuthorized("member");
        _jellyfin = new FakeJellyfinServer { ApiKey = ApiKey };
        _jellyfin.Movies.Clear();
        _jellyfin.AddFilm(FilmId, "Landing Stripling", 1962, FilmImdbId);
        _jellyfin.AddFilm(OtherFilmId, "Calypso Cat", 1962);
        _jellyfin.AddShow(JellyfinSamples.PenguinsTmdbTvId, "The Penguins of Madagascar", 2008, "tt0892700");
        _jellyfin.AddEpisodeMovie(EpisodeImdbId, "Blitzkrieg", 2019);
        _jellyfin.Arm(_factory.JellyfinHttp);
        _tmdb = FakeTmdbServer.Arm(_factory.TmdbHttp);
        _tmdb.AddWork(new FakeTmdbWork(MediaWorkKind.Movie, int.Parse(FilmId), "Посадкова смуга", ImdbId: FilmImdbId));
        _tmdb.AddEpisode(new FakeTmdbEpisode(int.Parse(ShowId), 1, 1, "Серія 1", EpisodeImdbId, "2019-07-12", 50));
        _tmdb.AddEpisode(new FakeTmdbEpisode(int.Parse(ShowId), 1, 2, "Битва за Британію"));
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task CatchUp_counts_the_attached_links_of_enabled_libraries_that_identify_and_are_not_identified_yet_and_nothing_else()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var works = await SyncLibraryAsync(cancellationToken);
        var identifiedAt = _factory.Time.GetUtcNow().UtcDateTime;
        // Counted: a film link the earlier ids-only rule wrote, read in Jellyfin, or lost; a series' link and a numbered
        // episode's link the first rules kept in the app; a film link whose identification was refused before any Apply; and
        // one whose only Apply was for the link it replaced.
        await _links.Create(_jellyfinIntegration, ItemId(1), works[FilmItemId], MediaIdentifierScheme.Tmdb, FilmId, writeState: LibraryLinkWriteState.Written);
        await _links.Create(_jellyfinIntegration, ItemId(2), works[FilmItemId], MediaIdentifierScheme.Tmdb, FilmId, writeState: LibraryLinkWriteState.InLibrary);
        await _links.Create(_jellyfinIntegration, ItemId(3), works[FilmItemId], MediaIdentifierScheme.Tmdb, FilmId, writeState: LibraryLinkWriteState.Lost);
        await _links.Create(_jellyfinIntegration, PenguinsId, works[PenguinsId], MediaIdentifierScheme.TmdbTv, JellyfinSamples.PenguinsTmdbTvId);
        await _links.Create(
            _jellyfinIntegration, EpisodeItemId, works[EpisodeItemId], MediaIdentifierScheme.TmdbTv, ShowId, LibraryLinkTarget.ShowEpisode,
            configure: row => (row.SeasonNumber, row.EpisodeNumber) = (1, 1));
        await _links.Create(
            _jellyfinIntegration, ItemId(4), works[FilmItemId], MediaIdentifierScheme.Tmdb, FilmId, writeState: LibraryLinkWriteState.NotWritten,
            configure: row => row.WriteError = Message.Of("jellyfin.identify.search-refused"));
        var replaced = await _links.Create(_jellyfinIntegration, ItemId(5), works[FilmItemId], MediaIdentifierScheme.Tmdb, FilmId, writeState: LibraryLinkWriteState.NotWritten);
        await AppliedAuditRowAsync(replaced, identifiedAt.AddHours(-1));
        // Not counted: identified; waiting or being identified; unattached; an episode item; a numberless or episode TMDb
        // knows no IMDb id of; and a link whose Apply was sent and not confirmed since it was set.
        await _links.Create(_jellyfinIntegration, ItemId(6), works[FilmItemId], MediaIdentifierScheme.Tmdb, FilmId, writeState: LibraryLinkWriteState.Written, configure: row => row.IdentifiedUtc = identifiedAt);
        await _links.Create(_jellyfinIntegration, ItemId(7), works[FilmItemId], MediaIdentifierScheme.Tmdb, FilmId, writeState: LibraryLinkWriteState.Queued);
        await _links.Create(_jellyfinIntegration, ItemId(8), works[FilmItemId], MediaIdentifierScheme.Tmdb, FilmId, writeState: LibraryLinkWriteState.Writing);
        await _links.Create(_jellyfinIntegration, ItemId(9), null, MediaIdentifierScheme.Tmdb, FilmId, writeState: LibraryLinkWriteState.NotWritten);
        await _links.Create(_jellyfinIntegration, ItemId(10), works[FilmItemId], MediaIdentifierScheme.Tmdb, FilmId, configure: row => row.ItemKind = MediaWorkKind.Episode);
        await _links.Create(
            _jellyfinIntegration, ItemId(11), works[EpisodeItemId], MediaIdentifierScheme.TmdbTv, ShowId, LibraryLinkTarget.ShowEpisode,
            configure: row => row.SeasonNumber = 1);
        await _links.Create(
            _jellyfinIntegration, ItemId(12), works[EpisodeItemId], MediaIdentifierScheme.TmdbTv, ShowId, LibraryLinkTarget.ShowEpisode,
            configure: row => (row.SeasonNumber, row.EpisodeNumber, row.WriteError) = (1, 2, LibraryLinkWritability.NoEpisodeImdbReason));
        var sent = await _links.Create(_jellyfinIntegration, ItemId(13), works[FilmItemId], MediaIdentifierScheme.Tmdb, FilmId, writeState: LibraryLinkWriteState.NotWritten);
        await AppliedAuditRowAsync(sent, sent.SetUtc);
        // Not counted: a library switched off, and a library type that can't identify.
        var bedroom = await _integrations.Create("jellyfin", "Bedroom", "http://127.0.0.1:9/bedroom", apiKey: ApiKey);
        await _factory.WithDbAsync(db => db.IntegrationConnections
            .Where(integration => integration.Id == bedroom.Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(integration => integration.IsEnabled, false), cancellationToken));
        await _links.Create(bedroom, ItemId(14), works[FilmItemId], MediaIdentifierScheme.Tmdb, FilmId, writeState: LibraryLinkWriteState.NotWritten);
        var oldLibrary = await _integrations.Create(name: "Old library");
        await _links.Create(oldLibrary, ItemId(15), works[FilmItemId], MediaIdentifierScheme.Tmdb, FilmId);
        var sentBefore = _factory.JellyfinHttp.Requests.Count;

        //Act
        var response = await _factory.GetHttpRequest(CatchUpUrl, _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new LibraryLinkCatchUpResponse(7, null), await response.ReadAsync<LibraryLinkCatchUpResponse>(cancellationToken));
        // Counting sends nothing and changes no link.
        Assert.Equal(sentBefore, _factory.JellyfinHttp.Requests.Count);
        Assert.DoesNotContain(await _factory.LinksAsync(cancellationToken), link => link.BatchId is not null);
        Assert.False(_factory.Resolve<LibraryLinkWriteQueue>().Reader.TryPeek(out _));
    }

    [Fact]
    public async Task CatchUp_queues_them_under_one_new_batch_and_answers_202_with_the_count_and_the_progress()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var works = await SyncLibraryAsync(cancellationToken);
        var written = await _links.Create(_jellyfinIntegration, FilmItemId, works[FilmItemId], MediaIdentifierScheme.Tmdb, FilmId, writeState: LibraryLinkWriteState.Written);
        var series = await _links.Create(_jellyfinIntegration, PenguinsId, works[PenguinsId], MediaIdentifierScheme.TmdbTv, JellyfinSamples.PenguinsTmdbTvId);
        var refused = await _links.Create(
            _jellyfinIntegration, OtherFilmItemId, works[OtherFilmItemId], MediaIdentifierScheme.Tmdb, OtherFilmId, writeState: LibraryLinkWriteState.NotWritten,
            configure: row => row.WriteError = Message.Legacy("Jellyfin holds a library of its own."));
        _factory.Time.Advance(TimeSpan.FromMinutes(1));
        var sentBefore = _factory.JellyfinHttp.Requests.Count;

        //Act
        var response = await _factory.PostHttpRequest(CatchUpUrl, _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var body = await response.ReadAsync<LibraryLinkCatchUpResponse>(cancellationToken);
        Assert.Equal(3, body.ToIdentify);
        var batchId = body.Progress!.BatchId;
        Assert.Equal(new LibraryLinkProgressDto(batchId, 3, 0), body.Progress);
        // One batch for the click: it persists across a reload, and it marks the links protected (ProtectHeldIds).
        var links = (await _factory.LinksAsync(cancellationToken)).ToDictionary(link => link.Id);
        Assert.All(
            new[] { written.Id, series.Id, refused.Id },
            id => Assert.Equal((LibraryLinkWriteState.Queued, (Guid?)batchId, (string?)null, _factory.Time.GetUtcNow().UtcDateTime), (links[id].WriteState, links[id].BatchId, TestTexts.En(links[id].WriteError), links[id].WriteStateUtc!.Value)));
        // The click only queues and wakes the writer: nothing is sent to Jellyfin, and no audit row is written for the click.
        Assert.Equal(sentBefore, _factory.JellyfinHttp.Requests.Count);
        Assert.True(_factory.Resolve<LibraryLinkWriteQueue>().Reader.TryPeek(out _));
        Assert.Empty(await _factory.AuditAsync(cancellationToken));
        // The GET agrees: nothing left to queue, the batch under way.
        var after = await (await _factory.GetHttpRequest(CatchUpUrl, _owner.AccessToken)).ReadAsync<LibraryLinkCatchUpResponse>(cancellationToken);
        Assert.Equal(new LibraryLinkCatchUpResponse(0, new LibraryLinkProgressDto(batchId, 3, 0)), after);
    }

    [Fact]
    public async Task CatchUp_queued_links_are_identified_by_the_worker_each_with_its_own_audit_row_and_the_progress_counts_the_settled()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var works = await SyncLibraryAsync(cancellationToken);
        var film = await _links.Create(_jellyfinIntegration, FilmItemId, works[FilmItemId], MediaIdentifierScheme.Tmdb, FilmId, writeState: LibraryLinkWriteState.Written, setBy: _owner.User);
        var other = await _links.Create(
            _jellyfinIntegration, OtherFilmItemId, works[OtherFilmItemId], MediaIdentifierScheme.Tmdb, OtherFilmId, writeState: LibraryLinkWriteState.NotWritten,
            setBy: _owner.User);
        var series = await _links.Create(_jellyfinIntegration, PenguinsId, works[PenguinsId], MediaIdentifierScheme.TmdbTv, JellyfinSamples.PenguinsTmdbTvId, setBy: _owner.User);
        var episode = await _links.Create(
            _jellyfinIntegration, EpisodeItemId, works[EpisodeItemId], MediaIdentifierScheme.TmdbTv, ShowId, LibraryLinkTarget.ShowEpisode, setBy: _owner.User,
            configure: row => (row.SeasonNumber, row.EpisodeNumber) = (1, 1));
        _factory.Time.Advance(TimeSpan.FromMinutes(1));
        var started = await (await _factory.PostHttpRequest(CatchUpUrl, _owner.AccessToken)).ReadAsync<LibraryLinkCatchUpResponse>(cancellationToken);
        var batchId = started.Progress!.BatchId;
        // One of the four settles before the next look: the progress counts it.
        await _factory.WithDbAsync(db => db.LibraryItemLinks
            .Where(row => row.Id == other.Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.WriteState, LibraryLinkWriteState.NotWritten), cancellationToken));
        var midway = await (await _factory.GetHttpRequest(CatchUpUrl, _owner.AccessToken)).ReadAsync<LibraryLinkCatchUpResponse>(cancellationToken);
        await _factory.WithDbAsync(db => db.LibraryItemLinks
            .Where(row => row.Id == other.Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.WriteState, LibraryLinkWriteState.Queued), cancellationToken));

        //Act
        await _factory.IdentifyQueuedLinksAsync(cancellationToken);
        var after = await (await _factory.GetHttpRequest(CatchUpUrl, _owner.AccessToken)).ReadAsync<LibraryLinkCatchUpResponse>(cancellationToken);

        //Assert
        Assert.Equal(new LibraryLinkCatchUpResponse(4, new LibraryLinkProgressDto(batchId, 4, 0)), started);
        Assert.Equal(new LibraryLinkProgressDto(batchId, 4, 1), midway.Progress);
        var links = (await _factory.LinksAsync(cancellationToken)).ToDictionary(link => link.Id);
        Assert.All(
            new[] { film.Id, other.Id, series.Id, episode.Id },
            id => Assert.Equal((LibraryLinkWriteState.Written, (string?)null), (links[id].WriteState, TestTexts.En(links[id].WriteError))));
        Assert.All(links.Values, link => Assert.NotNull(link.IdentifiedUtc));
        // The episode's own IMDb id, asked of TMDb by the writer.
        Assert.Equal(EpisodeImdbId, links[episode.Id].EpisodeImdbId);
        Assert.Single(_factory.TmdbHttp.Requests, request => request.Uri.AbsolutePath == $"/3/tv/{ShowId}/season/1/episode/1");
        // Each identified through the search of its own kind, with its own audit row in the name of whoever set the link.
        Assert.Equal(["Movie", "Movie", "Movie", "Series"], _jellyfin.RemoteSearches.Select(search => search.Endpoint).Order(StringComparer.Ordinal));
        Assert.Equal(4, _jellyfin.Applied.Count);
        var audit = await _factory.AuditAsync(cancellationToken);
        Assert.Equal(
            new[] { film.Id, other.Id, series.Id, episode.Id }.Order(),
            audit.Where(row => row.Action == AuditAction.LibraryItemIdentified).Select(row => row.TargetId!.Value).Order());
        Assert.All(audit, row => Assert.Equal("owner", row.ActorUserName));
        // Done: nothing left to identify, no batch under way.
        Assert.Equal(new LibraryLinkCatchUpResponse(0, null), after);
    }

    [Fact]
    public async Task CatchUp_jellyfin_down_leaves_the_links_not_written_with_no_apply_and_counts_them_again()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var works = await SyncLibraryAsync(cancellationToken);
        await _links.Create(_jellyfinIntegration, FilmItemId, works[FilmItemId], MediaIdentifierScheme.Tmdb, FilmId, writeState: LibraryLinkWriteState.Written);
        await _links.Create(_jellyfinIntegration, PenguinsId, works[PenguinsId], MediaIdentifierScheme.TmdbTv, JellyfinSamples.PenguinsTmdbTvId);
        // Jellyfin restarting: its connection test fails, so nothing is sent.
        _jellyfin.Intercept = request => request.RequestUri!.AbsolutePath.EndsWith("/System/Info", StringComparison.Ordinal)
            ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            : null;
        _factory.Time.Advance(TimeSpan.FromMinutes(1));

        //Act
        await _factory.PostHttpRequest(CatchUpUrl, _owner.AccessToken);
        await _factory.IdentifyQueuedLinksAsync(cancellationToken);
        var after = await (await _factory.GetHttpRequest(CatchUpUrl, _owner.AccessToken)).ReadAsync<LibraryLinkCatchUpResponse>(cancellationToken);

        //Assert
        var links = await _factory.LinksAsync(cancellationToken);
        Assert.All(links, link =>
        {
            Assert.Equal(LibraryLinkWriteState.NotWritten, link.WriteState);
            Assert.StartsWith("Jellyfin", TestTexts.En(link.WriteError), StringComparison.Ordinal);
            Assert.Null(link.IdentifiedUtc);
        });
        Assert.DoesNotContain(_factory.JellyfinHttp.Requests, request => request.Method == HttpMethod.Post);
        Assert.Empty(await _factory.AuditAsync(cancellationToken));
        // Nothing was sent, so the next click takes them both again.
        Assert.Equal(new LibraryLinkCatchUpResponse(2, null), after);
    }

    [Fact]
    public async Task CatchUp_never_requeues_a_link_whose_apply_was_sent_and_not_confirmed_and_never_sends_a_second_apply_for_it()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var works = await SyncLibraryAsync(cancellationToken);
        var link = await _links.Create(
            _jellyfinIntegration, FilmItemId, works[FilmItemId], MediaIdentifierScheme.Tmdb, FilmId, writeState: LibraryLinkWriteState.Written, setBy: _owner.User);
        // Jellyfin takes the Apply (204) and shows nothing of it: sent, not confirmed.
        _jellyfin.IgnoresApply = true;
        _factory.Time.Advance(TimeSpan.FromMinutes(1));
        await _factory.PostHttpRequest(CatchUpUrl, _owner.AccessToken);
        await _factory.IdentifyQueuedLinksAsync(cancellationToken);
        var afterFirst = await _factory.SingleLinkAsync(cancellationToken);
        _factory.Time.Advance(TimeSpan.FromMinutes(1));

        //Act
        var read = await (await _factory.GetHttpRequest(CatchUpUrl, _owner.AccessToken)).ReadAsync<LibraryLinkCatchUpResponse>(cancellationToken);
        var second = await (await _factory.PostHttpRequest(CatchUpUrl, _owner.AccessToken)).ReadAsync<LibraryLinkCatchUpResponse>(cancellationToken);
        await _factory.IdentifyQueuedLinksAsync(cancellationToken);

        //Assert
        Assert.Equal((link.Id, LibraryLinkWriteState.NotWritten, (DateTime?)null), (afterFirst.Id, afterFirst.WriteState, afterFirst.IdentifiedUtc));
        // Not counted, not queued: it waits for the owner's own Retry, once he has read the sentence.
        Assert.Equal(new LibraryLinkCatchUpResponse(0, null), read);
        Assert.Equal(new LibraryLinkCatchUpResponse(0, null), second);
        Assert.Single(_jellyfin.Applied);
        Assert.Equal(LibraryLinkWriteState.NotWritten, (await _factory.SingleLinkAsync(cancellationToken)).WriteState);
        // It is the panel's "refused or failed" figure that lists it.
        var listed = await (await _factory.GetHttpRequest("/api/library/tmdb/links?filter=NotWritten", _owner.AccessToken)).ReadAsync<LibraryLinkListResponse>(cancellationToken);
        Assert.Equal([link.Id], listed.Items.Select(row => row.LinkId));

        // Retry re-reads the item first and sends at most the one Apply it needs.
        _jellyfin.IgnoresApply = false;
        var retried = await _factory.PostHttpRequest($"/api/library/tmdb/links/{link.Id}/write", _owner.AccessToken);
        await _factory.IdentifyQueuedLinksAsync(cancellationToken);
        Assert.Equal(HttpStatusCode.OK, retried.StatusCode);
        Assert.Equal(2, _jellyfin.Applied.Count);
        Assert.Equal(LibraryLinkWriteState.Written, (await _factory.SingleLinkAsync(cancellationToken)).WriteState);
    }

    [Fact]
    public async Task CatchUp_leaves_a_link_a_restart_cut_to_the_owners_retry_and_takes_the_ones_that_failed_before_any_apply()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var works = await SyncLibraryAsync(cancellationToken);
        // A series inside its Apply when the app stopped: the Apply's audit row is written only after its answer, so none exists
        // — yet Jellyfin goes on with it for minutes, and an Apply sent into it now would delete its images again.
        var cut = await _links.Create(
            _jellyfinIntegration, PenguinsId, works[PenguinsId], MediaIdentifierScheme.TmdbTv, JellyfinSamples.PenguinsTmdbTvId, writeState: LibraryLinkWriteState.Writing,
            setBy: _owner.User);
        // A link refused with a sentence and one that failed with none: no Apply was sent for either.
        var refused = await _links.Create(
            _jellyfinIntegration, FilmItemId, works[FilmItemId], MediaIdentifierScheme.Tmdb, FilmId, writeState: LibraryLinkWriteState.NotWritten, setBy: _owner.User,
            configure: row => row.WriteError = Message.Legacy("Jellyfin holds a library of its own."));
        var plain = await _links.Create(
            _jellyfinIntegration, OtherFilmItemId, works[OtherFilmItemId], MediaIdentifierScheme.Tmdb, OtherFilmId, writeState: LibraryLinkWriteState.NotWritten, setBy: _owner.User);
        await _factory.Resolve<LibraryLinkWriteReconciler>().ReconcileAsync(cancellationToken);
        _factory.Time.Advance(TimeSpan.FromMinutes(1));

        //Act
        var read = await (await _factory.GetHttpRequest(CatchUpUrl, _owner.AccessToken)).ReadAsync<LibraryLinkCatchUpResponse>(cancellationToken);
        await _factory.PostHttpRequest(CatchUpUrl, _owner.AccessToken);
        await _factory.IdentifyQueuedLinksAsync(cancellationToken);

        //Assert
        Assert.Equal(new LibraryLinkCatchUpResponse(2, null), read);
        var links = (await _factory.LinksAsync(cancellationToken)).ToDictionary(link => link.Id);
        Assert.Equal(
            (LibraryLinkWriteState.NotWritten, LibraryLinkWriteReconciler.RestartedError, (Guid?)null),
            (links[cut.Id].WriteState, links[cut.Id].WriteError, links[cut.Id].BatchId));
        Assert.Equal((LibraryLinkWriteState.Written, LibraryLinkWriteState.Written), (links[refused.Id].WriteState, links[plain.Id].WriteState));
        Assert.DoesNotContain(_jellyfin.Applied, applied => applied.ItemId == PenguinsId);
        // It waits for the owner's own Retry, which re-reads the series first and sends the one Apply it needs.
        var retried = await _factory.PostHttpRequest($"/api/library/tmdb/links/{cut.Id}/write", _owner.AccessToken);
        await _factory.IdentifyQueuedLinksAsync(cancellationToken);
        Assert.Equal(HttpStatusCode.OK, retried.StatusCode);
        Assert.Equal(LibraryLinkWriteState.Written, (await _factory.LinksAsync(cancellationToken)).Single(link => link.Id == cut.Id).WriteState);
        Assert.Single(_jellyfin.Applied, applied => applied.ItemId == PenguinsId);
    }

    [Fact]
    public async Task CatchUp_leaves_a_link_whose_stored_error_is_the_restart_key_and_counts_one_with_another_error_or_none()
    {
        //Arrange
        // MAM-47 (ADR 0042) — the repair's query compares the column with the restart message in SQL. The cut link's error is
        // written here as raw text, as the startup converter writes an older build's restart sentence: the key's canonical
        // JSON with no values. A link refused with another message and one that failed with none are candidates, as before.
        const string storedRestart = """{"k":"library-links.write.restarted"}""";
        var cancellationToken = TestContext.Current.CancellationToken;
        var works = await SyncLibraryAsync(cancellationToken);
        var cut = await _links.Create(
            _jellyfinIntegration, PenguinsId, works[PenguinsId], MediaIdentifierScheme.TmdbTv, JellyfinSamples.PenguinsTmdbTvId, writeState: LibraryLinkWriteState.NotWritten,
            setBy: _owner.User);
        await _links.Create(
            _jellyfinIntegration, FilmItemId, works[FilmItemId], MediaIdentifierScheme.Tmdb, FilmId, writeState: LibraryLinkWriteState.NotWritten, setBy: _owner.User,
            configure: row => row.WriteError = Message.Of("jellyfin.identify.search-refused"));
        await _links.Create(
            _jellyfinIntegration, OtherFilmItemId, works[OtherFilmItemId], MediaIdentifierScheme.Tmdb, OtherFilmId, writeState: LibraryLinkWriteState.NotWritten, setBy: _owner.User);
        await _factory.WithDbAsync(db => db.Database.ExecuteSqlAsync(
            $"UPDATE \"LibraryItemLinks\" SET \"WriteError\" = {storedRestart} WHERE \"SourceItemId\" = {PenguinsId}",
            cancellationToken));
        _factory.Time.Advance(TimeSpan.FromMinutes(1));

        //Act
        var read = await (await _factory.GetHttpRequest(CatchUpUrl, _owner.AccessToken)).ReadAsync<LibraryLinkCatchUpResponse>(cancellationToken);

        //Assert
        // The two candidates, not the cut one: "WriteError" <> the restart JSON, or NULL — the comparison's null semantics.
        Assert.Equal(new LibraryLinkCatchUpResponse(2, null), read);
        // The raw text is exactly what the app writes for the restart, and reads back as it.
        Assert.Equal(storedRestart, MessageJson.Write(LibraryLinkWriteReconciler.RestartedError));
        var stored = (await _factory.LinksAsync(cancellationToken)).Single(link => link.Id == cut.Id).WriteError;
        Assert.Equal(
            (LibraryLinkWriteReconciler.RestartedKey, "The app restarted while identifying the item in Jellyfin. Jellyfin may still be working on it: Retry in a few minutes checks what it holds before anything is sent."),
            (stored?.Key, TestTexts.En(stored)));
    }

    [Fact]
    public async Task CatchUp_second_click_while_a_batch_runs_queues_only_what_is_not_queued()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var works = await SyncLibraryAsync(cancellationToken);
        await _links.Create(_jellyfinIntegration, FilmItemId, works[FilmItemId], MediaIdentifierScheme.Tmdb, FilmId, writeState: LibraryLinkWriteState.Written);
        await _links.Create(_jellyfinIntegration, PenguinsId, works[PenguinsId], MediaIdentifierScheme.TmdbTv, JellyfinSamples.PenguinsTmdbTvId);
        _factory.Time.Advance(TimeSpan.FromMinutes(1));
        var first = await (await _factory.PostHttpRequest(CatchUpUrl, _owner.AccessToken)).ReadAsync<LibraryLinkCatchUpResponse>(cancellationToken);
        // A link saved while the batch runs.
        _factory.Time.Advance(TimeSpan.FromMinutes(1));
        var late = await _links.Create(_jellyfinIntegration, OtherFilmItemId, works[OtherFilmItemId], MediaIdentifierScheme.Tmdb, OtherFilmId, writeState: LibraryLinkWriteState.Lost);

        //Act
        var second = await (await _factory.PostHttpRequest(CatchUpUrl, _owner.AccessToken)).ReadAsync<LibraryLinkCatchUpResponse>(cancellationToken);

        //Assert
        Assert.Equal(2, first.ToIdentify);
        Assert.Equal(1, second.ToIdentify);
        var links = await _factory.LinksAsync(cancellationToken);
        Assert.Equal(2, links.Count(link => link.BatchId == first.Progress!.BatchId));
        Assert.Equal((second.Progress!.BatchId, LibraryLinkWriteState.Queued), (links.Single(link => link.Id == late.Id).BatchId!.Value, links.Single(link => link.Id == late.Id).WriteState));
        Assert.NotEqual(first.Progress!.BatchId, second.Progress.BatchId);
        Assert.Equal(1, second.Progress.Total);
    }

    [Fact]
    public async Task CatchUp_of_old_written_links_whose_items_already_show_the_row_marks_them_identified_without_apply_and_without_audit_rows()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var works = await SyncLibraryAsync(cancellationToken);
        // The item was identified once, by Jellyfin's own button or an earlier run: it shows TMDb's name, year, ids, poster
        // and overview. The link is one the ids-only code wrote, never confirmed.
        var response = await _factory.PutHttpRequest(
            $"/api/library/works/{works[FilmItemId]}/tmdb", _owner.AccessToken, new LibraryLinkRequest("Movie", FilmId));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await _factory.IdentifyQueuedLinksAsync(cancellationToken);
        await _factory.WithDbAsync(db => db.LibraryItemLinks.ExecuteUpdateAsync(
            setters => setters.SetProperty(row => row.IdentifiedUtc, (DateTime?)null).SetProperty(row => row.WriteState, LibraryLinkWriteState.InLibrary), cancellationToken));
        _factory.Time.Advance(TimeSpan.FromMinutes(1));
        var recorded = (await _factory.AuditAsync(cancellationToken)).Count;

        //Act
        var started = await (await _factory.PostHttpRequest(CatchUpUrl, _owner.AccessToken)).ReadAsync<LibraryLinkCatchUpResponse>(cancellationToken);
        await _factory.IdentifyQueuedLinksAsync(cancellationToken);

        //Assert
        Assert.Equal(1, started.ToIdentify);
        var link = await _factory.SingleLinkAsync(cancellationToken);
        Assert.Equal((LibraryLinkWriteState.Written, (DateTime?)_factory.Time.GetUtcNow().UtcDateTime), (link.WriteState, link.IdentifiedUtc));
        // Found showing the match: the one Apply is the earlier one, and no audit row is written for this run.
        Assert.Single(_jellyfin.Applied);
        Assert.Equal(recorded, (await _factory.AuditAsync(cancellationToken)).Count);
        Assert.Equal(new LibraryLinkCatchUpResponse(0, null), await (await _factory.GetHttpRequest(CatchUpUrl, _owner.AccessToken)).ReadAsync<LibraryLinkCatchUpResponse>(cancellationToken));
    }

    [Fact]
    public async Task CatchUp_protects_the_libraries_own_ids_so_the_four_evident_mistakes_are_not_identified_and_no_sync_erases_the_refusal()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        const string ContinentalId = "e0e0e0e0e0e0e0e0e0e0e0e0e0e0e0e0";
        const string PolanskiId = "e1e1e1e1e1e1e1e1e1e1e1e1e1e1e1e1";
        // The Continental: an episode of a show Jellyfin lists as a film, holding the show's own IMDb id.
        _jellyfin.Movies.Add(JellyfinSamples.MovieWith(
            ContinentalId, "The Continental", null, 2023, $"{JellyfinSamples.DocumentaryRoot}/The Continental/E01.mkv", imdbId: "tt29669983"));
        _jellyfin.AddEpisodeMovie("tt13217676", "The Continental S01E01", 2023);
        // Polanski + Skolimowski: the documentary, which the older code gave the 1962 film's TMDb id — equal to the link —
        // beside its own IMDb id.
        _jellyfin.Movies.Add(JellyfinSamples.MovieWith(
            PolanskiId, "Polanski+Skolimowski o Noże", null, 2003, $"{JellyfinSamples.MoviesRoot}/Polanski.mkv", imdbId: "tt1163854", tmdbId: "11502"));
        _jellyfin.AddFilm("11502", "Knife in the Water", 1962, "tt0056291");
        var works = await SyncLibraryAsync(cancellationToken);
        var continentalWork = await _factory.WorkOfItemAsync(ContinentalId, cancellationToken);
        var polanskiWork = await _factory.WorkOfItemAsync(PolanskiId, cancellationToken);
        var continental = await _links.Create(
            _jellyfinIntegration, ContinentalId, continentalWork, MediaIdentifierScheme.TmdbTv, "72710", LibraryLinkTarget.ShowEpisode, LibraryLinkWriteState.Written,
            setBy: _owner.User, configure: row => (row.SeasonNumber, row.EpisodeNumber, row.EpisodeImdbId) = (1, 1, "tt13217676"));
        var polanski = await _links.Create(
            _jellyfinIntegration, PolanskiId, polanskiWork, MediaIdentifierScheme.Tmdb, "11502", writeState: LibraryLinkWriteState.Written, setBy: _owner.User,
            configure: row => row.ImdbId = "tt0056291");
        var normal = await _links.Create(
            _jellyfinIntegration, FilmItemId, works[FilmItemId], MediaIdentifierScheme.Tmdb, FilmId, writeState: LibraryLinkWriteState.Written, setBy: _owner.User);
        var continentalBefore = Movie(ContinentalId).DeepClone();
        var polanskiBefore = Movie(PolanskiId).DeepClone();
        _factory.Time.Advance(TimeSpan.FromMinutes(1));

        //Act
        await _factory.PostHttpRequest(CatchUpUrl, _owner.AccessToken);
        await _factory.IdentifyQueuedLinksAsync(cancellationToken);
        // A sync after the click: the Polanski item holds the link's TMDb id, which used to promote its link to InLibrary.
        _factory.Time.Advance(TimeSpan.FromMinutes(1));
        await _factory.SyncLibraryAsync(_jellyfinIntegration.Id, _owner.AccessToken, cancellationToken);
        var listed = await (await _factory.GetHttpRequest("/api/library/tmdb/links?filter=NotWritten", _owner.AccessToken)).ReadAsync<LibraryLinkListResponse>(cancellationToken);

        //Assert
        var links = (await _factory.LinksAsync(cancellationToken)).ToDictionary(link => link.Id);
        Assert.Equal(
            (LibraryLinkWriteState.NotWritten,
                "Jellyfin holds IMDb id tt29669983 for this item, not tt13217676, and the app doesn't replace a library's own ids on its own. If the link is right, press Retry to identify it anyway; otherwise change the link.",
                (DateTime?)null),
            (links[continental.Id].WriteState, TestTexts.En(links[continental.Id].WriteError), links[continental.Id].IdentifiedUtc));
        Assert.Equal(
            (LibraryLinkWriteState.NotWritten,
                "Jellyfin holds IMDb id tt1163854 for this item, not tt0056291, and the app doesn't replace a library's own ids on its own. If the link is right, press Retry to identify it anyway; otherwise change the link.",
                (DateTime?)null),
            (links[polanski.Id].WriteState, TestTexts.En(links[polanski.Id].WriteError), links[polanski.Id].IdentifiedUtc));
        // Nothing was sent for them and their items are as they were; the ordinary link in the same batch was identified.
        Assert.Equal([FilmItemId], _jellyfin.Applied.Select(applied => applied.ItemId));
        Assert.True(JsonNode.DeepEquals(continentalBefore, Movie(ContinentalId)));
        Assert.True(JsonNode.DeepEquals(polanskiBefore, Movie(PolanskiId)));
        Assert.Equal(LibraryLinkWriteState.InLibrary, links[normal.Id].WriteState);
        // Refused, not sent: no audit row of an identification for them.
        Assert.DoesNotContain(
            (await _factory.AuditAsync(cancellationToken)),
            row => row.Action == AuditAction.LibraryItemIdentified && row.TargetId == continental.Id || row.Action == AuditAction.LibraryItemIdentified && row.TargetId == polanski.Id);
        // The sync left both refusals as they were, and they are still in the not identified list with their sentences.
        Assert.Equal(
            new[] { continental.Id, polanski.Id }.Order(),
            listed.Items.Select(row => row.LinkId).Order());
        Assert.All(listed.Items, row => Assert.Equal(LibraryLinkWriteState.NotWritten, row.WriteState));
        Assert.All(listed.Items, row => Assert.StartsWith("Jellyfin holds IMDb id", row.WriteError, StringComparison.Ordinal));
    }

    [Fact]
    public async Task CatchUp_refuses_a_polanski_type_film_whose_row_has_no_imdb_id_with_the_uncheckable_sentence()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        const string PolanskiId = "e1e1e1e1e1e1e1e1e1e1e1e1e1e1e1e1";
        // TMDb knows no IMDb id for the film the link names, so there is nothing to compare the held one with.
        _jellyfin.Movies.Add(JellyfinSamples.MovieWith(
            PolanskiId, "Polanski+Skolimowski o Noże", null, 2003, $"{JellyfinSamples.MoviesRoot}/Polanski.mkv", imdbId: "tt1163854", tmdbId: "11503"));
        _jellyfin.AddFilm("11503", "Another Film", 1962);
        await SyncLibraryAsync(cancellationToken);
        var work = await _factory.WorkOfItemAsync(PolanskiId, cancellationToken);
        var link = await _links.Create(_jellyfinIntegration, PolanskiId, work, MediaIdentifierScheme.Tmdb, "11503", writeState: LibraryLinkWriteState.Written, setBy: _owner.User);
        _factory.Time.Advance(TimeSpan.FromMinutes(1));

        //Act
        await _factory.PostHttpRequest(CatchUpUrl, _owner.AccessToken);
        await _factory.IdentifyQueuedLinksAsync(cancellationToken);

        //Assert
        var stored = (await _factory.LinksAsync(cancellationToken)).Single(row => row.Id == link.Id);
        Assert.Equal(
            (LibraryLinkWriteState.NotWritten,
                "Jellyfin holds IMDb id tt1163854 for this item and TMDb gives no IMDb id for this link to compare it with, so the app doesn't replace a library's own id on its own. If the link is right, press Retry to identify it anyway; otherwise change the link."),
            (stored.WriteState, TestTexts.En(stored.WriteError)));
        Assert.Empty(_jellyfin.Applied);
    }

    [Fact]
    public async Task CatchUp_in_a_library_that_saves_beside_the_media_leaves_the_links_not_written_with_the_reason_and_applies_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var works = await SyncLibraryAsync(cancellationToken);
        await _links.Create(_jellyfinIntegration, FilmItemId, works[FilmItemId], MediaIdentifierScheme.Tmdb, FilmId, writeState: LibraryLinkWriteState.Written, setBy: _owner.User);
        // The library of the cartoons is set to save artwork and metadata beside the media: Identify would write files.
        _jellyfin.LibraryOptionsOf(JellyfinSamples.CartoonsRoot)["SaveLocalMetadata"] = true;
        _factory.Time.Advance(TimeSpan.FromMinutes(1));
        var sent = _factory.JellyfinHttp.Requests.Count;

        //Act
        await _factory.PostHttpRequest(CatchUpUrl, _owner.AccessToken);
        await _factory.IdentifyQueuedLinksAsync(cancellationToken);

        //Assert
        var link = await _factory.SingleLinkAsync(cancellationToken);
        Assert.Equal(
            (LibraryLinkWriteState.NotWritten,
                "Jellyfin's library “Мульфільми” saves artwork and metadata next to the media, so identifying this item could change files in your media folders; the app left it alone. Switch that off in the library's settings in Jellyfin, then retry.",
                (DateTime?)null),
            (link.WriteState, TestTexts.En(link.WriteError), link.IdentifiedUtc));
        // Read first, then nothing: no search, no Apply, and the files and folders were never touched.
        Assert.Empty(_jellyfin.RemoteSearches);
        Assert.Empty(_jellyfin.Applied);
        Assert.DoesNotContain(_factory.JellyfinHttp.Requests.Skip(sent), request => request.Method == HttpMethod.Post);
    }

    [Fact]
    public async Task CatchUp_counts_no_episode_link_whose_imdb_id_is_still_to_be_asked_while_no_catalog_is_connected()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var works = await SyncLibraryAsync(cancellationToken);
        await _factory.WithDbAsync(db => db.IntegrationConnections
            .Where(integration => integration.Kind == IntegrationKind.Metadata)
            .ExecuteUpdateAsync(setters => setters.SetProperty(integration => integration.IsEnabled, false), cancellationToken));
        // TMDb would have to be asked for this one's IMDb id; the other holds it already (not written: its item held another).
        await _links.Create(
            _jellyfinIntegration, EpisodeItemId, works[EpisodeItemId], MediaIdentifierScheme.TmdbTv, ShowId, LibraryLinkTarget.ShowEpisode,
            configure: row => (row.SeasonNumber, row.EpisodeNumber) = (1, 1));
        await _links.Create(
            _jellyfinIntegration, OtherFilmItemId, works[OtherFilmItemId], MediaIdentifierScheme.TmdbTv, ShowId, LibraryLinkTarget.ShowEpisode,
            writeState: LibraryLinkWriteState.NotWritten, configure: row => (row.SeasonNumber, row.EpisodeNumber, row.EpisodeImdbId) = (1, 1, EpisodeImdbId));

        //Act
        var response = await _factory.GetHttpRequest(CatchUpUrl, _owner.AccessToken);

        //Assert
        // Without a catalog the first one's identification could only fail: it waits for one.
        Assert.Equal(new LibraryLinkCatchUpResponse(1, null), await response.ReadAsync<LibraryLinkCatchUpResponse>(cancellationToken));
        Assert.Empty(_factory.TmdbHttp.Requests);
    }

    [Fact]
    public async Task CatchUp_of_an_episode_tmdb_gives_no_imdb_id_keeps_it_in_the_app_saying_why_and_counts_it_no_more()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var works = await SyncLibraryAsync(cancellationToken);
        await _links.Create(
            _jellyfinIntegration, EpisodeItemId, works[EpisodeItemId], MediaIdentifierScheme.TmdbTv, ShowId, LibraryLinkTarget.ShowEpisode,
            configure: row => (row.SeasonNumber, row.EpisodeNumber) = (1, 2));

        //Act
        var response = await _factory.PostHttpRequest(CatchUpUrl, _owner.AccessToken);
        await _factory.IdentifyQueuedLinksAsync(cancellationToken);
        var after = await _factory.GetHttpRequest(CatchUpUrl, _owner.AccessToken);

        //Assert
        Assert.Equal(1, (await response.ReadAsync<LibraryLinkCatchUpResponse>(cancellationToken)).ToIdentify);
        var link = Assert.Single(await _factory.LinksAsync(cancellationToken));
        Assert.Equal(
            (LibraryLinkWriteState.KeptInApp, (string?)"TMDb knows no IMDb id of this episode, so its link stays in the app.", (string?)null),
            (link.WriteState, TestTexts.En(link.WriteError), link.EpisodeImdbId));
        Assert.DoesNotContain(_factory.JellyfinHttp.Requests, request => request.Method == HttpMethod.Post);
        // Asked once: a second click would keep it in the app all the same.
        Assert.Equal(new LibraryLinkCatchUpResponse(0, null), await after.ReadAsync<LibraryLinkCatchUpResponse>(cancellationToken));
    }

    [Fact]
    public async Task CatchUp_left_waiting_through_a_sync_is_still_the_writers()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var works = await SyncLibraryAsync(cancellationToken);
        Movie(FilmItemId)["ProviderIds"] = new JsonObject { ["Tmdb"] = FilmId };
        await _links.Create(_jellyfinIntegration, FilmItemId, works[FilmItemId], MediaIdentifierScheme.Tmdb, FilmId, writeState: LibraryLinkWriteState.Written);
        _factory.Time.Advance(TimeSpan.FromMinutes(1));
        await _factory.PostHttpRequest(CatchUpUrl, _owner.AccessToken);
        _factory.Time.Advance(TimeSpan.FromMinutes(1));

        //Act
        // Jellyfin reports the link's id: a sync would mark it in Jellyfin, were it not waiting for its identification.
        await _factory.SyncLibraryAsync(_jellyfinIntegration.Id, _owner.AccessToken, cancellationToken);
        var throughTheSync = Assert.Single(await _factory.LinksAsync(cancellationToken)).WriteState;
        await _factory.IdentifyQueuedLinksAsync(cancellationToken);

        //Assert
        Assert.Equal(LibraryLinkWriteState.Queued, throughTheSync);
        var link = Assert.Single(await _factory.LinksAsync(cancellationToken));
        Assert.Equal((LibraryLinkWriteState.Written, true), (link.WriteState, link.IdentifiedUtc is not null));
    }

    [Fact]
    public async Task CatchUp_with_nothing_to_do_is_202_with_nothing_queued_and_wakes_no_writer()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var works = await SyncLibraryAsync(cancellationToken);
        await _links.Create(
            _jellyfinIntegration, FilmItemId, works[FilmItemId], MediaIdentifierScheme.Tmdb, FilmId, writeState: LibraryLinkWriteState.InLibrary,
            configure: row => row.IdentifiedUtc = _factory.Time.GetUtcNow().UtcDateTime);

        //Act
        var response = await _factory.PostHttpRequest(CatchUpUrl, _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal(new LibraryLinkCatchUpResponse(0, null), await response.ReadAsync<LibraryLinkCatchUpResponse>(cancellationToken));
        Assert.Equal(LibraryLinkWriteState.InLibrary, Assert.Single(await _factory.LinksAsync(cancellationToken)).WriteState);
        Assert.False(_factory.Resolve<LibraryLinkWriteQueue>().Reader.TryPeek(out _));
    }

    [Fact]
    public async Task Batch_of_a_repair_click_lists_its_links_by_work_with_their_states()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var works = await SyncLibraryAsync(cancellationToken);
        await _links.Create(_jellyfinIntegration, FilmItemId, works[FilmItemId], MediaIdentifierScheme.Tmdb, FilmId, writeState: LibraryLinkWriteState.Written);
        await _links.Create(_jellyfinIntegration, PenguinsId, works[PenguinsId], MediaIdentifierScheme.TmdbTv, JellyfinSamples.PenguinsTmdbTvId);
        _factory.Time.Advance(TimeSpan.FromMinutes(1));
        var started = await (await _factory.PostHttpRequest(CatchUpUrl, _owner.AccessToken)).ReadAsync<LibraryLinkCatchUpResponse>(cancellationToken);

        //Act
        var response = await _factory.GetHttpRequest($"/api/library/tmdb/batches/{started.Progress!.BatchId}", _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var batch = await response.ReadAsync<LibraryLinkBatchResponse>(cancellationToken);
        Assert.Equal(started.Progress.BatchId, batch.BatchId);
        Assert.Equal(
            new[] { works[FilmItemId], works[PenguinsId] }.Order(),
            batch.Items.Select(item => item.WorkId).Order());
        Assert.All(batch.Items.SelectMany(item => item.Links), row => Assert.Equal(LibraryLinkWriteState.Queued, row.WriteState));
    }

    [Fact]
    public async Task CatchUp_without_a_token_is_401()
    {
        //Arrange

        //Act
        var read = await _factory.GetHttpRequest(CatchUpUrl);
        var start = await _factory.PostHttpRequest(CatchUpUrl);

        //Assert
        Assert.Equal((HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized), (read.StatusCode, start.StatusCode));
    }

    [Fact]
    public async Task CatchUp_by_a_member_is_403_and_queues_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var works = await SyncLibraryAsync(cancellationToken);
        await _links.Create(_jellyfinIntegration, FilmItemId, works[FilmItemId], MediaIdentifierScheme.Tmdb, FilmId, writeState: LibraryLinkWriteState.Written);

        //Act
        var read = await _factory.GetHttpRequest(CatchUpUrl, _member.AccessToken);
        var start = await _factory.PostHttpRequest(CatchUpUrl, _member.AccessToken);

        //Assert
        Assert.Equal((HttpStatusCode.Forbidden, HttpStatusCode.Forbidden), (read.StatusCode, start.StatusCode));
        Assert.Equal(LibraryLinkWriteState.Written, Assert.Single(await _factory.LinksAsync(cancellationToken)).WriteState);
        Assert.False(_factory.Resolve<LibraryLinkWriteQueue>().Reader.TryPeek(out _));
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    // Two shorts, a documentary episode Jellyfin lists as a film and the Penguins series with one episode, none with ids,
    // synced, with TMDb connected: each item's work by its id. Every item sits under one of the library's roots, as an
    // Identify only ever reaches an item whose library it can find (fail closed otherwise).
    private async Task<Dictionary<string, Guid>> SyncLibraryAsync(CancellationToken cancellationToken)
    {
        _jellyfinIntegration = await _integrations.Create("jellyfin", "Living room", apiKey: ApiKey);
        _jellyfin.Movies.Add(JellyfinSamples.MovieWith(FilmItemId, "Landing Stripling", null, 1962, $"{JellyfinSamples.CartoonsRoot}/Tom & Jerry/120.mkv"));
        _jellyfin.Movies.Add(JellyfinSamples.MovieWith(OtherFilmItemId, "Calypso Cat", null, 1962, $"{JellyfinSamples.CartoonsRoot}/Tom & Jerry/121.mkv"));
        _jellyfin.Movies.Add(JellyfinSamples.MovieWith(
            EpisodeItemId, "Greatest Events of World War II in Colour.S01E01.Blitzkrieg", null, 2019, $"{JellyfinSamples.DocumentaryRoot}/WWII/S01E01.mkv"));
        _jellyfin.Series.Add(JellyfinSamples.SeriesWith(PenguinsId, "The Penguins Of Madagascar", 2008, $"{JellyfinSamples.TvShowsRoot}/Penguins"));
        _jellyfin.Episodes.Add(JellyfinSamples.EpisodeWith("f5f5f5f5f5f5f5f5f5f5f5f5f5f5f5f5", "Gone in a Flash", PenguinsId, 1, 1, $"{JellyfinSamples.TvShowsRoot}/Penguins/S01E01.mkv"));
        await _factory.SyncLibraryAsync(_jellyfinIntegration.Id, _owner.AccessToken, cancellationToken);
        await _integrations.Create("tmdb", "TMDb", FakeTmdbServer.BaseUrl, apiKey: FakeTmdbServer.Token);

        var works = new Dictionary<string, Guid>(StringComparer.Ordinal);
        foreach (var itemId in (string[])[FilmItemId, OtherFilmItemId, EpisodeItemId, PenguinsId])
            works[itemId] = await _factory.WorkOfItemAsync(itemId, cancellationToken);
        return works;
    }

    // The audit row of an Apply that was sent for the link, at the given instant.
    private Task AppliedAuditRowAsync(LibraryItemLinkEntity link, DateTime createdUtc) =>
        new AuditEventSeeder(_factory.Scope).CreateForLink(AuditAction.LibraryItemIdentified, link, "Landing Stripling", [], _owner.User, createdUtc);

    private JsonObject Movie(string itemId) => _jellyfin.Movies.Single(movie => (string?)movie["Id"] == itemId);

    // A 32-character item id for the n-th extra item of a test.
    private static string ItemId(int number) => number.ToString("x32", System.Globalization.CultureInfo.InvariantCulture);
}
