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

// MAM-34 — a work's TMDb dialog (GET /api/library/works/{id}/tmdb): the work as its card shows it, each library item it
// holds with the ids the last sync read for it, its link, where the link stands and when the app confirmed it identified,
// whether the item's library would identify it and why not (an episode item, a library type that can't identify, one
// switched off — a series is identified like a film, with no warning of any kind; an episode's link of its own in
// LibraryLinkEpisodeHttpTests), the third TMDb id Jellyfin holds beside a link, whether a catalog is connected, and the
// title and year the free search starts from. Reading calls nothing outside the app.
public sealed class LibraryLinkDialogHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/library";
    private const string ApiKey = "0123456789abcdef0123456789abcdef";
    private const string AliensTheatricalId = "a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1";
    private const string AliensSpecialId = "b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2";
    private const string TomAndJerryKidsId = "c3c3c3c3c3c3c3c3c3c3c3c3c3c3c3c3";
    private const string PenguinsId = "d4d4d4d4d4d4d4d4d4d4d4d4d4d4d4d4";
    private const string LandingId = "f6f6f6f6f6f6f6f6f6f6f6f6f6f6f6f6";
    private const string JunglesId = "a7a7a7a7a7a7a7a7a7a7a7a7a7a7a7a7";
    private const string AliensTmdbId = "679";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private IIntegrationConnectionSeeder _integrations = null!;
    private ILibraryItemLinkSeeder _links = null!;
    private AuthorizedUser _owner = null!;
    private FakeJellyfinServer _jellyfin = null!;
    private IntegrationConnectionEntity _jellyfinIntegration = null!;

    public LibraryLinkDialogHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _integrations = new IntegrationConnectionSeeder(_factory.Scope);
        _links = new LibraryItemLinkSeeder(_factory.Scope);
        _owner = await new UserSeeder(_factory.Scope).CreateAuthorizedOwner("owner");
        _jellyfin = new FakeJellyfinServer { ApiKey = ApiKey };
        _jellyfin.Movies.Clear();
        _jellyfin.Arm(_factory.JellyfinHttp);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Dialog_shows_each_item_of_the_work_with_its_library_ids_its_link_and_a_third_id_jellyfin_holds()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // The two "Чужі": one work, two items; one linked to another film while Jellyfin still holds its own id.
        _jellyfinIntegration = await _integrations.Create("jellyfin", "Living room", apiKey: ApiKey);
        _jellyfin.Movies.Add(JellyfinSamples.MovieWith(AliensTheatricalId, "Чужі", "Aliens", 1986, "/media/movies/Aliens.Theatrical.mkv", "tt0090605", AliensTmdbId));
        _jellyfin.Movies.Add(JellyfinSamples.MovieWith(AliensSpecialId, "Чужі", "Aliens", 1986, "/media/movies/Aliens.Special.mkv", "tt0090605", AliensTmdbId));
        await _factory.SyncLibraryAsync(_jellyfinIntegration.Id, _owner.AccessToken, cancellationToken);
        await _integrations.Create("tmdb", "TMDb", FakeTmdbServer.BaseUrl, apiKey: FakeTmdbServer.Token);
        var workId = await WorkOfAsync(AliensSpecialId, cancellationToken);
        var identifiedAt = _factory.Time.GetUtcNow().UtcDateTime;
        var link = await _links.Create(
            _jellyfinIntegration, AliensSpecialId, workId, MediaIdentifierScheme.Tmdb, "8077", writeState: LibraryLinkWriteState.InLibrary,
            configure: row => (row.CatalogTitle, row.IdentifiedUtc) = ("Чужий 3", identifiedAt));
        var sent = _factory.JellyfinHttp.Requests.Count;

        //Act
        var response = await _factory.GetHttpRequest(DialogUrl(workId), _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dialog = await ReadDialogAsync(response, cancellationToken);
        Assert.Equal((workId, "Чужі", true), (dialog.Work.Id, dialog.Work.Title, dialog.CatalogConnected));
        Assert.Equal(
            [(AliensTheatricalId, (Guid?)null, (string?)null), (AliensSpecialId, link.Id, "8077")],
            dialog.Items.Select(item => (item.SourceItemId, item.LinkId, item.CatalogId)));
        Assert.All(dialog.Items, item => Assert.Equal(("Living room", "Чужі", MediaWorkKind.Movie, true, (string?)null), (item.SourceName!, item.ItemTitle!, item.ItemKind, item.CanWrite, item.CannotWriteReason)));
        var linked = dialog.Items[1];
        Assert.Equal(
            ((LibraryLinkTarget?)LibraryLinkTarget.Work, "Чужий 3", (LibraryLinkWriteState?)LibraryLinkWriteState.InLibrary, (string?)AliensTmdbId, (DateTime?)identifiedAt),
            (linked.Target, linked.CatalogTitle!, linked.WriteState, linked.ConflictingValue, linked.IdentifiedUtc));
        Assert.Null(dialog.Items[0].IdentifiedUtc);
        Assert.Equal(
            [(MediaIdentifierScheme.Imdb, "tt0090605", ProvenanceSourceKind.MediaLibrary), (MediaIdentifierScheme.Tmdb, AliensTmdbId, ProvenanceSourceKind.MediaLibrary)],
            linked.LibraryIdentifiers.Select(identifier => (identifier.Scheme, identifier.Value, identifier.SourceKind)));
        Assert.Empty(dialog.Suggestions);
        Assert.Equal(sent, _factory.JellyfinHttp.Requests.Count);
    }

    [Fact]
    public async Task Dialog_says_an_item_of_a_library_switched_off_would_not_be_identified_and_why()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        _jellyfinIntegration = await _integrations.Create("jellyfin", "Living room", apiKey: ApiKey);
        _jellyfin.Movies.Add(JellyfinSamples.MovieWith(AliensTheatricalId, "Чужі", "Aliens", 1986, "/media/movies/Aliens.mkv"));
        await _factory.SyncLibraryAsync(_jellyfinIntegration.Id, _owner.AccessToken, cancellationToken);
        await _factory.WithDbAsync(db => db.IntegrationConnections
            .Where(integration => integration.Id == _jellyfinIntegration.Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(integration => integration.IsEnabled, false), cancellationToken));
        var workId = await WorkOfAsync(AliensTheatricalId, cancellationToken);

        //Act
        var response = await _factory.GetHttpRequest(DialogUrl(workId), _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dialog = await ReadDialogAsync(response, cancellationToken);
        var item = Assert.Single(dialog.Items);
        // MAM-47 — the sentence's key beside it: the kind of reason, which the dialog compares.
        Assert.Equal(
            (false, (string?)"“Living room” is switched off. Switch it on under Integrations, then retry.", (string?)"library-links.write.switched-off"),
            (item.CanWrite, item.CannotWriteReason, item.CannotWriteReasonCode));
        // No catalog connected: nothing can be linked yet.
        Assert.False(dialog.CatalogConnected);
    }

    [Fact]
    public async Task Dialog_says_an_episode_with_no_series_is_never_identified()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        _jellyfinIntegration = await _integrations.Create("jellyfin", "Living room", apiKey: ApiKey);
        _jellyfin.Episodes.Add(JellyfinSamples.EpisodeWith(TomAndJerryKidsId, "Tom & Jerry Kids 01", null, null, null, "/media/cartoons/Tom & Jerry Kids/01.mkv"));
        await _factory.SyncLibraryAsync(_jellyfinIntegration.Id, _owner.AccessToken, cancellationToken);
        var workId = await WorkOfAsync(TomAndJerryKidsId, cancellationToken);

        //Act
        var response = await _factory.GetHttpRequest(DialogUrl(workId), _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var item = Assert.Single((await ReadDialogAsync(response, cancellationToken)).Items);
        Assert.Equal(
            (MediaWorkKind.Episode, false, (string?)"Jellyfin identifies movie and series items only, so a link on an episode stays in the app.",
                (string?)"jellyfin.identify.episode-unidentifiable"),
            (item.ItemKind, item.CanWrite, item.CannotWriteReason, item.CannotWriteReasonCode));
    }

    [Fact]
    public async Task Dialog_says_an_item_of_a_library_type_that_cannot_identify_would_not_be_identified_and_why()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // The fake type is a media library without the identify capability (ADR 0005).
        var library = await _integrations.Create(name: "Old library");
        var work = await new MediaWorkSeeder(_factory.Scope).CreateMovie(library, "Landing Stripling", 1962);

        //Act
        var response = await _factory.GetHttpRequest(DialogUrl(work.Id), _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var item = Assert.Single((await ReadDialogAsync(response, cancellationToken)).Items);
        Assert.Equal(
            (false, (string?)"“Old library” can't identify items, so a link stays in the app.", (string?)"library-links.kept.no-identifier"),
            (item.CanWrite, item.CannotWriteReason, item.CannotWriteReasonCode));
    }

    [Fact]
    public async Task Dialog_says_a_series_is_identified_like_a_film_with_no_reason_against_it()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        _jellyfinIntegration = await _integrations.Create("jellyfin", "Living room", apiKey: ApiKey);
        _jellyfin.Series.Add(JellyfinSamples.SeriesWith(PenguinsId, "The Penguins Of Madagascar", 2008, "/media/tv/Penguins"));
        _jellyfin.Episodes.Add(JellyfinSamples.EpisodeWith("e5e5e5e5e5e5e5e5e5e5e5e5e5e5e5e5", "Gone in a Flash", PenguinsId, 1, 1, "/media/tv/Penguins/S01E01.mkv"));
        await _factory.SyncLibraryAsync(_jellyfinIntegration.Id, _owner.AccessToken, cancellationToken);
        var workId = await WorkOfAsync(PenguinsId, cancellationToken);
        var sent = _factory.JellyfinHttp.Requests.Count;

        //Act
        var response = await _factory.GetHttpRequest(DialogUrl(workId), _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var item = Assert.Single((await ReadDialogAsync(response, cancellationToken)).Items);
        // Jellyfin identifies a series (Vasyl's final decision of 2026-10-06): the time its Apply takes is no reason the
        // owner is shown — the identification runs in the background.
        Assert.Equal(
            (PenguinsId, MediaWorkKind.Series, true, (string?)null, (string?)null),
            (item.SourceItemId, item.ItemKind, item.CanWrite, item.CannotWriteReason, item.CannotWriteReasonCode));
        Assert.Equal(sent, _factory.JellyfinHttp.Requests.Count);
    }

    [Fact]
    public async Task Dialog_starts_the_free_search_from_the_title_cleaned_as_the_suggestions_are_and_the_year_its_brackets_name_first()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // The reference library's titles TMDb finds nothing by as they stand (the browser check of 2026-10-06).
        _jellyfinIntegration = await _integrations.Create("jellyfin", "Living room", apiKey: ApiKey);
        _jellyfin.Movies.Add(JellyfinSamples.MovieWith(LandingId, "120. Landing Stripling", null, 1962, "/media/cartoons/120. Landing Stripling (1962).mkv"));
        var junglesItem = JellyfinSamples.MovieWith(JunglesId, "Planet_Earth_E08 - Jungles", null, 2006, "/media/documentary/Planet Earth/Planet_Earth_E08 - Jungles.mkv");
        junglesItem.Remove("ProductionYear");
        _jellyfin.Movies.Add(junglesItem);
        // Named after its folder, and given the span's last year by Jellyfin (R23).
        _jellyfin.Series.Add(JellyfinSamples.SeriesWith(
            PenguinsId, "The Penguins Of Madagascar (Season 1-3) (2008-2015) WEBDL 1080p [Ukr,Eng] Sub [Eng] [ Hurtom ] by Jatx 11", 2015, "/media/tv/Penguins"));
        _jellyfin.Episodes.Add(JellyfinSamples.EpisodeWith("e5e5e5e5e5e5e5e5e5e5e5e5e5e5e5e5", "Gone in a Flash", PenguinsId, 1, 1, "/media/tv/Penguins/S01E01.mkv"));
        await _factory.SyncLibraryAsync(_jellyfinIntegration.Id, _owner.AccessToken, cancellationToken);
        var landingWorkId = await WorkOfAsync(LandingId, cancellationToken);
        var junglesWorkId = await WorkOfAsync(JunglesId, cancellationToken);
        var penguinsWorkId = await WorkOfAsync(PenguinsId, cancellationToken);

        //Act
        var landingResponse = await _factory.GetHttpRequest(DialogUrl(landingWorkId), _owner.AccessToken);
        var junglesResponse = await _factory.GetHttpRequest(DialogUrl(junglesWorkId), _owner.AccessToken);
        var penguinsResponse = await _factory.GetHttpRequest(DialogUrl(penguinsWorkId), _owner.AccessToken);

        //Assert
        Assert.Equal(
            (HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.OK),
            (landingResponse.StatusCode, junglesResponse.StatusCode, penguinsResponse.StatusCode));
        var landing = await ReadDialogAsync(landingResponse, cancellationToken);
        var jungles = await ReadDialogAsync(junglesResponse, cancellationToken);
        var penguins = await ReadDialogAsync(penguinsResponse, cancellationToken);
        Assert.Equal(("Landing Stripling", (int?)1962), (landing.SearchTitle!, landing.SearchYear));
        Assert.Equal(("Planet Earth", (int?)null), (jungles.SearchTitle!, jungles.SearchYear));
        // The first year of the span the title names, not the library's: TMDb's year filter finds the show by 2008 only.
        Assert.Equal(("The Penguins Of Madagascar", (int?)2008), (penguins.SearchTitle!, penguins.SearchYear));
        Assert.Equal((int?)2015, penguins.Work.Year);
    }

    [Fact]
    public async Task Dialog_of_an_unknown_work_is_a_bare_404()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var response = await _factory.GetHttpRequest(DialogUrl(Guid.NewGuid()), _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsStringAsync(cancellationToken));
    }

    [Fact]
    public async Task Dialog_without_a_token_is_401()
    {
        //Arrange

        //Act
        var response = await _factory.GetHttpRequest(DialogUrl(Guid.NewGuid()));

        //Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    private static string DialogUrl(Guid workId) => $"{BaseUrl}/works/{workId}/tmdb";

    private static async Task<LibraryLinkDialogResponse> ReadDialogAsync(HttpResponseMessage response, CancellationToken cancellationToken) =>
        (await response.Content.ReadFromJsonAsync<LibraryLinkDialogResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;

    private async Task<Guid> WorkOfAsync(string itemId, CancellationToken cancellationToken)
    {
        var workId = Guid.Empty;
        await _factory.WithDbAsync(async db => workId = await db.MediaWorkTitles
            .Where(title => title.SourceItemId == itemId)
            .Select(title => title.MediaWorkId)
            .FirstAsync(cancellationToken));
        return workId;
    }
}
