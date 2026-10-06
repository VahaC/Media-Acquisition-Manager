using System.Net;
using System.Text.Json.Nodes;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Api.Services.LibraryLinks;
using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Abstractions.Library;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Tests.LibraryLinks;

// MAM-34 (0.2.1, ADR 0026) — the guards and the guarantees of Identify as the app's flows meet them, through the real
// worker and the fake Jellyfin: a library that saves beside the media, one the app can't tell about, an item locked in
// Jellyfin (the whole item, its overview), no match and an item of another type send no search or Apply and leave the link
// NotWritten with the sentence; an id goes only to the search of its own kind; an item that already shows the match is found
// so with nothing sent and nothing recorded; an Apply that left the app is recorded and, when the item shows nothing of it,
// clears the identified time; a retry after a lost answer re-reads first and never sends a second Apply, and a series whose
// last Apply was sent minutes ago waits instead of being applied again; the language of the item's library goes to the
// search, so a Ukrainian library is identified with the Ukrainian name; every sentence stored is masked; and no flow ever
// changes anything in Jellyfin but its search and Apply.
public sealed class LibraryLinkIdentifyHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/library";
    private const string ApiKey = "0123456789abcdef0123456789abcdef";
    private const string ShortId = JellyfinSamples.LandingStriplingId;
    private const string PenguinsId = "d4d4d4d4d4d4d4d4d4d4d4d4d4d4d4d4";
    private const string FilmId = JellyfinSamples.LandingStriplingTmdbId;
    private const string FilmImdbId = JellyfinSamples.LandingStriplingImdbId;
    private const string ShowId = JellyfinSamples.PenguinsTmdbTvId;
    private const string UnknownFilmId = "999000";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private IIntegrationConnectionSeeder _integrations = null!;
    private ILibraryItemLinkSeeder _links = null!;
    private AuthorizedUser _owner = null!;
    private FakeJellyfinServer _jellyfin = null!;
    private FakeTmdbServer _tmdb = null!;
    private IntegrationConnectionEntity _jellyfinIntegration = null!;

    public LibraryLinkIdentifyHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _integrations = new IntegrationConnectionSeeder(_factory.Scope);
        _links = new LibraryItemLinkSeeder(_factory.Scope);
        _owner = await new UserSeeder(_factory.Scope).CreateAuthorizedOwner("owner");
        _jellyfin = new FakeJellyfinServer { ApiKey = ApiKey };
        _jellyfin.Movies.Clear();
        _jellyfin.AddFilm(FilmId, "Landing Stripling", 1962, FilmImdbId);
        _jellyfin.AddShow(ShowId, "The Penguins of Madagascar", 2008, "tt0892700", tvdbId: "79327");
        _jellyfin.Arm(_factory.JellyfinHttp);
        _tmdb = FakeTmdbServer.Arm(_factory.TmdbHttp);
        _tmdb.AddWork(new FakeTmdbWork(MediaWorkKind.Movie, int.Parse(FilmId), "Посадкова смуга", ImdbId: FilmImdbId));
        _tmdb.AddWork(new FakeTmdbWork(MediaWorkKind.Movie, int.Parse(UnknownFilmId), "Фільм, якого Jellyfin не знає"));
        _tmdb.AddWork(new FakeTmdbWork(MediaWorkKind.Series, int.Parse(ShowId), "Пінгвіни з Мадагаскару", ImdbId: "tt0892700"));
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Identify_in_a_library_that_saves_beside_the_media_sends_no_search_or_apply_and_stays_not_written_with_the_reason()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var workId = await SyncShortAsync(cancellationToken);
        _jellyfin.LibraryOptionsOf(JellyfinSamples.CartoonsRoot)["SaveLocalMetadata"] = true;
        await SetAsync(workId, new LibraryLinkRequest("Movie", FilmId));
        var sent = _factory.JellyfinHttp.Requests.Count;

        //Act
        await _factory.IdentifyQueuedLinksAsync(cancellationToken);

        //Assert
        var link = await _factory.SingleLinkAsync(cancellationToken);
        Assert.Equal(
            (LibraryLinkWriteState.NotWritten,
                "Jellyfin's library “Мульфільми” saves artwork and metadata next to the media, so identifying this item could change files in your media folders; the app left it alone. Switch that off in the library's settings in Jellyfin, then retry.",
                (DateTime?)null),
            (link.WriteState, link.WriteError, link.IdentifiedUtc));
        // The library's options are read first, before any search: the calls end there, and nothing changed.
        Assert.Equal(
            ["/System/Info", "/Users", $"/Items/{ShortId}", "/Library/VirtualFolders"],
            _factory.JellyfinHttp.Requests.Skip(sent).Select(request => request.Uri.AbsolutePath));
        Assert.Empty(_jellyfin.RemoteSearches);
        Assert.Empty(_jellyfin.Applied);
        Assert.Equal([AuditAction.LibraryLinkSet], await _factory.AuditActionsAsync(cancellationToken));
    }

    [Fact]
    public async Task Identify_of_an_item_in_no_known_library_is_not_written_unknown_and_applies_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // An item whose path no library of Jellyfin holds can't be told safe: the app fails closed.
        var workId = await SyncShortAsync(cancellationToken, path: "/srv/elsewhere/120.mkv");
        await SetAsync(workId, new LibraryLinkRequest("Movie", FilmId));

        //Act
        await _factory.IdentifyQueuedLinksAsync(cancellationToken);

        //Assert
        var link = await _factory.SingleLinkAsync(cancellationToken);
        Assert.Equal(
            (LibraryLinkWriteState.NotWritten,
                "The app couldn't tell what this item's Jellyfin library saves next to the media (none of Jellyfin's libraries holds the item's path), so it left the item alone."),
            (link.WriteState, link.WriteError));
        Assert.Empty(_jellyfin.RemoteSearches);
        Assert.Empty(_jellyfin.Applied);
    }

    [Theory]
    [InlineData("LockData")]
    [InlineData("LockedOverview")]
    public async Task Identify_of_a_locked_item_is_not_written_with_the_unlock_sentence_and_applies_nothing(string lockedBy)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var workId = await SyncShortAsync(cancellationToken);
        // The whole item locked, or its overview: Jellyfin would keep the old text and still report the item "identified".
        if (lockedBy == "LockData")
            Movie(ShortId)["LockData"] = true;
        else
            Movie(ShortId)["LockedFields"] = new JsonArray("Overview");
        await SetAsync(workId, new LibraryLinkRequest("Movie", FilmId));

        //Act
        await _factory.IdentifyQueuedLinksAsync(cancellationToken);

        //Assert
        var link = await _factory.SingleLinkAsync(cancellationToken);
        Assert.Equal(
            (LibraryLinkWriteState.NotWritten,
                "This item is locked in Jellyfin (the whole item, its name or its overview), so Jellyfin wouldn't take the match. Unlock it there, then retry."),
            (link.WriteState, link.WriteError));
        Assert.Empty(_jellyfin.RemoteSearches);
        Assert.Empty(_jellyfin.Applied);
    }

    [Fact]
    public async Task Identify_with_no_match_is_not_written_with_the_sentence_and_applies_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var workId = await SyncShortAsync(cancellationToken);
        // TMDb has the film, Jellyfin's provider doesn't (a lagging provider, a library without TheMovieDb among its sources).
        await SetAsync(workId, new LibraryLinkRequest("Movie", UnknownFilmId));

        //Act
        await _factory.IdentifyQueuedLinksAsync(cancellationToken);

        //Assert
        var link = await _factory.SingleLinkAsync(cancellationToken);
        Assert.Equal(
            (LibraryLinkWriteState.NotWritten,
                $"Jellyfin found no film with TMDb id {UnknownFilmId}. Check the id, and that TheMovieDb is among this library's metadata sources in Jellyfin.",
                (DateTime?)null),
            (link.WriteState, link.WriteError, link.IdentifiedUtc));
        Assert.Single(_jellyfin.RemoteSearches);
        Assert.Empty(_jellyfin.Applied);
        Assert.Equal([AuditAction.LibraryLinkSet], await _factory.AuditActionsAsync(cancellationToken));
    }

    [Fact]
    public async Task Identify_of_an_item_of_another_type_is_not_written_and_searches_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await ConnectJellyfinAsync();
        _jellyfin.Series.Add(JellyfinSamples.SeriesWith(PenguinsId, "The Penguins Of Madagascar", 2008, $"{JellyfinSamples.TvShowsRoot}/Penguins"));
        // The link says a film; Jellyfin holds a series under that id (an item re-created under a reused id).
        await _links.Create(
            _jellyfinIntegration, PenguinsId, null, MediaIdentifierScheme.Tmdb, FilmId, writeState: LibraryLinkWriteState.Queued, setBy: _owner.User);

        //Act
        await _factory.IdentifyQueuedLinksAsync(cancellationToken);

        //Assert
        var link = await _factory.SingleLinkAsync(cancellationToken);
        Assert.Equal(
            (LibraryLinkWriteState.NotWritten,
                "Jellyfin holds an item of type “Series” under this id, not a movie, so the app didn't identify it. Sync the library and link it again."),
            (link.WriteState, link.WriteError));
        Assert.Empty(_jellyfin.RemoteSearches);
    }

    [Fact]
    public async Task Identify_of_an_item_jellyfin_no_longer_has_is_not_written_with_its_sentence()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var workId = await SyncShortAsync(cancellationToken);
        await SetAsync(workId, new LibraryLinkRequest("Movie", FilmId));
        _jellyfin.Movies.Clear();

        //Act
        await _factory.IdentifyQueuedLinksAsync(cancellationToken);

        //Assert
        var link = await _factory.SingleLinkAsync(cancellationToken);
        Assert.Equal(
            (LibraryLinkWriteState.NotWritten, "Jellyfin no longer has this item; sync the library and link it again."),
            (link.WriteState, link.WriteError));
        Assert.Empty(_jellyfin.Applied);
    }

    [Fact]
    public async Task Identify_of_a_film_never_uses_the_series_search_and_a_show_never_the_movie_search()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var filmWorkId = await SyncShortAsync(cancellationToken);
        _jellyfin.Series.Add(JellyfinSamples.SeriesWith(PenguinsId, "The Penguins Of Madagascar", 2008, $"{JellyfinSamples.TvShowsRoot}/Penguins"));
        _jellyfin.Episodes.Add(JellyfinSamples.EpisodeWith("e5e5e5e5e5e5e5e5e5e5e5e5e5e5e5e5", "Gone in a Flash", PenguinsId, 1, 1, $"{JellyfinSamples.TvShowsRoot}/Penguins/S01E01.mkv"));
        await _factory.SyncLibraryAsync(_jellyfinIntegration.Id, _owner.AccessToken, cancellationToken);
        var showWorkId = await _factory.WorkOfItemAsync(PenguinsId, cancellationToken);
        await SetAsync(filmWorkId, new LibraryLinkRequest("Movie", FilmId));
        await SetAsync(showWorkId, new LibraryLinkRequest("Series", ShowId));

        //Act
        await _factory.IdentifyQueuedLinksAsync(cancellationToken);

        //Assert
        // An id goes only where its own kind is searched: a TV id searched as a film answers whatever film has that number.
        var searches = _jellyfin.RemoteSearches.ToDictionary(search => search.Body.Contains("\"Tmdb\":\"7869\"", StringComparison.Ordinal) ? "show" : "film", search => search.Endpoint);
        Assert.Equal(("Movie", "Series"), (searches["film"], searches["show"]));
        Assert.Equal(2, _jellyfin.RemoteSearches.Count);
    }

    [Fact]
    public async Task Every_flow_sends_jellyfin_only_gets_searches_and_applies()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var workId = await SyncShortAsync(cancellationToken);
        _jellyfin.AddFilm("223022", "Calypso Cat", 1962);
        _tmdb.AddWork(new FakeTmdbWork(MediaWorkKind.Movie, 223022, "Калипсо"));

        //Act
        // Set, Replace, Retry, the repair, Remove and a sync: every way a link reaches Jellyfin.
        await SetAsync(workId, new LibraryLinkRequest("Movie", FilmId));
        await _factory.IdentifyQueuedLinksAsync(cancellationToken);
        _factory.Time.Advance(TimeSpan.FromMinutes(1));
        await SetAsync(workId, new LibraryLinkRequest("Movie", "223022"));
        await _factory.IdentifyQueuedLinksAsync(cancellationToken);
        await _factory.PostHttpRequest($"{BaseUrl}/works/{workId}/tmdb/write", _owner.AccessToken);
        await _factory.IdentifyQueuedLinksAsync(cancellationToken);
        await _factory.WithDbAsync(db => db.LibraryItemLinks.ExecuteUpdateAsync(
            setters => setters.SetProperty(row => row.IdentifiedUtc, (DateTime?)null).SetProperty(row => row.WriteState, LibraryLinkWriteState.InLibrary), cancellationToken));
        await _factory.PostHttpRequest("/api/library/tmdb/catch-up", _owner.AccessToken);
        await _factory.IdentifyQueuedLinksAsync(cancellationToken);
        _factory.Time.Advance(TimeSpan.FromMinutes(1));
        await _factory.SyncLibraryAsync(_jellyfinIntegration.Id, _owner.AccessToken, cancellationToken);
        await _factory.DeleteHttpRequest($"{BaseUrl}/works/{workId}/tmdb", _owner.AccessToken);

        //Assert
        var requests = _factory.JellyfinHttp.Requests;
        Assert.Contains(requests, request => request.Uri.AbsolutePath.StartsWith("/Items/RemoteSearch/Apply/", StringComparison.Ordinal));
        Assert.All(requests, request =>
        {
            if (request.Method == HttpMethod.Get)
                return;
            // The only requests that change anything: the search (a POST that changes nothing) and Apply.
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.StartsWith("/Items/RemoteSearch/", request.Uri.AbsolutePath, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task Identify_that_finds_the_item_already_matching_marks_it_written_with_no_apply_and_no_audit_row()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var workId = await SyncShortAsync(cancellationToken);
        await SetAsync(workId, new LibraryLinkRequest("Movie", FilmId));
        await _factory.IdentifyQueuedLinksAsync(cancellationToken);
        // The owner forgets the link and links the film again: the item still shows the match Jellyfin gave it.
        Assert.Equal(HttpStatusCode.OK, (await _factory.DeleteHttpRequest($"{BaseUrl}/works/{workId}/tmdb", _owner.AccessToken)).StatusCode);
        _factory.Time.Advance(TimeSpan.FromMinutes(1));
        await SetAsync(workId, new LibraryLinkRequest("Movie", FilmId));
        var recorded = (await _factory.AuditAsync(cancellationToken)).Count(row => row.Action == AuditAction.LibraryItemIdentified);

        //Act
        await _factory.IdentifyQueuedLinksAsync(cancellationToken);

        //Assert
        var link = await _factory.SingleLinkAsync(cancellationToken);
        Assert.Equal((LibraryLinkWriteState.Written, (string?)null), (link.WriteState, link.WriteError));
        Assert.Equal(_factory.Time.GetUtcNow().UtcDateTime, link.IdentifiedUtc);
        Assert.Single(_jellyfin.Applied);
        Assert.Equal(1, recorded);
        Assert.Equal(
            1,
            (await _factory.AuditAsync(cancellationToken)).Count(row => row.Action == AuditAction.LibraryItemIdentified));
    }

    [Fact]
    public async Task Identify_not_confirmed_records_the_audit_row_of_what_was_sent_and_clears_the_identified_time()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var workId = await SyncShortAsync(cancellationToken);
        var identifiedAt = _factory.Time.GetUtcNow().UtcDateTime;
        // A link the app once confirmed, whose item no longer shows it (somebody changed it in Jellyfin): Retry sends an Apply
        // that Jellyfin takes and doesn't show.
        var link = await _links.Create(
            _jellyfinIntegration, ShortId, workId, MediaIdentifierScheme.Tmdb, FilmId, writeState: LibraryLinkWriteState.Lost, setBy: _owner.User,
            configure: row => row.IdentifiedUtc = identifiedAt);
        _jellyfin.IgnoresApply = true;
        _factory.Time.Advance(TimeSpan.FromMinutes(1));

        //Act
        await _factory.PostHttpRequest($"{BaseUrl}/tmdb/links/{link.Id}/write", _owner.AccessToken);
        await _factory.IdentifyQueuedLinksAsync(cancellationToken);

        //Assert
        var stored = await _factory.SingleLinkAsync(cancellationToken);
        Assert.Equal((LibraryLinkWriteState.NotWritten, (DateTime?)null), (stored.WriteState, stored.IdentifiedUtc));
        Assert.Contains("took the match, but the item read again doesn't show it", stored.WriteError, StringComparison.Ordinal);
        var audit = Assert.Single(await _factory.AuditAsync(cancellationToken));
        Assert.Equal((AuditAction.LibraryItemIdentified, (Guid?)link.Id, "owner"), (audit.Action, audit.TargetId, audit.ActorUserName));
    }

    [Fact]
    public async Task Identify_audit_row_records_the_ids_before_and_after_as_the_apps_schemes()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await ConnectJellyfinAsync();
        // A series Jellyfin gave a wrong show id and a TVDB id: its Tmdb key reads as a show's (TmdbTv), and Jellyfin's other
        // providers keep its TVDB id through the Identify.
        _jellyfin.Series.Add(JellyfinSamples.SeriesWith(
            PenguinsId, "The Penguins Of Madagascar", 2008, $"{JellyfinSamples.TvShowsRoot}/Penguins", tmdbId: "100", tvdbId: "79327"));
        await _links.Create(
            _jellyfinIntegration, PenguinsId, null, MediaIdentifierScheme.TmdbTv, ShowId, writeState: LibraryLinkWriteState.Queued, setBy: _owner.User);

        //Act
        await _factory.IdentifyQueuedLinksAsync(cancellationToken);

        //Assert
        var audit = Assert.Single(await _factory.AuditAsync(cancellationToken));
        Assert.Equal(
            (AuditAction.LibraryItemIdentified, "Before Tvdb 79327, Before TmdbTv 100, After Imdb tt0892700, After Tvdb 79327, After TmdbTv 7869"),
            (audit.Action, audit.Identifiers));
    }

    [Fact]
    public async Task Identify_that_jellyfin_finished_after_a_lost_answer_is_not_applied_a_second_time_by_retry()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var workId = await SyncShortAsync(cancellationToken);
        // The Apply's answer is lost and the item shows nothing when the app reads it again: Jellyfin is still working.
        _jellyfin.IgnoresApply = true;
        _jellyfin.AfterApply = _ => throw new HttpRequestException(HttpRequestError.ResponseEnded, "The response ended prematurely.");
        await SetAsync(workId, new LibraryLinkRequest("Movie", FilmId));
        await _factory.IdentifyQueuedLinksAsync(cancellationToken);
        var lost = await _factory.SingleLinkAsync(cancellationToken);
        // Jellyfin finishes by itself (its Apply goes on after the client gave up): the item shows the match now.
        _jellyfin.IgnoresApply = false;
        _jellyfin.AfterApply = null;
        FinishInJellyfin(ShortId);
        _factory.Time.Advance(TimeSpan.FromMinutes(5));

        //Act
        await _factory.PostHttpRequest($"{BaseUrl}/works/{workId}/tmdb/write", _owner.AccessToken);
        await _factory.IdentifyQueuedLinksAsync(cancellationToken);

        //Assert
        Assert.Equal(LibraryLinkWriteState.NotWritten, lost.WriteState);
        Assert.Contains("answer never came", lost.WriteError, StringComparison.Ordinal);
        var link = await _factory.SingleLinkAsync(cancellationToken);
        Assert.Equal((LibraryLinkWriteState.Written, (string?)null), (link.WriteState, link.WriteError));
        // Retry read the item first: it showed the match, so the one Apply of the first attempt is the only one the app ever sent.
        Assert.Single(_factory.JellyfinHttp.Requests, request => request.Uri.AbsolutePath.StartsWith("/Items/RemoteSearch/Apply/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Retry_of_a_series_whose_last_apply_was_sent_minutes_ago_waits_and_calls_jellyfin_not_at_all_while_a_film_never_waits()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var filmWorkId = await SyncShortAsync(cancellationToken);
        _jellyfin.Series.Add(JellyfinSamples.SeriesWith(PenguinsId, "The Penguins Of Madagascar", 2008, $"{JellyfinSamples.TvShowsRoot}/Penguins"));
        _jellyfin.Episodes.Add(JellyfinSamples.EpisodeWith("e5e5e5e5e5e5e5e5e5e5e5e5e5e5e5e5", "Gone in a Flash", PenguinsId, 1, 1, $"{JellyfinSamples.TvShowsRoot}/Penguins/S01E01.mkv"));
        await _factory.SyncLibraryAsync(_jellyfinIntegration.Id, _owner.AccessToken, cancellationToken);
        var showWorkId = await _factory.WorkOfItemAsync(PenguinsId, cancellationToken);
        var sentAt = _factory.Time.GetUtcNow().UtcDateTime.AddMinutes(1);
        // A series and a film whose Apply was sent a minute after the links were set, and not confirmed.
        var series = await _links.Create(
            _jellyfinIntegration, PenguinsId, showWorkId, MediaIdentifierScheme.TmdbTv, ShowId, writeState: LibraryLinkWriteState.NotWritten, setBy: _owner.User);
        var film = await _links.Create(
            _jellyfinIntegration, ShortId, filmWorkId, MediaIdentifierScheme.Tmdb, FilmId, writeState: LibraryLinkWriteState.NotWritten, setBy: _owner.User);
        var audits = new AuditEventSeeder(_factory.Scope);
        await audits.CreateForLink(AuditAction.LibraryItemIdentified, series, "The Penguins", [], _owner.User, sentAt);
        await audits.CreateForLink(AuditAction.LibraryItemIdentified, film, "Landing Stripling", [], _owner.User, sentAt);
        _factory.Time.Advance(TimeSpan.FromMinutes(3));
        var sent = _factory.JellyfinHttp.Requests.Count;

        //Act
        await _factory.PostHttpRequest($"{BaseUrl}/tmdb/links/{series.Id}/write", _owner.AccessToken);
        await _factory.PostHttpRequest($"{BaseUrl}/tmdb/links/{film.Id}/write", _owner.AccessToken);
        await _factory.IdentifyQueuedLinksAsync(cancellationToken);
        var waiting = (await _factory.LinksAsync(cancellationToken)).ToDictionary(link => link.Id);
        var seriesRequests = _factory.JellyfinHttp.Requests.Skip(sent).Count(request => request.Uri.AbsolutePath.Contains(PenguinsId, StringComparison.OrdinalIgnoreCase));

        //Assert
        // The series waits, with the time it may be tried again named; Jellyfin is not called for it at all.
        Assert.Equal(
            (LibraryLinkWriteState.NotWritten, LibraryLinkWriter.SeriesStillWorkingError(sentAt.AddMinutes(10)), (DateTime?)null),
            (waiting[series.Id].WriteState, waiting[series.Id].WriteError, waiting[series.Id].IdentifiedUtc));
        Assert.EndsWith(" UTC.", waiting[series.Id].WriteError, StringComparison.Ordinal);
        Assert.Equal(0, seriesRequests);
        Assert.DoesNotContain(_jellyfin.Applied, applied => applied.ItemId == PenguinsId);
        // A film's Apply takes seconds: it is never made to wait.
        Assert.Equal(LibraryLinkWriteState.Written, waiting[film.Id].WriteState);

        // Past the ten minutes the series is read and identified.
        _factory.Time.Advance(TimeSpan.FromMinutes(11));
        await _factory.PostHttpRequest($"{BaseUrl}/tmdb/links/{series.Id}/write", _owner.AccessToken);
        await _factory.IdentifyQueuedLinksAsync(cancellationToken);
        Assert.Equal(LibraryLinkWriteState.Written, (await _factory.LinksAsync(cancellationToken)).Single(link => link.Id == series.Id).WriteState);
        Assert.Contains(_jellyfin.Applied, applied => applied.ItemId == PenguinsId);
    }

    [Fact]
    public async Task Identify_of_a_film_in_a_ukrainian_library_is_identified_with_the_ukrainian_name_through_the_worker()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // "Фільми" is a Ukrainian-language library: Apply refreshes the item in it, while a search with no language falls back
        // to the server's English. The search carries the library's own language, so its result is the one Apply gives.
        _jellyfin.AddFilm("55001", "Landing Stripling", 1962, namesByLanguage: new Dictionary<string, string> { ["uk"] = "Посадка салаги" });
        _tmdb.AddWork(new FakeTmdbWork(MediaWorkKind.Movie, 55001, "Посадка салаги"));
        var workId = await SyncShortAsync(cancellationToken, path: $"{JellyfinSamples.MoviesRoot}/Landing Stripling (1962).mkv");
        await SetAsync(workId, new LibraryLinkRequest("Movie", "55001"));

        //Act
        await _factory.IdentifyQueuedLinksAsync(cancellationToken);

        //Assert
        var link = await _factory.SingleLinkAsync(cancellationToken);
        Assert.Equal((LibraryLinkWriteState.Written, (string?)null), (link.WriteState, link.WriteError));
        Assert.NotNull(link.IdentifiedUtc);
        Assert.Contains("\"MetadataLanguage\":\"uk\",\"MetadataCountryCode\":\"UA\"", Assert.Single(_jellyfin.RemoteSearches).Body, StringComparison.Ordinal);
        Assert.Equal("Посадка салаги", (string?)Movie(ShortId)["Name"]);
    }

    [Fact]
    public async Task Identify_masks_the_key_in_every_stored_sentence()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        const string Key = "sekret-api-key-0123";
        var library = await _integrations.Create(FakeLibraryItemIdentifier.TypeId, "Fake library", apiKey: Key);
        var work = await new MediaWorkSeeder(_factory.Scope).CreateMovie(library, "Landing Stripling", 1962);
        var failed = await _links.Create(library, "a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1", work.Id, MediaIdentifierScheme.Tmdb, FilmId, writeState: LibraryLinkWriteState.Queued);
        var refused = await _links.Create(library, "a2a2a2a2a2a2a2a2a2a2a2a2a2a2a2a2", work.Id, MediaIdentifierScheme.Tmdb, FilmId, writeState: LibraryLinkWriteState.Queued);
        // The library echoes the key in its failure's message, and in the sentence of a refusal.
        _factory.FakeLibraryIdentifier.Answer = request => request.SourceItemId == "a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1"
            ? IntegrationCallResult<LibraryIdentification>.Failed(IntegrationCallStatus.Unreachable, $"The library at https://x/?api_key={Key} didn't answer.")
            : IntegrationCallResult<LibraryIdentification>.Succeeded(
                FakeLibraryItemIdentifier.Refused(LibraryIdentifyRefusal.LibraryUnknown, $"Refused for {Key}."));

        //Act
        await _factory.IdentifyQueuedLinksAsync(cancellationToken);

        //Assert
        var links = (await _factory.LinksAsync(cancellationToken)).ToDictionary(link => link.Id);
        Assert.All(new[] { failed.Id, refused.Id }, id =>
        {
            Assert.Equal(LibraryLinkWriteState.NotWritten, links[id].WriteState);
            Assert.DoesNotContain(Key, links[id].WriteError, StringComparison.Ordinal);
        });
        Assert.StartsWith("The library at https://x/?api_key=", links[failed.Id].WriteError, StringComparison.Ordinal);
        Assert.StartsWith("Refused for ", links[refused.Id].WriteError, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Dialog_and_card_say_a_link_the_old_code_wrote_is_not_identified_yet_and_show_the_sentence_of_a_refusal()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var workId = await SyncShortAsync(cancellationToken);
        // The link the ids-only code wrote: Written, the item holds its id, and the app never confirmed the item identified.
        var legacy = await _links.Create(
            _jellyfinIntegration, ShortId, workId, MediaIdentifierScheme.Tmdb, FilmId, writeState: LibraryLinkWriteState.Written, setBy: _owner.User,
            configure: row => row.LookupRequestedUtc = row.SetUtc);

        //Act
        var before = await (await _factory.GetHttpRequest($"{BaseUrl}/works/{workId}/tmdb", _owner.AccessToken)).ReadDialogAsync(cancellationToken);
        // The library is set to save beside the media: the repair's identification is refused with its reason.
        _jellyfin.LibraryOptionsOf(JellyfinSamples.CartoonsRoot)["SaveLocalMetadata"] = true;
        _factory.Time.Advance(TimeSpan.FromMinutes(1));
        await _factory.PostHttpRequest("/api/library/tmdb/catch-up", _owner.AccessToken);
        await _factory.IdentifyQueuedLinksAsync(cancellationToken);
        var after = await (await _factory.GetHttpRequest($"{BaseUrl}/works/{workId}/tmdb", _owner.AccessToken)).ReadDialogAsync(cancellationToken);

        //Assert
        // Before: the state says Written and the identified time says "not yet", on the item and on the card, so the page
        // can't say "Identified" for a link nothing confirmed.
        var legacyItem = Assert.Single(before.Items);
        Assert.Equal(
            (legacy.Id, (LibraryLinkWriteState?)LibraryLinkWriteState.Written, (DateTime?)null, (string?)null),
            (legacyItem.LinkId, legacyItem.WriteState, legacyItem.IdentifiedUtc, legacyItem.WriteError));
        Assert.False(before.Work.Link!.Identified);
        Assert.False(before.Work.Link.UnderWay);
        // After: the item's own sentence reaches the dialog and the card, for the owner, and nothing was sent.
        const string Sentence =
            "Jellyfin's library “Мульфільми” saves artwork and metadata next to the media, so identifying this item could change files in your media folders; the app left it alone. Switch that off in the library's settings in Jellyfin, then retry.";
        var refusedItem = Assert.Single(after.Items);
        Assert.Equal(
            ((LibraryLinkWriteState?)LibraryLinkWriteState.NotWritten, (string?)Sentence, (DateTime?)null),
            (refusedItem.WriteState, refusedItem.WriteError, refusedItem.IdentifiedUtc));
        Assert.Equal((LibraryLinkWriteState.NotWritten, Sentence, false), (after.Work.Link!.WriteState, after.Work.Link.WriteError, after.Work.Link.Identified));
        Assert.Empty(_jellyfin.Applied);
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    private Task ConnectJellyfinAsync() => CreateJellyfinAsync();

    private async Task CreateJellyfinAsync() =>
        _jellyfinIntegration = await _integrations.Create("jellyfin", "Living room", apiKey: ApiKey);

    // The short of the live check, synced, with TMDb connected; its work.
    private async Task<Guid> SyncShortAsync(CancellationToken cancellationToken, string? path = null)
    {
        await CreateJellyfinAsync();
        _jellyfin.Movies.Add(JellyfinSamples.MovieWith(
            ShortId, "Landing Stripling", null, 1962, path ?? $"{JellyfinSamples.CartoonsRoot}/Tom & Jerry/120.mkv"));
        await _factory.SyncLibraryAsync(_jellyfinIntegration.Id, _owner.AccessToken, cancellationToken);
        await _integrations.Create("tmdb", "TMDb", FakeTmdbServer.BaseUrl, apiKey: FakeTmdbServer.Token);
        return await _factory.WorkOfItemAsync(ShortId, cancellationToken);
    }

    // The owner's Save in the dialog: the link is queued, nothing is identified until the worker runs.
    private async Task SetAsync(Guid workId, LibraryLinkRequest request)
    {
        var response = await _factory.PutHttpRequest($"{BaseUrl}/works/{workId}/tmdb", _owner.AccessToken, request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // What Jellyfin's own Apply, still running after the client gave up, does to the item: its result is on it.
    private void FinishInJellyfin(string itemId)
    {
        var row = _jellyfin.Matches.Single(match => match.Value == FilmId).Row;
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"{FakeJellyfinServer.DefaultBaseUrl}/Items/RemoteSearch/Apply/{itemId}?replaceAllImages=true"));
        request.Headers.TryAddWithoutValidation("Authorization", $"MediaBrowser Token=\"{ApiKey}\"");
        using var answer = _jellyfin.Answer(request, row.ToJsonString());
        Assert.Equal(HttpStatusCode.NoContent, answer.StatusCode);
    }

    private JsonObject Movie(string itemId) => _jellyfin.Movies.Single(movie => (string?)movie["Id"] == itemId);
}
