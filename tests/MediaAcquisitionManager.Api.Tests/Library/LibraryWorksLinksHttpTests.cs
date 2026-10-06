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

namespace MediaAcquisitionManager.Api.Tests.Library;

// MAM-34 — the Library page counts an item's TMDb link like the library's own ids (LinkedIdentifiers, ILibraryLinkReader):
// the card lists the linked id with the person who set it as its source — the name for the owner only — and the IMDb id
// TMDb gave where the library holds none (R8); the id a link replaces is hidden while the link isn't confirmed in the
// library (R6), and a library id that is neither stays beside the link as a conflict. A linked work, and an episode of a
// linked series (R16), leaves the missing-ids filter and shows no missing id (decision 9); the search finds a work by
// the ids its card counts. An episode-to-show link counts none of the show's ids as the item's. The links are planted as
// the owner set them; how they are set and written is the links' own tests'.
public sealed class LibraryWorksLinksHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/library/works";
    private const string ApiKey = "0123456789abcdef0123456789abcdef";

    private const string LandingStripling = "120. Landing Stripling";
    private const string Alien = "Чужий";
    private const string PlanetEarthJungles = "Planet_Earth_E08 - Jungles";
    private const string Penguins = "Пінгвіни з Мадагаскару";
    private const string WithoutIds = "Без ідентифікаторів";

    /// <summary>A Tom &amp; Jerry short Jellyfin found no metadata for.</summary>
    private static readonly string LandingStriplingItemId = ItemId('a');

    /// <summary>"Чужий" (1979), which Jellyfin gave a wrong TMDb id, 111, and its right IMDb id.</summary>
    private static readonly string AlienItemId = ItemId('b');

    /// <summary>A Planet Earth episode Jellyfin lists as a film, with a wrong film id, 999.</summary>
    private static readonly string PlanetEarthItemId = ItemId('c');

    /// <summary>A series without ids, with two episodes without ids.</summary>
    private static readonly string PenguinsItemId = ItemId('s');

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private ILibraryItemLinkSeeder _links = null!;
    private AuthorizedUser _owner = null!;
    private AuthorizedUser _member = null!;
    private IntegrationConnectionEntity _livingRoom = null!;

    public LibraryWorksLinksHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _links = new LibraryItemLinkSeeder(_factory.Scope);
        var users = new UserSeeder(_factory.Scope);
        _owner = await users.CreateAuthorizedOwner("owner");
        _member = await users.CreateAuthorized("member");
        var jellyfin = new FakeJellyfinServer { ApiKey = ApiKey };
        jellyfin.Movies.Clear();
        jellyfin.Movies.Add(JellyfinSamples.MovieWith(
            LandingStriplingItemId, LandingStripling, null, 1962, "/media/cartoons/Tom & Jerry/120. Landing Stripling (1962).mkv"));
        jellyfin.Movies.Add(JellyfinSamples.MovieWith(AlienItemId, Alien, "Alien", 1979, "/media/movies/Alien (1979).mkv", "tt0078748", "111"));
        jellyfin.Movies.Add(JellyfinSamples.MovieWith(
            PlanetEarthItemId, PlanetEarthJungles, null, 2006, "/media/documentary/Planet Earth/Planet_Earth_E08 - Jungles.mkv", tmdbId: "999"));
        jellyfin.Movies.Add(JellyfinSamples.MovieWith(
            ItemId('d'), "Тіні забутих предків", null, 1965, "/media/movies/Shadows (1965).mkv", "tt0058642", "38800"));
        jellyfin.Movies.Add(JellyfinSamples.MovieWith(ItemId('e'), WithoutIds, null, 2020, "/media/movies/unknown.mkv"));
        // Both "Чужі" items: one work of two items, sharing Jellyfin's IMDb tt0090605 and TMDb 679.
        jellyfin.Movies.Add(JellyfinSamples.Movie(JellyfinSamples.AliensTheatricalId));
        jellyfin.Movies.Add(JellyfinSamples.Movie(JellyfinSamples.AliensSpecialEditionId));
        jellyfin.Series.Add(JellyfinSamples.SeriesWith(PenguinsItemId, Penguins, 2008, "/media/cartoons/Penguins"));
        jellyfin.Episodes.Add(JellyfinSamples.EpisodeWith(ItemId('t'), "Gone in a Flash", PenguinsItemId, 1, 1, "/media/cartoons/Penguins/S01E01.mkv"));
        jellyfin.Episodes.Add(JellyfinSamples.EpisodeWith(ItemId('u'), "Popcorn Panic", PenguinsItemId, 1, 2, "/media/cartoons/Penguins/S01E02.mkv"));
        jellyfin.Arm(_factory.JellyfinHttp);
        _livingRoom = await new IntegrationConnectionSeeder(_factory.Scope).Create("jellyfin", "Living room", apiKey: ApiKey);
        await new LibrarySnapshotSeeder(_factory.Scope).Sync(_livingRoom, _owner.User);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Works_lists_a_linked_id_with_who_set_it_and_the_imdb_id_tmdb_gave_for_the_owner()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var link = await LinkLandingStriplingAsync(LibraryLinkWriteState.NotWritten, cancellationToken);

        //Act
        var work = await CardAsync(LandingStripling, _owner, cancellationToken);

        //Assert
        Assert.Equal(
            [
                new LibraryIdentifierDto(MediaIdentifierScheme.Imdb, "tt0056164", "TMDb", ProvenanceSourceKind.Metadata),
                new LibraryIdentifierDto(MediaIdentifierScheme.Tmdb, "223021", "owner", ProvenanceSourceKind.Person, link.SetUtc),
            ],
            work.Identifiers);
        Assert.Empty(work.MissingIdentifiers);
        Assert.Equal(
            new LibraryWorkLinkDto(
                LibraryLinkTarget.Work, MediaWorkKind.Movie, "223021", "Посадка", null, null, "tt0056164", "owner", link.SetUtc,
                LibraryLinkWriteState.NotWritten, "Jellyfin refused to change the item's ids.", null, Identified: false, UnderWay: false),
            work.Link);
    }

    [Fact]
    public async Task Works_lists_a_linked_id_without_the_name_of_who_set_it_or_the_write_error_for_a_member()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var link = await LinkLandingStriplingAsync(LibraryLinkWriteState.NotWritten, cancellationToken);

        //Act
        var work = await CardAsync(LandingStripling, _member, cancellationToken);

        //Assert
        // "Set by hand": the owner's sign-in name is the owner's only, as is the sentence about the library.
        Assert.Contains(new LibraryIdentifierDto(MediaIdentifierScheme.Tmdb, "223021", null, ProvenanceSourceKind.Person, link.SetUtc), work.Identifiers);
        Assert.Null(work.Link!.SetByUserName);
        Assert.Null(work.Link.WriteError);
        Assert.Equal(LibraryLinkWriteState.NotWritten, work.Link.WriteState);
    }

    [Fact]
    public async Task Works_missing_an_identifier_leaves_out_linked_works_whatever_ids_their_links_give()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var before = await ListAsync("?identifiers=Missing", _owner, cancellationToken);
        // A link without an IMDb id still lacks one, and a link to a show gives the film item no film id: neither is
        // missing anything once the owner said what it is.
        await _links.Create(_livingRoom, LandingStriplingItemId, await WorkOfItemAsync(LandingStriplingItemId, cancellationToken),
            MediaIdentifierScheme.Tmdb, "223021", setBy: _owner.User);
        await LinkPlanetEarthAsync(cancellationToken);

        //Act
        var missing = await ListAsync("?identifiers=Missing", _owner, cancellationToken);
        var cards = await ListAsync("?search=landing", _owner, cancellationToken);

        //Assert
        Assert.Equal([LandingStripling, WithoutIds, Penguins, PlanetEarthJungles], before.Items.Select(work => work.Title));
        Assert.Equal([WithoutIds, Penguins], missing.Items.Select(work => work.Title));
        Assert.Empty(Assert.Single(cards.Items).MissingIdentifiers);
    }

    [Fact]
    public async Task Works_missing_an_identifier_leaves_out_a_linked_series_and_its_episodes()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var penguins = await WorkOfItemAsync(PenguinsItemId, cancellationToken);
        var link = await _links.Create(_livingRoom, PenguinsItemId, penguins, MediaIdentifierScheme.TmdbTv, "7869", setBy: _owner.User);

        //Act
        var missing = await ListAsync("?identifiers=Missing", _owner, cancellationToken);
        var series = Assert.Single((await ListAsync($"?search={Uri.EscapeDataString(Penguins)}", _owner, cancellationToken)).Items);
        var episodes = await ListAsync($"/{penguins}/episodes", _owner, cancellationToken);
        var missingEpisodes = await ListAsync($"/{penguins}/episodes?identifiers=Missing", _owner, cancellationToken);

        //Assert
        // R16 — an episode is known by its series' ids, so neither the series nor an episode of it is missing one.
        Assert.Equal([LandingStripling, WithoutIds, PlanetEarthJungles], missing.Items.Select(work => work.Title));
        Assert.Equal([new LibraryIdentifierDto(MediaIdentifierScheme.TmdbTv, "7869", "owner", ProvenanceSourceKind.Person, link.SetUtc)], series.Identifiers);
        Assert.Empty(series.MissingIdentifiers);
        Assert.Equal(2, episodes.TotalCount);
        Assert.All(episodes.Items, episode => Assert.Empty(episode.MissingIdentifiers));
        Assert.Equal(0, missingEpisodes.TotalCount);
    }

    [Theory]
    [InlineData("223021")]
    [InlineData("TT0056164")]
    public async Task Works_search_finds_a_work_by_its_linked_id_or_the_imdb_id_tmdb_gave(string search)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await LinkLandingStriplingAsync(LibraryLinkWriteState.KeptInApp, cancellationToken);

        //Act
        var list = await ListAsync($"?search={search}", _member, cancellationToken);

        //Assert
        Assert.Equal([LandingStripling], list.Items.Select(work => work.Title));
    }

    [Fact]
    public async Task Works_counts_the_imdb_id_tmdb_gave_nowhere_the_library_holds_its_own()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await LinkAlienAsync(LibraryLinkWriteState.KeptInApp, supersededValue: "111", imdbId: "tt9999999", cancellationToken);

        //Act
        var work = await CardAsync(Alien, _owner, cancellationToken);
        var found = await ListAsync("?search=tt9999999", _owner, cancellationToken);

        //Assert
        // R8 — the library's IMDb id wins: TMDb's is shown on the link line only, and found by nothing.
        Assert.Equal(["tt0078748"], work.Identifiers.Where(identifier => identifier.Scheme == MediaIdentifierScheme.Imdb).Select(identifier => identifier.Value));
        Assert.Equal("tt9999999", work.Link!.ImdbId);
        Assert.Equal(0, found.TotalCount);
    }

    [Theory]
    [InlineData(LibraryLinkWriteState.KeptInApp, false)]
    [InlineData(LibraryLinkWriteState.Queued, false)]
    [InlineData(LibraryLinkWriteState.Writing, false)]
    [InlineData(LibraryLinkWriteState.NotWritten, false)]
    [InlineData(LibraryLinkWriteState.Written, true)]
    [InlineData(LibraryLinkWriteState.InLibrary, true)]
    [InlineData(LibraryLinkWriteState.Lost, true)]
    public async Task Works_hides_the_id_a_link_replaces_only_until_the_library_has_taken_the_link(LibraryLinkWriteState state, bool shown)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await LinkAlienAsync(state, supersededValue: "111", imdbId: null, cancellationToken);

        //Act
        var work = await CardAsync(Alien, _owner, cancellationToken);
        var found = await ListAsync("?search=111", _owner, cancellationToken);

        //Assert
        // R6 — once the library took the link's id, a different id there is the library's word again, and conflicts.
        Assert.Equal(
            shown ? new[] { "111", "348" } : ["348"],
            work.Identifiers.Where(identifier => identifier.Scheme == MediaIdentifierScheme.Tmdb).Select(identifier => identifier.Value).Distinct());
        Assert.Equal(shown ? "111" : null, work.Link!.ConflictingValue);
        Assert.Equal(shown ? new[] { Alien } : [], found.Items.Select(item => item.Title));
    }

    [Fact]
    public async Task Works_shows_a_library_id_the_link_did_not_replace_beside_it_as_a_conflict()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // Jellyfin's 111 isn't the id the link replaced: someone changed Jellyfin since, or the link replaced none.
        var link = await LinkAlienAsync(LibraryLinkWriteState.KeptInApp, supersededValue: null, imdbId: null, cancellationToken);

        //Act
        var work = await CardAsync(Alien, _owner, cancellationToken);

        //Assert
        Assert.Equal(
            [
                new LibraryIdentifierDto(MediaIdentifierScheme.Imdb, "tt0078748", "Living room"),
                new LibraryIdentifierDto(MediaIdentifierScheme.Tmdb, "111", "Living room"),
                new LibraryIdentifierDto(MediaIdentifierScheme.Tmdb, "348", "owner", ProvenanceSourceKind.Person, link.SetUtc),
            ],
            work.Identifiers);
        Assert.Equal("111", work.Link!.ConflictingValue);
    }

    [Fact]
    public async Task Works_lists_an_episode_to_show_link_without_the_shows_ids_or_the_film_id_it_replaces()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var link = await LinkPlanetEarthAsync(cancellationToken);

        //Act
        var work = await CardAsync("planet_earth", _owner, cancellationToken);
        var byShow = await ListAsync("?search=1044", _owner, cancellationToken);
        var byFilm = await ListAsync("?search=999", _owner, cancellationToken);

        //Assert
        // R7 — the film id Jellyfin wrongly gave the item gives way, and the show's ids are the show's, not the item's.
        Assert.Empty(work.Identifiers);
        Assert.Empty(work.MissingIdentifiers);
        Assert.Equal(
            new LibraryWorkLinkDto(
                LibraryLinkTarget.ShowEpisode, MediaWorkKind.Series, "1044", "Планета Земля", 1, 8, "tt0795176", "owner", link.SetUtc,
                LibraryLinkWriteState.KeptInApp, null, null, Identified: false, UnderWay: false),
            work.Link);
        Assert.Equal((0, 0), (byShow.TotalCount, byFilm.TotalCount));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("555")]
    public async Task Works_shows_a_film_id_jellyfin_gave_an_episode_linked_item_after_its_link_as_a_conflict(string? replaced)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // The link was set while the item held no film id, or another one: Jellyfin's 999 came after (a refresh).
        var link = await LinkPlanetEarthAsync(cancellationToken, supersededValue: replaced);

        //Act
        var work = await CardAsync("planet_earth", _owner, cancellationToken);

        //Assert
        // Neither wins: the film id stays, and the card flags it beside the owner's episode link.
        Assert.Equal([new LibraryIdentifierDto(MediaIdentifierScheme.Tmdb, "999", "Living room")], work.Identifiers);
        Assert.Equal((LibraryLinkTarget.ShowEpisode, link.Value, (string?)"999"), (work.Link!.Target, work.Link.CatalogId, work.Link.ConflictingValue));
    }

    [Fact]
    public async Task Works_shows_the_worst_state_of_the_links_of_a_works_items_and_their_id_once()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var aliens = await WorkOfItemAsync(JellyfinSamples.AliensTheatricalId, cancellationToken);
        var theatrical = await _links.Create(
            _livingRoom, JellyfinSamples.AliensTheatricalId, aliens, MediaIdentifierScheme.Tmdb, "679", writeState: LibraryLinkWriteState.InLibrary, setBy: _owner.User);
        await _links.Create(
            _livingRoom, JellyfinSamples.AliensSpecialEditionId, aliens, MediaIdentifierScheme.Tmdb, "679", writeState: LibraryLinkWriteState.NotWritten, setBy: _owner.User,
            configure: link => link.WriteError = "The special edition's library refused the ids.");

        //Act
        var work = await CardAsync("Чужі", _owner, cancellationToken);

        //Assert
        // Jellyfin's 679 and the link's 679: two claims of one id, no conflict; the two items' links are one claim.
        Assert.Equal(
            [
                new LibraryIdentifierDto(MediaIdentifierScheme.Imdb, "tt0090605", "Living room"),
                new LibraryIdentifierDto(MediaIdentifierScheme.Tmdb, "679", "Living room"),
                new LibraryIdentifierDto(MediaIdentifierScheme.Tmdb, "679", "owner", ProvenanceSourceKind.Person, theatrical.SetUtc),
            ],
            work.Identifiers);
        Assert.Equal(
            (LibraryLinkWriteState.NotWritten, "The special edition's library refused the ids."),
            (work.Link!.WriteState, work.Link.WriteError));
        Assert.Null(work.Link.ConflictingValue);
    }

    [Fact]
    public async Task Works_say_whether_their_links_are_identified_and_whether_one_is_under_way()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var now = _factory.Time.GetUtcNow().UtcDateTime;
        // Identified: the app confirmed the item shows the link.
        await _links.Create(
            _livingRoom, LandingStriplingItemId, await WorkOfItemAsync(LandingStriplingItemId, cancellationToken), MediaIdentifierScheme.Tmdb, "223021",
            writeState: LibraryLinkWriteState.Written, setBy: _owner.User, configure: link => link.IdentifiedUtc = now);
        // Not identified yet: a link the earlier ids-only rule wrote reads "in the library" but was never confirmed.
        await LinkAlienAsync(LibraryLinkWriteState.Written, supersededValue: null, imdbId: null, cancellationToken);
        // Under way beside a refusal: the card's state is the worst of the two, so only the flag says one is waiting.
        var aliens = await WorkOfItemAsync(JellyfinSamples.AliensTheatricalId, cancellationToken);
        await _links.Create(
            _livingRoom, JellyfinSamples.AliensTheatricalId, aliens, MediaIdentifierScheme.Tmdb, "679", writeState: LibraryLinkWriteState.Queued, setBy: _owner.User,
            configure: link => link.IdentifiedUtc = now);
        await _links.Create(
            _livingRoom, JellyfinSamples.AliensSpecialEditionId, aliens, MediaIdentifierScheme.Tmdb, "679", writeState: LibraryLinkWriteState.NotWritten, setBy: _owner.User,
            configure: link => link.WriteError = "The special edition's library refused the match.");

        //Act
        var landing = await CardAsync(LandingStripling, _owner, cancellationToken);
        var alien = await CardAsync(Alien, _owner, cancellationToken);
        var both = await CardAsync("Чужі", _owner, cancellationToken);
        var asMember = await CardAsync("Чужі", _member, cancellationToken);

        //Assert
        Assert.Equal((true, false), (landing.Link!.Identified, landing.Link.UnderWay));
        Assert.Equal((false, false), (alien.Link!.Identified, alien.Link.UnderWay));
        Assert.Equal((LibraryLinkWriteState.NotWritten, false, true), (both.Link!.WriteState, both.Link.Identified, both.Link.UnderWay));
        // They carry no secret: a member reads them as the owner does.
        Assert.Equal((false, true), (asMember.Link!.Identified, asMember.Link.UnderWay));
    }

    [Fact]
    public async Task Works_say_a_work_is_identified_only_when_every_item_the_library_holds_has_been_confirmed()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var now = _factory.Time.GetUtcNow().UtcDateTime;
        var aliens = await WorkOfItemAsync(JellyfinSamples.AliensTheatricalId, cancellationToken);
        await _links.Create(
            _livingRoom, JellyfinSamples.AliensTheatricalId, aliens, MediaIdentifierScheme.Tmdb, "679", writeState: LibraryLinkWriteState.InLibrary, setBy: _owner.User,
            configure: link => link.IdentifiedUtc = now);
        var special = await _links.Create(
            _livingRoom, JellyfinSamples.AliensSpecialEditionId, aliens, MediaIdentifierScheme.Tmdb, "679", writeState: LibraryLinkWriteState.Written, setBy: _owner.User);

        //Act
        var oneUnconfirmed = await CardAsync("Чужі", _owner, cancellationToken);
        await _factory.WithDbAsync(db => db.LibraryItemLinks
            .Where(link => link.Id == special.Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(link => link.IdentifiedUtc, (DateTime?)now), cancellationToken));
        var bothConfirmed = await CardAsync("Чужі", _owner, cancellationToken);

        //Assert
        Assert.False(oneUnconfirmed.Link!.Identified);
        Assert.True(bothConfirmed.Link!.Identified);
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>The short linked to TMDb's 223021, with the IMDb id TMDb gave, in <paramref name="state"/>.</summary>
    private async Task<LibraryItemLinkEntity> LinkLandingStriplingAsync(LibraryLinkWriteState state, CancellationToken cancellationToken) =>
        await _links.Create(
            _livingRoom, LandingStriplingItemId, await WorkOfItemAsync(LandingStriplingItemId, cancellationToken), MediaIdentifierScheme.Tmdb, "223021",
            writeState: state, setBy: _owner.User,
            configure: link =>
            {
                link.CatalogTitle = "Посадка";
                link.ImdbId = "tt0056164";
                link.ImdbSourceName = "TMDb";
                link.ImdbObservedUtc = link.SetUtc;
                link.WriteError = state == LibraryLinkWriteState.NotWritten ? "Jellyfin refused to change the item's ids." : null;
            });

    /// <summary>"Чужий" linked to TMDb's 348 in <paramref name="state"/>, replacing <paramref name="supersededValue"/>.</summary>
    private async Task<LibraryItemLinkEntity> LinkAlienAsync(
        LibraryLinkWriteState state, string? supersededValue, string? imdbId, CancellationToken cancellationToken) =>
        await _links.Create(
            _livingRoom, AlienItemId, await WorkOfItemAsync(AlienItemId, cancellationToken), MediaIdentifierScheme.Tmdb, "348",
            writeState: state, setBy: _owner.User,
            configure: link =>
            {
                link.SupersededValue = supersededValue;
                link.ImdbId = imdbId;
                link.ImdbSourceName = imdbId is null ? null : "TMDb";
            });

    /// <summary>The Planet Earth episode linked to the show 1044, season 1, episode 8 — replacing Jellyfin's film id,
    /// unless <paramref name="supersededValue"/> names another —, kept in the app as every episode-to-show link is.</summary>
    private async Task<LibraryItemLinkEntity> LinkPlanetEarthAsync(CancellationToken cancellationToken, string? supersededValue = "999") =>
        await _links.Create(
            _livingRoom, PlanetEarthItemId, await WorkOfItemAsync(PlanetEarthItemId, cancellationToken), MediaIdentifierScheme.TmdbTv, "1044",
            LibraryLinkTarget.ShowEpisode, LibraryLinkWriteState.KeptInApp, _owner.User,
            configure: link =>
            {
                link.SeasonNumber = 1;
                link.EpisodeNumber = 8;
                link.CatalogTitle = "Планета Земля";
                link.ImdbId = "tt0795176";
                link.ImdbSourceName = "TMDb";
                link.SupersededValue = supersededValue;
            });

    /// <summary>The work the library item <paramref name="itemId"/> sits on.</summary>
    private async Task<Guid> WorkOfItemAsync(string itemId, CancellationToken cancellationToken)
    {
        var workId = Guid.Empty;
        await _factory.WithDbAsync(async db => workId = await db.MediaWorkTitles
            .Where(title => title.SourceItemId == itemId)
            .Select(title => title.MediaWorkId)
            .Distinct()
            .SingleAsync(cancellationToken));
        return workId;
    }

    /// <summary>The one card a search for <paramref name="search"/> lists to <paramref name="user"/>.</summary>
    private async Task<LibraryWorkDto> CardAsync(string search, AuthorizedUser user, CancellationToken cancellationToken) =>
        Assert.Single((await ListAsync($"?search={Uri.EscapeDataString(search)}", user, cancellationToken)).Items);

    /// <summary>The works list (or a series' episodes) at <paramref name="query"/>, as <paramref name="user"/> reads
    /// it; throws unless it was listed.</summary>
    private async Task<LibraryWorkListResponse> ListAsync(string query, AuthorizedUser user, CancellationToken cancellationToken)
    {
        var response = await _factory.GetHttpRequest(BaseUrl + query, user.AccessToken);
        if (response.StatusCode != HttpStatusCode.OK)
            throw new InvalidOperationException($"The list answered {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync(cancellationToken)}");
        return (await response.Content.ReadFromJsonAsync<LibraryWorkListResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
    }

    private static string ItemId(char letter) => new(letter, 32);
}
