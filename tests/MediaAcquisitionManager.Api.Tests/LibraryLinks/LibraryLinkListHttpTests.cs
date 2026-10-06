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

// MAM-34 — the TMDb page's lists of links (GET /api/library/tmdb/links?filter=…): the links whose item the library no
// longer reports, those waiting for or under an identification, those whose item Jellyfin gave a third TMDb id, those not
// identified (attached ones only: an unattached link has no card to open and is listed under Unattached) and those
// Jellyfin lost — filtered, counted and paged by the server, newest first, each with its work, its library, its conflict
// and when the app confirmed it identified. A missing or unknown filter, or a bad page, is a 400 with its sentence.
public sealed class LibraryLinkListHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string LinksUrl = "/api/library/tmdb/links";
    private const string ApiKey = "0123456789abcdef0123456789abcdef";
    private const string ShortId = JellyfinSamples.LandingStriplingId;
    private const string OtherShortId = "b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2";
    private const string FilmId = JellyfinSamples.LandingStriplingTmdbId;

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private IIntegrationConnectionSeeder _integrations = null!;
    private ILibraryItemLinkSeeder _links = null!;
    private AuthorizedUser _owner = null!;
    private FakeJellyfinServer _jellyfin = null!;
    private IntegrationConnectionEntity _jellyfinIntegration = null!;

    public LibraryLinkListHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

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
    public async Task List_of_unattached_links_pages_them_newest_first_with_the_title_their_item_had()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var workId = await SyncAsync(cancellationToken, (ShortId, null));
        await _links.Create(_jellyfinIntegration, ShortId, workId, MediaIdentifierScheme.Tmdb, FilmId);
        var oldest = await GoneAsync("01", "Calypso Cat");
        _factory.Time.Advance(TimeSpan.FromMinutes(1));
        var middle = await GoneAsync("02", "Mice Follies");
        _factory.Time.Advance(TimeSpan.FromMinutes(1));
        var newest = await GoneAsync("03", "Cruise Cat");

        //Act
        var first = await ReadListAsync($"{LinksUrl}?filter=unattached&pageSize=2", cancellationToken);
        var second = await ReadListAsync($"{LinksUrl}?filter=Unattached&page=2&pageSize=2", cancellationToken);

        //Assert
        Assert.Equal((3, 1, 2), (first.TotalCount, first.Page, first.PageSize));
        Assert.Equal([newest.Id, middle.Id], first.Items.Select(row => row.LinkId));
        Assert.Equal([oldest.Id], second.Items.Select(row => row.LinkId));
        var row = first.Items[0];
        Assert.Equal(
            ((Guid?)null, (string?)null, "Living room", "Cruise Cat", MediaWorkKind.Movie, FilmId, "owner"),
            (row.WorkId, row.WorkTitle, row.SourceName, row.ItemTitle, row.CatalogKind, row.CatalogId, row.SetByUserName));
    }

    [Fact]
    public async Task List_of_pending_links_has_the_queued_and_writing_ones()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var workId = await SyncAsync(cancellationToken, (ShortId, null), (OtherShortId, null));
        var otherWorkId = await WorkOfAsync(OtherShortId, cancellationToken);
        var queued = await _links.Create(_jellyfinIntegration, ShortId, workId, MediaIdentifierScheme.Tmdb, FilmId, writeState: LibraryLinkWriteState.Queued);
        _factory.Time.Advance(TimeSpan.FromMinutes(1));
        var writing = await _links.Create(_jellyfinIntegration, OtherShortId, otherWorkId, MediaIdentifierScheme.Tmdb, "300", writeState: LibraryLinkWriteState.Writing);
        await GoneAsync("01", "Calypso Cat", LibraryLinkWriteState.Written);

        //Act
        var list = await ReadListAsync($"{LinksUrl}?filter=Pending", cancellationToken);

        //Assert
        Assert.Equal(
            [(writing.Id, (Guid?)otherWorkId, (string?)"Calypso Cat", LibraryLinkWriteState.Writing), (queued.Id, workId, "Landing Stripling", LibraryLinkWriteState.Queued)],
            list.Items.Select(row => (row.LinkId, row.WorkId, row.WorkTitle, row.WriteState)));
    }

    [Fact]
    public async Task List_of_conflicts_has_the_links_whose_item_jellyfin_gave_a_third_tmdb_id()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var workId = await SyncAsync(cancellationToken, (ShortId, "300"), (OtherShortId, "100"));
        var otherWorkId = await WorkOfAsync(OtherShortId, cancellationToken);
        // Jellyfin holds 300 beside the link's own id, confirmed once: neither wins.
        var conflicting = await _links.Create(_jellyfinIntegration, ShortId, workId, MediaIdentifierScheme.Tmdb, FilmId, writeState: LibraryLinkWriteState.Lost);
        // Jellyfin holds the id this link replaces, not confirmed yet: it gives way (R6), no conflict.
        await _links.Create(
            _jellyfinIntegration, OtherShortId, otherWorkId, MediaIdentifierScheme.Tmdb, FilmId, writeState: LibraryLinkWriteState.NotWritten,
            configure: row => row.SupersededValue = "100");

        //Act
        var list = await ReadListAsync($"{LinksUrl}?filter=Conflict", cancellationToken);

        //Assert
        var row = Assert.Single(list.Items);
        Assert.Equal((conflicting.Id, (string?)"300"), (row.LinkId, row.ConflictingValue));
        Assert.Equal(1, list.TotalCount);
    }

    [Fact]
    public async Task List_of_conflicts_has_an_episode_link_whose_item_jellyfin_gave_a_film_id_after_it()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var workId = await SyncAsync(cancellationToken, (ShortId, "12345"), (OtherShortId, "100"));
        var otherWorkId = await WorkOfAsync(OtherShortId, cancellationToken);
        // Linked as an episode of a show while it held no film id; Jellyfin has given it 12345 since.
        var later = await _links.Create(_jellyfinIntegration, ShortId, workId, MediaIdentifierScheme.TmdbTv, "1044", LibraryLinkTarget.ShowEpisode);
        // The film id this one's link replaced: it gives way (R7), no conflict.
        await _links.Create(
            _jellyfinIntegration, OtherShortId, otherWorkId, MediaIdentifierScheme.TmdbTv, "1044", LibraryLinkTarget.ShowEpisode,
            configure: row => row.SupersededValue = "100");

        //Act
        var list = await ReadListAsync($"{LinksUrl}?filter=Conflict", cancellationToken);

        //Assert
        var row = Assert.Single(list.Items);
        Assert.Equal((later.Id, (string?)"12345"), (row.LinkId, row.ConflictingValue));
        Assert.Equal(1, list.TotalCount);
    }

    [Theory]
    [InlineData("NotWritten", LibraryLinkWriteState.NotWritten)]
    [InlineData("lost", LibraryLinkWriteState.Lost)]
    public async Task List_of_a_state_has_the_links_in_it_with_their_errors(string filter, LibraryLinkWriteState state)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var workId = await SyncAsync(cancellationToken, (ShortId, null), (OtherShortId, null));
        var otherWorkId = await WorkOfAsync(OtherShortId, cancellationToken);
        var listed = await _links.Create(
            _jellyfinIntegration, ShortId, workId, MediaIdentifierScheme.Tmdb, FilmId, writeState: state, configure: row => row.WriteError = "Jellyfin said no.");
        await _links.Create(_jellyfinIntegration, OtherShortId, otherWorkId, MediaIdentifierScheme.Tmdb, "300", writeState: LibraryLinkWriteState.InLibrary);

        //Act
        var list = await ReadListAsync($"{LinksUrl}?filter={filter}", cancellationToken);

        //Assert
        var row = Assert.Single(list.Items);
        Assert.Equal((listed.Id, state, (string?)"Jellyfin said no."), (row.LinkId, row.WriteState, row.WriteError));
    }

    [Fact]
    public async Task NotWritten_lists_attached_links_only_and_an_unattached_not_written_link_stays_under_unattached()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var workId = await SyncAsync(cancellationToken, (ShortId, null));
        var attached = await _links.Create(
            _jellyfinIntegration, ShortId, workId, MediaIdentifierScheme.Tmdb, FilmId, writeState: LibraryLinkWriteState.NotWritten,
            configure: row => row.WriteError = "Jellyfin said no.");
        // Its item is gone: its card has no button (Retry needs a work) and the repair never retries it.
        var gone = await GoneAsync("01", "Calypso Cat", LibraryLinkWriteState.NotWritten);

        //Act
        var notWritten = await ReadListAsync($"{LinksUrl}?filter=NotWritten", cancellationToken);
        var unattached = await ReadListAsync($"{LinksUrl}?filter=Unattached", cancellationToken);

        //Assert
        Assert.Equal([attached.Id], notWritten.Items.Select(row => row.LinkId));
        Assert.Equal(1, notWritten.TotalCount);
        Assert.Equal([gone.Id], unattached.Items.Select(row => row.LinkId));
    }

    [Fact]
    public async Task List_rows_carry_the_time_the_app_confirmed_the_link_identified()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var workId = await SyncAsync(cancellationToken, (ShortId, null), (OtherShortId, null));
        var otherWorkId = await WorkOfAsync(OtherShortId, cancellationToken);
        var identifiedAt = _factory.Time.GetUtcNow().UtcDateTime;
        await _links.Create(
            _jellyfinIntegration, ShortId, workId, MediaIdentifierScheme.Tmdb, FilmId, writeState: LibraryLinkWriteState.Lost,
            configure: row => row.IdentifiedUtc = identifiedAt);
        _factory.Time.Advance(TimeSpan.FromMinutes(1));
        await _links.Create(_jellyfinIntegration, OtherShortId, otherWorkId, MediaIdentifierScheme.Tmdb, "300", writeState: LibraryLinkWriteState.Lost);

        //Act
        var list = await ReadListAsync($"{LinksUrl}?filter=Lost", cancellationToken);

        //Assert
        // Newest first: the link never confirmed, then the one the app did.
        Assert.Equal([(DateTime?)null, identifiedAt], list.Items.Select(row => row.IdentifiedUtc));
    }

    [Theory]
    [InlineData("", "Choose which links to list: Unattached, Pending, Conflict, NotWritten or Lost.")]
    [InlineData("?filter=Gone", "“Gone” is not a link filter. Use Unattached, Pending, Conflict, NotWritten or Lost.")]
    [InlineData("?filter=Lost&page=0", "Page must be 1 or more.")]
    [InlineData("?filter=Lost&pageSize=101", "Page size must be between 1 and 100.")]
    public async Task List_with_a_query_it_does_not_take_is_400_with_its_sentence(string query, string expected)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var response = await _factory.GetHttpRequest(LinksUrl + query, _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(expected, await response.ReadErrorAsync(cancellationToken));
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    // Shorts of their own, with the TMDb ids Jellyfin gave them; the first one's work.
    private async Task<Guid> SyncAsync(CancellationToken cancellationToken, params (string ItemId, string? TmdbId)[] items)
    {
        _jellyfinIntegration = await _integrations.Create("jellyfin", "Living room", apiKey: ApiKey);
        foreach (var (itemId, tmdbId) in items)
        {
            var title = itemId == ShortId ? "Landing Stripling" : "Calypso Cat";
            _jellyfin.Movies.Add(JellyfinSamples.MovieWith(itemId, title, null, 1962, $"/media/cartoons/Tom & Jerry/{itemId}.mkv", tmdbId: tmdbId));
        }

        await _factory.SyncLibraryAsync(_jellyfinIntegration.Id, _owner.AccessToken, cancellationToken);
        return await WorkOfAsync(items[0].ItemId, cancellationToken);
    }

    // A link whose item the library no longer reports.
    private Task<LibraryItemLinkEntity> GoneAsync(string suffix, string itemTitle, LibraryLinkWriteState state = LibraryLinkWriteState.KeptInApp) =>
        _links.Create(
            _jellyfinIntegration, $"f6f6f6f6f6f6f6f6f6f6f6f6f6f6f6{suffix}", null, MediaIdentifierScheme.Tmdb, FilmId, writeState: state,
            configure: row => row.ItemTitle = itemTitle);

    private async Task<LibraryLinkListResponse> ReadListAsync(string url, CancellationToken cancellationToken)
    {
        var response = await _factory.GetHttpRequest(url, _owner.AccessToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<LibraryLinkListResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
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
}
