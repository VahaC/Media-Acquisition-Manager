using System.Net;
using System.Text.Json.Nodes;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Api.Services.LibraryLinks;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Infrastructure.Persistence;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Data;
using MediaAcquisitionManager.TestInfrastructure.Files;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using MediaAcquisitionManager.TestInfrastructure.Texts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MediaAcquisitionManager.Api.Tests.LibraryLinks;

// MAM-34 (0.2.1, ADR 0026) — the dialog's Save (PUT /api/library/works/{id}/tmdb): the link and its audit row first, with
// the ids the library held, the link saved Queued and the request answered at once — a series' Identify took 159 s live,
// which no request waits for; the background writer then identifies the item the way Jellyfin's own Identify button does
// (read, search by the id in the item's kind, Apply once with replaceAllImages, read again), records the Apply that left the
// app and settles the link: Written only when the item read again shows the match, NotWritten with the sentence otherwise.
// A film's id searches the film search and a show's the series search; a show linked to a film item is an episode of it,
// identified by its own IMDb id when TMDb gives one (episode class of its own: LibraryLinkEpisodeHttpTests); a library type
// that can't identify, or one switched off (R12), keeps the link without calling it. Per item (R17), and a save that changes
// nothing records and queues nothing. The dialog's own click is the owner's word: it replaces the ids Jellyfin holds.
// The replace, the removal, Retry, the background writer and the guards have classes of their own.
public sealed class LibraryLinkSetHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/library";
    private const string ApiKey = "0123456789abcdef0123456789abcdef";
    private const string ShortId = JellyfinSamples.LandingStriplingId;
    private const string OtherShortId = "b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2";
    private const string PlanetEarthEpisodeId = "c3c3c3c3c3c3c3c3c3c3c3c3c3c3c3c3";
    private const string PenguinsId = "d4d4d4d4d4d4d4d4d4d4d4d4d4d4d4d4";
    private const string PenguinsEpisodeId = "e5e5e5e5e5e5e5e5e5e5e5e5e5e5e5e5";
    private const string FilmId = JellyfinSamples.LandingStriplingTmdbId;
    private const string FilmImdbId = JellyfinSamples.LandingStriplingImdbId;
    private const string FilmTitle = "Посадкова смуга";
    private const string FilmName = "Landing Stripling";
    private const string ShowId = JellyfinSamples.PenguinsTmdbTvId;
    private const string ShowImdbId = "tt0892700";
    private const string PlanetEarthId = "1044";
    private const string WrongTmdbId = "100";
    private const string ShortPath = "/media/cartoons/Tom & Jerry/120. Landing Stripling (1962).mkv";
    private const long ShortInode = 2_001;
    private const long ShortSizeBytes = 52_428_800;
    private const string UnderWayError = "A write to the library is under way for this work; try again in a moment.";
    private const string NotConfirmed =
        "Jellyfin took the match, but the item read again doesn't show it (its name, year or id): it may be locked, or its metadata sources failed. Its own log says why; Retry checks again.";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private IIntegrationConnectionSeeder _integrations = null!;
    private ILibraryItemLinkSeeder _links = null!;
    private AuthorizedUser _owner = null!;
    private FakeJellyfinServer _jellyfin = null!;

    public LibraryLinkSetHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _integrations = new IntegrationConnectionSeeder(_factory.Scope);
        _links = new LibraryItemLinkSeeder(_factory.Scope);
        _owner = await new UserSeeder(_factory.Scope).CreateAuthorizedOwner("owner");
        _jellyfin = new FakeJellyfinServer { ApiKey = ApiKey };
        _jellyfin.Movies.Clear();
        // What Jellyfin's TMDb provider holds for the film and the show the owner links to.
        _jellyfin.AddFilm(FilmId, FilmName, 1962, FilmImdbId);
        _jellyfin.AddShow(ShowId, "The Penguins of Madagascar", 2008, ShowImdbId);
        _jellyfin.Arm(_factory.JellyfinHttp);
        var tmdb = FakeTmdbServer.Arm(_factory.TmdbHttp);
        tmdb.AddWork(new FakeTmdbWork(MediaWorkKind.Movie, int.Parse(FilmId), FilmTitle, "Landing Stripling", "1962-11-17", ImdbId: FilmImdbId, RuntimeMinutes: 6));
        tmdb.AddWork(new FakeTmdbWork(MediaWorkKind.Series, int.Parse(ShowId), "Пінгвіни з Мадагаскару", "The Penguins of Madagascar", "2008-11-28", ImdbId: ShowImdbId));
        tmdb.AddWork(new FakeTmdbWork(MediaWorkKind.Series, int.Parse(PlanetEarthId), "Планета Земля", "Planet Earth", "2006-03-05", ImdbId: "tt0795176"));
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Set_of_a_film_saves_the_link_with_its_audit_row_queues_it_and_calls_jellyfin_not_at_all()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var (_, workId) = await SyncShortAsync(tmdbId: null);
        var sent = _factory.JellyfinHttp.Requests.Count;
        var now = _factory.Time.GetUtcNow().UtcDateTime;

        //Act
        var response = await _factory.PutHttpRequest(LinkUrl(workId), _owner.AccessToken, new LibraryLinkRequest("Movie", FilmId));

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dialog = await response.ReadDialogAsync(cancellationToken);
        var item = Assert.Single(dialog.Items);
        // Queued at once: the dialog's poll follows it to Written or NotWritten.
        Assert.Equal(
            (ShortId, LibraryLinkTarget.Work, (MediaWorkKind?)MediaWorkKind.Movie, FilmId, FilmTitle, FilmImdbId, (LibraryLinkWriteState?)LibraryLinkWriteState.Queued, (string?)null, "owner", true),
            (item.SourceItemId, item.Target!.Value, item.CatalogKind, item.CatalogId, item.CatalogTitle, item.ImdbId, item.WriteState, item.WriteError, item.SetByUserName, item.CanWrite));
        Assert.Equal(((DateTime?)null, (string?)null), (item.IdentifiedUtc, item.EpisodeImdbId));
        Assert.Equal(FilmId, dialog.Work.Link!.CatalogId);

        var link = await _factory.SingleLinkAsync(cancellationToken);
        Assert.Equal(
            (ShortId, MediaWorkKind.Movie, (Guid?)workId, LibraryLinkTarget.Work, MediaIdentifierScheme.Tmdb, FilmId, (string?)null),
            (link.SourceItemId, link.ItemKind, link.MediaWorkId, link.Target, link.Scheme, link.Value, link.SupersededValue));
        Assert.Equal(
            (FilmImdbId, "TMDb", (Guid?)_owner.User.Id, "owner", now, (Guid?)null, LibraryLinkWriteState.Queued, (string?)null, (DateTime?)null),
            (link.ImdbId, link.ImdbSourceName, link.SetByUserId, link.SetByUserName, link.SetUtc, link.BatchId, link.WriteState, TestTexts.En(link.WriteError), link.IdentifiedUtc));

        // Only the link's own row, with the ids the library held (none) and the link's: nothing was sent to Jellyfin.
        var audit = await _factory.AuditAsync(cancellationToken);
        Assert.Equal(
            [(AuditAction.LibraryLinkSet, FilmName, "After Imdb tt0056164, After Tmdb 223021")],
            audit.Select(row => (row.Action, row.TargetName, row.Identifiers)));
        Assert.All(audit, row => Assert.Equal((AuditTargetKind.LibraryLink, (Guid?)link.Id, "owner"), (row.TargetKind, row.TargetId, row.ActorUserName)));
        Assert.Equal(sent, _factory.JellyfinHttp.Requests.Count);
        Assert.Empty(_jellyfin.RemoteSearches);
        Assert.Empty(_jellyfin.Applied);
        // The worker is woken for it.
        Assert.True(_factory.Resolve<LibraryLinkWriteQueue>().Reader.TryPeek(out _));
    }

    [Fact]
    public async Task Set_of_a_film_then_the_worker_identifies_it_through_remote_search_and_apply_and_the_dialog_shows_it_written()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var (_, workId) = await SyncShortAsync(tmdbId: null);
        var sent = _factory.JellyfinHttp.Requests.Count;
        var response = await _factory.PutHttpRequest(LinkUrl(workId), _owner.AccessToken, new LibraryLinkRequest("Movie", FilmId));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var before = Movie(ShortId).DeepClone().AsObject();
        _factory.Time.Advance(TimeSpan.FromSeconds(5));
        var identifiedAt = _factory.Time.GetUtcNow().UtcDateTime;

        //Act
        await _factory.IdentifyQueuedLinksAsync(cancellationToken);
        var polled = await _factory.GetHttpRequest(LinkUrl(workId), _owner.AccessToken);

        //Assert
        var item = Assert.Single((await polled.ReadDialogAsync(cancellationToken)).Items);
        Assert.Equal(
            ((LibraryLinkWriteState?)LibraryLinkWriteState.Written, (string?)null, (DateTime?)identifiedAt),
            (item.WriteState, item.WriteError, item.IdentifiedUtc));
        var link = await _factory.SingleLinkAsync(cancellationToken);
        Assert.Equal((LibraryLinkWriteState.Written, (string?)null, (DateTime?)identifiedAt, (Guid?)null), (link.WriteState, TestTexts.En(link.WriteError), link.IdentifiedUtc, link.BatchId));

        // The version gate, the administrator to read as, the item, its libraries, the search of the film's kind, Apply
        // once with the images replaced (Jellyfin's own button), and the item read again — and no POST of the item itself.
        var path = $"/Items/{ShortId}";
        Assert.Equal(
            [
                ("GET", "/System/Info", ""), ("GET", "/Users", ""), ("GET", path, $"?userId={JellyfinSamples.AdministratorUserId}"),
                ("GET", "/Library/VirtualFolders", ""), ("POST", "/Items/RemoteSearch/Movie", ""),
                ("POST", $"/Items/RemoteSearch/Apply/{ShortId}", "?replaceAllImages=true"),
                ("GET", path, $"?userId={JellyfinSamples.AdministratorUserId}"),
            ],
            _factory.JellyfinHttp.Requests.Skip(sent).Select(request => (request.Method.Method, request.Uri.AbsolutePath, request.Uri.Query)));
        var search = Assert.Single(_jellyfin.RemoteSearches);
        Assert.Equal("Movie", search.Endpoint);
        Assert.Equal(
            """{"SearchInfo":{"ProviderIds":{"Tmdb":"223021"}},"ItemId":"750eaa45-b083-6d08-e4ae-4b35750b93ca","SearchProviderName":"TheMovieDb"}""",
            search.Body);
        var applied = Assert.Single(_jellyfin.Applied);
        Assert.Equal(("true", ShortId), (applied.ReplaceAllImages, applied.ItemId));

        // Jellyfin's own result is on the item now: TMDb's name, year, ids, a new poster and a new Etag.
        var after = Movie(ShortId);
        Assert.Equal((FilmName, 1962), ((string?)after["Name"], (int?)after["ProductionYear"]));
        Assert.Equal(
            new Dictionary<string, string> { ["Tmdb"] = FilmId, ["Imdb"] = FilmImdbId },
            after["ProviderIds"]!.AsObject().ToDictionary(pair => pair.Key, pair => (string)pair.Value!));
        Assert.NotEqual((string?)before["Etag"], (string?)after["Etag"]);
        Assert.False(string.IsNullOrWhiteSpace((string?)after["ImageTags"]?["Primary"]));

        // The ticket's order: the link's row, then the Apply that left the app with the ids the item held and holds now —
        // and none of the rows of the earlier ids-only code.
        var audit = await _factory.AuditAsync(cancellationToken);
        Assert.Equal(
            [
                (AuditAction.LibraryLinkSet, FilmName, "After Imdb tt0056164, After Tmdb 223021"),
                (AuditAction.LibraryItemIdentified, FilmName, "After Imdb tt0056164, After Tmdb 223021"),
            ],
            audit.Select(row => (row.Action, row.TargetName, row.Identifiers)));
        Assert.All(audit, row => Assert.Equal((AuditTargetKind.LibraryLink, (Guid?)link.Id, "owner"), (row.TargetKind, row.TargetId, row.ActorUserName)));
    }

    [Fact]
    public async Task Set_of_a_film_whose_item_jellyfin_does_not_take_is_not_written_never_written_and_says_why()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var (_, workId) = await SyncShortAsync(tmdbId: null);
        _jellyfin.IgnoresApply = true;
        await _factory.PutHttpRequest(LinkUrl(workId), _owner.AccessToken, new LibraryLinkRequest("Movie", FilmId));

        //Act
        await _factory.IdentifyQueuedLinksAsync(cancellationToken);

        //Assert
        var link = await _factory.SingleLinkAsync(cancellationToken);
        Assert.Equal((LibraryLinkWriteState.NotWritten, NotConfirmed, (DateTime?)null), (link.WriteState, TestTexts.En(link.WriteError), link.IdentifiedUtc));
        // R10 — the Apply left the app, so it is recorded even though the item shows nothing of it.
        Assert.Equal([AuditAction.LibraryLinkSet, AuditAction.LibraryItemIdentified], await _factory.AuditActionsAsync(cancellationToken));
        Assert.Single(_jellyfin.Applied);
    }

    [Fact]
    public async Task Set_of_a_film_whose_metadata_jellyfin_only_half_takes_is_not_written()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var (_, workId) = await SyncShortAsync(tmdbId: null);
        // The ids went in, TMDb failed after the images were deleted: the item's name already equals TMDb's.
        _jellyfin.AppliesIdsOnly = true;
        await _factory.PutHttpRequest(LinkUrl(workId), _owner.AccessToken, new LibraryLinkRequest("Movie", FilmId));

        //Act
        await _factory.IdentifyQueuedLinksAsync(cancellationToken);

        //Assert
        var link = await _factory.SingleLinkAsync(cancellationToken);
        Assert.Equal((LibraryLinkWriteState.NotWritten, NotConfirmed, (DateTime?)null), (link.WriteState, TestTexts.En(link.WriteError), link.IdentifiedUtc));
        Assert.Empty(Movie(ShortId)["ImageTags"]!.AsObject());
        Assert.Equal([AuditAction.LibraryLinkSet, AuditAction.LibraryItemIdentified], await _factory.AuditActionsAsync(cancellationToken));
    }

    [Fact]
    public async Task Set_of_a_show_on_a_series_is_queued_and_the_request_never_waits_for_the_identify()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var jellyfin = await CreateJellyfinAsync();
        _jellyfin.Series.Add(JellyfinSamples.SeriesWith(PenguinsId, "The Penguins Of Madagascar", 2008, $"{JellyfinSamples.TvShowsRoot}/Penguins"));
        _jellyfin.Episodes.Add(JellyfinSamples.EpisodeWith(PenguinsEpisodeId, "Gone in a Flash", PenguinsId, 1, 1, $"{JellyfinSamples.TvShowsRoot}/Penguins/S01E01.mkv"));
        await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);
        await ConnectTmdbAsync();
        var seriesWorkId = await _factory.WorkOfItemAsync(PenguinsId, cancellationToken);
        // An Apply of a series runs for minutes in Jellyfin: were the request to wait for it, this would take as long.
        _jellyfin.ApplyDelay = TimeSpan.FromSeconds(30);
        var sent = _factory.JellyfinHttp.Requests.Count;

        //Act
        var response = await _factory.PutHttpRequest(LinkUrl(seriesWorkId), _owner.AccessToken, new LibraryLinkRequest("Series", ShowId));

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var item = Assert.Single((await response.ReadDialogAsync(cancellationToken)).Items);
        Assert.Equal(
            ((MediaWorkKind?)MediaWorkKind.Series, ShowId, (LibraryLinkWriteState?)LibraryLinkWriteState.Queued, true, (string?)null),
            (item.CatalogKind, item.CatalogId, item.WriteState, item.CanWrite, item.CannotWriteReason));
        Assert.Equal(sent, _factory.JellyfinHttp.Requests.Count);
        Assert.Empty(_jellyfin.Applied);
    }

    [Fact]
    public async Task Set_of_a_show_on_a_series_is_identified_by_the_worker_through_the_series_search_with_the_shows_tmdb_id_and_the_next_sync_finds_it()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var jellyfin = await CreateJellyfinAsync();
        // Jellyfin gave the series a wrong show id: the link replaces it, in the app (R6) and in Jellyfin.
        _jellyfin.Series.Add(JellyfinSamples.SeriesWith(PenguinsId, "The Penguins Of Madagascar", 2008, $"{JellyfinSamples.TvShowsRoot}/Penguins", tmdbId: WrongTmdbId));
        _jellyfin.Episodes.Add(JellyfinSamples.EpisodeWith(PenguinsEpisodeId, "Gone in a Flash", PenguinsId, 1, 1, $"{JellyfinSamples.TvShowsRoot}/Penguins/S01E01.mkv"));
        await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);
        await ConnectTmdbAsync();
        var seriesWorkId = await _factory.WorkOfItemAsync(PenguinsId, cancellationToken);
        var response = await _factory.PutHttpRequest(LinkUrl(seriesWorkId), _owner.AccessToken, new LibraryLinkRequest("Series", ShowId));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var now = _factory.Time.GetUtcNow().UtcDateTime;

        //Act
        await _factory.IdentifyQueuedLinksAsync(cancellationToken);

        //Assert
        var link = await _factory.SingleLinkAsync(cancellationToken);
        Assert.Equal(
            (MediaWorkKind.Series, LibraryLinkTarget.Work, MediaIdentifierScheme.TmdbTv, ShowId, (string?)WrongTmdbId, ShowImdbId, LibraryLinkWriteState.Written, (DateTime?)now),
            (link.ItemKind, link.Target, link.Scheme, link.Value, link.SupersededValue, link.ImdbId, link.WriteState, link.IdentifiedUtc));
        // The series search with the show's number under Jellyfin's Tmdb key — never the film search, which would answer
        // whatever film has that number (recorded live).
        var search = Assert.Single(_jellyfin.RemoteSearches);
        Assert.Equal("Series", search.Endpoint);
        Assert.Contains("\"ProviderIds\":{\"Tmdb\":\"7869\"}", search.Body, StringComparison.Ordinal);
        Assert.Equal("""{"Tmdb":"7869","Imdb":"tt0892700"}""", _jellyfin.Series.Single()["ProviderIds"]!.ToJsonString());
        Assert.Equal(
            [
                (AuditAction.LibraryLinkSet, "Before TmdbTv 100, After Imdb tt0892700, After TmdbTv 7869"),
                (AuditAction.LibraryItemIdentified, "Before TmdbTv 100, After Imdb tt0892700, After TmdbTv 7869"),
            ],
            (await _factory.AuditAsync(cancellationToken)).Select(row => (row.Action, row.Identifiers)));

        // The next sync reads the show's id as Jellyfin's own (R5): in Jellyfin, on the work it was on, still identified.
        _factory.Time.Advance(TimeSpan.FromMinutes(1));
        await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);
        var synced = await _factory.SingleLinkAsync(cancellationToken);
        Assert.Equal((LibraryLinkWriteState.InLibrary, (Guid?)seriesWorkId, (DateTime?)now), (synced.WriteState, synced.MediaWorkId, synced.IdentifiedUtc));
    }

    [Fact]
    public async Task Set_of_a_show_on_a_film_item_is_an_episode_link_kept_in_the_app_that_replaces_the_wrong_film_id_and_calls_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var jellyfin = await CreateJellyfinAsync();
        _jellyfin.Movies.Add(JellyfinSamples.MovieWith(
            PlanetEarthEpisodeId, "Planet_Earth_E08 - Jungles", null, 2006, $"{JellyfinSamples.DocumentaryRoot}/Planet.Earth/E08.mkv", tmdbId: WrongTmdbId));
        await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);
        await ConnectTmdbAsync();
        var workId = await _factory.WorkOfItemAsync(PlanetEarthEpisodeId, cancellationToken);
        var sent = _factory.JellyfinHttp.Requests.Count;

        //Act
        var response = await _factory.PutHttpRequest(
            LinkUrl(workId), _owner.AccessToken, new LibraryLinkRequest("Series", PlanetEarthId, SeasonNumber: "1", EpisodeNumber: "8"));
        await _factory.IdentifyQueuedLinksAsync(cancellationToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var item = Assert.Single((await response.ReadDialogAsync(cancellationToken)).Items);
        Assert.Equal(
            ((LibraryLinkTarget?)LibraryLinkTarget.ShowEpisode, (int?)1, (int?)8, (LibraryLinkWriteState?)LibraryLinkWriteState.KeptInApp),
            (item.Target, item.SeasonNumber, item.EpisodeNumber, item.WriteState));
        var link = await _factory.SingleLinkAsync(cancellationToken);
        // R7 — the film id Jellyfin wrongly gave the episode gives way in the app; TMDb gives this episode no IMDb id, so
        // there is nothing to identify it by and nothing is ever sent.
        Assert.Equal(
            (MediaIdentifierScheme.TmdbTv, PlanetEarthId, (string?)WrongTmdbId, LibraryLinkWriteState.KeptInApp),
            (link.Scheme, link.Value, link.SupersededValue, link.WriteState));
        Assert.Equal(sent, _factory.JellyfinHttp.Requests.Count);
        Assert.Equal([AuditAction.LibraryLinkSet], await _factory.AuditActionsAsync(cancellationToken));
    }

    [Fact]
    public async Task Set_with_a_library_type_that_cannot_identify_keeps_the_link_in_the_app_and_says_why()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // The fake type is a media library without the identify capability (ADR 0005).
        var library = await _integrations.Create(name: "Old library");
        var work = await new MediaWorkSeeder(_factory.Scope).CreateMovie(library, "Landing Stripling", 1962);
        await ConnectTmdbAsync();

        //Act
        var response = await _factory.PutHttpRequest(LinkUrl(work.Id), _owner.AccessToken, new LibraryLinkRequest("Movie", FilmId));

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var item = Assert.Single((await response.ReadDialogAsync(cancellationToken)).Items);
        Assert.Equal(
            ((LibraryLinkWriteState?)LibraryLinkWriteState.KeptInApp, false, "“Old library” can't identify items, so a link stays in the app."),
            (item.WriteState, item.CanWrite, item.CannotWriteReason));
        Assert.Equal(LibraryLinkWriteState.KeptInApp, (await _factory.SingleLinkAsync(cancellationToken)).WriteState);
        Assert.Empty(_factory.FakeAdapter.Calls);
        Assert.Equal([AuditAction.LibraryLinkSet], await _factory.AuditActionsAsync(cancellationToken));
    }

    [Fact]
    public async Task Set_while_jellyfin_is_switched_off_saves_the_link_not_written_with_the_sentence_and_calls_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var (jellyfin, workId) = await SyncShortAsync(tmdbId: null);
        await SwitchOffAsync(jellyfin.Id);
        var sent = _factory.JellyfinHttp.Requests.Count;

        //Act
        var response = await _factory.PutHttpRequest(LinkUrl(workId), _owner.AccessToken, new LibraryLinkRequest("Movie", FilmId));

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        const string switchedOff = "“Living room” is switched off. Switch it on under Integrations, then retry.";
        var item = Assert.Single((await response.ReadDialogAsync(cancellationToken)).Items);
        // MAM-47 — each sentence with its key: the dialog compares the kinds of reason, not the words.
        Assert.Equal(
            ((LibraryLinkWriteState?)LibraryLinkWriteState.NotWritten, switchedOff, "library-links.write.switched-off", false, switchedOff, "library-links.write.switched-off"),
            (item.WriteState, item.WriteError, item.WriteErrorCode, item.CanWrite, item.CannotWriteReason, item.CannotWriteReasonCode));
        Assert.Equal(sent, _factory.JellyfinHttp.Requests.Count);
        Assert.False(_factory.Resolve<LibraryLinkWriteQueue>().Reader.TryPeek(out _));
    }

    [Fact]
    public async Task Set_of_an_item_jellyfin_holds_other_ids_for_replaces_them_when_set_by_hand()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // Jellyfin matched the file to another film: its Tmdb and Imdb ids are another film's.
        var (_, workId) = await SyncShortAsync(tmdbId: WrongTmdbId, imdbId: "tt9999999");

        //Act
        var response = await _factory.PutHttpRequest(LinkUrl(workId), _owner.AccessToken, new LibraryLinkRequest("Movie", FilmId));
        await _factory.IdentifyQueuedLinksAsync(cancellationToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // R6 — the library's wrong id is the one the link replaces until Jellyfin confirms it.
        var link = await _factory.SingleLinkAsync(cancellationToken);
        Assert.Equal((LibraryLinkWriteState.Written, (string?)WrongTmdbId, (Guid?)null), (link.WriteState, link.SupersededValue, link.BatchId));
        // The owner's own click is his word: Identify replaced both ids, as Jellyfin's own button does.
        Assert.Equal(
            new Dictionary<string, string> { ["Tmdb"] = FilmId, ["Imdb"] = FilmImdbId },
            Movie(ShortId)["ProviderIds"]!.AsObject().ToDictionary(pair => pair.Key, pair => (string)pair.Value!));
        Assert.Equal(
            [
                (AuditAction.LibraryLinkSet, "Before Imdb tt9999999, Before Tmdb 100, After Imdb tt0056164, After Tmdb 223021"),
                (AuditAction.LibraryItemIdentified, "Before Imdb tt9999999, Before Tmdb 100, After Imdb tt0056164, After Tmdb 223021"),
            ],
            (await _factory.AuditAsync(cancellationToken)).Select(row => (row.Action, row.Identifiers)));
    }

    [Fact]
    public async Task Set_that_changes_nothing_records_nothing_and_queues_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var (_, workId) = await SyncShortAsync(tmdbId: null);
        await _factory.PutHttpRequest(LinkUrl(workId), _owner.AccessToken, new LibraryLinkRequest("Movie", FilmId));
        await _factory.IdentifyQueuedLinksAsync(cancellationToken);
        var sent = _factory.JellyfinHttp.Requests.Count;
        var recorded = (await _factory.AuditAsync(cancellationToken)).Count;

        //Act
        var response = await _factory.PutHttpRequest(LinkUrl(workId), _owner.AccessToken, new LibraryLinkRequest("movie", $" {FilmId} "));

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(LibraryLinkWriteState.Written, Assert.Single((await response.ReadDialogAsync(cancellationToken)).Items).WriteState);
        Assert.Equal(sent, _factory.JellyfinHttp.Requests.Count);
        Assert.Equal(recorded, (await _factory.AuditAsync(cancellationToken)).Count);
        Assert.False(_factory.Resolve<LibraryLinkWriteQueue>().Reader.TryPeek(out _));
        Assert.Equal(LibraryLinkWriteState.Written, (await _factory.SingleLinkAsync(cancellationToken)).WriteState);
    }

    [Fact]
    public async Task Set_of_a_work_with_two_items_identifies_each_item_on_its_own()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var jellyfin = await CreateJellyfinAsync();
        _jellyfin.Movies.Add(Cartoon(ShortId, WrongTmdbId));
        _jellyfin.Movies.Add(Cartoon(OtherShortId, WrongTmdbId));
        await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);
        await ConnectTmdbAsync();
        var workId = await _factory.WorkOfItemAsync(ShortId, cancellationToken);

        //Act
        var response = await _factory.PutHttpRequest(LinkUrl(workId), _owner.AccessToken, new LibraryLinkRequest("Movie", FilmId));
        await _factory.IdentifyQueuedLinksAsync(cancellationToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var links = await _factory.LinksAsync(cancellationToken);
        Assert.Equal(2, links.Count);
        Assert.All(links, link => Assert.Equal(LibraryLinkWriteState.Written, link.WriteState));
        // Each item is applied on its own, and each has its own audit row.
        Assert.Equal([ShortId, OtherShortId], _jellyfin.Applied.Select(applied => applied.ItemId).Order(StringComparer.Ordinal));
        Assert.Equal(2, (await _factory.AuditActionsAsync(cancellationToken)).Count(action => action == AuditAction.LibraryItemIdentified));
        Assert.Equal((FilmId, FilmId), ((string?)Movie(ShortId)["ProviderIds"]!["Tmdb"], (string?)Movie(OtherShortId)["ProviderIds"]!["Tmdb"]));
    }

    [Fact]
    public async Task Set_of_one_of_two_items_a_wrong_id_grouped_links_only_that_one_and_the_next_sync_splits_them()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var jellyfin = await CreateJellyfinAsync();
        _jellyfin.Movies.Add(Cartoon(ShortId, WrongTmdbId));
        _jellyfin.Movies.Add(Cartoon(OtherShortId, WrongTmdbId));
        await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);
        await ConnectTmdbAsync();
        var workId = await _factory.WorkOfItemAsync(ShortId, cancellationToken);
        var groupedTogether = workId == await _factory.WorkOfItemAsync(OtherShortId, cancellationToken);

        //Act
        // R17 — only the item the owner ticked.
        var response = await _factory.PutHttpRequest(
            LinkUrl(workId), _owner.AccessToken, new LibraryLinkRequest("Movie", FilmId, SourceItemIds: [ShortId]));
        await _factory.IdentifyQueuedLinksAsync(cancellationToken);
        _factory.Time.Advance(TimeSpan.FromMinutes(1));
        await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);

        //Assert
        Assert.True(groupedTogether);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var link = await _factory.SingleLinkAsync(cancellationToken);
        Assert.Equal(ShortId, link.SourceItemId);
        // The item the owner left unticked is untouched: not searched for, not applied.
        Assert.Equal(WrongTmdbId, (string?)Movie(OtherShortId)["ProviderIds"]!["Tmdb"]);
        Assert.Equal([ShortId], _jellyfin.Applied.Select(applied => applied.ItemId));
        // Jellyfin now reports the linked item's id as its own, and the two are two works again.
        var linkedWorkId = await _factory.WorkOfItemAsync(ShortId, cancellationToken);
        Assert.NotEqual(linkedWorkId, await _factory.WorkOfItemAsync(OtherShortId, cancellationToken));
        var synced = await _factory.SingleLinkAsync(cancellationToken);
        Assert.Equal(((Guid?)linkedWorkId, LibraryLinkWriteState.InLibrary), (synced.MediaWorkId, synced.WriteState));
    }

    [Fact]
    public async Task Set_copies_the_identities_of_the_files_the_inspection_read()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await new AllowedRootSeeder(_factory.Scope).Allow("/media");
        _factory.Files.AddFile(ShortPath, TestMedia.File(ShortInode, ShortSizeBytes));
        var jellyfin = await CreateJellyfinAsync();
        _jellyfin.Movies.Add(JellyfinSamples.MovieWith(ShortId, "Landing Stripling", null, 1962, ShortPath));
        await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);
        await _factory.RunQueuedInspectionsAsync(cancellationToken);
        await ConnectTmdbAsync();
        var workId = await _factory.WorkOfItemAsync(ShortId, cancellationToken);

        //Act
        var response = await _factory.PutHttpRequest(LinkUrl(workId), _owner.AccessToken, new LibraryLinkRequest("Movie", FilmId));

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var link = await _factory.SingleLinkAsync(cancellationToken);
        await _factory.WithDbAsync(async db => Assert.Equal(
            [(TestMedia.DeviceId, ShortInode, ShortSizeBytes)],
            await db.LibraryItemLinkFiles
                .Where(file => file.LibraryItemLinkId == link.Id)
                .Select(file => new ValueTuple<long, long, long>(file.DeviceId, file.Inode, file.SizeBytes))
                .ToListAsync(cancellationToken)));
    }

    [Fact]
    public async Task Set_of_a_show_on_two_series_items_of_one_work_gives_neither_the_others_episodes_and_the_next_sync_gives_each_its_own()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await new AllowedRootSeeder(_factory.Scope).Allow("/media");
        const string OtherPenguinsId = "f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0";
        var firstFolder = $"{JellyfinSamples.TvShowsRoot}/Penguins";
        var otherFolder = $"{JellyfinSamples.TvShowsRoot}/Penguins (Season 1-3)";
        _factory.Files.AddFile($"{firstFolder}/S01E01.mkv", TestMedia.File(4_001, ShortSizeBytes));
        _factory.Files.AddFile($"{otherFolder}/S01E01.mkv", TestMedia.File(4_002, ShortSizeBytes));
        var jellyfin = await CreateJellyfinAsync();
        // The show in two folders, both identified: one series work of two series items.
        _jellyfin.Series.Add(JellyfinSamples.SeriesWith(PenguinsId, "The Penguins Of Madagascar", 2008, firstFolder, tmdbId: ShowId));
        _jellyfin.Series.Add(JellyfinSamples.SeriesWith(OtherPenguinsId, "The Penguins Of Madagascar (Season 1-3)", 2008, otherFolder, tmdbId: ShowId));
        _jellyfin.Episodes.Add(JellyfinSamples.EpisodeWith(PenguinsEpisodeId, "Gone in a Flash", PenguinsId, 1, 1, $"{firstFolder}/S01E01.mkv"));
        _jellyfin.Episodes.Add(JellyfinSamples.EpisodeWith("e0e0e0e0e0e0e0e0e0e0e0e0e0e0e0e0", "Gone in a Flash", OtherPenguinsId, 1, 1, $"{otherFolder}/S01E01.mkv"));
        await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);
        await _factory.RunQueuedInspectionsAsync(cancellationToken);
        await ConnectTmdbAsync();
        var seriesWorkId = await _factory.WorkOfItemAsync(PenguinsId, cancellationToken);

        //Act
        var response = await _factory.PutHttpRequest(LinkUrl(seriesWorkId), _owner.AccessToken, new LibraryLinkRequest("Series", ShowId));
        var afterSet = await InodesByItemAsync(cancellationToken);
        _factory.Time.Advance(TimeSpan.FromMinutes(1));
        await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(seriesWorkId, await _factory.WorkOfItemAsync(OtherPenguinsId, cancellationToken));
        // Which episode is whose isn't stored once both sit on one work: neither link gets the other's, nor its own yet.
        Assert.Equal([(PenguinsId, ""), (OtherPenguinsId, "")], afterSet);
        // The sync knows each episode's series item.
        Assert.Equal([(PenguinsId, "4001"), (OtherPenguinsId, "4002")], await InodesByItemAsync(cancellationToken));
    }

    [Fact]
    public async Task Set_queues_a_recommendation_run_for_the_changed_link()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var (_, workId) = await SyncShortAsync(tmdbId: null);

        //Act
        var response = await _factory.PutHttpRequest(LinkUrl(workId), _owner.AccessToken, new LibraryLinkRequest("Movie", FilmId));

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await _factory.WithDbAsync(async db =>
        {
            var run = await db.RecommendationRuns.AsNoTracking().SingleAsync(cancellationToken);
            Assert.Equal((RecommendationRunStatus.Queued, RecommendationRunTrigger.LinkChanged), (run.Status, run.Trigger));
        });
    }

    [Theory]
    [InlineData(null, FilmId, null, null, "Choose a TMDb film (Movie) or show (Series).")]
    [InlineData("Book", FilmId, null, null, "“Book” is not a TMDb kind. Use Movie or Series.")]
    [InlineData("Movie", null, null, null, "Give the TMDb id to link.")]
    [InlineData("Movie", FilmImdbId, null, null, "“tt0056164” is not a TMDb id.")]
    [InlineData("Series", PlanetEarthId, "first", null, "“first” is not a season number. Use 0 to 999.")]
    [InlineData("Series", PlanetEarthId, "1000", null, "“1000” is not a season number. Use 0 to 999.")]
    [InlineData("Series", PlanetEarthId, "1", "0", "“0” is not an episode number. Use 1 to 9999.")]
    [InlineData("Series", PlanetEarthId, null, "8", "An episode number needs a season number.")]
    [InlineData("Movie", FilmId, "1", null, "Season and episode numbers go only with a show linked to a film or to an episode.")]
    public async Task Set_with_a_request_the_rules_refuse_is_400_with_its_sentence_and_sends_nothing(
        string? catalogKind, string? catalogId, string? season, string? episode, string expected)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var (_, workId) = await SyncShortAsync(tmdbId: null);
        var sent = _factory.JellyfinHttp.Requests.Count;

        //Act
        var response = await _factory.PutHttpRequest(LinkUrl(workId), _owner.AccessToken, new LibraryLinkRequest(catalogKind, catalogId, season, episode));

        //Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(expected, await response.ReadErrorAsync(cancellationToken));
        await AssertNothingSavedAsync(sent, cancellationToken);
    }

    [Fact]
    public async Task Set_of_a_film_on_a_series_is_400_with_its_sentence()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var seriesWorkId = await SyncPenguinsAsync(cancellationToken);

        //Act
        var response = await _factory.PutHttpRequest(LinkUrl(seriesWorkId), _owner.AccessToken, new LibraryLinkRequest("Movie", FilmId));

        //Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("A show or an episode can be linked only to a TMDb show.", await response.ReadErrorAsync(cancellationToken));
        await AssertNothingSavedAsync(_factory.JellyfinHttp.Requests.Count, cancellationToken);
    }

    [Fact]
    public async Task Set_on_an_episode_of_a_series_is_400_saying_to_link_its_series()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await SyncPenguinsAsync(cancellationToken);
        var episodeWorkId = await _factory.WorkOfItemAsync(PenguinsEpisodeId, cancellationToken);

        //Act
        var response = await _factory.PutHttpRequest(LinkUrl(episodeWorkId), _owner.AccessToken, new LibraryLinkRequest("Series", ShowId));

        //Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Link its series instead: an episode is known by its series' ids.", await response.ReadErrorAsync(cancellationToken));
    }

    [Fact]
    public async Task Set_naming_an_item_the_work_does_not_hold_is_400_with_its_sentence()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var (_, workId) = await SyncShortAsync(tmdbId: null);

        //Act
        var response = await _factory.PutHttpRequest(
            LinkUrl(workId), _owner.AccessToken, new LibraryLinkRequest("Movie", FilmId, SourceItemIds: [ShortId, OtherShortId]));

        //Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal($"“{OtherShortId}” is not an item of this work.", await response.ReadErrorAsync(cancellationToken));
        await AssertNothingSavedAsync(_factory.JellyfinHttp.Requests.Count, cancellationToken);
    }

    [Fact]
    public async Task Set_on_an_unknown_work_is_a_bare_404()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await ConnectTmdbAsync();

        //Act
        var response = await _factory.PutHttpRequest(LinkUrl(Guid.NewGuid()), _owner.AccessToken, new LibraryLinkRequest("Movie", FilmId));

        //Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsStringAsync(cancellationToken));
    }

    [Fact]
    public async Task Set_of_an_id_tmdb_does_not_have_is_404_with_the_sentence()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var (_, workId) = await SyncShortAsync(tmdbId: null);
        var sent = _factory.JellyfinHttp.Requests.Count;

        //Act
        var response = await _factory.PutHttpRequest(LinkUrl(workId), _owner.AccessToken, new LibraryLinkRequest("Movie", "999999"));

        //Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("TMDb has no film 999999.", await response.ReadErrorAsync(cancellationToken));
        await AssertNothingSavedAsync(sent, cancellationToken);
    }

    [Fact]
    public async Task Set_without_a_catalog_is_409_saying_to_connect_one()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var jellyfin = await CreateJellyfinAsync();
        _jellyfin.Movies.Add(Cartoon(ShortId, null));
        await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);
        var workId = await _factory.WorkOfItemAsync(ShortId, cancellationToken);

        //Act
        var response = await _factory.PutHttpRequest(LinkUrl(workId), _owner.AccessToken, new LibraryLinkRequest("Movie", FilmId));

        //Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("Connect a metadata catalog under Integrations first.", await response.ReadErrorAsync(cancellationToken));
        await AssertNothingSavedAsync(_factory.JellyfinHttp.Requests.Count, cancellationToken);
    }

    [Fact]
    public async Task Set_of_a_work_no_library_reports_is_409_saying_there_is_nothing_to_link()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // A work no library reports an item of: no title, id or version of a library's item.
        var work = await new MediaWorkDataFactory(_factory.Scope.GetRequiredService<ApplicationDbContext>(), _factory.Time).Create();
        var sent = _factory.JellyfinHttp.Requests.Count;

        //Act
        var response = await _factory.PutHttpRequest(LinkUrl(work.Id), _owner.AccessToken, new LibraryLinkRequest("Movie", FilmId));

        //Assert
        // MAM-47 (ADR 0042) — the sentence with its key as the code; it had no test before.
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(
            ErrorBody.Json("No library reports this work, so there is nothing to link.", "library-links.set.no-items"),
            await response.Content.ReadAsStringAsync(cancellationToken));
        await AssertNothingSavedAsync(sent, cancellationToken);
    }

    [Fact]
    public async Task Set_when_tmdb_cannot_be_read_is_502_and_saves_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var (_, workId) = await SyncShortAsync(tmdbId: null);
        FakeTmdbServer.Arm(_factory.TmdbHttp).Intercept = _ => new HttpResponseMessage(HttpStatusCode.InternalServerError);
        var sent = _factory.JellyfinHttp.Requests.Count;

        //Act
        var response = await _factory.PutHttpRequest(LinkUrl(workId), _owner.AccessToken, new LibraryLinkRequest("Movie", FilmId));

        //Assert
        // The only 502 left: no library failure can fail the request any more, a library problem is the item's own sentence.
        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.NotNull(await response.ReadErrorAsync(cancellationToken));
        await AssertNothingSavedAsync(sent, cancellationToken);
    }

    [Fact]
    public async Task Set_while_an_identify_is_under_way_is_409_and_changes_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var (jellyfin, workId) = await SyncShortAsync(tmdbId: null);
        var writing = await _links.Create(jellyfin, ShortId, workId, MediaIdentifierScheme.Tmdb, "300", writeState: LibraryLinkWriteState.Writing);
        var sent = _factory.JellyfinHttp.Requests.Count;

        //Act
        var response = await _factory.PutHttpRequest(LinkUrl(workId), _owner.AccessToken, new LibraryLinkRequest("Movie", FilmId));

        //Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(UnderWayError, await response.ReadErrorAsync(cancellationToken));
        var link = await _factory.SingleLinkAsync(cancellationToken);
        Assert.Equal((writing.Id, "300", LibraryLinkWriteState.Writing), (link.Id, link.Value, link.WriteState));
        Assert.Equal(sent, _factory.JellyfinHttp.Requests.Count);
        Assert.Empty(await _factory.AuditAsync(cancellationToken));
    }

    [Fact]
    public async Task Set_racing_an_identify_that_claimed_the_item_first_is_409_and_leaves_that_identify_alone()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var (jellyfin, workId) = await SyncShortAsync(tmdbId: null);
        LibraryItemLinkEntity? racer = null;
        // R9 — the save has read the work and TMDb; another identification of the item is claimed before its transaction begins.
        _factory.TransactionHook.BeforeNextTransaction(
            _ => true,
            async _ => racer = await _links.Create(jellyfin, ShortId, workId, MediaIdentifierScheme.Tmdb, "300", writeState: LibraryLinkWriteState.Writing));
        var sent = _factory.JellyfinHttp.Requests.Count;

        //Act
        var response = await _factory.PutHttpRequest(LinkUrl(workId), _owner.AccessToken, new LibraryLinkRequest("Movie", FilmId));

        //Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(UnderWayError, await response.ReadErrorAsync(cancellationToken));
        var link = await _factory.SingleLinkAsync(cancellationToken);
        Assert.Equal((racer!.Id, "300", LibraryLinkWriteState.Writing), (link.Id, link.Value, link.WriteState));
        Assert.Equal(sent, _factory.JellyfinHttp.Requests.Count);
        Assert.Empty(await _factory.AuditAsync(cancellationToken));
    }

    [Fact]
    public async Task Set_of_a_work_a_sync_removed_meanwhile_is_a_bare_404_and_saves_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var (jellyfin, workId) = await SyncShortAsync(tmdbId: null);
        // R9 — the save has read the work and TMDb; a sync that no longer finds the short deletes its work first.
        _factory.TransactionHook.BeforeNextTransaction(
            _ => true,
            async _ =>
            {
                _jellyfin.Movies.Clear();
                await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);
            });

        //Act
        var response = await _factory.PutHttpRequest(LinkUrl(workId), _owner.AccessToken, new LibraryLinkRequest("Movie", FilmId));

        //Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsStringAsync(cancellationToken));
        await _factory.WithDbAsync(async db =>
        {
            Assert.False(await db.MediaWorks.AnyAsync(work => work.Id == workId, cancellationToken));
            Assert.Equal(0, await db.LibraryItemLinks.CountAsync(cancellationToken));
            Assert.Equal(0, await db.AuditEvents.CountAsync(cancellationToken));
        });
    }

    [Fact]
    public async Task Set_of_a_show_on_an_episode_no_series_holds_is_an_episode_link_kept_in_the_app()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var jellyfin = await CreateJellyfinAsync();
        _jellyfin.Episodes.Add(JellyfinSamples.EpisodeWith(PlanetEarthEpisodeId, "Tom & Jerry Kids 01", null, null, null, "/media/cartoons/Tom & Jerry Kids/01.mkv"));
        await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);
        await ConnectTmdbAsync();
        var workId = await _factory.WorkOfItemAsync(PlanetEarthEpisodeId, cancellationToken);
        var sent = _factory.JellyfinHttp.Requests.Count;

        //Act
        var response = await _factory.PutHttpRequest(LinkUrl(workId), _owner.AccessToken, new LibraryLinkRequest("Series", ShowId, SeasonNumber: "1"));

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var link = await _factory.SingleLinkAsync(cancellationToken);
        Assert.Equal(
            (MediaWorkKind.Episode, LibraryLinkTarget.ShowEpisode, ShowId, (int?)1, (int?)null, LibraryLinkWriteState.KeptInApp),
            (link.ItemKind, link.Target, link.Value, link.SeasonNumber, link.EpisodeNumber, link.WriteState));
        Assert.Equal(sent, _factory.JellyfinHttp.Requests.Count);
    }

    [Fact]
    public async Task Set_without_a_token_is_401()
    {
        //Arrange
        var (_, workId) = await SyncShortAsync(tmdbId: null);

        //Act
        var response = await _factory.PutHttpRequest(LinkUrl(workId), body: new LibraryLinkRequest("Movie", FilmId));

        //Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    private static string LinkUrl(Guid workId) => $"{BaseUrl}/works/{workId}/tmdb";

    private Task<IntegrationConnectionEntity> CreateJellyfinAsync() => _integrations.Create("jellyfin", "Living room", apiKey: ApiKey);

    // After the syncs, so a sync queues no recommendation run of its own.
    private Task<IntegrationConnectionEntity> ConnectTmdbAsync() =>
        _integrations.Create("tmdb", "TMDb", FakeTmdbServer.BaseUrl, apiKey: FakeTmdbServer.Token);

    // The Tom & Jerry short of the live check, synced, with TMDb connected.
    private async Task<(IntegrationConnectionEntity Jellyfin, Guid WorkId)> SyncShortAsync(string? tmdbId, string? imdbId = null)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var jellyfin = await CreateJellyfinAsync();
        _jellyfin.Movies.Add(Cartoon(ShortId, tmdbId, imdbId));
        await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);
        await ConnectTmdbAsync();
        return (jellyfin, await _factory.WorkOfItemAsync(ShortId, cancellationToken));
    }

    private async Task<Guid> SyncPenguinsAsync(CancellationToken cancellationToken)
    {
        var jellyfin = await CreateJellyfinAsync();
        _jellyfin.Series.Add(JellyfinSamples.SeriesWith(PenguinsId, "The Penguins Of Madagascar", 2008, $"{JellyfinSamples.TvShowsRoot}/Penguins"));
        _jellyfin.Episodes.Add(JellyfinSamples.EpisodeWith(PenguinsEpisodeId, "Gone in a Flash", PenguinsId, 1, 1, $"{JellyfinSamples.TvShowsRoot}/Penguins/S01E01.mkv"));
        await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);
        await ConnectTmdbAsync();
        return await _factory.WorkOfItemAsync(PenguinsId, cancellationToken);
    }

    private static JsonObject Cartoon(string itemId, string? tmdbId, string? imdbId = null) =>
        JellyfinSamples.MovieWith(itemId, "Landing Stripling", null, 1962, $"{JellyfinSamples.CartoonsRoot}/Tom & Jerry/{itemId}.mkv", imdbId, tmdbId);

    private JsonObject Movie(string itemId) => _jellyfin.Movies.Single(movie => (string?)movie["Id"] == itemId);

    private Task SwitchOffAsync(Guid integrationId) =>
        _factory.WithDbAsync(db => db.IntegrationConnections
            .Where(integration => integration.Id == integrationId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(integration => integration.IsEnabled, false)));

    // Each link's item with the inodes of the file identities it holds, joined by commas.
    private async Task<List<(string ItemId, string Inodes)>> InodesByItemAsync(CancellationToken cancellationToken)
    {
        var rows = new List<(string, string)>();
        await _factory.WithDbAsync(async db =>
        {
            var files = (await db.LibraryItemLinkFiles.AsNoTracking().ToListAsync(cancellationToken)).ToLookup(file => file.LibraryItemLinkId);
            rows.AddRange((await db.LibraryItemLinks.AsNoTracking().ToListAsync(cancellationToken))
                .OrderBy(link => link.SourceItemId, StringComparer.Ordinal)
                .Select(link => (link.SourceItemId, string.Join(",", files[link.Id].Select(file => file.Inode).Order()))));
        });
        return rows;
    }

    private async Task AssertNothingSavedAsync(int sent, CancellationToken cancellationToken)
    {
        Assert.Equal(sent, _factory.JellyfinHttp.Requests.Count);
        await _factory.WithDbAsync(async db =>
        {
            Assert.Equal(0, await db.LibraryItemLinks.CountAsync(cancellationToken));
            Assert.Equal(0, await db.AuditEvents.CountAsync(cancellationToken));
        });
    }
}
