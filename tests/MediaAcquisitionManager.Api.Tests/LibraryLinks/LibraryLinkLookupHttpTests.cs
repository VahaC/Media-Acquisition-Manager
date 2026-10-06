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

// MAM-34 follow-up (Vasyl's decision of 2026-10-06) — the lookup of an item's missing metadata Jellyfin is asked for
// after a write leaves the link's ids in the item: once per write, recorded on the link (when, and why Jellyfin didn't
// take it, masked) and in an audit row; a failed lookup leaves the link written, as the ids are in; Write again and Retry
// — for the work or one link — ask again, a write that finds the ids in place included, which sends no change; a write
// that isn't confirmed or is refused asks for nothing. A link replaced by another is the new link's to look up: the old
// one's lookup is not shown for it, and the catch-up counts it once Jellyfin holds its ids.
public sealed class LibraryLinkLookupHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/library";
    private const string CatchUpUrl = $"{BaseUrl}/tmdb/catch-up";
    private const string ApiKey = "0123456789abcdef0123456789abcdef";
    private const string ShortId = JellyfinSamples.LandingStriplingId;
    private const string FilmId = JellyfinSamples.LandingStriplingTmdbId;
    private const string FilmImdbId = JellyfinSamples.LandingStriplingImdbId;
    private const string OtherFilmId = "223022";
    private const string ShowId = TmdbSamples.TvEpisodeWwiiShowId;
    private const string LookupForbidden = "Jellyfin refused to look the item up. Check that the API key belongs to an administrator.";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private IIntegrationConnectionSeeder _integrations = null!;
    private AuthorizedUser _owner = null!;
    private FakeJellyfinServer _jellyfin = null!;

    public LibraryLinkLookupHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _integrations = new IntegrationConnectionSeeder(_factory.Scope);
        _owner = await new UserSeeder(_factory.Scope).CreateAuthorizedOwner("owner");
        _jellyfin = new FakeJellyfinServer { ApiKey = ApiKey };
        _jellyfin.Movies.Clear();
        _jellyfin.Arm(_factory.JellyfinHttp);
        var tmdb = FakeTmdbServer.Arm(_factory.TmdbHttp);
        tmdb.AddWork(new FakeTmdbWork(MediaWorkKind.Movie, int.Parse(FilmId), "Посадкова смуга", ImdbId: FilmImdbId));
        tmdb.AddWork(new FakeTmdbWork(MediaWorkKind.Movie, int.Parse(OtherFilmId), "Кіт Каліпсо"));
        tmdb.AddWork(new FakeTmdbWork(MediaWorkKind.Series, int.Parse(ShowId), "Найвизначніші події Другої світової у кольорі", "Greatest Events of WWII in Colour"));
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Set_whose_lookup_jellyfin_refuses_keeps_the_link_written_with_the_lookup_error_and_records_the_request()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var workId = await SyncShortAsync();
        _jellyfin.LookupRefusal = HttpStatusCode.Forbidden;
        var now = _factory.Time.GetUtcNow().UtcDateTime;

        //Act
        var response = await _factory.PutHttpRequest(WorkUrl(workId), _owner.AccessToken, new LibraryLinkRequest("Movie", FilmId));

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var item = Assert.Single((await ReadDialogAsync(response, cancellationToken)).Items);
        // The ids are in: a lookup Jellyfin didn't take never makes the link "not written".
        Assert.Equal(
            ((LibraryLinkWriteState?)LibraryLinkWriteState.Written, (string?)null, (DateTime?)now, (string?)LookupForbidden),
            (item.WriteState, item.WriteError, item.LookupRequestedUtc, item.LookupError));
        Assert.Equal((FilmId, FilmImdbId), ((string?)Movie()["ProviderIds"]!["Tmdb"], (string?)Movie()["ProviderIds"]!["Imdb"]));
        Assert.Empty(_jellyfin.Lookups);
        Assert.Equal(
            [AuditAction.LibraryLinkSet, AuditAction.LibraryIdsWritten, AuditAction.LibraryMetadataLookupRequested],
            await AuditActionsAsync(cancellationToken));
        // Sent once, never again by itself (house rule 8).
        Assert.Single(_factory.JellyfinHttp.Requests, request => request.Uri.AbsolutePath.EndsWith("/Refresh", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Lookup_error_masks_the_key_jellyfins_answer_echoes()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var workId = await SyncShortAsync();
        _jellyfin.Intercept = request => request.RequestUri!.AbsolutePath.EndsWith("/Refresh", StringComparison.Ordinal)
            ? VendorResponses.Redirect($"https://media.test/{ApiKey}/web/index.html")
            : null;

        //Act
        await _factory.PutHttpRequest(WorkUrl(workId), _owner.AccessToken, new LibraryLinkRequest("Movie", FilmId));

        //Assert
        var link = await SingleLinkAsync(cancellationToken);
        Assert.Equal(LibraryLinkWriteState.Written, link.WriteState);
        Assert.StartsWith("The address answered with a redirect to ", link.LookupError, StringComparison.Ordinal);
        Assert.DoesNotContain(ApiKey, link.LookupError, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Write_again_of_a_link_whose_lookup_failed_sends_no_change_asks_jellyfin_again_and_clears_the_error(bool oneLink)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var workId = await SyncShortAsync();
        _jellyfin.LookupRefusal = HttpStatusCode.Forbidden;
        await _factory.PutHttpRequest(WorkUrl(workId), _owner.AccessToken, new LibraryLinkRequest("Movie", FilmId));
        var failed = await SingleLinkAsync(cancellationToken);
        _jellyfin.LookupRefusal = null;
        _factory.Time.Advance(TimeSpan.FromMinutes(1));
        var sent = _factory.JellyfinHttp.Requests.Count;

        //Act
        var response = await _factory.PostHttpRequest(oneLink ? $"{BaseUrl}/tmdb/links/{failed.Id}/write" : $"{WorkUrl(workId)}/write", _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(LookupForbidden, failed.LookupError);
        var link = await SingleLinkAsync(cancellationToken);
        Assert.Equal(
            (LibraryLinkWriteState.Written, (DateTime?)_factory.Time.GetUtcNow().UtcDateTime, (string?)null),
            (link.WriteState, link.LookupRequestedUtc, link.LookupError));
        // The ids were in place: read, nothing posted but the lookup.
        Assert.Equal(
            ["GET /System/Info", "GET /Users", $"GET /Items/{ShortId}", $"POST /Items/{ShortId}/Refresh"],
            _factory.JellyfinHttp.Requests.Skip(sent).Select(request => $"{request.Method} {request.Uri.AbsolutePath}"));
        Assert.Equal([ShortId], _jellyfin.Lookups);
        Assert.Equal(
            [
                AuditAction.LibraryLinkSet, AuditAction.LibraryIdsWritten, AuditAction.LibraryMetadataLookupRequested,
                AuditAction.LibraryMetadataLookupRequested,
            ],
            await AuditActionsAsync(cancellationToken));
    }

    [Fact]
    public async Task Write_whose_change_jellyfin_does_not_confirm_asks_for_no_lookup()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var workId = await SyncShortAsync();
        _jellyfin.IgnoresWrites = true;

        //Act
        await _factory.PutHttpRequest(WorkUrl(workId), _owner.AccessToken, new LibraryLinkRequest("Movie", FilmId));

        //Assert
        // The read again shows no ids: there is nothing for Jellyfin to look the item up by.
        var link = await SingleLinkAsync(cancellationToken);
        Assert.Equal((LibraryLinkWriteState.NotWritten, (DateTime?)null), (link.WriteState, link.LookupRequestedUtc));
        Assert.DoesNotContain(_factory.JellyfinHttp.Requests, request => request.Uri.AbsolutePath.EndsWith("/Refresh", StringComparison.Ordinal));
        Assert.DoesNotContain(AuditAction.LibraryMetadataLookupRequested, await AuditActionsAsync(cancellationToken));
    }

    [Fact]
    public async Task Replace_of_a_looked_up_link_shows_no_lookup_until_its_own_and_the_catch_up_counts_it_once_jellyfin_holds_it()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var workId = await SyncShortAsync();
        _jellyfin.LookupRefusal = HttpStatusCode.Forbidden;
        await _factory.PutHttpRequest(WorkUrl(workId), _owner.AccessToken, new LibraryLinkRequest("Movie", FilmId));
        var lookedUp = await SingleLinkAsync(cancellationToken);
        _jellyfin.LookupRefusal = null;
        // The new film's write is taken and not applied: sent, not confirmed, so no lookup is asked for it.
        _jellyfin.IgnoresWrites = true;
        _factory.Time.Advance(TimeSpan.FromMinutes(1));

        //Act
        var response = await _factory.PutHttpRequest(WorkUrl(workId), _owner.AccessToken, new LibraryLinkRequest("Movie", OtherFilmId));
        var beforeTheSync = await ReadAsync<LibraryLinkCatchUpResponse>(await _factory.GetHttpRequest(CatchUpUrl, _owner.AccessToken), cancellationToken);
        // Jellyfin saved it after all, and the next sync reads it there.
        _jellyfin.IgnoresWrites = false;
        Movie()["ProviderIds"] = new System.Text.Json.Nodes.JsonObject { ["Tmdb"] = OtherFilmId };
        _factory.Time.Advance(TimeSpan.FromMinutes(1));
        await _factory.SyncLibraryAsync(lookedUp.IntegrationConnectionId, _owner.AccessToken, cancellationToken);
        var afterTheSync = await ReadAsync<LibraryLinkCatchUpResponse>(await _factory.GetHttpRequest(CatchUpUrl, _owner.AccessToken), cancellationToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(((DateTime?)_factory.Time.GetUtcNow().UtcDateTime.AddMinutes(-2), (string?)LookupForbidden), (lookedUp.LookupRequestedUtc, lookedUp.LookupError));
        // The first film's lookup, and why Jellyfin didn't take it, are not the new link's.
        var item = Assert.Single((await ReadDialogAsync(response, cancellationToken)).Items);
        Assert.Equal(
            ((LibraryLinkWriteState?)LibraryLinkWriteState.NotWritten, (DateTime?)null, (string?)null),
            (item.WriteState, item.LookupRequestedUtc, item.LookupError));
        // Not in Jellyfin yet: one to write; then in Jellyfin, never looked up: one to look up.
        Assert.Equal((new LibraryLinkCatchUpResponse(1, 0), new LibraryLinkCatchUpResponse(0, 1)), (beforeTheSync, afterTheSync));
        var link = await SingleLinkAsync(cancellationToken);
        Assert.Equal((lookedUp.Id, LibraryLinkWriteState.InLibrary, (DateTime?)null), (link.Id, link.WriteState, link.LookupRequestedUtc));
    }

    [Fact]
    public async Task Replace_of_a_looked_up_film_link_with_an_episode_link_kept_in_the_app_shows_no_lookup()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var workId = await SyncShortAsync();
        await _factory.PutHttpRequest(WorkUrl(workId), _owner.AccessToken, new LibraryLinkRequest("Movie", FilmId));
        var lookedUp = await SingleLinkAsync(cancellationToken);
        _factory.Time.Advance(TimeSpan.FromMinutes(1));

        //Act
        // An episode of a show with no season or episode named: it writes nothing of its own, so it is never looked up.
        var response = await _factory.PutHttpRequest(WorkUrl(workId), _owner.AccessToken, new LibraryLinkRequest("Series", ShowId));

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(lookedUp.LookupRequestedUtc);
        var item = Assert.Single((await ReadDialogAsync(response, cancellationToken)).Items);
        Assert.Equal(
            ((LibraryLinkTarget?)LibraryLinkTarget.ShowEpisode, (LibraryLinkWriteState?)LibraryLinkWriteState.KeptInApp, (DateTime?)null, (string?)null),
            (item.Target, item.WriteState, item.LookupRequestedUtc, item.LookupError));
        Assert.Equal([ShortId], _jellyfin.Lookups);
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    private static string WorkUrl(Guid workId) => $"{BaseUrl}/works/{workId}/tmdb";

    private async Task<Guid> SyncShortAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var jellyfin = await _integrations.Create("jellyfin", "Living room", apiKey: ApiKey);
        _jellyfin.Movies.Add(JellyfinSamples.MovieWith(ShortId, "Landing Stripling", null, 1962, "/media/cartoons/Tom & Jerry/120.mkv"));
        await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);
        await _integrations.Create("tmdb", "TMDb", FakeTmdbServer.BaseUrl, apiKey: FakeTmdbServer.Token);
        var workId = Guid.Empty;
        await _factory.WithDbAsync(async db => workId = await db.MediaWorkTitles
            .Where(title => title.SourceItemId == ShortId)
            .Select(title => title.MediaWorkId)
            .FirstAsync(cancellationToken));
        return workId;
    }

    private System.Text.Json.Nodes.JsonObject Movie() => _jellyfin.Movies.Single(movie => (string?)movie["Id"] == ShortId);

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
