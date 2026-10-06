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

// MAM-34 (0.2.1, ADR 0026; Vasyl's decision 3 of 2026-10-06) — an episode's link to its show on a film item (Jellyfin lists a
// documentary's episodes as movies): with its season and episode, the episode's own IMDb id TMDb gives is what the item is
// identified by — a film search through the Open Movie Database by that id, the one route recorded live (Blitzkrieg,
// tt9103946), never a TMDb id, which on a movie item names a film. Jellyfin's Identify then corrects the item's name,
// year and poster and keeps its overview (the Open Movie Database gives none), and drops the film id it held. Without both
// numbers, or with an episode TMDb gives no IMDb id, the link stays in the app and says why — until the same pick saved
// again finds TMDb giving one; an episode item no series holds keeps its link in the app. A sync judges the link by that
// IMDb id.
public sealed class LibraryLinkEpisodeHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/library";
    private const string ApiKey = "0123456789abcdef0123456789abcdef";
    private const string EpisodeItemId = "c3c3c3c3c3c3c3c3c3c3c3c3c3c3c3c3";
    private const string KidsItemId = "d4d4d4d4d4d4d4d4d4d4d4d4d4d4d4d4";
    private const string ShowId = TmdbSamples.TvEpisodeWwiiShowId;
    private const string EpisodeImdbId = TmdbSamples.TvEpisodeWwiiImdbId;
    private const string NoImdbShowId = "1044";
    private const string PlanetEarthJunglesImdbId = TmdbSamples.TvEpisodePlanetEarthImdbId;
    private const string WrongTmdbId = "100";
    private const string NoNumbersReason = "Without a season and an episode the app has no id of this episode to identify it by.";
    private const string NoEpisodeImdbReason = "TMDb knows no IMDb id of this episode, so its link stays in the app.";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private IIntegrationConnectionSeeder _integrations = null!;
    private AuthorizedUser _owner = null!;
    private FakeJellyfinServer _jellyfin = null!;
    private FakeTmdbServer _tmdb = null!;
    private IntegrationConnectionEntity _jellyfinIntegration = null!;

    public LibraryLinkEpisodeHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _integrations = new IntegrationConnectionSeeder(_factory.Scope);
        _owner = await new UserSeeder(_factory.Scope).CreateAuthorizedOwner("owner");
        _jellyfin = new FakeJellyfinServer { ApiKey = ApiKey };
        _jellyfin.Movies.Clear();
        // What the Open Movie Database holds for the episodes, as the live check saw it: an IMDb id only, no overview.
        _jellyfin.AddEpisodeMovie(EpisodeImdbId, "Blitzkrieg", 2019);
        _jellyfin.AddEpisodeMovie(PlanetEarthJunglesImdbId, "Jungles", 2006);
        _jellyfin.Arm(_factory.JellyfinHttp);
        _tmdb = FakeTmdbServer.Arm(_factory.TmdbHttp);
        _tmdb.AddWork(new FakeTmdbWork(MediaWorkKind.Series, int.Parse(ShowId), "Найвизначніші події Другої світової у кольорі", "Greatest Events of WWII in Colour"));
        // As TMDb answered it live (VendorSamples/Tmdb/tv-episode-wwii-s01e01.json).
        _tmdb.AddEpisode(new FakeTmdbEpisode(int.Parse(ShowId), 1, 1, "Серія 1", EpisodeImdbId, "2019-07-12", 50));
        _tmdb.AddWork(new FakeTmdbWork(MediaWorkKind.Series, int.Parse(NoImdbShowId), "Планета Земля", "Planet Earth", "2006-03-05", ImdbId: "tt0795176"));
        _tmdb.AddEpisode(new FakeTmdbEpisode(int.Parse(NoImdbShowId), 1, 8, "Джунглі"));
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Set_of_an_episode_on_a_film_item_is_identified_through_the_open_movie_database_by_the_episodes_imdb_id()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // Jellyfin gave the episode, which it lists as a movie, a film's id.
        var workId = await SyncEpisodeItemAsync(tmdbId: WrongTmdbId, imdbId: null);
        var before = Movie(EpisodeItemId).DeepClone().AsObject();
        var jellyfinSent = _factory.JellyfinHttp.Requests.Count;

        //Act
        var response = await _factory.PutHttpRequest(LinkUrl(workId), _owner.AccessToken, new LibraryLinkRequest("Series", ShowId, SeasonNumber: "1", EpisodeNumber: "1"));
        var queued = Assert.Single((await response.ReadDialogAsync(cancellationToken)).Items);
        var sentByTheRequest = _factory.JellyfinHttp.Requests.Count - jellyfinSent;
        await _factory.IdentifyQueuedLinksAsync(cancellationToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            ((LibraryLinkTarget?)LibraryLinkTarget.ShowEpisode, (LibraryLinkWriteState?)LibraryLinkWriteState.Queued, (string?)EpisodeImdbId, true, (string?)null),
            (queued.Target, queued.WriteState, queued.EpisodeImdbId, queued.CanWrite, queued.CannotWriteReason));
        Assert.Equal(0, sentByTheRequest);
        // TMDb asked for the episode with its external ids, once.
        Assert.Single(
            _factory.TmdbHttp.Requests,
            request => request.Uri.PathAndQuery == $"/3/tv/{ShowId}/season/1/episode/1?append_to_response=external_ids&language=uk-UA");

        // The film search, by the episode's own IMDb id through the Open Movie Database — never a TMDb id.
        var search = Assert.Single(_jellyfin.RemoteSearches);
        Assert.Equal("Movie", search.Endpoint);
        Assert.Equal(
            """{"SearchInfo":{"ProviderIds":{"Imdb":"tt9103946"}},"ItemId":"c3c3c3c3-c3c3-c3c3-c3c3-c3c3c3c3c3c3","SearchProviderName":"The Open Movie Database"}""",
            search.Body);
        Assert.Equal("true", Assert.Single(_jellyfin.Applied).ReplaceAllImages);

        // Jellyfin's Identify corrected the name and year and gave a new poster, dropped the film id the item held and left
        // its overview, which the Open Movie Database has none of.
        var after = Movie(EpisodeItemId);
        Assert.Equal(("Blitzkrieg", 2019), ((string?)after["Name"], (int?)after["ProductionYear"]));
        Assert.Equal(new Dictionary<string, string> { ["Imdb"] = EpisodeImdbId }, ProviderIdsOf(EpisodeItemId));
        Assert.Equal((string?)before["Overview"], (string?)after["Overview"]);
        Assert.False(string.IsNullOrWhiteSpace((string?)after["ImageTags"]?["Primary"]));

        var link = await _factory.SingleLinkAsync(cancellationToken);
        Assert.Equal(
            (EpisodeImdbId, (string?)WrongTmdbId, LibraryLinkWriteState.Written, (string?)null),
            (link.EpisodeImdbId, link.SupersededValue, link.WriteState, link.WriteError));
        Assert.NotNull(link.IdentifiedUtc);
        Assert.Equal(
            [
                (AuditAction.LibraryLinkSet, $"Before Tmdb {WrongTmdbId}, After TmdbTv {ShowId}"),
                (AuditAction.LibraryItemIdentified, $"Before Tmdb {WrongTmdbId}, After Imdb {EpisodeImdbId}"),
            ],
            (await _factory.AuditAsync(cancellationToken)).Select(row => (row.Action, row.Identifiers)));
    }

    [Fact]
    public async Task Identified_episode_link_no_longer_sees_the_film_id_apply_dropped_and_the_next_sync_finds_it_by_the_episodes_imdb_id()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var workId = await SyncEpisodeItemAsync(tmdbId: WrongTmdbId, imdbId: null);
        await SetAndIdentifyAsync(workId, new LibraryLinkRequest("Series", ShowId, SeasonNumber: "1", EpisodeNumber: "1"));
        // A sync reads the episode's IMDb id from Jellyfin, where the film id is gone: Identify dropped it.
        await _factory.SyncLibraryAsync(_jellyfinIntegration.Id, _owner.AccessToken, cancellationToken);

        //Act
        var byFilmId = await _factory.GetHttpRequest($"{BaseUrl}/works?search={WrongTmdbId}", _owner.AccessToken);
        var byEpisodeImdbId = await _factory.GetHttpRequest($"{BaseUrl}/works?search={EpisodeImdbId}", _owner.AccessToken);

        //Assert
        // R7 — what the app identifies an episode by is its IMDb id, never a TMDb one: the wrong film id is gone from Jellyfin
        // and gives way in the app whatever the link's state. The episode's IMDb id is Jellyfin's own now.
        Assert.Equal(0, (await byFilmId.ReadAsync<LibraryWorkListResponse>(cancellationToken)).TotalCount);
        var found = Assert.Single((await byEpisodeImdbId.ReadAsync<LibraryWorkListResponse>(cancellationToken)).Items);
        Assert.Equal(workId, found.Id);
        Assert.Null(found.Link!.ConflictingValue);
        var link = await _factory.SingleLinkAsync(cancellationToken);
        Assert.Equal(LibraryLinkWriteState.InLibrary, link.WriteState);
        // Judging never touches the identified time.
        Assert.NotNull(link.IdentifiedUtc);
    }

    [Fact]
    public async Task Set_of_an_episode_with_a_season_alone_keeps_the_link_in_the_app_says_why_and_asks_tmdb_for_no_episode()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var workId = await SyncEpisodeItemAsync(tmdbId: null, imdbId: null);
        var jellyfinSent = _factory.JellyfinHttp.Requests.Count;

        //Act
        var response = await _factory.PutHttpRequest(LinkUrl(workId), _owner.AccessToken, new LibraryLinkRequest("Series", ShowId, SeasonNumber: "1"));

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var item = Assert.Single((await response.ReadDialogAsync(cancellationToken)).Items);
        // The item is identified by its library; the link has no id of its own to identify it by.
        Assert.Equal(
            ((LibraryLinkWriteState?)LibraryLinkWriteState.KeptInApp, true, (string?)NoNumbersReason, (string?)null),
            (item.WriteState, item.CanWrite, item.CannotWriteReason, item.EpisodeImdbId));
        Assert.DoesNotContain(_factory.TmdbHttp.Requests, request => request.Uri.AbsolutePath.Contains("/episode/", StringComparison.Ordinal));
        Assert.Equal(jellyfinSent, _factory.JellyfinHttp.Requests.Count);
        Assert.Equal([(AuditAction.LibraryLinkSet, $"After TmdbTv {ShowId}")], (await _factory.AuditAsync(cancellationToken)).Select(row => (row.Action, row.Identifiers)));
    }

    [Fact]
    public async Task Set_of_an_episode_tmdb_gives_no_imdb_id_keeps_the_link_in_the_app_says_why_and_calls_jellyfin_not_at_all()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var workId = await SyncEpisodeItemAsync(tmdbId: null, imdbId: null);
        var jellyfinSent = _factory.JellyfinHttp.Requests.Count;

        //Act
        var response = await _factory.PutHttpRequest(LinkUrl(workId), _owner.AccessToken, new LibraryLinkRequest("Series", NoImdbShowId, SeasonNumber: "1", EpisodeNumber: "8"));
        await _factory.IdentifyQueuedLinksAsync(cancellationToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var item = Assert.Single((await response.ReadDialogAsync(cancellationToken)).Items);
        Assert.Equal(
            ((LibraryLinkWriteState?)LibraryLinkWriteState.KeptInApp, true, (string?)NoEpisodeImdbReason, (string?)null),
            (item.WriteState, item.CanWrite, item.CannotWriteReason, item.EpisodeImdbId));
        // Only asking TMDb tells it, so the link keeps it: the repair and the writer don't ask again.
        Assert.Equal(NoEpisodeImdbReason, (await _factory.SingleLinkAsync(cancellationToken)).WriteError);
        Assert.Equal(jellyfinSent, _factory.JellyfinHttp.Requests.Count);
        Assert.Empty(_jellyfin.RemoteSearches);
    }

    [Fact]
    public async Task Set_again_of_an_episode_tmdb_gave_no_imdb_id_once_it_gives_one_queues_and_identifies_it()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var workId = await SyncEpisodeItemAsync(tmdbId: null, imdbId: null);
        await SetAsync(workId, new LibraryLinkRequest("Series", NoImdbShowId, SeasonNumber: "1", EpisodeNumber: "8"));
        var kept = await _factory.SingleLinkAsync(cancellationToken);
        // TMDb gives the episode an IMDb id since (VendorSamples/Tmdb/tv-episode-planet-earth-s01e08.json).
        _tmdb.AddEpisode(new FakeTmdbEpisode(int.Parse(NoImdbShowId), 1, 8, "Джунглі", PlanetEarthJunglesImdbId));

        //Act
        // The same pick: only asking TMDb tells it, and Retry and the repair leave such a link alone.
        var response = await _factory.PutHttpRequest(LinkUrl(workId), _owner.AccessToken, new LibraryLinkRequest("Series", NoImdbShowId, SeasonNumber: "1", EpisodeNumber: "8"));
        var queued = await _factory.SingleLinkAsync(cancellationToken);
        await _factory.IdentifyQueuedLinksAsync(cancellationToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal((LibraryLinkWriteState.KeptInApp, (string?)NoEpisodeImdbReason), (kept.WriteState, kept.WriteError));
        Assert.Equal((kept.Id, LibraryLinkWriteState.Queued, (string?)PlanetEarthJunglesImdbId), (queued.Id, queued.WriteState, queued.EpisodeImdbId));
        var link = await _factory.SingleLinkAsync(cancellationToken);
        Assert.Equal(
            (kept.Id, LibraryLinkWriteState.Written, (string?)null, (string?)PlanetEarthJunglesImdbId),
            (link.Id, link.WriteState, link.WriteError, link.EpisodeImdbId));
        Assert.Equal(new Dictionary<string, string> { ["Imdb"] = PlanetEarthJunglesImdbId }, ProviderIdsOf(EpisodeItemId));
        Assert.Equal(
            [AuditAction.LibraryLinkSet, AuditAction.LibraryLinkReplaced, AuditAction.LibraryItemIdentified],
            await _factory.AuditActionsAsync(cancellationToken));
    }

    [Fact]
    public async Task Set_of_an_episode_tmdb_does_not_know_keeps_the_link_in_the_app_as_one_tmdb_gives_no_imdb_id()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var workId = await SyncEpisodeItemAsync(tmdbId: null, imdbId: null);

        //Act
        var response = await _factory.PutHttpRequest(LinkUrl(workId), _owner.AccessToken, new LibraryLinkRequest("Series", ShowId, SeasonNumber: "9", EpisodeNumber: "99"));

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var link = await _factory.SingleLinkAsync(cancellationToken);
        Assert.Equal((LibraryLinkWriteState.KeptInApp, (string?)NoEpisodeImdbReason, (int?)9, (int?)99), (link.WriteState, link.WriteError, link.SeasonNumber, link.EpisodeNumber));
    }

    [Fact]
    public async Task Set_of_an_episode_when_tmdb_cannot_be_asked_for_it_is_502_and_saves_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var workId = await SyncEpisodeItemAsync(tmdbId: null, imdbId: null);
        _tmdb.Intercept = request => request.RequestUri!.AbsolutePath.Contains("/episode/", StringComparison.Ordinal)
            ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            : null;
        var jellyfinSent = _factory.JellyfinHttp.Requests.Count;

        //Act
        var response = await _factory.PutHttpRequest(LinkUrl(workId), _owner.AccessToken, new LibraryLinkRequest("Series", ShowId, SeasonNumber: "1", EpisodeNumber: "1"));

        //Assert
        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal("TMDb answered 503 Service Unavailable. Its own log says why.", await response.ReadErrorAsync(cancellationToken));
        await _factory.WithDbAsync(async db =>
        {
            Assert.Equal(0, await db.LibraryItemLinks.CountAsync(cancellationToken));
            Assert.Equal(0, await db.AuditEvents.CountAsync(cancellationToken));
        });
        Assert.Equal(jellyfinSent, _factory.JellyfinHttp.Requests.Count);
    }

    [Fact]
    public async Task Set_of_an_episode_whose_item_holds_another_imdb_id_replaces_it_when_set_by_hand()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // Jellyfin matched the file to a film and gave it that film's IMDb id: the owner's own click replaces it, as
        // Jellyfin's Identify button would (only a bulk click protects the library's own ids).
        var workId = await SyncEpisodeItemAsync(tmdbId: null, imdbId: "tt5555555");

        //Act
        var response = await _factory.PutHttpRequest(LinkUrl(workId), _owner.AccessToken, new LibraryLinkRequest("Series", ShowId, SeasonNumber: "1", EpisodeNumber: "1"));
        await _factory.IdentifyQueuedLinksAsync(cancellationToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var link = await _factory.SingleLinkAsync(cancellationToken);
        Assert.Equal((LibraryLinkWriteState.Written, (string?)null), (link.WriteState, link.WriteError));
        Assert.Equal(new Dictionary<string, string> { ["Imdb"] = EpisodeImdbId }, ProviderIdsOf(EpisodeItemId));
        Assert.Equal(
            [
                (AuditAction.LibraryLinkSet, "Before Imdb tt5555555, After TmdbTv 91724"),
                (AuditAction.LibraryItemIdentified, "Before Imdb tt5555555, After Imdb tt9103946"),
            ],
            (await _factory.AuditAsync(cancellationToken)).Select(row => (row.Action, row.Identifiers)));
    }

    [Fact]
    public async Task Resync_marks_an_identified_episode_link_in_jellyfin_by_the_episodes_imdb_id_and_lost_once_jellyfin_drops_it()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var workId = await SyncEpisodeItemAsync(tmdbId: null, imdbId: null);
        await SetAndIdentifyAsync(workId, new LibraryLinkRequest("Series", ShowId, SeasonNumber: "1", EpisodeNumber: "1"));

        //Act
        await _factory.SyncLibraryAsync(_jellyfinIntegration.Id, _owner.AccessToken, cancellationToken);
        var inJellyfin = (await _factory.SingleLinkAsync(cancellationToken)).WriteState;
        // A metadata replace dropped it.
        Movie(EpisodeItemId)["ProviderIds"] = new JsonObject();
        _factory.Time.Advance(TimeSpan.FromMinutes(1));
        await _factory.SyncLibraryAsync(_jellyfinIntegration.Id, _owner.AccessToken, cancellationToken);

        //Assert
        // R5 by the link's identified key — the episode's IMDb id —, as a film's link is judged by its TMDb id.
        Assert.Equal((LibraryLinkWriteState.InLibrary, LibraryLinkWriteState.Lost), (inJellyfin, (await _factory.SingleLinkAsync(cancellationToken)).WriteState));
    }

    [Fact]
    public async Task Resync_judges_no_episode_link_kept_in_the_app_whatever_jellyfin_reports()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var workId = await SyncEpisodeItemAsync(tmdbId: null, imdbId: null);
        await SetAsync(workId, new LibraryLinkRequest("Series", NoImdbShowId, SeasonNumber: "1", EpisodeNumber: "8"));
        var kept = await _factory.SingleLinkAsync(cancellationToken);
        Movie(EpisodeItemId)["ProviderIds"] = new JsonObject { ["Imdb"] = "tt5555555" };

        //Act
        await _factory.SyncLibraryAsync(_jellyfinIntegration.Id, _owner.AccessToken, cancellationToken);

        //Assert
        var link = await _factory.SingleLinkAsync(cancellationToken);
        Assert.Equal((LibraryLinkWriteState.KeptInApp, kept.WriteStateUtc), (link.WriteState, link.WriteStateUtc));
    }

    [Fact]
    public async Task Set_of_an_episode_on_an_episode_item_no_series_holds_keeps_the_link_in_the_app_and_asks_tmdb_for_no_episode()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        _jellyfinIntegration = await _integrations.Create("jellyfin", "Living room", apiKey: ApiKey);
        _jellyfin.Episodes.Add(JellyfinSamples.EpisodeWith(KidsItemId, "Tom & Jerry Kids 01", null, null, null, "/media/cartoons/Tom & Jerry Kids/01.mkv"));
        await _factory.SyncLibraryAsync(_jellyfinIntegration.Id, _owner.AccessToken, cancellationToken);
        await ConnectTmdbAsync();
        var workId = await _factory.WorkOfItemAsync(KidsItemId, cancellationToken);
        var jellyfinSent = _factory.JellyfinHttp.Requests.Count;

        //Act
        var response = await _factory.PutHttpRequest(LinkUrl(workId), _owner.AccessToken, new LibraryLinkRequest("Series", ShowId, SeasonNumber: "1", EpisodeNumber: "1"));

        //Assert
        // Jellyfin identifies movie and series items only: an episode item keeps its link in the app.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var item = Assert.Single((await response.ReadDialogAsync(cancellationToken)).Items);
        Assert.Equal(
            ((LibraryLinkWriteState?)LibraryLinkWriteState.KeptInApp, false,
                (string?)"Jellyfin identifies movie and series items only, so a link on an episode stays in the app."),
            (item.WriteState, item.CanWrite, item.CannotWriteReason));
        Assert.DoesNotContain(_factory.TmdbHttp.Requests, request => request.Uri.AbsolutePath.Contains("/episode/", StringComparison.Ordinal));
        Assert.Equal(jellyfinSent, _factory.JellyfinHttp.Requests.Count);
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    private static string LinkUrl(Guid workId) => $"{BaseUrl}/works/{workId}/tmdb";

    // The documentary episode Jellyfin lists as a movie, synced, with TMDb connected.
    private async Task<Guid> SyncEpisodeItemAsync(string? tmdbId, string? imdbId)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        _jellyfinIntegration = await _integrations.Create("jellyfin", "Living room", apiKey: ApiKey);
        _jellyfin.Movies.Add(JellyfinSamples.MovieWith(
            EpisodeItemId, "Greatest Events of World War II in Colour.S01E01.Blitzkrieg", null, 2019,
            $"{JellyfinSamples.DocumentaryRoot}/Greatest Events of World War II in Colour/S01E01.Blitzkrieg.mkv", imdbId, tmdbId));
        await _factory.SyncLibraryAsync(_jellyfinIntegration.Id, _owner.AccessToken, cancellationToken);
        await ConnectTmdbAsync();
        return await _factory.WorkOfItemAsync(EpisodeItemId, cancellationToken);
    }

    // After the syncs, so a sync queues no recommendation run of its own.
    private Task<IntegrationConnectionEntity> ConnectTmdbAsync() =>
        _integrations.Create("tmdb", "TMDb", FakeTmdbServer.BaseUrl, apiKey: FakeTmdbServer.Token);

    private async Task SetAsync(Guid workId, LibraryLinkRequest request)
    {
        var response = await _factory.PutHttpRequest(LinkUrl(workId), _owner.AccessToken, request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        _factory.Time.Advance(TimeSpan.FromMinutes(1));
    }

    // The Save and the identification it queued, with a minute passing after them.
    private async Task SetAndIdentifyAsync(Guid workId, LibraryLinkRequest request)
    {
        await SetAsync(workId, request);
        await _factory.IdentifyQueuedLinksAsync(TestContext.Current.CancellationToken);
        _factory.Time.Advance(TimeSpan.FromMinutes(1));
    }

    private JsonObject Movie(string itemId) => _jellyfin.Movies.Single(movie => (string?)movie["Id"] == itemId);

    private Dictionary<string, string> ProviderIdsOf(string itemId) =>
        Movie(itemId)["ProviderIds"]!.AsObject().ToDictionary(pair => pair.Key, pair => (string)pair.Value!);
}
