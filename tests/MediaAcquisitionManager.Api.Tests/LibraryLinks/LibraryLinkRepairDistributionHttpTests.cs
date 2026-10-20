using System.Net;
using System.Text.Json.Nodes;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Api.Services.LibraryLinks;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Texts;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using MediaAcquisitionManager.TestInfrastructure.Texts;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Tests.LibraryLinks;

// MAM-34 (0.2.1, ADR 0026) — the repair ("Update Jellyfin") against the shape of the owner's real server database (inventory of
// 2026-10-06): 229 links — 220 Written by the earlier ids-only code, never confirmed identified (166 cartoons and 14 films that
// need an Identify, 3 whose items already show TMDb's result, 3 series, 33 episode-movies, and the Polanski mistake: the
// documentary that holds the 1962 film's TMDb id beside its own IMDb id), 3 NotWritten "Continental" episodes whose items hold
// the show's own IMDb id, and 6 kept in the app (episode items, two episode-movies TMDb gives no IMDb id). Through the real worker
// and the real Jellyfin adapter against the fake Jellyfin, it asserts what a click counts, what it queues, what each flow
// sends, what the conflict rule refuses, the progress "done of total" midway through a stop and a restart of the worker, and
// that a second click never sends an Apply that already left the app.
public sealed class LibraryLinkRepairDistributionHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string CatchUpUrl = "/api/library/tmdb/catch-up";
    private const string ApiKey = "0123456789abcdef0123456789abcdef";
    private const string ShowTmdbId = "91724";
    private const string ContinentalImdbId = "tt29669983";
    private const string ContinentalEpisodeImdbId = "tt13217676";
    private const string PolanskiItemId = "e1e1e1e1e1e1e1e1e1e1e1e1e1e1e1e1";
    private const string HoldsAnotherImdbSentence = "Jellyfin holds IMDb id {0} for this item, not {1}, and the app doesn't replace a library's own ids on its own. If the link is right, press Retry to identify it anyway; otherwise change the link.";

    private const int Cartoons = 166;
    private const int Films = 14;
    private const int AlreadyShowingFilms = 3;
    private const int Series = 3;
    private const int EpisodeMovies = 33;
    private const int Continentals = 3;
    private const int Written = Cartoons + Films + AlreadyShowingFilms + Series + EpisodeMovies + 1;
    private const int KeptInApp = 6;

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private IIntegrationConnectionSeeder _integrations = null!;
    private ILibraryItemLinkSeeder _links = null!;
    private AuthorizedUser _owner = null!;
    private FakeJellyfinServer _jellyfin = null!;
    private IntegrationConnectionEntity _jellyfinIntegration = null!;
    private Guid _movieWorkId;
    private Guid _seriesWorkId;
    private int _nextItem;

    public LibraryLinkRepairDistributionHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _integrations = new IntegrationConnectionSeeder(_factory.Scope);
        _links = new LibraryItemLinkSeeder(_factory.Scope);
        _owner = await new UserSeeder(_factory.Scope).CreateAuthorizedOwner("owner");
        _jellyfin = new FakeJellyfinServer { ApiKey = ApiKey };
        _jellyfin.Movies.Clear();
        _jellyfin.Arm(_factory.JellyfinHttp);
        _jellyfinIntegration = await _integrations.Create("jellyfin", "Living room", apiKey: ApiKey);
        var works = new MediaWorkSeeder(_factory.Scope);
        _movieWorkId = (await works.CreateMovie(_jellyfinIntegration, "A film", 1962)).Id;
        _seriesWorkId = (await works.CreateSeries(_jellyfinIntegration, "A show", 2008)).Id;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Repair_of_the_owners_distribution_counts_queues_identifies_and_refuses_what_it_must_and_a_second_click_never_sends_an_apply_twice()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var seeded = await SeedAsync();
        var refusedBefore = seeded.RefusedItems.ToDictionary(itemId => itemId, itemId => ItemOf(itemId).DeepClone());
        var keptBefore = (await _factory.LinksAsync(cancellationToken)).Where(link => link.WriteState == LibraryLinkWriteState.KeptInApp).ToDictionary(link => link.Id);
        Assert.Equal(KeptInApp, keptBefore.Count);
        _factory.Time.Advance(TimeSpan.FromMinutes(1));
        var jellyfinRequests = _factory.JellyfinHttp.Requests.Count;

        //Act
        var counted = await _factory.GetHttpRequest(CatchUpUrl, _owner.AccessToken);
        var started = await _factory.PostHttpRequest(CatchUpUrl, _owner.AccessToken);

        //Assert
        // Counted: the 220 links of the earlier code and the 3 refused ones that no Apply was ever sent for; the 6 kept in the
        // app are never counted (an episode item the library doesn't identify, a numbered episode TMDb gives no IMDb id).
        Assert.Equal(HttpStatusCode.OK, counted.StatusCode);
        Assert.Equal(new LibraryLinkCatchUpResponse(Written + Continentals, null), await counted.ReadAsync<LibraryLinkCatchUpResponse>(cancellationToken));
        Assert.Equal(HttpStatusCode.Accepted, started.StatusCode);
        var body = await started.ReadAsync<LibraryLinkCatchUpResponse>(cancellationToken);
        var batchId = body.Progress!.BatchId;
        Assert.Equal(new LibraryLinkCatchUpResponse(Written + Continentals, new LibraryLinkProgressDto(batchId, Written + Continentals, 0)), body);
        // Queued under the click's batch (which also marks them protected); nothing was sent or recorded by the click itself.
        var queued = await _factory.LinksAsync(cancellationToken);
        Assert.Equal(Written + Continentals, queued.Count(link => link.WriteState == LibraryLinkWriteState.Queued && link.BatchId == batchId && link.WriteError is null));
        Assert.Equal(
            keptBefore.Values.OrderBy(link => link.Id).Select(link => (link.Id, link.WriteState, link.WriteError, link.BatchId)),
            queued.Where(link => keptBefore.ContainsKey(link.Id)).OrderBy(link => link.Id).Select(link => (link.Id, link.WriteState, link.WriteError, link.BatchId)));
        Assert.Equal(jellyfinRequests, _factory.JellyfinHttp.Requests.Count);
        Assert.Empty(await _factory.AuditAsync(cancellationToken));

        // The worker is stopped after some identifications, as a stopping host stops it: each lane finishes the link it is
        // inside, and the rest stay queued. Nothing is left Writing and no identification is marked failed for the stop.
        using var stop = new CancellationTokenSource();
        var applies = 0;
        _jellyfin.AfterApply = _ =>
        {
            if (Interlocked.Increment(ref applies) == 60)
                stop.Cancel();
            return null;
        };
        var worker = _factory.Resolve<LibraryLinkWriteWorker>();
        await worker.RunQueuedAsync(stop.Token);
        var midway = await _factory.LinksAsync(cancellationToken);
        var progress = (await (await _factory.GetHttpRequest(CatchUpUrl, _owner.AccessToken)).ReadAsync<LibraryLinkCatchUpResponse>(cancellationToken)).Progress!;
        Assert.DoesNotContain(midway, link => link.WriteState == LibraryLinkWriteState.Writing);
        Assert.DoesNotContain(midway, link => link.WriteError == LibraryLinkWriter.AppErrorMessage);
        var stillQueued = midway.Count(link => link.WriteState == LibraryLinkWriteState.Queued);
        Assert.InRange(stillQueued, 1, Written + Continentals - 60);
        Assert.Equal(new LibraryLinkProgressDto(batchId, Written + Continentals, Written + Continentals - stillQueued), progress);
        Assert.Equal(_jellyfin.Applied.Count, (await _factory.AuditActionsAsync(cancellationToken)).Count(action => action == AuditAction.LibraryItemIdentified));

        // The worker comes back (the restart signals the queued links) and finishes the batch.
        _jellyfin.AfterApply = null;
        await worker.RunQueuedAsync(cancellationToken);
        var identifiedAfter = await (await _factory.GetHttpRequest(CatchUpUrl, _owner.AccessToken)).ReadAsync<LibraryLinkCatchUpResponse>(cancellationToken);

        //Assert
        var links = await _factory.LinksAsync(cancellationToken);
        var byItem = links.ToDictionary(link => link.SourceItemId);
        // 216 Applies: 180 films and cartoons, 3 series, 33 episode-movies — each item exactly once. The 3 items that already show
        // TMDb's result are found so with nothing sent; the 4 mistakes are refused with nothing sent.
        var expectedApplies = Cartoons + Films + Series + EpisodeMovies;
        Assert.Equal(expectedApplies, _jellyfin.Applied.Count);
        Assert.Equal(expectedApplies, _jellyfin.Applied.Select(applied => applied.ItemId).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(EpisodeMovies, _jellyfin.Applied.Count(applied => (string?)applied.Row["SearchProviderName"] == FakeJellyfinServer.OmdbProvider));
        Assert.Equal(3, _jellyfin.RemoteSearches.Count(search => search.Endpoint == "Series"));
        Assert.DoesNotContain(_jellyfin.Applied, applied => seeded.RefusedItems.Contains(applied.ItemId) || seeded.AlreadyShowing.Contains(applied.ItemId));
        Assert.Equal(Written - 1, links.Count(link => link.WriteState == LibraryLinkWriteState.Written));
        Assert.All(links.Where(link => link.WriteState == LibraryLinkWriteState.Written), link => Assert.NotNull(link.IdentifiedUtc));
        foreach (var itemId in seeded.AlreadyShowing)
            Assert.Equal(LibraryLinkWriteState.Written, byItem[itemId].WriteState);
        // The refused ones: the reason in their sentence, never identified, their items exactly as they were.
        foreach (var itemId in seeded.RefusedItems)
        {
            Assert.Equal((LibraryLinkWriteState.NotWritten, (DateTime?)null), (byItem[itemId].WriteState, byItem[itemId].IdentifiedUtc));
            Assert.True(JsonNode.DeepEquals(refusedBefore[itemId], ItemOf(itemId)), $"The refused item {itemId} was changed.");
        }

        Assert.All(seeded.ContinentalItems, itemId => Assert.Equal(string.Format(HoldsAnotherImdbSentence, ContinentalImdbId, ContinentalEpisodeImdbId), TestTexts.En(byItem[itemId].WriteError)));
        Assert.Equal(string.Format(HoldsAnotherImdbSentence, "tt1163854", "tt0056291"), TestTexts.En(byItem[PolanskiItemId].WriteError));
        // The 6 kept in the app are exactly as they were.
        Assert.All(keptBefore.Values, before => Assert.Equal((before.WriteState, before.WriteError, before.BatchId, before.IdentifiedUtc), (byItem[before.SourceItemId].WriteState, byItem[before.SourceItemId].WriteError, byItem[before.SourceItemId].BatchId, byItem[before.SourceItemId].IdentifiedUtc)));
        // One audit row per Apply that left the app, none for the 3 already showing, the 4 refused or the 6 kept.
        var identified = (await _factory.AuditAsync(cancellationToken)).Where(row => row.Action == AuditAction.LibraryItemIdentified).ToList();
        Assert.Equal(expectedApplies, identified.Count);
        Assert.Equal(expectedApplies, identified.Select(row => row.TargetId).Distinct().Count());
        Assert.Equal(expectedApplies, links.Count(link => identified.Any(row => row.TargetId == link.Id)));
        Assert.All(identified, row => Assert.Equal("owner", row.ActorUserName));
        // Done: the batch is no longer pending; what is left to a next click is the 4 refused, which sent nothing.
        Assert.Equal(new LibraryLinkCatchUpResponse(Continentals + 1, null), identifiedAfter);
        var notIdentified = await (await _factory.GetHttpRequest("/api/library/tmdb/links?filter=NotWritten", _owner.AccessToken)).ReadAsync<LibraryLinkListResponse>(cancellationToken);
        Assert.Equal(Continentals + 1, notIdentified.TotalCount);
        Assert.All(notIdentified.Items, row => Assert.StartsWith("Jellyfin holds IMDb id", row.WriteError, StringComparison.Ordinal));

        // A second click: the 4 refused go through the guard again and send nothing; no item that was identified is touched.
        _factory.Time.Advance(TimeSpan.FromMinutes(1));
        var second = await (await _factory.PostHttpRequest(CatchUpUrl, _owner.AccessToken)).ReadAsync<LibraryLinkCatchUpResponse>(cancellationToken);
        await worker.RunQueuedAsync(cancellationToken);
        Assert.Equal(Continentals + 1, second.ToIdentify);
        Assert.Equal(new LibraryLinkProgressDto(second.Progress!.BatchId, Continentals + 1, 0), second.Progress);
        Assert.NotEqual(batchId, second.Progress.BatchId);
        Assert.Equal(expectedApplies, _jellyfin.Applied.Count);
        Assert.Equal(expectedApplies, (await _factory.AuditActionsAsync(cancellationToken)).Count(action => action == AuditAction.LibraryItemIdentified));
        Assert.Equal(
            new LibraryLinkCatchUpResponse(Continentals + 1, null),
            await (await _factory.GetHttpRequest(CatchUpUrl, _owner.AccessToken)).ReadAsync<LibraryLinkCatchUpResponse>(cancellationToken));
        // Nothing else changed: the identified ones keep their identified time and state.
        var afterSecond = (await _factory.LinksAsync(cancellationToken)).ToDictionary(link => link.Id);
        Assert.All(links.Where(link => link.WriteState == LibraryLinkWriteState.Written), link =>
            Assert.Equal((link.WriteState, link.IdentifiedUtc, link.BatchId), (afterSecond[link.Id].WriteState, afterSecond[link.Id].IdentifiedUtc, afterSecond[link.Id].BatchId)));
    }

    // ── the owner's database, as the inventory describes it ─────────────────────────────────────

    private async Task<Seeded> SeedAsync()
    {
        var setUtc = _factory.Time.GetUtcNow().UtcDateTime;
        void Legacy(LibraryItemLinkEntity row) => row.LookupRequestedUtc = setUtc;

        // 166 cartoons and 14 films: Jellyfin names them after their files; the older code wrote their TMDb id into the item
        // (and, on every other one, TMDb's IMDb id beside it).
        for (var number = 1; number <= Cartoons + Films; number++)
        {
            var root = number <= Cartoons ? JellyfinSamples.CartoonsRoot : JellyfinSamples.MoviesRoot;
            await AddFilmLinkAsync($"Film {number}", 100_000 + number, root, holdsImdbId: number % 2 == 0, setUtc, Legacy);
        }

        // 3 films whose items already show TMDb's result (an earlier Identify of Jellyfin's own).
        var alreadyShowing = new List<string>();
        for (var number = 1; number <= AlreadyShowingFilms; number++)
        {
            var itemId = await AddFilmLinkAsync($"Shown {number}", 200_000 + number, JellyfinSamples.CartoonsRoot, holdsImdbId: true, setUtc, Legacy);
            var item = ItemOf(itemId);
            item["Name"] = $"Shown {number}";
            item["Overview"] = $"Overview of shown {number}.";
            item["ImageTags"] = new JsonObject { ["Primary"] = "earlier-poster" };
            alreadyShowing.Add(itemId);
        }

        // Polanski + Skolimowski: Jellyfin's own match is the 2003 documentary; the older code gave it the 1962 film's TMDb id.
        _jellyfin.Movies.Add(JellyfinSamples.MovieWith(
            PolanskiItemId, "Polanski+Skolimowski o Noze", null, 2003, $"{JellyfinSamples.MoviesRoot}/Polanski.mkv", imdbId: "tt1163854", tmdbId: "11502"));
        _jellyfin.AddFilm("11502", "Knife in the Water", 1962, "tt0056291");
        await _links.Create(
            _jellyfinIntegration, PolanskiItemId, _movieWorkId, MediaIdentifierScheme.Tmdb, "11502", writeState: LibraryLinkWriteState.Written, setBy: _owner.User,
            configure: row => (row.ImdbId, row.LookupRequestedUtc) = ("tt0056291", setUtc));

        // 3 series: only the folder's name is there, as Jellyfin found it.
        for (var number = 1; number <= Series; number++)
        {
            var itemId = NextItemId();
            var tmdbTvId = (300_000 + number).ToString(System.Globalization.CultureInfo.InvariantCulture);
            _jellyfin.Series.Add(JellyfinSamples.SeriesWith(itemId, $"Show {number} (folder)", 2008, $"{JellyfinSamples.TvShowsRoot}/Show {number}"));
            _jellyfin.AddShow(tmdbTvId, $"Show {number}", 2008, $"tt{3_000_000 + number}");
            await _links.Create(
                _jellyfinIntegration, itemId, _seriesWorkId, MediaIdentifierScheme.TmdbTv, tmdbTvId, writeState: LibraryLinkWriteState.Written, setBy: _owner.User, configure: Legacy);
        }

        // 33 episode-movies of the documentary library: written by the older code with the episode's own IMDb id.
        for (var number = 1; number <= EpisodeMovies; number++)
        {
            var itemId = NextItemId();
            var episodeImdbId = $"tt{9_000_000 + number}";
            _jellyfin.Movies.Add(JellyfinSamples.MovieWith(
                itemId, $"Greatest Events.S01E{number:00} (file)", null, 2025, $"{JellyfinSamples.DocumentaryRoot}/Greatest Events/S01E{number:00}.mkv", imdbId: number <= 30 ? episodeImdbId : null));
            _jellyfin.AddEpisodeMovie(episodeImdbId, $"Episode {number}", 2019);
            var episode = number;
            await _links.Create(
                _jellyfinIntegration, itemId, _movieWorkId, MediaIdentifierScheme.TmdbTv, ShowTmdbId, LibraryLinkTarget.ShowEpisode, LibraryLinkWriteState.Written, _owner.User,
                configure: row => (row.SeasonNumber, row.EpisodeNumber, row.EpisodeImdbId, row.LookupRequestedUtc) = (1, episode, episodeImdbId, setUtc));
        }

        // The Continental: three items of one show Jellyfin lists as films, each holding the show's own IMDb id; the three links
        // carry the same season, episode and IMDb id (the wrong show was linked). The older code refused to write: NotWritten.
        _jellyfin.AddEpisodeMovie(ContinentalEpisodeImdbId, "The Continental: From the World of John Wick", 2023);
        var continentals = new List<string>();
        for (var number = 1; number <= Continentals; number++)
        {
            var itemId = NextItemId();
            _jellyfin.Movies.Add(JellyfinSamples.MovieWith(
                itemId, $"The Continental E0{number}", null, 2023, $"{JellyfinSamples.MoviesRoot}/John Wick. Collection/Continental E0{number}.mkv", imdbId: ContinentalImdbId));
            await _links.Create(
                _jellyfinIntegration, itemId, _movieWorkId, MediaIdentifierScheme.TmdbTv, "72710", LibraryLinkTarget.ShowEpisode, LibraryLinkWriteState.NotWritten, _owner.User,
                configure: row => (row.SeasonNumber, row.EpisodeNumber, row.EpisodeImdbId, row.WriteError) =
                    (1, 1, ContinentalEpisodeImdbId, Message.Legacy("Jellyfin holds another IMDb id for this item, so the app wrote nothing.")));
            continentals.Add(itemId);
        }

        // Kept in the app: three episode items of a library of series, one in the cartoons, and two episode-movies TMDb gave no
        // IMDb id (the link keeps TMDb's "none" as its error).
        for (var number = 1; number <= 4; number++)
        {
            await _links.Create(
                _jellyfinIntegration, NextItemId(), _movieWorkId, MediaIdentifierScheme.TmdbTv, "1044", LibraryLinkTarget.ShowEpisode, LibraryLinkWriteState.KeptInApp, _owner.User,
                configure: row => (row.ItemKind, row.SeasonNumber, row.EpisodeNumber) = (MediaWorkKind.Episode, 5, 1));
        }

        for (var number = 1; number <= 2; number++)
        {
            var episode = number;
            await _links.Create(
                _jellyfinIntegration, NextItemId(), _movieWorkId, MediaIdentifierScheme.TmdbTv, "84958", LibraryLinkTarget.ShowEpisode, LibraryLinkWriteState.KeptInApp, _owner.User,
                configure: row => (row.SeasonNumber, row.EpisodeNumber, row.WriteError) = (3, episode, LibraryLinkWritability.NoEpisodeImdbReason));
        }

        return new Seeded([.. continentals, PolanskiItemId], continentals, alreadyShowing);
    }

    // A film of the library as Jellyfin has it before its Identify, with the link the earlier code wrote for it.
    private async Task<string> AddFilmLinkAsync(
        string name, int tmdbId, string root, bool holdsImdbId, DateTime setUtc, Action<LibraryItemLinkEntity> legacy)
    {
        var itemId = NextItemId();
        var tmdb = tmdbId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var imdb = $"tt{1_000_000 + tmdbId}";
        _jellyfin.Movies.Add(JellyfinSamples.MovieWith(
            itemId, $"{tmdbId}. {name} (file)", null, 1962, $"{root}/{name}.mkv", imdbId: holdsImdbId ? imdb : null, tmdbId: tmdb));
        _jellyfin.AddFilm(tmdb, name, 1962, imdb, overview: $"Overview of {name.ToLowerInvariant()}.");
        await _links.Create(
            _jellyfinIntegration, itemId, _movieWorkId, MediaIdentifierScheme.Tmdb, tmdb, writeState: LibraryLinkWriteState.Written, setBy: _owner.User,
            configure: row =>
            {
                row.ImdbId = imdb;
                legacy(row);
            });
        return itemId;
    }

    private string NextItemId() => (++_nextItem).ToString("x32", System.Globalization.CultureInfo.InvariantCulture);

    private JsonObject ItemOf(string itemId) =>
        _jellyfin.Movies.Concat(_jellyfin.Series).Single(item => (string?)item["Id"] == itemId);

    private sealed record Seeded(IReadOnlyList<string> RefusedItems, IReadOnlyList<string> ContinentalItems, IReadOnlyList<string> AlreadyShowing);
}
