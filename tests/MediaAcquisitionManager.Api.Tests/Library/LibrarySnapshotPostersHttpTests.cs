using System.Net;
using System.Text.Json.Nodes;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Infrastructure.Persistence;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Data;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using Microsoft.Extensions.DependencyInjection;

namespace MediaAcquisitionManager.Api.Tests.Library;

// MAM-35, ADR 0027 — what a sync stores of the library's pictures: each item's own poster tag, a fact with its provenance
// (ADR 0006) on the work the item lands on — never its series' or season's tag, nothing for a virtual episode; replaced in
// place when the library gives the image a new tag, removed when the image or the item goes, moved with its item to another
// work, kept as it was by a sync that fails, and purged with its integration. A poster never makes a work. Syncs run over
// real HTTP and the real worker on the samples Jellyfin answered with its images' tags; only Jellyfin's network and the
// clock are faked.
public sealed class LibrarySnapshotPostersHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string ApiKey = "0123456789abcdef0123456789abcdef";
    private const string SecondJellyfinAddress = "http://127.0.0.2:9";

    /// <summary>Daybreak's own poster tag (Yellowstone S01E01), as <c>episodes-page-with-images.json</c> records it.</summary>
    private const string DaybreakTag = "c46fbb41d55088253b976fab0894d6a8";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private IIntegrationConnectionSeeder _integrations = null!;
    private AuthorizedUser _owner = null!;
    private FakeJellyfinServer _jellyfin = null!;

    public LibrarySnapshotPostersHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _integrations = new IntegrationConnectionSeeder(_factory.Scope);
        _owner = await new UserSeeder(_factory.Scope).CreateAuthorizedOwner("owner");
        _jellyfin = SamplesWithImages(FakeJellyfinServer.DefaultBaseUrl);
        _jellyfin.Arm(_factory.JellyfinHttp);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Sync_of_the_samples_with_images_stores_each_items_own_poster_tag_on_its_work_with_its_provenance()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var jellyfin = await CreateJellyfinAsync("Living room");
        var now = _factory.Time.GetUtcNow().UtcDateTime;

        //Act
        var run = await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);

        //Assert
        // Every movie, series and episode with its own tag — Daybreak its own, not Yellowstone's — and nothing for the
        // virtual special, which is stored as nothing; the counts of the run are what they were without the posters.
        Assert.Equal(LibrarySyncStatus.Succeeded, run.Status);
        Assert.Equal((15, 7, 5), (run.ItemsRead, run.WorksCount, run.SeriesCount));
        var snapshot = await ReadSnapshotAsync(cancellationToken);
        Assert.Equal(
            new[]
            {
                $"{JellyfinSamples.District13Id} 876b255c3ee8ce3ab0bb55c4c03ab0ef",
                $"{JellyfinSamples.ContinentalId} 41f7c98888a81ea514189c27c6c3970c",
                $"{JellyfinSamples.AliensTheatricalId} a608b4104259bc0bb01c6ad262c53aed",
                $"{JellyfinSamples.AliensSpecialEditionId} 5e28b80243c705099a2c9b398fd53fd2",
                $"{JellyfinSamples.ClarksonsFarmId} 84efddafd1408cbacb7700107c602dff",
                $"{JellyfinSamples.HowTheUniverseWorksId} 658e4aa24e972c31d2f4af570181385c",
                $"{JellyfinSamples.MickeyMouseClubhouseId} 01de7f6467277e81d5abec6fc8256006",
                $"{JellyfinSamples.PenguinsId} 52464c1fd74945e5f6990cb1901cf7c6",
                $"{JellyfinSamples.YellowstoneId} {JellyfinSamples.YellowstonePosterTag}",
                $"{JellyfinSamples.BigBangId} ea78070489e6279af4ba737e6119f610",
                $"{JellyfinSamples.YellowstoneS01E01Id} {DaybreakTag}",
                $"{JellyfinSamples.TomAndJerryKidsId} 4a58e8e95f7f6ad8307f541d94e23800",
                $"{JellyfinSamples.RoadRallyAId} b4c46ed28a9b22f17cdfcbc96d8937ba",
                $"{JellyfinSamples.RoadRallyBId} 03c967be49c613b617f83562b53e09f9",
            }.Order(StringComparer.Ordinal),
            snapshot.Posters.Select(poster => $"{poster.SourceItemId} {poster.Tag}").Order(StringComparer.Ordinal));
        Assert.DoesNotContain(snapshot.Posters, poster => poster.SourceItemId == JellyfinSamples.FollowTheMouseId);
        // Each on the work its item's titles sit on: the two halves of Road Rally both on their one work.
        var workOf = WorksByItem(snapshot);
        Assert.All(snapshot.Posters, poster =>
        {
            Assert.Equal(workOf[poster.SourceItemId!], poster.MediaWorkId);
            Assert.Equal(ProvenanceSourceKind.MediaLibrary, poster.SourceKind);
            Assert.Equal((jellyfin.Id, "Living room"), (poster.SourceConnectionId, poster.SourceName));
            Assert.Equal((now, now), (poster.CreatedUtc, poster.ObservedUtc));
        });
        Assert.Equal(workOf[JellyfinSamples.RoadRallyAId], workOf[JellyfinSamples.RoadRallyBId]);
    }

    [Fact]
    public async Task Resync_of_an_unchanged_library_keeps_every_poster_row_and_refreshes_its_last_seen_time()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var jellyfin = await CreateJellyfinAsync("Living room");
        var firstSeen = _factory.Time.GetUtcNow().UtcDateTime;
        await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);
        var before = await ReadSnapshotAsync(cancellationToken);
        _factory.Time.Advance(TimeSpan.FromMinutes(1));
        var lastSeen = _factory.Time.GetUtcNow().UtcDateTime;

        //Act
        await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);

        //Assert
        // Upserted by item, never re-created: the same rows on the same works, first seen a minute ago.
        var after = await ReadSnapshotAsync(cancellationToken);
        Assert.Equal(14, after.Posters.Count);
        Assert.Equal(
            before.Posters.Select(poster => (poster.Id, poster.MediaWorkId, poster.Tag, poster.SourceItemId)),
            after.Posters.Select(poster => (poster.Id, poster.MediaWorkId, poster.Tag, poster.SourceItemId)));
        Assert.All(after.Posters, poster => Assert.Equal((firstSeen, lastSeen), (poster.CreatedUtc, poster.ObservedUtc)));
    }

    [Fact]
    public async Task Resync_after_the_library_gives_an_items_image_a_new_tag_replaces_its_poster_in_place()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        const string NewTag = "9f674fd57061c114f4e1654fbecee5ff";
        var jellyfin = await CreateJellyfinAsync("Living room");
        var firstSeen = _factory.Time.GetUtcNow().UtcDateTime;
        await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);
        var before = await ReadSnapshotAsync(cancellationToken);
        // A new picture (an Identify's, say): Jellyfin gives the image a new tag.
        Movie(JellyfinSamples.ContinentalId)["ImageTags"] = new JsonObject { ["Primary"] = NewTag };
        _factory.Time.Advance(TimeSpan.FromMinutes(1));
        var lastSeen = _factory.Time.GetUtcNow().UtcDateTime;

        //Act
        await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);

        //Assert
        var after = await ReadSnapshotAsync(cancellationToken);
        var old = before.Posters.Single(poster => poster.SourceItemId == JellyfinSamples.ContinentalId);
        var replaced = after.Posters.Single(poster => poster.SourceItemId == JellyfinSamples.ContinentalId);
        Assert.Equal((old.Id, old.MediaWorkId, NewTag), (replaced.Id, replaced.MediaWorkId, replaced.Tag));
        Assert.Equal((firstSeen, lastSeen), (replaced.CreatedUtc, replaced.ObservedUtc));
        // Every other poster as it was.
        Assert.Equal(
            before.Posters.Where(poster => poster.Id != old.Id).Select(poster => (poster.Id, poster.Tag)),
            after.Posters.Where(poster => poster.Id != old.Id).Select(poster => (poster.Id, poster.Tag)));
    }

    [Fact]
    public async Task Resync_after_an_items_image_is_gone_removes_its_poster_and_keeps_its_work()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var jellyfin = await CreateJellyfinAsync("Living room");
        await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);
        var before = await ReadSnapshotAsync(cancellationToken);
        Movie(JellyfinSamples.ContinentalId)["ImageTags"] = new JsonObject();
        _factory.Time.Advance(TimeSpan.FromMinutes(1));

        //Act
        var run = await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);

        //Assert
        Assert.Equal((LibrarySyncStatus.Succeeded, 7), (run.Status, run.WorksCount));
        var after = await ReadSnapshotAsync(cancellationToken);
        var gone = before.Posters.Single(poster => poster.SourceItemId == JellyfinSamples.ContinentalId);
        Assert.Equal(before.Posters.Where(poster => poster.Id != gone.Id).Select(poster => poster.Id), after.Posters.Select(poster => poster.Id));
        // The item keeps its work: its other facts still hold it.
        Assert.Contains(after.Works, work => work.Id == gone.MediaWorkId);
        Assert.Equal(gone.MediaWorkId, WorksByItem(after)[JellyfinSamples.ContinentalId]);
    }

    [Fact]
    public async Task Resync_without_an_item_removes_its_poster_with_its_other_facts()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var jellyfin = await CreateJellyfinAsync("Living room");
        await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);
        var before = await ReadSnapshotAsync(cancellationToken);
        _jellyfin.Movies.RemoveAll(movie => (string?)movie["Id"] == JellyfinSamples.District13Id);
        _factory.Time.Advance(TimeSpan.FromMinutes(1));

        //Act
        await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);

        //Assert
        var after = await ReadSnapshotAsync(cancellationToken);
        var gone = before.Posters.Single(poster => poster.SourceItemId == JellyfinSamples.District13Id);
        Assert.Equal(before.Posters.Where(poster => poster.Id != gone.Id).Select(poster => poster.Id), after.Posters.Select(poster => poster.Id));
        Assert.DoesNotContain(after.Works, work => work.Id == gone.MediaWorkId);
    }

    [Fact]
    public async Task Resync_after_an_item_moves_to_a_work_of_its_own_moves_its_poster_with_it()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var jellyfin = await CreateJellyfinAsync("Living room");
        await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);
        var before = await ReadSnapshotAsync(cancellationToken);
        var aliens = WorksByItem(before)[JellyfinSamples.AliensTheatricalId];
        // The special edition now names another film (Alien, 1979): it leaves the two "Чужі"'s work.
        Movie(JellyfinSamples.AliensSpecialEditionId)["ProviderIds"] = new JsonObject { ["Imdb"] = "tt0078748", ["Tmdb"] = "348" };
        _factory.Time.Advance(TimeSpan.FromMinutes(1));

        //Act
        await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);

        //Assert
        var after = await ReadSnapshotAsync(cancellationToken);
        var workOf = WorksByItem(after);
        Assert.Equal(aliens, workOf[JellyfinSamples.AliensTheatricalId]);
        Assert.NotEqual(aliens, workOf[JellyfinSamples.AliensSpecialEditionId]);
        // The same row, on the item's new work.
        var old = before.Posters.Single(poster => poster.SourceItemId == JellyfinSamples.AliensSpecialEditionId);
        var moved = after.Posters.Single(poster => poster.SourceItemId == JellyfinSamples.AliensSpecialEditionId);
        Assert.Equal((old.Id, old.Tag), (moved.Id, moved.Tag));
        Assert.Equal(workOf[JellyfinSamples.AliensSpecialEditionId], moved.MediaWorkId);
        Assert.Equal(aliens, after.Posters.Single(poster => poster.SourceItemId == JellyfinSamples.AliensTheatricalId).MediaWorkId);
    }

    [Fact]
    public async Task Resync_that_fails_keeps_every_poster_as_it_was()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var jellyfin = await CreateJellyfinAsync("Living room");
        await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);
        var before = await ReadSnapshotAsync(cancellationToken);
        Movie(JellyfinSamples.ContinentalId)["ImageTags"] = new JsonObject { ["Primary"] = "9f674fd57061c114f4e1654fbecee5ff" };
        Movie(JellyfinSamples.District13Id)["ImageTags"] = new JsonObject();
        // The episodes' list breaks after the movies were read.
        _jellyfin.Intercept = request => request.RequestUri!.Query.Contains("includeItemTypes=Episode", StringComparison.Ordinal)
            ? new HttpResponseMessage(HttpStatusCode.InternalServerError)
            : null;
        _factory.Time.Advance(TimeSpan.FromMinutes(1));

        //Act
        var run = await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);

        //Assert
        Assert.Equal(LibrarySyncStatus.Failed, run.Status);
        var after = await ReadSnapshotAsync(cancellationToken);
        Assert.Equal(before.Rows(), after.Rows());
    }

    [Fact]
    public async Task Sync_of_an_item_with_nothing_but_a_poster_stores_no_work_and_no_poster_for_it()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        const string ItemId = "ffffffffffffffffffffffffffffffff";
        var jellyfin = await CreateJellyfinAsync("Living room");
        // A movie Jellyfin knows only by its id and its picture: no title, no provider ids, no media source.
        _jellyfin.Movies.Add(new JsonObject
        {
            ["Id"] = ItemId,
            ["Type"] = "Movie",
            ["ImageTags"] = new JsonObject { ["Primary"] = "0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f" },
        });

        //Act
        var run = await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);

        //Assert
        // A picture alone says nothing of a work: no blank card with a poster.
        Assert.Equal((LibrarySyncStatus.Succeeded, 16, 7), (run.Status, run.ItemsRead, run.WorksCount));
        var snapshot = await ReadSnapshotAsync(cancellationToken);
        Assert.DoesNotContain(snapshot.Posters, poster => poster.SourceItemId == ItemId);
        Assert.Equal(14, snapshot.Posters.Count);
    }

    [Fact]
    public async Task Delete_of_the_library_integration_purges_its_posters_and_keeps_another_librarys()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var bedroomServer = SamplesWithImages(SecondJellyfinAddress);
        FakeJellyfinServer.Arm(_factory.JellyfinHttp, _jellyfin, bedroomServer);
        var livingRoom = await CreateJellyfinAsync("Living room");
        var bedroom = await CreateJellyfinAsync("Bedroom", SecondJellyfinAddress);
        await _factory.SyncLibraryAsync(livingRoom.Id, _owner.AccessToken, cancellationToken);
        await _factory.SyncLibraryAsync(bedroom.Id, _owner.AccessToken, cancellationToken);
        var before = await ReadSnapshotAsync(cancellationToken);
        // A title another source holds on the Living room's Continental: that work outlives the removal, and its poster must
        // go all the same, not stay behind with no library to show it from.
        var continental = before.Posters.Single(poster => poster.SourceConnectionId == livingRoom.Id && poster.SourceItemId == JellyfinSamples.ContinentalId);
        await new MediaWorkDataFactory(_factory.Scope.GetRequiredService<ApplicationDbContext>(), _factory.Time)
            .AddTitle(continental.MediaWorkId, "The Continental", MediaTitleKind.Display, 2023, bedroom.Id, bedroom.Name, "c0c0c0c0c0c0c0c0c0c0c0c0c0c0c0c0");

        //Act
        var response = await _factory.DeleteHttpRequest($"/api/settings/integrations/{livingRoom.Id}", _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var after = await ReadSnapshotAsync(cancellationToken);
        Assert.Equal(28, before.Posters.Count);
        Assert.Contains(after.Works, work => work.Id == continental.MediaWorkId);
        // None of the removed library's is left, not even with its connection set null; the other library's are untouched.
        Assert.Equal(
            before.Posters.Where(poster => poster.SourceConnectionId == bedroom.Id).Select(poster => (poster.Id, poster.MediaWorkId, poster.Tag)),
            after.Posters.Select(poster => (poster.Id, poster.MediaWorkId, poster.Tag)));
        Assert.Equal(14, after.Posters.Count);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    // A Jellyfin at baseUrl holding the sample movies, series and episodes as it listed them with their images' tags.
    private static FakeJellyfinServer SamplesWithImages(string baseUrl)
    {
        var server = new FakeJellyfinServer(baseUrl) { ApiKey = ApiKey };
        server.Movies.Clear();
        server.Movies.AddRange(JellyfinSamples.MoviesWithImages());
        server.Series.AddRange(JellyfinSamples.SeriesWithImages());
        server.Episodes.AddRange(JellyfinSamples.EpisodesWithImages());
        return server;
    }

    private Task<IntegrationConnectionEntity> CreateJellyfinAsync(string name, string baseUrl = FakeJellyfinServer.DefaultBaseUrl) =>
        _integrations.Create("jellyfin", name, baseUrl, apiKey: ApiKey);

    private JsonObject Movie(string id) => _jellyfin.Movies.Single(movie => (string?)movie["Id"] == id);

    private async Task<StoredLibrarySnapshot> ReadSnapshotAsync(CancellationToken cancellationToken)
    {
        StoredLibrarySnapshot? snapshot = null;
        await _factory.WithDbAsync(async db => snapshot = await StoredLibrarySnapshot.ReadAsync(db, cancellationToken));
        return snapshot!;
    }

    // The work of each item, read off its titles.
    private static Dictionary<string, Guid> WorksByItem(StoredLibrarySnapshot snapshot) =>
        snapshot.Titles
            .GroupBy(title => title.SourceItemId!, StringComparer.Ordinal)
            .ToDictionary(item => item.Key, item => item.Select(title => title.MediaWorkId).Distinct().Single(), StringComparer.Ordinal);
}
