using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Tests.Library;

// MAM-35, ADR 0027 — the picture on a Library card (GET /api/library/works): each work's posterUrl, the app's own URL of
// its library's poster of the item the card is named after — a movie's, a series' own (never an episode's), an episode of
// no series its own — else of the next of its items in title order that has one, then of the original titles' items, then
// of any other, and null when none has. A series' episodes carry their own. Every signed-in user gets it.
public sealed class LibraryWorksPostersHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/library/works";
    private const string PostersUrl = "/api/library/posters/";
    private const string ApiKey = "0123456789abcdef0123456789abcdef";

    /// <summary>The title of the Tom &amp; Jerry Kids episode item, the sample episode of no series.</summary>
    private const string TomAndJerryKids = "1. Flippin Fido; Dakota Droopy & the Lost";

    // The titled item's id sorts after the other's, so the rule that picks it is the title's, not the last fallback's.
    private const string AlphaItemId = "f1f1f1f1f1f1f1f1f1f1f1f1f1f1f1f1";
    private const string BravoItemId = "a2a2a2a2a2a2a2a2a2a2a2a2a2a2a2a2";
    private const string AlphaTag = "aaaa1111aaaa1111aaaa1111aaaa1111";
    private const string BravoTag = "bbbb2222bbbb2222bbbb2222bbbb2222";

    // The original title's item sorts after the untitled one, so the rule that picks it is the original title's, not the
    // last fallback's.
    private const string CharlieItemId = "f3f3f3f3f3f3f3f3f3f3f3f3f3f3f3f3";
    private const string DeltaItemId = "a4a4a4a4a4a4a4a4a4a4a4a4a4a4a4a4";
    private const string CharlieTag = "cccc3333cccc3333cccc3333cccc3333";
    private const string DeltaTag = "dddd4444dddd4444dddd4444dddd4444";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private IIntegrationConnectionSeeder _integrations = null!;
    private AuthorizedUser _owner = null!;
    private AuthorizedUser _member = null!;
    private FakeJellyfinServer _jellyfin = null!;

    public LibraryWorksPostersHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _integrations = new IntegrationConnectionSeeder(_factory.Scope);
        var users = new UserSeeder(_factory.Scope);
        _owner = await users.CreateAuthorizedOwner("owner");
        _member = await users.CreateAuthorized("member");
        _jellyfin = new FakeJellyfinServer { ApiKey = ApiKey };
        _jellyfin.Arm(_factory.JellyfinHttp);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Works_as_a_member_carries_the_poster_url_of_each_movie_series_and_episode_of_no_series()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await SyncSamplesWithImagesAsync();

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, _member.AccessToken);
        var again = await _factory.GetHttpRequest(BaseUrl, _member.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var list = await ReadListAsync(response, cancellationToken);
        // A series shows its own poster, never Daybreak's or its season's; Tom & Jerry Kids, of no series, its own.
        var expected = new Dictionary<string, string>
        {
            [TomAndJerryKids] = Url(JellyfinSamples.TomAndJerryKidsId, "4a58e8e95f7f6ad8307f541d94e23800"),
            ["13-й район"] = Url(JellyfinSamples.District13Id, "876b255c3ee8ce3ab0bb55c4c03ab0ef"),
            ["How the Universe Works"] = Url(JellyfinSamples.HowTheUniverseWorksId, "658e4aa24e972c31d2f4af570181385c"),
            ["Mickey Mouse Clubhouse"] = Url(JellyfinSamples.MickeyMouseClubhouseId, "01de7f6467277e81d5abec6fc8256006"),
            ["The Continental"] = Url(JellyfinSamples.ContinentalId, "41f7c98888a81ea514189c27c6c3970c"),
            ["Yellowstone"] = Url(JellyfinSamples.YellowstoneId, JellyfinSamples.YellowstonePosterTag),
        };
        Assert.Equal(expected, list.Items.Where(work => work.Title != "Чужі").ToDictionary(work => work.Title!, work => work.PosterUrl!));
        // The two "Чужі" items share one title: either's poster, the same one on every read.
        var aliens = list.Items.Single(work => work.Title == "Чужі").PosterUrl;
        Assert.Contains(
            aliens,
            new[]
            {
                Url(JellyfinSamples.AliensTheatricalId, "a608b4104259bc0bb01c6ad262c53aed"),
                Url(JellyfinSamples.AliensSpecialEditionId, "5e28b80243c705099a2c9b398fd53fd2"),
            });
        Assert.Equal(aliens, (await ReadListAsync(again, cancellationToken)).Items.Single(work => work.Title == "Чужі").PosterUrl);
        // The URLs name the stored rows, nothing else.
        await _factory.WithDbAsync(async db =>
        {
            var stored = await db.MediaWorkPosters.Select(poster => PostersUrl + poster.SourceItemId + "-" + poster.Tag + ".jpg").ToListAsync(cancellationToken);
            Assert.All(list.Items, work => Assert.Contains(work.PosterUrl, stored));
        });
    }

    [Fact]
    public async Task Works_of_a_work_of_two_items_shows_the_poster_of_the_item_whose_title_it_shows()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // One film (one TMDb id) in two items of different titles: the card is named after the first in title order.
        await SyncMoviesAsync(Pictured(Bravo(), BravoTag), Pictured(Alpha(), AlphaTag));

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, _member.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var work = Assert.Single((await ReadListAsync(response, cancellationToken)).Items);
        Assert.Equal(("Alpha", Url(AlphaItemId, AlphaTag)), (work.Title, work.PosterUrl));
        await _factory.WithDbAsync(async db =>
            Assert.Equal(
                [(BravoItemId, BravoTag, work.Id), (AlphaItemId, AlphaTag, work.Id)],
                (await db.MediaWorkPosters.ToListAsync(cancellationToken))
                    .Select(poster => (poster.SourceItemId, poster.Tag, poster.MediaWorkId))
                    .OrderBy(poster => poster.SourceItemId, StringComparer.Ordinal)));
    }

    [Fact]
    public async Task Works_of_a_work_whose_titled_item_has_no_poster_shows_the_next_items()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await SyncMoviesAsync(Pictured(Bravo(), BravoTag), Alpha());

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, _member.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var work = Assert.Single((await ReadListAsync(response, cancellationToken)).Items);
        Assert.Equal(("Alpha", Url(BravoItemId, BravoTag)), (work.Title, work.PosterUrl));
        await _factory.WithDbAsync(async db =>
            Assert.Equal(BravoItemId, (await db.MediaWorkPosters.SingleAsync(cancellationToken)).SourceItemId));
    }

    [Fact]
    public async Task Works_of_a_work_whose_displayed_titles_item_has_no_poster_shows_the_original_titles_item_before_an_untitled_ones()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // Delta, with no title, sorts first by item id: the last fallback would pick it, the original title's rule Charlie.
        await SyncMoviesAsync(Alpha(), Pictured(Charlie(), CharlieTag), Pictured(Delta(), DeltaTag));

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, _member.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var work = Assert.Single((await ReadListAsync(response, cancellationToken)).Items);
        Assert.Equal(("Alpha", Url(CharlieItemId, CharlieTag)), (work.Title, work.PosterUrl));
        await _factory.WithDbAsync(async db =>
            Assert.Equal(
                [(DeltaItemId, DeltaTag, work.Id), (CharlieItemId, CharlieTag, work.Id)],
                (await db.MediaWorkPosters.ToListAsync(cancellationToken))
                    .Select(poster => (poster.SourceItemId, poster.Tag, poster.MediaWorkId))
                    .OrderBy(poster => poster.SourceItemId, StringComparer.Ordinal)));
    }

    [Fact]
    public async Task Works_of_a_work_whose_only_poster_is_an_untitled_items_shows_it()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await SyncMoviesAsync(Alpha(), Pictured(Delta(), DeltaTag));

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, _member.AccessToken);

        //Assert
        // No title of the work names Delta; its picture is the work's all the same, never no picture.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var work = Assert.Single((await ReadListAsync(response, cancellationToken)).Items);
        Assert.Equal(("Alpha", Url(DeltaItemId, DeltaTag)), (work.Title, work.PosterUrl));
        await _factory.WithDbAsync(async db =>
        {
            var poster = await db.MediaWorkPosters.SingleAsync(cancellationToken);
            Assert.Equal((DeltaItemId, DeltaTag, work.Id), (poster.SourceItemId, poster.Tag, poster.MediaWorkId));
        });
    }

    [Fact]
    public async Task Works_of_works_whose_items_have_no_image_carry_no_poster_url()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // The samples as recorded before the sync asked for images: no ImageTags at all.
        _jellyfin.Series.AddRange(JellyfinSamples.Series());
        _jellyfin.Episodes.AddRange(JellyfinSamples.Episodes());
        await SyncAsync();

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var list = await ReadListAsync(response, cancellationToken);
        Assert.Equal(7, list.Items.Count);
        Assert.All(list.Items, work => Assert.Null(work.PosterUrl));
        await _factory.WithDbAsync(async db => Assert.Equal(0, await db.MediaWorkPosters.CountAsync(cancellationToken)));
    }

    [Fact]
    public async Task Works_after_a_sync_that_read_a_new_tag_carries_the_new_url()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        const string NewTag = "9f674fd57061c114f4e1654fbecee5ff";
        var jellyfin = await SyncSamplesWithImagesAsync();
        _jellyfin.Movies.Single(movie => (string?)movie["Id"] == JellyfinSamples.ContinentalId)["ImageTags"] = new JsonObject { ["Primary"] = NewTag };
        await new LibrarySnapshotSeeder(_factory.Scope).Sync(jellyfin, _owner.User);

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}?search=continental", _member.AccessToken);

        //Assert
        // A new picture is a new URL, so a browser that keeps the old one for good never shows it again.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var work = Assert.Single((await ReadListAsync(response, cancellationToken)).Items);
        Assert.Equal(Url(JellyfinSamples.ContinentalId, NewTag), work.PosterUrl);
    }

    [Fact]
    public async Task Episodes_of_a_series_carry_each_episodes_own_poster_url()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await SyncSamplesWithImagesAsync();
        var yellowstone = await WorkOfAsync(JellyfinSamples.YellowstoneId, cancellationToken);

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}/{yellowstone}/episodes?season=1", _member.AccessToken);

        //Assert
        // The page shows none inside a season; the work carries it as every work does.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var episode = Assert.Single((await ReadListAsync(response, cancellationToken)).Items);
        Assert.Equal(("Daybreak", Url(JellyfinSamples.YellowstoneS01E01Id, "c46fbb41d55088253b976fab0894d6a8")), (episode.Title, episode.PosterUrl));
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static string Url(string itemId, string tag) => $"{PostersUrl}{itemId}-{tag}.jpg";

    // The samples as Jellyfin listed them with their images' tags: four movies, five series and six episodes.
    private Task<IntegrationConnectionEntity> SyncSamplesWithImagesAsync()
    {
        _jellyfin.Series.AddRange(JellyfinSamples.SeriesWithImages());
        _jellyfin.Episodes.AddRange(JellyfinSamples.EpisodesWithImages());
        return SyncAsync(JellyfinSamples.MoviesWithImages());
    }

    private Task<IntegrationConnectionEntity> SyncMoviesAsync(params JsonObject[] movies) => SyncAsync([.. movies]);

    // The library a sync stores for "Living room": these movies (the samples unless given) and the series and episodes the
    // test put on the server.
    private async Task<IntegrationConnectionEntity> SyncAsync(List<JsonObject>? movies = null)
    {
        if (movies is not null)
        {
            _jellyfin.Movies.Clear();
            _jellyfin.Movies.AddRange(movies);
        }

        var jellyfin = await _integrations.Create("jellyfin", "Living room", apiKey: ApiKey);
        await new LibrarySnapshotSeeder(_factory.Scope).Sync(jellyfin, _owner.User);
        return jellyfin;
    }

    // Two items of one film (TMDb 679) under titles that sort alike in any culture.
    private static JsonObject Alpha() =>
        JellyfinSamples.MovieWith(AlphaItemId, "Alpha", null, 1986, "/media/movies/Alpha (1986).mkv", "tt0090605", "679");

    private static JsonObject Bravo() =>
        JellyfinSamples.MovieWith(BravoItemId, "Bravo", null, 1986, "/media/movies/Bravo (1986).mkv", "tt0090605", "679");

    // Two more items of the film: one with an original title only, one with no title at all (its ids and file alone).
    private static JsonObject Charlie() =>
        JellyfinSamples.MovieWith(CharlieItemId, "", "Charlie", 1986, "/media/movies/Charlie (1986).mkv", "tt0090605", "679");

    private static JsonObject Delta() =>
        JellyfinSamples.MovieWith(DeltaItemId, "", null, 1986, "/media/movies/Delta (1986).mkv", "tt0090605", "679");

    private static JsonObject Pictured(JsonObject item, string tag)
    {
        item["ImageTags"] = new JsonObject { ["Primary"] = tag };
        return item;
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

    private static async Task<LibraryWorkListResponse> ReadListAsync(HttpResponseMessage response, CancellationToken cancellationToken) =>
        (await response.Content.ReadFromJsonAsync<LibraryWorkListResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
}
