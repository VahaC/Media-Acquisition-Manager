using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Api.Services.LibraryLinks;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Tests.LibraryLinks;

// MAM-34 — the TMDb page's review of the works without a TMDb id: the queue one work at a time (GET tmdb/queue, the
// works in the Library page's title order without those the owner marked "none fits"), "none fits" itself (PUT/DELETE
// works/{id}/tmdb/none-fits), the confident suggestions paged by title with each library's numbers (GET tmdb/confident,
// "none fits" left out) and "Accept all confident" (POST tmdb/confident): every ticked work checked again on the server,
// linked only while its suggestion is still the confident one — skipped with the reason otherwise —, and written into the
// library in the background, only what was linked: a series' link stays in the app, as Jellyfin's save of a series
// rewrites all its seasons and episodes.
public sealed class LibraryLinkReviewHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/library";
    private const string JellyfinApiKey = "0123456789abcdef0123456789abcdef";
    private const string TomAndJerryFolder = "/media/cartoons/Tom & Jerry e001-e161 (1940-1967)";
    private const string CalypsoId = "a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1";
    private const string LandingId = "b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2";
    private const string DickyId = "c3c3c3c3c3c3c3c3c3c3c3c3c3c3c3c3";
    private const string TakenId = "d4d4d4d4d4d4d4d4d4d4d4d4d4d4d4d4";
    private const string IdentifiedId = "e5e5e5e5e5e5e5e5e5e5e5e5e5e5e5e5";
    private const string PenguinsId = "f6f6f6f6f6f6f6f6f6f6f6f6f6f6f6f6";
    private const string PenguinsFolder = "/media/tv/The Penguins of Madagascar";
    private const string ShowsFolder = "/media/tv-shows/The Penguins of Madagascar";
    private const string NoLongerConfident = "It is no longer the work's confident suggestion.";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private IIntegrationConnectionSeeder _integrations = null!;
    private AuthorizedUser _owner = null!;
    private FakeJellyfinServer _jellyfin = null!;
    private FakeTmdbServer _tmdb = null!;
    private IntegrationConnectionEntity _library = null!;

    public LibraryLinkReviewHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _integrations = new IntegrationConnectionSeeder(_factory.Scope);
        _owner = await new UserSeeder(_factory.Scope).CreateAuthorizedOwner("owner");
        _jellyfin = new FakeJellyfinServer { ApiKey = JellyfinApiKey };
        _jellyfin.Movies.Clear();
        _jellyfin.Movies.AddRange(
        [
            Short(CalypsoId, "119. Calypso Cat"),
            Short(LandingId, "120. Landing Stripling"),
            Short(DickyId, "121. Dicky Moe"),
            JellyfinSamples.MovieWith(TakenId, "Заручниця", "Taken", 2008, "/media/movies/Taken (2008)/Taken.mkv"),
            JellyfinSamples.MovieWith(IdentifiedId, "Том і Джеррі: Фільм", null, 1992, "/media/cartoons/Tom and Jerry The Movie (1992).mkv", tmdbId: "11134"),
        ]);
        _jellyfin.Arm(_factory.JellyfinHttp);
        _tmdb = FakeTmdbServer.Arm(_factory.TmdbHttp);
        _tmdb.AddWork(new FakeTmdbWork(MediaWorkKind.Movie, 7001, "Калипсо", "Calypso Cat", "1962-06-22", RuntimeMinutes: 7));
        _tmdb.AddWork(new FakeTmdbWork(MediaWorkKind.Movie, 223021, "Посадка салаги", "Landing Stripling", "1962-04-20", RuntimeMinutes: 6));
        _tmdb.AddWork(new FakeTmdbWork(MediaWorkKind.Movie, 7002, "Дикі Мо", "Dicky Moe", "1962-12-14", RuntimeMinutes: 7));
        _tmdb.AddWork(new FakeTmdbWork(MediaWorkKind.Movie, 8681, "Заручниця", "Taken", "2008-02-18", RuntimeMinutes: 94));
        _tmdb.AddWork(new FakeTmdbWork(MediaWorkKind.Movie, 11134, "Том і Джеррі: Фільм", "Tom and Jerry: The Movie", "1992-07-30"));
        _tmdb.AddSearch(MediaWorkKind.Movie, "Calypso Cat", 1962, 7001);
        _tmdb.AddSearch(MediaWorkKind.Movie, "Landing Stripling", 1962, 223021);
        _tmdb.AddSearch(MediaWorkKind.Movie, "Dicky Moe", 1962, 7002);
        _tmdb.AddSearch(MediaWorkKind.Movie, "Taken", 2008, 8681);
        _library = await _integrations.Create("jellyfin", "Living room", apiKey: JellyfinApiKey);
        var cancellationToken = TestContext.Current.CancellationToken;
        await _factory.SyncLibraryAsync(_library.Id, _owner.AccessToken, cancellationToken);
        // Measured: every work but "Dicky Moe", whose runtime is then unknown — so not confident (decision 8).
        var inspections = new MediaInspectionSeeder(_factory.Scope);
        await inspections.Measure($"{TomAndJerryFolder}/119. Calypso Cat (1962).mkv", TimeSpan.FromMinutes(7));
        await inspections.Measure($"{TomAndJerryFolder}/120. Landing Stripling (1962).mkv", TimeSpan.FromMinutes(6.2));
        await inspections.Measure("/media/movies/Taken (2008)/Taken.mkv", TimeSpan.FromMinutes(93));
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Queue_shows_the_work_at_an_index_as_its_dialog_in_title_order()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await PrepareAsync(cancellationToken);
        var landingWorkId = await WorkOfAsync(LandingId, cancellationToken);

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}/tmdb/queue?index=1", _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var queue = await ReadAsync<LibraryLinkQueueResponse>(response, cancellationToken);
        // The four works without a TMDb id — the identified cartoon is not one —, the second by title.
        Assert.Equal((4, 1, landingWorkId), (queue.TotalCount, queue.Index, queue.Current!.Work.Id));
        Assert.Equal(("223021", true), (queue.Current.Suggestions[0].CatalogId, queue.Current.Suggestions[0].IsConfident));
        Assert.Equal("Living room", Assert.Single(queue.Current.Items).SourceName);
    }

    [Fact]
    public async Task Queue_without_an_index_shows_the_first_and_past_its_end_shows_none()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await PrepareAsync(cancellationToken);
        var calypsoWorkId = await WorkOfAsync(CalypsoId, cancellationToken);

        //Act
        var first = await ReadAsync<LibraryLinkQueueResponse>(await _factory.GetHttpRequest($"{BaseUrl}/tmdb/queue", _owner.AccessToken), cancellationToken);
        var past = await ReadAsync<LibraryLinkQueueResponse>(await _factory.GetHttpRequest($"{BaseUrl}/tmdb/queue?index=4", _owner.AccessToken), cancellationToken);

        //Assert
        Assert.Equal((4, 0, calypsoWorkId), (first.TotalCount, first.Index, first.Current!.Work.Id));
        Assert.Equal((4, 4, (LibraryLinkDialogResponse?)null), (past.TotalCount, past.Index, past.Current));
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("first")]
    public async Task Queue_with_an_index_that_is_not_one_is_400(string index)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}/tmdb/queue?index={index}", _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal($"“{index}” is not a place in the queue. Use 0 or more.", await response.ReadErrorAsync(cancellationToken));
    }

    [Fact]
    public async Task None_fits_takes_the_work_out_of_the_queue_and_the_same_index_shows_the_next()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await PrepareAsync(cancellationToken);
        var calypsoWorkId = await WorkOfAsync(CalypsoId, cancellationToken);
        var landingWorkId = await WorkOfAsync(LandingId, cancellationToken);
        var now = _factory.Time.GetUtcNow().UtcDateTime;

        //Act
        var response = await _factory.PutHttpRequest(NoneFitsUrl(calypsoWorkId), _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var queue = await ReadAsync<LibraryLinkQueueResponse>(await _factory.GetHttpRequest($"{BaseUrl}/tmdb/queue?index=0", _owner.AccessToken), cancellationToken);
        Assert.Equal((3, landingWorkId), (queue.TotalCount, queue.Current!.Work.Id));
        await _factory.WithDbAsync(async db =>
        {
            var search = await db.LibraryLinkSearches.AsNoTracking().SingleAsync(row => row.MediaWorkId == calypsoWorkId, cancellationToken);
            Assert.Equal(((DateTime?)now, (Guid?)_owner.User.Id), (search.NoneFitsUtc, search.NoneFitsByUserId));
            // Its suggestions stay: the owner may still pick one in its dialog.
            Assert.Equal(1, await db.LibraryLinkCandidates.CountAsync(candidate => candidate.LibraryLinkSearchId == search.Id, cancellationToken));
        });
    }

    [Fact]
    public async Task None_fits_on_a_work_never_searched_keeps_it_unsearched_until_a_run_searches_it_and_keeps_the_mark()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await ConnectCatalogAsync();
        var workId = await WorkOfAsync(DickyId, cancellationToken);

        //Act
        var response = await _factory.PutHttpRequest(NoneFitsUrl(workId), _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var dialog = await ReadAsync<LibraryLinkDialogResponse>(await _factory.GetHttpRequest($"{BaseUrl}/works/{workId}/tmdb", _owner.AccessToken), cancellationToken);
        Assert.Equal((null, true), (dialog.Search.SearchedUtc, dialog.Search.NoneFitsUtc is not null));
        await _factory.PrepareLinkSuggestionsAsync(_owner.AccessToken, cancellationToken);
        await _factory.WithDbAsync(async db =>
        {
            var search = await db.LibraryLinkSearches.AsNoTracking().SingleAsync(row => row.MediaWorkId == workId, cancellationToken);
            Assert.Equal(("Movie,Series|1962|Title:Dicky Moe|FolderName:Tom & Jerry", true), (search.QueryKey, search.NoneFitsUtc is not null));
        });
    }

    [Fact]
    public async Task Clearing_none_fits_puts_the_work_back_in_the_queue()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await PrepareAsync(cancellationToken);
        var calypsoWorkId = await WorkOfAsync(CalypsoId, cancellationToken);
        await _factory.PutHttpRequest(NoneFitsUrl(calypsoWorkId), _owner.AccessToken);

        //Act
        var response = await _factory.DeleteHttpRequest(NoneFitsUrl(calypsoWorkId), _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var queue = await ReadAsync<LibraryLinkQueueResponse>(await _factory.GetHttpRequest($"{BaseUrl}/tmdb/queue", _owner.AccessToken), cancellationToken);
        Assert.Equal((4, calypsoWorkId), (queue.TotalCount, queue.Current!.Work.Id));
        await _factory.WithDbAsync(async db =>
        {
            var search = await db.LibraryLinkSearches.AsNoTracking().SingleAsync(row => row.MediaWorkId == calypsoWorkId, cancellationToken);
            Assert.Equal(((DateTime?)null, (Guid?)null), (search.NoneFitsUtc, search.NoneFitsByUserId));
        });
    }

    [Fact]
    public async Task None_fits_of_an_unknown_work_is_a_bare_404_either_way()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var url = NoneFitsUrl(Guid.NewGuid());

        //Act
        var marked = await _factory.PutHttpRequest(url, _owner.AccessToken);
        var cleared = await _factory.DeleteHttpRequest(url, _owner.AccessToken);

        //Assert
        Assert.Equal((HttpStatusCode.NotFound, HttpStatusCode.NotFound), (marked.StatusCode, cleared.StatusCode));
        Assert.Empty(await marked.Content.ReadAsStringAsync(cancellationToken));
        await _factory.WithDbAsync(async db => Assert.Equal(0, await db.LibraryLinkSearches.CountAsync(cancellationToken)));
    }

    [Fact]
    public async Task Confident_list_pages_the_confident_works_by_title_with_each_librarys_numbers_none_fits_left_out()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await PrepareAsync(cancellationToken);
        await _factory.PutHttpRequest(NoneFitsUrl(await WorkOfAsync(CalypsoId, cancellationToken)), _owner.AccessToken);
        var landingWorkId = await WorkOfAsync(LandingId, cancellationToken);

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}/tmdb/confident?page=1&pageSize=1", _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var list = await ReadAsync<ConfidentSuggestionListResponse>(response, cancellationToken);
        // "Landing Stripling" and "Taken" are confident; "Calypso Cat" too, but the owner said none fits; "Dicky Moe" has
        // no measured runtime.
        Assert.Equal((2, 1, 1), (list.TotalCount, list.Page, list.PageSize));
        var item = Assert.Single(list.Items);
        Assert.Equal((landingWorkId, "120. Landing Stripling", "223021", true), (item.Work.Id, item.Work.Title, item.Candidate.CatalogId, item.Candidate.IsConfident));
        // Per library that holds a file, in name order: its works without an id, and how many of them are confident
        // (decision 4).
        Assert.Equal(
            [("Мульфільми", 3, 1), ("Фільми", 1, 1)],
            list.Locations.Select(location => (location.LocationName!, location.PopulationCount, location.ConfidentCount)));
    }

    [Fact]
    public async Task Confident_list_counts_the_libraries_the_library_page_offers_and_leaves_out_those_that_hold_no_file()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // A series with its TMDb id: "Серіали" holds a file though none of its works lacks an id. The box sets library
        // ("Колекції") and "Документалки" hold none.
        _jellyfin.Series.Add(JellyfinSamples.SeriesWith(PenguinsId, "The Penguins of Madagascar", 2008, ShowsFolder, tmdbId: JellyfinSamples.PenguinsTmdbTvId));
        _jellyfin.Episodes.Add(JellyfinSamples.EpisodeWith("f7f7f7f7f7f7f7f7f7f7f7f7f7f7f7f7", "Gone in a Flash", PenguinsId, 1, 1, $"{ShowsFolder}/S01E01.mkv"));
        await _factory.SyncLibraryAsync(_library.Id, _owner.AccessToken, cancellationToken);
        await PrepareAsync(cancellationToken);
        var offered = await ReadAsync<LibraryLocationListResponse>(await _factory.GetHttpRequest($"{BaseUrl}/locations", _owner.AccessToken), cancellationToken);

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}/tmdb/confident", _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var list = await ReadAsync<ConfidentSuggestionListResponse>(response, cancellationToken);
        // The Library page's library filter (GET api/library/locations), library for library.
        Assert.Equal(offered.Items.Select(location => (Guid?)location.Id), list.Locations.Select(location => location.LocationId));
        Assert.Equal(
            [("Мульфільми", 3, 2), ("Серіали", 0, 0), ("Фільми", 1, 1)],
            list.Locations.Select(location => (location.LocationName!, location.PopulationCount, location.ConfidentCount)));
    }

    [Fact]
    public async Task Confident_list_of_one_library_lists_only_its_works()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await PrepareAsync(cancellationToken);
        var takenWorkId = await WorkOfAsync(TakenId, cancellationToken);
        var moviesId = Guid.Empty;
        await _factory.WithDbAsync(async db => moviesId = await db.LibraryLocations.Where(location => location.Name == "Фільми").Select(location => location.Id).SingleAsync(cancellationToken));

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}/tmdb/confident?locationId={moviesId}", _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var list = await ReadAsync<ConfidentSuggestionListResponse>(response, cancellationToken);
        Assert.Equal((1, takenWorkId, "8681"), (list.TotalCount, Assert.Single(list.Items).Work.Id, list.Items[0].Candidate.CatalogId));
    }

    [Theory]
    [InlineData("locationId=movies", "“movies” is not a library id.")]
    [InlineData("page=0", "Page must be 1 or more.")]
    [InlineData("pageSize=101", "Page size must be between 1 and 100.")]
    public async Task Confident_list_with_a_bad_library_or_page_is_400(string query, string error)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}/tmdb/confident?{query}", _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(error, await response.ReadErrorAsync(cancellationToken));
    }

    [Fact]
    public async Task Accept_all_links_only_what_is_still_confident_writes_only_that_and_skips_the_rest_with_reasons()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await PrepareAsync(cancellationToken);
        var calypsoWorkId = await WorkOfAsync(CalypsoId, cancellationToken);
        var landingWorkId = await WorkOfAsync(LandingId, cancellationToken);
        var dickyWorkId = await WorkOfAsync(DickyId, cancellationToken);
        var takenWorkId = await WorkOfAsync(TakenId, cancellationToken);
        await _factory.PutHttpRequest(NoneFitsUrl(calypsoWorkId), _owner.AccessToken);
        var sent = _factory.JellyfinHttp.Requests.Count;

        //Act
        var response = await _factory.PostHttpRequest(
            $"{BaseUrl}/tmdb/confident",
            _owner.AccessToken,
            new LibraryLinkAcceptRequest(
            [
                Accept(landingWorkId, "Movie", "223021"),
                Accept(calypsoWorkId, "Movie", "7001"),
                Accept(dickyWorkId, "Movie", "7002"),
                // The page was read before; this is not what is confident for it.
                Accept(takenWorkId, "Series", "8681"),
            ]));

        //Assert
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var batch = await ReadAsync<LibraryLinkBatchResponse>(response, cancellationToken);
        Assert.Equal(
            [
                (landingWorkId, LinkBatchOutcome.Linked, (string?)null, 1),
                (calypsoWorkId, LinkBatchOutcome.Skipped, "You marked that none fits.", 0),
                (dickyWorkId, LinkBatchOutcome.Skipped, NoLongerConfident, 0),
                (takenWorkId, LinkBatchOutcome.Skipped, NoLongerConfident, 0),
            ],
            batch.Items.Select(item => (item.WorkId, item.Outcome, item.Reason, item.Links.Count)));
        Assert.Equal(LibraryLinkWriteState.Queued, batch.Items[0].Links[0].WriteState);
        // Nothing sent yet: the writes are the background writer's (decision 6), which the click woke (R13).
        Assert.Equal(sent, _factory.JellyfinHttp.Requests.Count);
        Assert.True(_factory.Resolve<LibraryLinkWriteQueue>().Reader.TryPeek(out _));
        await _factory.WithDbAsync(async db =>
        {
            var link = await db.LibraryItemLinks.AsNoTracking().SingleAsync(cancellationToken);
            Assert.Equal(
                (LandingId, (Guid?)landingWorkId, "223021", (Guid?)batch.BatchId, LibraryLinkWriteState.Queued),
                (link.SourceItemId, link.MediaWorkId, link.Value, link.BatchId, link.WriteState));
            Assert.Equal(AuditAction.LibraryLinkSet, (await db.AuditEvents.AsNoTracking().SingleAsync(cancellationToken)).Action);
            // One recommendation run for the click.
            Assert.Equal(RecommendationRunTrigger.LinkChanged, (await db.RecommendationRuns.AsNoTracking().SingleAsync(cancellationToken)).Trigger);
        });

        await _factory.Resolve<LibraryLinkWriteWorker>().RunQueuedAsync(cancellationToken);
        var polled = await ReadAsync<LibraryLinkBatchResponse>(
            await _factory.GetHttpRequest($"{BaseUrl}/tmdb/batches/{batch.BatchId}", _owner.AccessToken), cancellationToken);
        Assert.Equal(LibraryLinkWriteState.Written, Assert.Single(polled.Items[0].Links).WriteState);
        // Jellyfin was written once, the linked item only.
        var posted = Assert.Single(_factory.JellyfinHttp.Requests, request => request.Method == HttpMethod.Post);
        Assert.Contains(LandingId, posted.Uri.AbsolutePath, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(["223021", null, null, null], ((string[])[LandingId, CalypsoId, DickyId, TakenId]).Select(TmdbOf));
    }

    [Fact]
    public async Task Accept_all_of_a_confident_show_links_its_series_kept_in_the_app_and_leaves_the_writer_nothing_to_send()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        _jellyfin.Series.Add(JellyfinSamples.SeriesWith(PenguinsId, "The Penguins of Madagascar", 2008, PenguinsFolder));
        _jellyfin.Episodes.Add(JellyfinSamples.EpisodeWith("f7f7f7f7f7f7f7f7f7f7f7f7f7f7f7f7", "Gone in a Flash", PenguinsId, 1, 1, $"{PenguinsFolder}/S01E01.mkv"));
        await _factory.SyncLibraryAsync(_library.Id, _owner.AccessToken, cancellationToken);
        await new MediaInspectionSeeder(_factory.Scope).Measure($"{PenguinsFolder}/S01E01.mkv", TimeSpan.FromMinutes(23));
        _tmdb.AddWork(new FakeTmdbWork(
            MediaWorkKind.Series, int.Parse(JellyfinSamples.PenguinsTmdbTvId), "Пінгвіни з Мадагаскару", "The Penguins of Madagascar", "2008-11-28", RuntimeMinutes: 23));
        _tmdb.AddSearch(MediaWorkKind.Series, "The Penguins of Madagascar", 2008, int.Parse(JellyfinSamples.PenguinsTmdbTvId));
        await PrepareAsync(cancellationToken);
        var seriesWorkId = await WorkOfAsync(PenguinsId, cancellationToken);
        var confident = await ReadAsync<ConfidentSuggestionListResponse>(
            await _factory.GetHttpRequest($"{BaseUrl}/tmdb/confident?page=1&pageSize=50", _owner.AccessToken), cancellationToken);
        Assert.Contains(confident.Items, item => item.Work.Id == seriesWorkId && item.Candidate.CatalogId == JellyfinSamples.PenguinsTmdbTvId);
        var asHeld = _jellyfin.Series.Single().DeepClone();
        var sent = _factory.JellyfinHttp.Requests.Count;

        //Act
        var response = await _factory.PostHttpRequest(
            $"{BaseUrl}/tmdb/confident", _owner.AccessToken, new LibraryLinkAcceptRequest([Accept(seriesWorkId, "Series", JellyfinSamples.PenguinsTmdbTvId)]));
        var woken = _factory.Resolve<LibraryLinkWriteQueue>().Reader.TryPeek(out _);
        await _factory.Resolve<LibraryLinkWriteWorker>().RunQueuedAsync(cancellationToken);

        //Assert
        // Linked, kept in the app (Jellyfin's save of a series rewrites all its seasons and episodes): nothing queued,
        // nothing sent.
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var batch = await ReadAsync<LibraryLinkBatchResponse>(response, cancellationToken);
        var linked = Assert.Single(batch.Items);
        Assert.Equal((seriesWorkId, LinkBatchOutcome.Linked), (linked.WorkId, linked.Outcome));
        Assert.Equal(LibraryLinkWriteState.KeptInApp, Assert.Single(linked.Links).WriteState);
        Assert.False(woken);
        Assert.Equal(sent, _factory.JellyfinHttp.Requests.Count);
        Assert.True(JsonNode.DeepEquals(asHeld, _jellyfin.Series.Single()));
        await _factory.WithDbAsync(async db =>
        {
            var link = await db.LibraryItemLinks.AsNoTracking().SingleAsync(cancellationToken);
            Assert.Equal(
                (PenguinsId, MediaWorkKind.Series, (Guid?)batch.BatchId, LibraryLinkWriteState.KeptInApp),
                (link.SourceItemId, link.ItemKind, link.BatchId, link.WriteState));
        });
    }

    [Fact]
    public async Task Accept_all_skips_a_work_linked_meanwhile_one_with_an_id_now_and_one_gone()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await PrepareAsync(cancellationToken);
        var landingWorkId = await WorkOfAsync(LandingId, cancellationToken);
        var identifiedWorkId = await WorkOfAsync(IdentifiedId, cancellationToken);
        var goneWorkId = Guid.NewGuid();
        await new LibraryItemLinkSeeder(_factory.Scope).Create(_library, LandingId, landingWorkId, MediaIdentifierScheme.Tmdb, "223021");

        //Act
        var response = await _factory.PostHttpRequest(
            $"{BaseUrl}/tmdb/confident",
            _owner.AccessToken,
            new LibraryLinkAcceptRequest([Accept(landingWorkId, "Movie", "223021"), Accept(identifiedWorkId, "Movie", "11134"), Accept(goneWorkId, "Movie", "1")]));

        //Assert
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var batch = await ReadAsync<LibraryLinkBatchResponse>(response, cancellationToken);
        Assert.Equal(
            ["It was linked meanwhile.", "Its library reports a TMDb id for it now.", "The work is no longer in the library."],
            batch.Items.Select(item => item.Reason));
        Assert.All(batch.Items, item => Assert.Equal(LinkBatchOutcome.Skipped, item.Outcome));
        await _factory.WithDbAsync(async db => Assert.Equal(1, await db.LibraryItemLinks.CountAsync(cancellationToken)));
    }

    [Fact]
    public async Task Accept_all_the_browser_leaves_halfway_still_wakes_the_writer_for_the_works_it_saved()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await PrepareAsync(cancellationToken);
        var landingWorkId = await WorkOfAsync(LandingId, cancellationToken);
        var takenWorkId = await WorkOfAsync(TakenId, cancellationToken);
        // The first work's links are saved; the tab closes while the second's are, and the request is cancelled.
        static bool SavesLinks(System.Data.Common.DbCommand command) =>
            command.CommandText.Contains("INSERT INTO \"LibraryItemLinks\"", StringComparison.Ordinal);
        _factory.DbCommandHook.BeforeNextCommand(
            SavesLinks,
            _ =>
            {
                _factory.DbCommandHook.BeforeNextCommand(SavesLinks, _ => throw new OperationCanceledException());
                return Task.CompletedTask;
            });

        //Act
        var response = await _factory.PostHttpRequest(
            $"{BaseUrl}/tmdb/confident", _owner.AccessToken, new LibraryLinkAcceptRequest([Accept(landingWorkId, "Movie", "223021"), Accept(takenWorkId, "Movie", "8681")]));
        var woken = _factory.Resolve<LibraryLinkWriteQueue>().Reader.TryPeek(out _);
        await _factory.Resolve<LibraryLinkWriteWorker>().RunQueuedAsync(cancellationToken);

        //Assert
        // R9 — what was saved before the browser left is written all the same, and its recommendations computed again.
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.True(woken);
        Assert.Equal(["223021", null], ((string[])[LandingId, TakenId]).Select(TmdbOf));
        await _factory.WithDbAsync(async db =>
        {
            var link = await db.LibraryItemLinks.AsNoTracking().SingleAsync(cancellationToken);
            Assert.Equal((LandingId, LibraryLinkWriteState.Written), (link.SourceItemId, link.WriteState));
            Assert.Equal(RecommendationRunTrigger.LinkChanged, (await db.RecommendationRuns.AsNoTracking().SingleAsync(cancellationToken)).Trigger);
        });
    }

    [Fact]
    public async Task Accept_all_with_nothing_ticked_is_400()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await ConnectCatalogAsync();

        //Act
        var response = await _factory.PostHttpRequest($"{BaseUrl}/tmdb/confident", _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Tick at least one work to link.", await response.ReadErrorAsync(cancellationToken));
    }

    [Fact]
    public async Task Accept_all_of_more_than_a_page_is_400()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await ConnectCatalogAsync();
        var items = Enumerable.Range(0, 101).Select(_ => Accept(Guid.NewGuid(), "Movie", "1")).ToList();

        //Act
        var response = await _factory.PostHttpRequest($"{BaseUrl}/tmdb/confident", _owner.AccessToken, new LibraryLinkAcceptRequest(items));

        //Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("At most 100 works can be linked at once.", await response.ReadErrorAsync(cancellationToken));
    }

    [Theory]
    [InlineData(null, "Movie", "1", "Give the work to link.")]
    [InlineData("work", "Movie", "1", "“work” is not a work id.")]
    [InlineData("00000000-0000-0000-0000-000000000001", null, "1", "Choose a TMDb film (Movie) or show (Series).")]
    [InlineData("00000000-0000-0000-0000-000000000001", "Episode", "1", "“Episode” is not a TMDb kind. Use Movie or Series.")]
    [InlineData("00000000-0000-0000-0000-000000000001", "Movie", null, "Give the TMDb id to link.")]
    [InlineData("00000000-0000-0000-0000-000000000001", "Movie", "tt0056164", "“tt0056164” is not a TMDb id.")]
    public async Task Accept_all_of_a_malformed_work_is_400_and_links_nothing(string? workId, string? kind, string? id, string error)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await PrepareAsync(cancellationToken);
        var landingWorkId = await WorkOfAsync(LandingId, cancellationToken);

        //Act
        var response = await _factory.PostHttpRequest(
            $"{BaseUrl}/tmdb/confident",
            _owner.AccessToken,
            new LibraryLinkAcceptRequest([Accept(landingWorkId, "Movie", "223021"), new LibraryLinkAcceptItemRequest(workId, kind, id)]));

        //Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(error, await response.ReadErrorAsync(cancellationToken));
        await _factory.WithDbAsync(async db => Assert.Equal(0, await db.LibraryItemLinks.CountAsync(cancellationToken)));
    }

    [Fact]
    public async Task Accept_all_of_a_work_ticked_twice_is_400()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await ConnectCatalogAsync();
        var workId = Guid.NewGuid();

        //Act
        var response = await _factory.PostHttpRequest(
            $"{BaseUrl}/tmdb/confident", _owner.AccessToken, new LibraryLinkAcceptRequest([Accept(workId, "Movie", "1"), Accept(workId, "Movie", "2")]));

        //Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal($"“{workId}” is ticked twice.", await response.ReadErrorAsync(cancellationToken));
    }

    [Fact]
    public async Task Accept_all_without_a_catalog_is_409()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var response = await _factory.PostHttpRequest(
            $"{BaseUrl}/tmdb/confident", _owner.AccessToken, new LibraryLinkAcceptRequest([Accept(Guid.NewGuid(), "Movie", "1")]));

        //Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("Connect a metadata catalog under Integrations first.", await response.ReadErrorAsync(cancellationToken));
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    private static JsonObject Short(string id, string title) =>
        JellyfinSamples.MovieWith(id, title, null, 1962, $"{TomAndJerryFolder}/{title} (1962).mkv");

    private static LibraryLinkAcceptItemRequest Accept(Guid workId, string kind, string id) => new(workId.ToString(), kind, id);

    private Task<IntegrationConnectionEntity> ConnectCatalogAsync() =>
        _integrations.Create("tmdb", "TMDb", FakeTmdbServer.BaseUrl, apiKey: FakeTmdbServer.Token);

    private async Task PrepareAsync(CancellationToken cancellationToken)
    {
        await ConnectCatalogAsync();
        await _factory.PrepareLinkSuggestionsAsync(_owner.AccessToken, cancellationToken);
    }

    private async Task<Guid> WorkOfAsync(string sourceItemId, CancellationToken cancellationToken)
    {
        var workId = Guid.Empty;
        await _factory.WithDbAsync(async db => workId = await db.MediaWorkTitles
            .Where(title => title.SourceItemId == sourceItemId)
            .Select(title => title.MediaWorkId)
            .FirstAsync(cancellationToken));
        return workId;
    }

    private string? TmdbOf(string itemId) =>
        (string?)_jellyfin.Movies.Single(movie => (string?)movie["Id"] == itemId)["ProviderIds"]!.AsObject()["Tmdb"];

    private static string NoneFitsUrl(Guid workId) => $"{BaseUrl}/works/{workId}/tmdb/none-fits";

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken) =>
        (await response.Content.ReadFromJsonAsync<T>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
}
