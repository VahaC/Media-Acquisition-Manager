using System.Net;
using System.Net.Http.Json;
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

// MAM-34 follow-up (Vasyl's decision of 2026-10-06) — an episode's link to its show on a film item (Jellyfin lists a
// documentary's episodes as movies): with its season and episode, the episode's own IMDb id TMDb gives is the link's
// written key — written where the item holds no IMDb id, never a TMDb id (on a movie item Tmdb names a film), then
// Jellyfin asked to look the item up, which finds the episode by it (the live check of "Greatest Events of World War II
// in Colour" S01E01, tt9103946). Without both numbers, or with an episode TMDb gives no IMDb id, the link stays in the app
// and says why — until the same pick saved again finds TMDb giving one; an episode item no series holds keeps its link in
// the app as before. A sync judges the link by that IMDb
// id; a removal takes back only the IMDb id the app wrote; a film link it replaces is put back first, in the same write.
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
    private const string FilmId = JellyfinSamples.LandingStriplingTmdbId;
    private const string FilmImdbId = JellyfinSamples.LandingStriplingImdbId;
    private const string WrongTmdbId = "100";
    private const string NoNumbersReason = "Without a season and an episode the app has no id of this episode to write.";
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
        _jellyfin.Arm(_factory.JellyfinHttp);
        _tmdb = FakeTmdbServer.Arm(_factory.TmdbHttp);
        _tmdb.AddWork(new FakeTmdbWork(MediaWorkKind.Series, int.Parse(ShowId), "Найвизначніші події Другої світової у кольорі", "Greatest Events of WWII in Colour"));
        // As TMDb answered it live (VendorSamples/Tmdb/tv-episode-wwii-s01e01.json).
        _tmdb.AddEpisode(new FakeTmdbEpisode(int.Parse(ShowId), 1, 1, "Серія 1", EpisodeImdbId, "2019-07-12", 50));
        _tmdb.AddWork(new FakeTmdbWork(MediaWorkKind.Series, int.Parse(NoImdbShowId), "Планета Земля", "Planet Earth", "2006-03-05", ImdbId: "tt0795176"));
        _tmdb.AddEpisode(new FakeTmdbEpisode(int.Parse(NoImdbShowId), 1, 8, "Джунглі"));
        _tmdb.AddWork(new FakeTmdbWork(MediaWorkKind.Movie, int.Parse(FilmId), "Посадкова смуга", ImdbId: FilmImdbId));
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Set_of_an_episode_on_a_film_item_writes_the_episodes_own_imdb_id_only_and_asks_jellyfin_to_look_the_item_up()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // Jellyfin gave the episode, which it lists as a movie, a film's id.
        var workId = await SyncEpisodeItemAsync(tmdbId: WrongTmdbId, imdbId: null);
        var jellyfinSent = _factory.JellyfinHttp.Requests.Count;
        var now = _factory.Time.GetUtcNow().UtcDateTime;

        //Act
        var response = await _factory.PutHttpRequest(LinkUrl(workId), _owner.AccessToken, new LibraryLinkRequest("Series", ShowId, SeasonNumber: "1", EpisodeNumber: "1"));

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var item = Assert.Single((await ReadDialogAsync(response, cancellationToken)).Items);
        Assert.Equal(
            ((LibraryLinkTarget?)LibraryLinkTarget.ShowEpisode, (LibraryLinkWriteState?)LibraryLinkWriteState.Written, (string?)EpisodeImdbId, true, (string?)null, (DateTime?)now),
            (item.Target, item.WriteState, item.EpisodeImdbId, item.CanWrite, item.CannotWriteReason, item.LookupRequestedUtc));
        // TMDb asked for the episode with its external ids, once.
        Assert.Single(
            _factory.TmdbHttp.Requests,
            request => request.Uri.PathAndQuery == $"/3/tv/{ShowId}/season/1/episode/1?append_to_response=external_ids&language=uk-UA");
        // The episode's IMDb id only: Jellyfin's film id stays in Jellyfin (on a movie item Tmdb names a film), and
        // Jellyfin's lookup finds the episode by the IMDb id.
        Assert.Equal(new Dictionary<string, string> { ["Tmdb"] = WrongTmdbId, ["Imdb"] = EpisodeImdbId }, ProviderIdsOf(EpisodeItemId));
        Assert.Equal([EpisodeItemId], _jellyfin.Lookups);
        Assert.Single(_factory.JellyfinHttp.Requests.Skip(jellyfinSent), request => request.Method == HttpMethod.Post && request.Body is not null);
        var link = await SingleLinkAsync(cancellationToken);
        Assert.Equal(
            (EpisodeImdbId, (string?)WrongTmdbId, LibraryLinkWriteState.Written, (DateTime?)now, (string?)null),
            (link.EpisodeImdbId, link.SupersededValue, link.WriteState, link.LookupRequestedUtc, link.LookupError));
        Assert.Equal(
            [
                (AuditAction.LibraryLinkSet, $"Before Tmdb {WrongTmdbId}, After TmdbTv {ShowId}"),
                (AuditAction.LibraryIdsWritten, $"After Imdb {EpisodeImdbId}"),
                (AuditAction.LibraryMetadataLookupRequested, ""),
            ],
            await AuditAsync(cancellationToken));
    }

    [Fact]
    public async Task Written_episode_link_keeps_the_film_id_it_replaced_giving_way_in_the_app()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var workId = await SyncEpisodeItemAsync(tmdbId: WrongTmdbId, imdbId: null);
        await SetAsync(workId, new LibraryLinkRequest("Series", ShowId, SeasonNumber: "1", EpisodeNumber: "1"));
        // A sync reads the episode's IMDb id from Jellyfin, beside the film id Jellyfin still holds.
        await _factory.SyncLibraryAsync(_jellyfinIntegration.Id, _owner.AccessToken, cancellationToken);

        //Act
        var byFilmId = await _factory.GetHttpRequest($"{BaseUrl}/works?search={WrongTmdbId}", _owner.AccessToken);
        var byEpisodeImdbId = await _factory.GetHttpRequest($"{BaseUrl}/works?search={EpisodeImdbId}", _owner.AccessToken);

        //Assert
        // R7 — what the app writes for an episode is its IMDb id, never a TMDb one: the wrong film id gives way whatever the
        // link's state. The episode's IMDb id is Jellyfin's own now, read as any library id.
        Assert.Equal(0, (await ReadAsync<LibraryWorkListResponse>(byFilmId, cancellationToken)).TotalCount);
        var found = Assert.Single((await ReadAsync<LibraryWorkListResponse>(byEpisodeImdbId, cancellationToken)).Items);
        Assert.Equal(workId, found.Id);
        Assert.Null(found.Link!.ConflictingValue);
        Assert.Equal(LibraryLinkWriteState.InLibrary, (await SingleLinkAsync(cancellationToken)).WriteState);
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
        var item = Assert.Single((await ReadDialogAsync(response, cancellationToken)).Items);
        // The item takes ids; the link has none of its own to write.
        Assert.Equal(
            ((LibraryLinkWriteState?)LibraryLinkWriteState.KeptInApp, true, (string?)NoNumbersReason, (string?)null),
            (item.WriteState, item.CanWrite, item.CannotWriteReason, item.EpisodeImdbId));
        Assert.DoesNotContain(_factory.TmdbHttp.Requests, request => request.Uri.AbsolutePath.Contains("/episode/", StringComparison.Ordinal));
        Assert.Equal(jellyfinSent, _factory.JellyfinHttp.Requests.Count);
        Assert.Equal([(AuditAction.LibraryLinkSet, $"After TmdbTv {ShowId}")], await AuditAsync(cancellationToken));
    }

    [Fact]
    public async Task Set_of_an_episode_tmdb_gives_no_imdb_id_keeps_the_link_in_the_app_says_why_and_sends_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var workId = await SyncEpisodeItemAsync(tmdbId: null, imdbId: null);
        var jellyfinSent = _factory.JellyfinHttp.Requests.Count;

        //Act
        var response = await _factory.PutHttpRequest(LinkUrl(workId), _owner.AccessToken, new LibraryLinkRequest("Series", NoImdbShowId, SeasonNumber: "1", EpisodeNumber: "8"));

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var item = Assert.Single((await ReadDialogAsync(response, cancellationToken)).Items);
        Assert.Equal(
            ((LibraryLinkWriteState?)LibraryLinkWriteState.KeptInApp, true, (string?)NoEpisodeImdbReason, (string?)null),
            (item.WriteState, item.CanWrite, item.CannotWriteReason, item.EpisodeImdbId));
        // Only asking TMDb tells it, so the link keeps it: the catch-up and the writer don't ask again.
        Assert.Equal(NoEpisodeImdbReason, (await SingleLinkAsync(cancellationToken)).WriteError);
        Assert.Equal(jellyfinSent, _factory.JellyfinHttp.Requests.Count);
        Assert.Empty(_jellyfin.Lookups);
    }

    [Fact]
    public async Task Set_again_of_an_episode_tmdb_gave_no_imdb_id_once_it_gives_one_writes_it_and_asks_jellyfin_to_look_the_item_up()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var workId = await SyncEpisodeItemAsync(tmdbId: null, imdbId: null);
        await SetAsync(workId, new LibraryLinkRequest("Series", NoImdbShowId, SeasonNumber: "1", EpisodeNumber: "8"));
        var kept = await SingleLinkAsync(cancellationToken);
        // TMDb gives the episode an IMDb id since (VendorSamples/Tmdb/tv-episode-planet-earth-s01e08.json).
        _tmdb.AddEpisode(new FakeTmdbEpisode(int.Parse(NoImdbShowId), 1, 8, "Джунглі", PlanetEarthJunglesImdbId));
        var now = _factory.Time.GetUtcNow().UtcDateTime;

        //Act
        // The same pick: only asking TMDb tells it, and Retry and the catch-up leave such a link alone.
        var response = await _factory.PutHttpRequest(LinkUrl(workId), _owner.AccessToken, new LibraryLinkRequest("Series", NoImdbShowId, SeasonNumber: "1", EpisodeNumber: "8"));

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal((LibraryLinkWriteState.KeptInApp, (string?)NoEpisodeImdbReason), (kept.WriteState, kept.WriteError));
        var link = await SingleLinkAsync(cancellationToken);
        Assert.Equal(
            (kept.Id, LibraryLinkWriteState.Written, (string?)null, (string?)PlanetEarthJunglesImdbId, (DateTime?)now),
            (link.Id, link.WriteState, link.WriteError, link.EpisodeImdbId, link.LookupRequestedUtc));
        Assert.Equal(new Dictionary<string, string> { ["Imdb"] = PlanetEarthJunglesImdbId }, ProviderIdsOf(EpisodeItemId));
        Assert.Equal([EpisodeItemId], _jellyfin.Lookups);
        Assert.Equal(
            [AuditAction.LibraryLinkSet, AuditAction.LibraryLinkReplaced, AuditAction.LibraryIdsWritten, AuditAction.LibraryMetadataLookupRequested],
            (await AuditAsync(cancellationToken)).Select(row => row.Action));
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
        var link = await SingleLinkAsync(cancellationToken);
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
    public async Task Set_of_an_episode_whose_item_holds_another_imdb_id_leaves_it_and_is_not_written_with_the_sentence()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // Jellyfin matched the file to a film and gave it that film's IMDb id: the library's own, never written over.
        var workId = await SyncEpisodeItemAsync(tmdbId: null, imdbId: "tt5555555");

        //Act
        var response = await _factory.PutHttpRequest(LinkUrl(workId), _owner.AccessToken, new LibraryLinkRequest("Series", ShowId, SeasonNumber: "1", EpisodeNumber: "1"));

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var link = await SingleLinkAsync(cancellationToken);
        Assert.Equal(
            (LibraryLinkWriteState.NotWritten,
                (string?)$"“Living room” holds another IMDb id for this item, and the app never writes over the library's own ids, so the episode's {EpisodeImdbId} isn't in it. Change it there, then retry.",
                (DateTime?)null),
            (link.WriteState, link.WriteError, link.LookupRequestedUtc));
        // Nothing changed, so nothing was posted, and no lookup asked for: the episode's id isn't there to find it by.
        Assert.Equal(new Dictionary<string, string> { ["Imdb"] = "tt5555555" }, ProviderIdsOf(EpisodeItemId));
        Assert.DoesNotContain(_factory.JellyfinHttp.Requests, request => request.Method == HttpMethod.Post);
        Assert.Equal([AuditAction.LibraryLinkSet], (await AuditAsync(cancellationToken)).Select(row => row.Action));
    }

    [Fact]
    public async Task Resync_marks_a_written_episode_link_in_jellyfin_by_the_episodes_imdb_id_and_lost_once_jellyfin_drops_it()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var workId = await SyncEpisodeItemAsync(tmdbId: null, imdbId: null);
        await SetAsync(workId, new LibraryLinkRequest("Series", ShowId, SeasonNumber: "1", EpisodeNumber: "1"));

        //Act
        await _factory.SyncLibraryAsync(_jellyfinIntegration.Id, _owner.AccessToken, cancellationToken);
        var inJellyfin = (await SingleLinkAsync(cancellationToken)).WriteState;
        // A metadata replace dropped it.
        Movie(EpisodeItemId)["ProviderIds"] = new JsonObject();
        _factory.Time.Advance(TimeSpan.FromMinutes(1));
        await _factory.SyncLibraryAsync(_jellyfinIntegration.Id, _owner.AccessToken, cancellationToken);

        //Assert
        // R5 by the link's written key — the episode's IMDb id —, as a film's link is judged by its TMDb id.
        Assert.Equal((LibraryLinkWriteState.InLibrary, LibraryLinkWriteState.Lost), (inJellyfin, (await SingleLinkAsync(cancellationToken)).WriteState));
    }

    [Fact]
    public async Task Resync_judges_no_episode_link_kept_in_the_app_whatever_jellyfin_reports()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var workId = await SyncEpisodeItemAsync(tmdbId: null, imdbId: null);
        await SetAsync(workId, new LibraryLinkRequest("Series", NoImdbShowId, SeasonNumber: "1", EpisodeNumber: "8"));
        var kept = await SingleLinkAsync(cancellationToken);
        Movie(EpisodeItemId)["ProviderIds"] = new JsonObject { ["Imdb"] = "tt5555555" };

        //Act
        await _factory.SyncLibraryAsync(_jellyfinIntegration.Id, _owner.AccessToken, cancellationToken);

        //Assert
        var link = await SingleLinkAsync(cancellationToken);
        Assert.Equal((LibraryLinkWriteState.KeptInApp, kept.WriteStateUtc), (link.WriteState, link.WriteStateUtc));
    }

    [Fact]
    public async Task Remove_of_a_written_episode_link_takes_back_only_the_imdb_id_the_app_wrote()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var workId = await SyncEpisodeItemAsync(tmdbId: WrongTmdbId, imdbId: null);
        await SetAsync(workId, new LibraryLinkRequest("Series", ShowId, SeasonNumber: "1", EpisodeNumber: "1"));
        var written = ProviderIdsOf(EpisodeItemId);

        //Act
        var response = await _factory.DeleteHttpRequest(LinkUrl(workId), _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new Dictionary<string, string> { ["Tmdb"] = WrongTmdbId, ["Imdb"] = EpisodeImdbId }, written);
        // Jellyfin's own film id was never the app's: it stays.
        Assert.Equal(new Dictionary<string, string> { ["Tmdb"] = WrongTmdbId }, ProviderIdsOf(EpisodeItemId));
        await _factory.WithDbAsync(async db => Assert.False(await db.LibraryItemLinks.AnyAsync(cancellationToken)));
        Assert.Equal(
            [
                (AuditAction.LibraryLinkRemoved, $"Before TmdbTv {ShowId}"),
                (AuditAction.LibraryIdsRestored, $"Before Imdb {EpisodeImdbId}"),
            ],
            (await AuditAsync(cancellationToken)).Skip(3));
    }

    [Fact]
    public async Task Replace_of_a_written_film_link_with_an_episode_link_puts_the_film_id_back_and_writes_the_episodes_imdb_id_in_one_write()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var workId = await SyncEpisodeItemAsync(tmdbId: WrongTmdbId, imdbId: null);
        await SetAsync(workId, new LibraryLinkRequest("Movie", FilmId));
        var asFilm = ProviderIdsOf(EpisodeItemId);
        var posted = _factory.JellyfinHttp.Requests.Count(request => request.Method == HttpMethod.Post && request.Body is not null);

        //Act
        var response = await _factory.PutHttpRequest(LinkUrl(workId), _owner.AccessToken, new LibraryLinkRequest("Series", ShowId, SeasonNumber: "1", EpisodeNumber: "1"));

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new Dictionary<string, string> { ["Tmdb"] = FilmId, ["Imdb"] = FilmImdbId }, asFilm);
        // R11 — the film id Jellyfin held is back, and the IMDb id the app added is swapped for the episode's: one POST.
        Assert.Equal(new Dictionary<string, string> { ["Tmdb"] = WrongTmdbId, ["Imdb"] = EpisodeImdbId }, ProviderIdsOf(EpisodeItemId));
        Assert.Equal(posted + 1, _factory.JellyfinHttp.Requests.Count(request => request.Method == HttpMethod.Post && request.Body is not null));
        var link = await SingleLinkAsync(cancellationToken);
        Assert.Equal(
            (LibraryLinkTarget.ShowEpisode, LibraryLinkWriteState.Written, (string?)EpisodeImdbId, (string?)WrongTmdbId),
            (link.Target, link.WriteState, link.EpisodeImdbId, link.SupersededValue));
        Assert.Equal([EpisodeItemId, EpisodeItemId], _jellyfin.Lookups);
        Assert.Equal(
            (AuditAction.LibraryIdsWritten, $"Before Imdb {FilmImdbId}, Before Tmdb {FilmId}, After Imdb {EpisodeImdbId}, After Tmdb {WrongTmdbId}"),
            (await AuditAsync(cancellationToken)).Skip(4).First());
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
        var workId = await WorkOfAsync(KidsItemId, cancellationToken);
        var jellyfinSent = _factory.JellyfinHttp.Requests.Count;

        //Act
        var response = await _factory.PutHttpRequest(LinkUrl(workId), _owner.AccessToken, new LibraryLinkRequest("Series", ShowId, SeasonNumber: "1", EpisodeNumber: "1"));

        //Assert
        // Jellyfin's ids are written on movie and series items only: an episode item keeps its link in the app, as before.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var item = Assert.Single((await ReadDialogAsync(response, cancellationToken)).Items);
        Assert.Equal(
            ((LibraryLinkWriteState?)LibraryLinkWriteState.KeptInApp, false,
                (string?)"Jellyfin's ids are written on movie and series items only, so a link on an episode stays in the app."),
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
            "/media/documentary/Greatest Events of World War II in Colour/S01E01.Blitzkrieg.mkv", imdbId, tmdbId));
        await _factory.SyncLibraryAsync(_jellyfinIntegration.Id, _owner.AccessToken, cancellationToken);
        await ConnectTmdbAsync();
        return await WorkOfAsync(EpisodeItemId, cancellationToken);
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

    private JsonObject Movie(string itemId) => _jellyfin.Movies.Single(movie => (string?)movie["Id"] == itemId);

    private Dictionary<string, string> ProviderIdsOf(string itemId) =>
        Movie(itemId)["ProviderIds"]!.AsObject().ToDictionary(pair => pair.Key, pair => (string)pair.Value!);

    private static async Task<LibraryLinkDialogResponse> ReadDialogAsync(HttpResponseMessage response, CancellationToken cancellationToken) =>
        await ReadAsync<LibraryLinkDialogResponse>(response, cancellationToken);

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken) =>
        (await response.Content.ReadFromJsonAsync<T>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;

    private async Task<LibraryItemLinkEntity> SingleLinkAsync(CancellationToken cancellationToken)
    {
        LibraryItemLinkEntity? link = null;
        await _factory.WithDbAsync(async db => link = await db.LibraryItemLinks.AsNoTracking().SingleAsync(cancellationToken));
        return link!;
    }

    private async Task<Guid> WorkOfAsync(string itemId, CancellationToken cancellationToken)
    {
        var workId = Guid.Empty;
        await _factory.WithDbAsync(async db => workId = await db.MediaWorkTitles
            .Where(title => title.SourceItemId == itemId)
            .Select(title => title.MediaWorkId)
            .FirstAsync(cancellationToken));
        return workId;
    }

    // The audit rows in the order they were recorded, each with its ids as "Role Scheme Value" in role and scheme order.
    private async Task<List<(AuditAction Action, string Identifiers)>> AuditAsync(CancellationToken cancellationToken)
    {
        var rows = new List<(AuditAction, string)>();
        await _factory.WithDbAsync(async db =>
        {
            var identifiers = (await db.AuditEventIdentifiers.AsNoTracking().ToListAsync(cancellationToken)).ToLookup(identifier => identifier.AuditEventId);
            rows.AddRange((await db.AuditEvents.AsNoTracking().ToListAsync(cancellationToken))
                .OrderBy(auditEvent => auditEvent.CreatedUtc)
                .ThenBy(auditEvent => auditEvent.Action)
                .Select(auditEvent => (
                    auditEvent.Action,
                    string.Join(", ", identifiers[auditEvent.Id]
                        .OrderBy(identifier => identifier.Role)
                        .ThenBy(identifier => identifier.Scheme)
                        .Select(identifier => $"{identifier.Role} {identifier.Scheme} {identifier.Value}")))));
        });
        return rows;
    }
}
