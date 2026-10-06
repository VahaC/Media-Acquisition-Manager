using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Files;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Tests.LibraryLinks;

// MAM-34 — the dialog's Save (PUT /api/library/works/{id}/tmdb): the link and its audit row first, with the ids the
// library held, then the write into Jellyfin — read, the item sent back once with only its ProviderIds changed, read
// again — recorded when it left the app and settled on the link: written, refused, or not confirmed; once written,
// Jellyfin is asked to look the item's missing metadata up, recorded on the link and in an audit row (Vasyl's decision
// of 2026-10-06). A film's id goes into a film item, and a show's into a series item the same way (Vasyl's final
// decision of 2026-10-06); a show linked to a film item is an episode of it, whose own IMDb id from TMDb is written when
// it names its season and episode (episode classes of its own: LibraryLinkEpisodeHttpTests); a library type that can't
// set ids, or one switched off (R12), keeps the link without calling it. Per item (R17), and a save that changes nothing
// records and sends nothing. Every refusal writes nothing. The replace, the removal, Retry, the background writer and
// the lookup have classes of their own.
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
    private const string ShowId = JellyfinSamples.PenguinsTmdbTvId;
    private const string ShowImdbId = "tt0892700";
    private const string PlanetEarthId = "1044";
    private const string WrongTmdbId = "100";
    private const string ShortPath = "/media/cartoons/Tom & Jerry/120. Landing Stripling (1962).mkv";
    private const long ShortInode = 2_001;
    private const long ShortSizeBytes = 52_428_800;
    private const string UnderWayError = "A write to the library is under way for this work; try again in a moment.";

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
        _jellyfin.Arm(_factory.JellyfinHttp);
        var tmdb = FakeTmdbServer.Arm(_factory.TmdbHttp);
        tmdb.AddWork(new FakeTmdbWork(MediaWorkKind.Movie, int.Parse(FilmId), FilmTitle, "Landing Stripling", "1962-11-17", ImdbId: FilmImdbId, RuntimeMinutes: 6));
        tmdb.AddWork(new FakeTmdbWork(MediaWorkKind.Series, int.Parse(ShowId), "Пінгвіни з Мадагаскару", "The Penguins of Madagascar", "2008-11-28", ImdbId: ShowImdbId));
        tmdb.AddWork(new FakeTmdbWork(MediaWorkKind.Series, int.Parse(PlanetEarthId), "Планета Земля", "Planet Earth", "2006-03-05", ImdbId: "tt0795176"));
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Set_of_a_film_saves_the_link_with_its_audit_row_and_writes_it_into_jellyfin_confirmed()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var (_, workId) = await SyncShortAsync(tmdbId: null);
        var asRead = Movie(ShortId).DeepClone().AsObject();
        var sent = _factory.JellyfinHttp.Requests.Count;
        var now = _factory.Time.GetUtcNow().UtcDateTime;

        //Act
        var response = await _factory.PutHttpRequest(LinkUrl(workId), _owner.AccessToken, new LibraryLinkRequest("Movie", FilmId));

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dialog = await ReadDialogAsync(response, cancellationToken);
        var item = Assert.Single(dialog.Items);
        Assert.Equal(
            (ShortId, LibraryLinkTarget.Work, (MediaWorkKind?)MediaWorkKind.Movie, FilmId, FilmTitle, FilmImdbId, (LibraryLinkWriteState?)LibraryLinkWriteState.Written, (string?)null, "owner", true),
            (item.SourceItemId, item.Target!.Value, item.CatalogKind, item.CatalogId, item.CatalogTitle, item.ImdbId, item.WriteState, item.WriteError, item.SetByUserName, item.CanWrite));
        Assert.Equal(((DateTime?)now, (string?)null, (string?)null), (item.LookupRequestedUtc, item.LookupError, item.EpisodeImdbId));
        Assert.Equal(FilmId, dialog.Work.Link!.CatalogId);

        var link = await SingleLinkAsync(cancellationToken);
        Assert.Equal(
            (ShortId, MediaWorkKind.Movie, (Guid?)workId, LibraryLinkTarget.Work, MediaIdentifierScheme.Tmdb, FilmId, (string?)null),
            (link.SourceItemId, link.ItemKind, link.MediaWorkId, link.Target, link.Scheme, link.Value, link.SupersededValue));
        Assert.Equal(
            (FilmImdbId, "TMDb", (Guid?)_owner.User.Id, "owner", now, (Guid?)null, LibraryLinkWriteState.Written, (string?)null),
            (link.ImdbId, link.ImdbSourceName, link.SetByUserId, link.SetByUserName, link.SetUtc, link.BatchId, link.WriteState, link.WriteError));
        Assert.Equal(((DateTime?)now, (string?)null, (string?)null), (link.LookupRequestedUtc, link.LookupError, link.EpisodeImdbId));

        // The ticket's order: the link with the ids the library held (none) and its own, then what was written, then the
        // lookup Jellyfin was asked for (no ids: it changes none).
        var audit = await AuditAsync(cancellationToken);
        Assert.Equal(
            [
                (AuditAction.LibraryLinkSet, "Landing Stripling", "After Imdb tt0056164, After Tmdb 223021"),
                (AuditAction.LibraryIdsWritten, "Landing Stripling", "After Imdb tt0056164, After Tmdb 223021"),
                (AuditAction.LibraryMetadataLookupRequested, "Landing Stripling", ""),
            ],
            audit.Select(row => (row.Action, row.TargetName, row.Identifiers)));
        Assert.All(audit, row => Assert.Equal((AuditTargetKind.LibraryLink, (Guid?)link.Id, "owner"), (row.TargetKind, row.TargetId, row.ActorUserName)));

        // The version gate, the administrator to read as, the item, the item sent back once, the item read again, and
        // once it shows the ids, Jellyfin's "Search for missing metadata" on it — nothing replaced.
        var path = $"/Items/{ShortId}";
        Assert.Equal(
            [
                ("GET", "/System/Info", ""), ("GET", "/Users", ""), ("GET", path, $"?userId={JellyfinSamples.AdministratorUserId}"), ("POST", path, ""),
                ("GET", path, $"?userId={JellyfinSamples.AdministratorUserId}"),
                ("POST", $"{path}/Refresh", "?metadataRefreshMode=FullRefresh&imageRefreshMode=FullRefresh&replaceAllMetadata=false&replaceAllImages=false"),
            ],
            _factory.JellyfinHttp.Requests.Skip(sent).Select(request => (request.Method.Method, request.Uri.AbsolutePath, request.Uri.Query)));
        Assert.Equal([ShortId], _jellyfin.Lookups);
        // Every field went back as read but its people, which Jellyfin would rebuild (ADR 0026); only the ids changed.
        var posted = JsonNode.Parse(_factory.JellyfinHttp.Requests.Single(request => request.Method == HttpMethod.Post && request.Body is not null).Body!)!.AsObject();
        Assert.Equal(
            new Dictionary<string, string> { ["Imdb"] = FilmImdbId, ["Tmdb"] = FilmId },
            posted["ProviderIds"]!.AsObject().ToDictionary(pair => pair.Key, pair => (string)pair.Value!));
        Assert.False(posted.ContainsKey("People"));
        posted.Remove("ProviderIds");
        asRead.Remove("ProviderIds");
        asRead.Remove("People");
        Assert.True(JsonNode.DeepEquals(asRead, posted));
    }

    [Fact]
    public async Task Set_of_a_show_on_a_series_writes_the_shows_ids_into_jellyfin_confirmed_asks_it_to_look_the_series_up_and_the_next_sync_finds_them()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var jellyfin = await CreateJellyfinAsync();
        // Jellyfin gave the series a wrong show id: the link replaces it, in the app (R6) and in Jellyfin.
        _jellyfin.Series.Add(JellyfinSamples.SeriesWith(PenguinsId, "The Penguins Of Madagascar", 2008, "/media/tv/Penguins", tmdbId: WrongTmdbId));
        _jellyfin.Episodes.Add(JellyfinSamples.EpisodeWith(PenguinsEpisodeId, "Gone in a Flash", PenguinsId, 1, 1, "/media/tv/Penguins/S01E01.mkv"));
        await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);
        await ConnectTmdbAsync();
        var seriesWorkId = await WorkOfAsync(PenguinsId, cancellationToken);
        var asRead = _jellyfin.Series.Single().DeepClone().AsObject();
        var sent = _factory.JellyfinHttp.Requests.Count;
        var now = _factory.Time.GetUtcNow().UtcDateTime;

        //Act
        var response = await _factory.PutHttpRequest(LinkUrl(seriesWorkId), _owner.AccessToken, new LibraryLinkRequest("Series", ShowId));

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var item = Assert.Single((await ReadDialogAsync(response, cancellationToken)).Items);
        // A series is written like a film (Vasyl's final decision of 2026-10-06): no warning, nothing held back.
        Assert.Equal(
            ((MediaWorkKind?)MediaWorkKind.Series, ShowId, (LibraryLinkWriteState?)LibraryLinkWriteState.Written, (string?)null, true, (string?)null),
            (item.CatalogKind, item.CatalogId, item.WriteState, item.WriteError, item.CanWrite, item.CannotWriteReason));
        Assert.Equal(((DateTime?)now, (string?)null), (item.LookupRequestedUtc, item.LookupError));
        var link = await SingleLinkAsync(cancellationToken);
        Assert.Equal(
            (MediaWorkKind.Series, LibraryLinkTarget.Work, MediaIdentifierScheme.TmdbTv, ShowId, (string?)WrongTmdbId, ShowImdbId, LibraryLinkWriteState.Written),
            (link.ItemKind, link.Target, link.Scheme, link.Value, link.SupersededValue, link.ImdbId, link.WriteState));
        Assert.Equal(((DateTime?)now, (string?)null), (link.LookupRequestedUtc, link.LookupError));

        // The series item read, sent back once with the show's id under Jellyfin's Tmdb key and its IMDb id where it had
        // none, read again, then Jellyfin's "Search for missing metadata" on it — nothing replaced.
        var path = $"/Items/{PenguinsId}";
        Assert.Equal(
            [
                ("GET", "/System/Info", ""), ("GET", "/Users", ""), ("GET", path, $"?userId={JellyfinSamples.AdministratorUserId}"), ("POST", path, ""),
                ("GET", path, $"?userId={JellyfinSamples.AdministratorUserId}"),
                ("POST", $"{path}/Refresh", "?metadataRefreshMode=FullRefresh&imageRefreshMode=FullRefresh&replaceAllMetadata=false&replaceAllImages=false"),
            ],
            _factory.JellyfinHttp.Requests.Skip(sent).Select(request => (request.Method.Method, request.Uri.AbsolutePath, request.Uri.Query)));
        Assert.Equal([PenguinsId], _jellyfin.Lookups);
        var posted = JsonNode.Parse(_factory.JellyfinHttp.Requests.Single(request => request.Method == HttpMethod.Post && request.Body is not null).Body!)!.AsObject();
        Assert.Equal("""{"Tmdb":"7869","Imdb":"tt0892700"}""", posted["ProviderIds"]!.ToJsonString());
        // Every other field went back as read but its people, which Jellyfin would rebuild (ADR 0026).
        Assert.False(posted.ContainsKey("People"));
        posted.Remove("ProviderIds");
        asRead.Remove("ProviderIds");
        asRead.Remove("People");
        Assert.True(JsonNode.DeepEquals(asRead, posted));

        Assert.Equal(
            [
                (AuditAction.LibraryLinkSet, "Before TmdbTv 100, After Imdb tt0892700, After TmdbTv 7869"),
                (AuditAction.LibraryIdsWritten, "Before TmdbTv 100, After Imdb tt0892700, After TmdbTv 7869"),
                (AuditAction.LibraryMetadataLookupRequested, ""),
            ],
            (await AuditAsync(cancellationToken)).Select(row => (row.Action, row.Identifiers)));

        // The next sync reads the show's id as Jellyfin's own (R5): in Jellyfin, on the work it was on.
        _factory.Time.Advance(TimeSpan.FromMinutes(1));
        await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);
        var synced = await SingleLinkAsync(cancellationToken);
        Assert.Equal((LibraryLinkWriteState.InLibrary, (Guid?)seriesWorkId), (synced.WriteState, synced.MediaWorkId));
    }

    [Fact]
    public async Task Set_of_a_show_on_a_film_item_is_an_episode_link_kept_in_the_app_that_replaces_the_wrong_film_id_and_calls_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var jellyfin = await CreateJellyfinAsync();
        _jellyfin.Movies.Add(JellyfinSamples.MovieWith(
            PlanetEarthEpisodeId, "Planet_Earth_E08 - Jungles", null, 2006, "/media/documentaries/Planet.Earth/E08.mkv", tmdbId: WrongTmdbId));
        await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);
        await ConnectTmdbAsync();
        var workId = await WorkOfAsync(PlanetEarthEpisodeId, cancellationToken);
        var sent = _factory.JellyfinHttp.Requests.Count;

        //Act
        var response = await _factory.PutHttpRequest(
            LinkUrl(workId), _owner.AccessToken, new LibraryLinkRequest("Series", PlanetEarthId, SeasonNumber: "1", EpisodeNumber: "8"));

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var item = Assert.Single((await ReadDialogAsync(response, cancellationToken)).Items);
        Assert.Equal(
            ((LibraryLinkTarget?)LibraryLinkTarget.ShowEpisode, (int?)1, (int?)8, (LibraryLinkWriteState?)LibraryLinkWriteState.KeptInApp),
            (item.Target, item.SeasonNumber, item.EpisodeNumber, item.WriteState));
        var link = await SingleLinkAsync(cancellationToken);
        // R7 — the film id Jellyfin wrongly gave the episode gives way in the app; nothing is ever written for it.
        Assert.Equal(
            (MediaIdentifierScheme.TmdbTv, PlanetEarthId, (string?)WrongTmdbId, LibraryLinkWriteState.KeptInApp),
            (link.Scheme, link.Value, link.SupersededValue, link.WriteState));
        Assert.Equal(sent, _factory.JellyfinHttp.Requests.Count);
        Assert.Equal([AuditAction.LibraryLinkSet], (await AuditAsync(cancellationToken)).Select(row => row.Action));
    }

    [Fact]
    public async Task Set_with_a_library_type_that_cannot_set_ids_keeps_the_link_in_the_app_and_says_why()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // The fake type is a media library without the writer capability (ADR 0005).
        var library = await _integrations.Create(name: "Old library");
        var work = await new MediaWorkSeeder(_factory.Scope).CreateMovie(library, "Landing Stripling", 1962);
        await ConnectTmdbAsync();

        //Act
        var response = await _factory.PutHttpRequest(LinkUrl(work.Id), _owner.AccessToken, new LibraryLinkRequest("Movie", FilmId));

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var item = Assert.Single((await ReadDialogAsync(response, cancellationToken)).Items);
        Assert.Equal(
            ((LibraryLinkWriteState?)LibraryLinkWriteState.KeptInApp, false, "“Old library” can't set ids, so a link stays in the app."),
            (item.WriteState, item.CanWrite, item.CannotWriteReason));
        Assert.Equal(LibraryLinkWriteState.KeptInApp, (await SingleLinkAsync(cancellationToken)).WriteState);
        Assert.Empty(_factory.FakeAdapter.Calls);
        Assert.Equal([AuditAction.LibraryLinkSet], (await AuditAsync(cancellationToken)).Select(row => row.Action));
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
        var item = Assert.Single((await ReadDialogAsync(response, cancellationToken)).Items);
        Assert.Equal(
            ((LibraryLinkWriteState?)LibraryLinkWriteState.NotWritten, switchedOff, false, switchedOff),
            (item.WriteState, item.WriteError, item.CanWrite, item.CannotWriteReason));
        Assert.Equal(sent, _factory.JellyfinHttp.Requests.Count);
    }

    [Fact]
    public async Task Set_that_jellyfin_refuses_keeps_the_link_not_written_with_its_sentence_and_records_no_write()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var (_, workId) = await SyncShortAsync(tmdbId: null);
        _jellyfin.WriteRefusal = HttpStatusCode.Forbidden;

        //Act
        var response = await _factory.PutHttpRequest(LinkUrl(workId), _owner.AccessToken, new LibraryLinkRequest("Movie", FilmId));

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var link = await SingleLinkAsync(cancellationToken);
        Assert.Equal(
            (LibraryLinkWriteState.NotWritten, "Jellyfin refused to change the item's ids. Check that the API key belongs to an administrator."),
            (link.WriteState, link.WriteError));
        // Refused, so nothing was changed: only the link's own row.
        Assert.Equal([AuditAction.LibraryLinkSet], (await AuditAsync(cancellationToken)).Select(row => row.Action));
        Assert.Empty(Movie(ShortId)["ProviderIds"]!.AsObject());
    }

    [Fact]
    public async Task Set_whose_write_jellyfin_takes_but_does_not_confirm_is_not_written_and_records_what_was_sent()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var (_, workId) = await SyncShortAsync(tmdbId: null);
        _jellyfin.IgnoresWrites = true;

        //Act
        var response = await _factory.PutHttpRequest(LinkUrl(workId), _owner.AccessToken, new LibraryLinkRequest("Movie", FilmId));

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var link = await SingleLinkAsync(cancellationToken);
        Assert.Equal(
            (LibraryLinkWriteState.NotWritten, "Jellyfin took the change, but the item read again holds other ids than the ones sent."),
            (link.WriteState, link.WriteError));
        // R10 — it left the app, so it is recorded, with what it meant to leave.
        Assert.Equal(
            [(AuditAction.LibraryLinkSet, "After Imdb tt0056164, After Tmdb 223021"), (AuditAction.LibraryIdsWritten, "After Imdb tt0056164, After Tmdb 223021")],
            (await AuditAsync(cancellationToken)).Select(row => (row.Action, row.Identifiers)));
    }

    [Fact]
    public async Task Set_keeps_an_imdb_id_jellyfin_holds_and_records_the_ids_it_found()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var (_, workId) = await SyncShortAsync(tmdbId: WrongTmdbId, imdbId: "tt9999999");

        //Act
        var response = await _factory.PutHttpRequest(LinkUrl(workId), _owner.AccessToken, new LibraryLinkRequest("Movie", FilmId));

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var link = await SingleLinkAsync(cancellationToken);
        // R6 — the library's wrong id is the one the link replaces until Jellyfin confirms it.
        Assert.Equal((LibraryLinkWriteState.Written, (string?)WrongTmdbId), (link.WriteState, link.SupersededValue));
        // The TMDb id is the link's to set; the IMDb id Jellyfin holds is Jellyfin's.
        var providerIds = Movie(ShortId)["ProviderIds"]!.AsObject();
        Assert.Equal((FilmId, "tt9999999"), ((string?)providerIds["Tmdb"], (string?)providerIds["Imdb"]));
        Assert.Equal(
            [
                (AuditAction.LibraryLinkSet, "Before Imdb tt9999999, Before Tmdb 100, After Imdb tt0056164, After Tmdb 223021"),
                (AuditAction.LibraryIdsWritten, "Before Imdb tt9999999, Before Tmdb 100, After Imdb tt9999999, After Tmdb 223021"),
                (AuditAction.LibraryMetadataLookupRequested, ""),
            ],
            (await AuditAsync(cancellationToken)).Select(row => (row.Action, row.Identifiers)));
    }

    [Fact]
    public async Task Set_that_changes_nothing_records_nothing_and_sends_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var (_, workId) = await SyncShortAsync(tmdbId: null);
        await _factory.PutHttpRequest(LinkUrl(workId), _owner.AccessToken, new LibraryLinkRequest("Movie", FilmId));
        var sent = _factory.JellyfinHttp.Requests.Count;
        var recorded = (await AuditAsync(cancellationToken)).Count;

        //Act
        var response = await _factory.PutHttpRequest(LinkUrl(workId), _owner.AccessToken, new LibraryLinkRequest("movie", $" {FilmId} "));

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(LibraryLinkWriteState.Written, Assert.Single((await ReadDialogAsync(response, cancellationToken)).Items).WriteState);
        Assert.Equal(sent, _factory.JellyfinHttp.Requests.Count);
        Assert.Equal(recorded, (await AuditAsync(cancellationToken)).Count);
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
        var workId = await WorkOfAsync(ShortId, cancellationToken);
        var groupedTogether = workId == await WorkOfAsync(OtherShortId, cancellationToken);

        //Act
        // R17 — only the item the owner ticked.
        var response = await _factory.PutHttpRequest(
            LinkUrl(workId), _owner.AccessToken, new LibraryLinkRequest("Movie", FilmId, SourceItemIds: [ShortId]));
        _factory.Time.Advance(TimeSpan.FromMinutes(1));
        await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);

        //Assert
        Assert.True(groupedTogether);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var link = await SingleLinkAsync(cancellationToken);
        Assert.Equal(ShortId, link.SourceItemId);
        Assert.Equal(WrongTmdbId, (string?)Movie(OtherShortId)["ProviderIds"]!["Tmdb"]);
        // Jellyfin now reports the linked item's id as its own, and the two are two works again.
        var linkedWorkId = await WorkOfAsync(ShortId, cancellationToken);
        Assert.NotEqual(linkedWorkId, await WorkOfAsync(OtherShortId, cancellationToken));
        var synced = await SingleLinkAsync(cancellationToken);
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
        var workId = await WorkOfAsync(ShortId, cancellationToken);

        //Act
        var response = await _factory.PutHttpRequest(LinkUrl(workId), _owner.AccessToken, new LibraryLinkRequest("Movie", FilmId));

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var link = await SingleLinkAsync(cancellationToken);
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
        const string OtherFolder = "/media/tv/Penguins (Season 1-3)";
        _factory.Files.AddFile("/media/tv/Penguins/S01E01.mkv", TestMedia.File(4_001, ShortSizeBytes));
        _factory.Files.AddFile($"{OtherFolder}/S01E01.mkv", TestMedia.File(4_002, ShortSizeBytes));
        var jellyfin = await CreateJellyfinAsync();
        // The show in two folders, both identified: one series work of two series items.
        _jellyfin.Series.Add(JellyfinSamples.SeriesWith(PenguinsId, "The Penguins Of Madagascar", 2008, "/media/tv/Penguins", tmdbId: ShowId));
        _jellyfin.Series.Add(JellyfinSamples.SeriesWith(OtherPenguinsId, "The Penguins Of Madagascar (Season 1-3)", 2008, OtherFolder, tmdbId: ShowId));
        _jellyfin.Episodes.Add(JellyfinSamples.EpisodeWith(PenguinsEpisodeId, "Gone in a Flash", PenguinsId, 1, 1, "/media/tv/Penguins/S01E01.mkv"));
        _jellyfin.Episodes.Add(JellyfinSamples.EpisodeWith("e0e0e0e0e0e0e0e0e0e0e0e0e0e0e0e0", "Gone in a Flash", OtherPenguinsId, 1, 1, $"{OtherFolder}/S01E01.mkv"));
        await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);
        await _factory.RunQueuedInspectionsAsync(cancellationToken);
        await ConnectTmdbAsync();
        var seriesWorkId = await WorkOfAsync(PenguinsId, cancellationToken);

        //Act
        var response = await _factory.PutHttpRequest(LinkUrl(seriesWorkId), _owner.AccessToken, new LibraryLinkRequest("Series", ShowId));
        var afterSet = await InodesByItemAsync(cancellationToken);
        _factory.Time.Advance(TimeSpan.FromMinutes(1));
        await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(seriesWorkId, await WorkOfAsync(OtherPenguinsId, cancellationToken));
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
    public async Task Set_with_a_request_the_rules_refuse_is_400_with_its_sentence_and_writes_nothing(
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
        await AssertNothingWrittenAsync(sent, cancellationToken);
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
        await AssertNothingWrittenAsync(_factory.JellyfinHttp.Requests.Count, cancellationToken);
    }

    [Fact]
    public async Task Set_on_an_episode_of_a_series_is_400_saying_to_link_its_series()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await SyncPenguinsAsync(cancellationToken);
        var episodeWorkId = await WorkOfAsync(PenguinsEpisodeId, cancellationToken);

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
        await AssertNothingWrittenAsync(_factory.JellyfinHttp.Requests.Count, cancellationToken);
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
        await AssertNothingWrittenAsync(sent, cancellationToken);
    }

    [Fact]
    public async Task Set_without_a_catalog_is_409_saying_to_connect_one()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var jellyfin = await CreateJellyfinAsync();
        _jellyfin.Movies.Add(Cartoon(ShortId, null));
        await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);
        var workId = await WorkOfAsync(ShortId, cancellationToken);

        //Act
        var response = await _factory.PutHttpRequest(LinkUrl(workId), _owner.AccessToken, new LibraryLinkRequest("Movie", FilmId));

        //Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("Connect a metadata catalog under Integrations first.", await response.ReadErrorAsync(cancellationToken));
        await AssertNothingWrittenAsync(_factory.JellyfinHttp.Requests.Count, cancellationToken);
    }

    [Fact]
    public async Task Set_when_tmdb_cannot_be_read_is_502_and_writes_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var (_, workId) = await SyncShortAsync(tmdbId: null);
        FakeTmdbServer.Arm(_factory.TmdbHttp).Intercept = _ => new HttpResponseMessage(HttpStatusCode.InternalServerError);
        var sent = _factory.JellyfinHttp.Requests.Count;

        //Act
        var response = await _factory.PutHttpRequest(LinkUrl(workId), _owner.AccessToken, new LibraryLinkRequest("Movie", FilmId));

        //Assert
        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.NotNull(await response.ReadErrorAsync(cancellationToken));
        await AssertNothingWrittenAsync(sent, cancellationToken);
    }

    [Fact]
    public async Task Set_while_a_write_to_the_work_is_under_way_is_409_and_changes_nothing()
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
        var link = await SingleLinkAsync(cancellationToken);
        Assert.Equal((writing.Id, "300", LibraryLinkWriteState.Writing), (link.Id, link.Value, link.WriteState));
        Assert.Equal(sent, _factory.JellyfinHttp.Requests.Count);
        Assert.Empty(await AuditAsync(cancellationToken));
    }

    [Fact]
    public async Task Set_racing_a_write_that_claimed_the_item_first_is_409_and_leaves_that_write_alone()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var (jellyfin, workId) = await SyncShortAsync(tmdbId: null);
        LibraryItemLinkEntity? racer = null;
        // R9 — the save has read the work and TMDb; another write of the item is saved before its transaction begins.
        _factory.TransactionHook.BeforeNextTransaction(
            _ => true,
            async _ => racer = await _links.Create(jellyfin, ShortId, workId, MediaIdentifierScheme.Tmdb, "300", writeState: LibraryLinkWriteState.Writing));
        var sent = _factory.JellyfinHttp.Requests.Count;

        //Act
        var response = await _factory.PutHttpRequest(LinkUrl(workId), _owner.AccessToken, new LibraryLinkRequest("Movie", FilmId));

        //Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(UnderWayError, await response.ReadErrorAsync(cancellationToken));
        var link = await SingleLinkAsync(cancellationToken);
        Assert.Equal((racer!.Id, "300", LibraryLinkWriteState.Writing), (link.Id, link.Value, link.WriteState));
        Assert.Equal(sent, _factory.JellyfinHttp.Requests.Count);
        Assert.Empty(await AuditAsync(cancellationToken));
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
        var workId = await WorkOfAsync(PlanetEarthEpisodeId, cancellationToken);
        var sent = _factory.JellyfinHttp.Requests.Count;

        //Act
        var response = await _factory.PutHttpRequest(LinkUrl(workId), _owner.AccessToken, new LibraryLinkRequest("Series", ShowId, SeasonNumber: "1"));

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var link = await SingleLinkAsync(cancellationToken);
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
        return (jellyfin, await WorkOfAsync(ShortId, cancellationToken));
    }

    private async Task<Guid> SyncPenguinsAsync(CancellationToken cancellationToken)
    {
        var jellyfin = await CreateJellyfinAsync();
        _jellyfin.Series.Add(JellyfinSamples.SeriesWith(PenguinsId, "The Penguins Of Madagascar", 2008, "/media/tv/Penguins"));
        _jellyfin.Episodes.Add(JellyfinSamples.EpisodeWith(PenguinsEpisodeId, "Gone in a Flash", PenguinsId, 1, 1, "/media/tv/Penguins/S01E01.mkv"));
        await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);
        await ConnectTmdbAsync();
        return await WorkOfAsync(PenguinsId, cancellationToken);
    }

    private static JsonObject Cartoon(string itemId, string? tmdbId, string? imdbId = null) =>
        JellyfinSamples.MovieWith(itemId, "Landing Stripling", null, 1962, $"/media/cartoons/Tom & Jerry/{itemId}.mkv", imdbId, tmdbId);

    private JsonObject Movie(string itemId) => _jellyfin.Movies.Single(movie => (string?)movie["Id"] == itemId);

    private Task SwitchOffAsync(Guid integrationId) =>
        _factory.WithDbAsync(db => db.IntegrationConnections
            .Where(integration => integration.Id == integrationId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(integration => integration.IsEnabled, false)));

    private static async Task<LibraryLinkDialogResponse> ReadDialogAsync(HttpResponseMessage response, CancellationToken cancellationToken) =>
        (await response.Content.ReadFromJsonAsync<LibraryLinkDialogResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;

    private async Task<LibraryItemLinkEntity> SingleLinkAsync(CancellationToken cancellationToken)
    {
        LibraryItemLinkEntity? link = null;
        await _factory.WithDbAsync(async db => link = await db.LibraryItemLinks.AsNoTracking().SingleAsync(cancellationToken));
        return link!;
    }

    // The audit rows in the order they were recorded, each with its ids as "Role Scheme Value" in their order.
    private async Task<List<(AuditAction Action, string? ActorUserName, AuditTargetKind? TargetKind, Guid? TargetId, string? TargetName, string Identifiers)>> AuditAsync(
        CancellationToken cancellationToken)
    {
        var rows = new List<(AuditAction, string?, AuditTargetKind?, Guid?, string?, string)>();
        await _factory.WithDbAsync(async db =>
        {
            var events = (await db.AuditEvents.AsNoTracking().ToListAsync(cancellationToken))
                .OrderBy(auditEvent => auditEvent.CreatedUtc)
                .ThenBy(auditEvent => auditEvent.Action)
                .ToList();
            var identifiers = (await db.AuditEventIdentifiers.AsNoTracking().ToListAsync(cancellationToken)).ToLookup(identifier => identifier.AuditEventId);
            rows.AddRange(events.Select(auditEvent => (
                auditEvent.Action,
                auditEvent.ActorUserName,
                auditEvent.TargetKind,
                auditEvent.TargetId,
                auditEvent.TargetName,
                string.Join(", ", identifiers[auditEvent.Id]
                    .OrderBy(identifier => identifier.Role)
                    .ThenBy(identifier => identifier.Scheme)
                    .Select(identifier => $"{identifier.Role} {identifier.Scheme} {identifier.Value}")))));
        });
        return rows;
    }

    private async Task<Guid> WorkOfAsync(string itemId, CancellationToken cancellationToken)
    {
        var workId = Guid.Empty;
        await _factory.WithDbAsync(async db => workId = (await db.MediaWorkTitles
                .AsNoTracking()
                .Where(title => title.SourceItemId == itemId)
                .Select(title => title.MediaWorkId)
                .Distinct()
                .ToListAsync(cancellationToken))
            .Single());
        return workId;
    }

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

    private async Task AssertNothingWrittenAsync(int sent, CancellationToken cancellationToken)
    {
        Assert.Equal(sent, _factory.JellyfinHttp.Requests.Count);
        await _factory.WithDbAsync(async db =>
        {
            Assert.Equal(0, await db.LibraryItemLinks.CountAsync(cancellationToken));
            Assert.Equal(0, await db.AuditEvents.CountAsync(cancellationToken));
        });
    }
}
