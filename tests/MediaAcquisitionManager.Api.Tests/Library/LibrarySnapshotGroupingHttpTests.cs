using System.Text.Json.Nodes;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Data;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Seeders;

namespace MediaAcquisitionManager.Api.Tests.Library;

// MAM-8, ADR 0008 — which work a sync puts each movie on: versions Jellyfin merged stay versions of one
// work, stored once whichever Jellyfin shape lists them; works are grouped per library by identifiers,
// keep their ids across syncs (by the items they held, else by an identifier they held), split to the
// group holding most of their items and merge into the older one; and two libraries never share a work.
// Every rule is decided by the clock or by counts here, never by the order of Guids.
public sealed class LibrarySnapshotGroupingHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string ApiKey = "0123456789abcdef0123456789abcdef";
    private const string SecondJellyfinAddress = "http://127.0.0.2:9";

    private const string AliensTheatricalPath =
        "/media/movies/Alien. Collection/[02.1] Aliens (1986) [Theatrical Cut, Fundamental Collection] BDRip 1080p H.265 [2xUKR_ENG] [Hurtom].mkv";

    private const string AliensSpecialEditionPath =
        "/media/movies/Alien. Collection/[02.2] Aliens (1986) [Special Edition] BDRip 1080p H.265 [2xUKR_ENG] [Hurtom, UTOPIA].mkv";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private IIntegrationConnectionSeeder _integrations = null!;
    private AuthorizedUser _owner = null!;
    private FakeJellyfinServer _jellyfin = null!;
    private FakeJellyfinServer _secondJellyfin = null!;

    public LibrarySnapshotGroupingHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _integrations = new IntegrationConnectionSeeder(_factory.Scope);
        _owner = await new UserSeeder(_factory.Scope).CreateAuthorizedOwner("owner");
        _jellyfin = new FakeJellyfinServer { ApiKey = ApiKey };
        _secondJellyfin = new FakeJellyfinServer(SecondJellyfinAddress) { ApiKey = ApiKey };
        FakeJellyfinServer.Arm(_factory.JellyfinHttp, _jellyfin, _secondJellyfin);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Sync_of_one_item_with_two_media_sources_stores_one_work_with_two_labelled_versions()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var jellyfin = await CreateJellyfinAsync("Living room");
        // Jellyfin 12.1's merged versions: one item, both "Чужі" files as its media sources.
        SetMovies(_jellyfin, JellyfinSamples.AliensWithBothSources());

        //Act
        var run = await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);

        //Assert
        Assert.Equal((LibrarySyncStatus.Succeeded, 1, 2, 2), (run.Status, run.WorksCount, run.VersionsCount, run.FilesCount));
        var snapshot = await ReadSnapshotAsync(cancellationToken);
        var work = Assert.Single(snapshot.Works);
        // Each version keeps its own source id, and its label is Jellyfin's version picker's: the source's name.
        Assert.Equal(
            [
                $"{JellyfinSamples.AliensTheatricalId} {JellyfinSamples.AliensTheatricalId} [02.1] Aliens (1986) [Theatrical Cut, Fundamental Collection] BDRip 1080p H.265 [2xUKR_ENG] [Hurtom] {AliensTheatricalPath}",
                $"{JellyfinSamples.AliensSpecialEditionId} {JellyfinSamples.AliensTheatricalId} [02.2] Aliens (1986) [Special Edition] BDRip 1080p H.265 [2xUKR_ENG] [Hurtom, UTOPIA] {AliensSpecialEditionPath}",
            ],
            snapshot.Versions
                .OrderBy(version => version.SourceVersionId, StringComparer.Ordinal)
                .Select(version =>
                    $"{version.SourceVersionId} {version.SourceItemId} {version.EditionLabel} {snapshot.Files.Single(file => file.MediaVersionId == version.Id).ServicePath}"));
        Assert.All(snapshot.Versions, version => Assert.Equal(work.Id, version.MediaWorkId));
    }

    [Fact]
    public async Task Sync_of_two_items_that_each_list_both_media_sources_stores_two_versions_not_four()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var jellyfin = await CreateJellyfinAsync("Living room");
        // Jellyfin 10.9 to 10.11's merged versions: two items, each listing both files.
        SetMovies(_jellyfin, [.. JellyfinSamples.AliensEachListingBothSources()]);

        //Act
        var run = await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);

        //Assert
        Assert.Equal((LibrarySyncStatus.Succeeded, 2, 1, 2, 2), (run.Status, run.ItemsRead, run.WorksCount, run.VersionsCount, run.FilesCount));
        var snapshot = await ReadSnapshotAsync(cancellationToken);
        Assert.Single(snapshot.Works);
        // Each source goes to the item named after it, once; each file is stored once.
        Assert.Equal(
            [
                $"{JellyfinSamples.AliensTheatricalId} {JellyfinSamples.AliensTheatricalId} {AliensTheatricalPath}",
                $"{JellyfinSamples.AliensSpecialEditionId} {JellyfinSamples.AliensSpecialEditionId} {AliensSpecialEditionPath}",
            ],
            snapshot.Versions
                .OrderBy(version => version.SourceVersionId, StringComparer.Ordinal)
                .Select(version =>
                    $"{version.SourceVersionId} {version.SourceItemId} {snapshot.Files.Single(file => file.MediaVersionId == version.Id).ServicePath}"));
    }

    [Fact]
    public async Task Resync_of_a_movie_under_a_new_item_id_with_the_same_identifiers_keeps_its_work()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var jellyfin = await CreateJellyfinAsync("Living room");
        var firstSeen = _factory.Time.GetUtcNow().UtcDateTime;
        await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);
        var before = await ReadSnapshotAsync(cancellationToken);
        var district13Work = WorkOf(before, JellyfinSamples.District13Id);
        // An upgraded file: Jellyfin lists it as a new item, with a new id and path, and the same metadata.
        const string UpgradedId = "d13d13d13d13d13d13d13d13d13d13d1";
        var upgraded = MovieOf(_jellyfin, JellyfinSamples.District13Id);
        upgraded["Id"] = UpgradedId;
        upgraded["MediaSources"]![0]!["Id"] = UpgradedId;
        upgraded["MediaSources"]![0]!["Path"] = "/media/movies/Banlieue 13 (2004) 2160p.mkv";
        _factory.Time.Advance(TimeSpan.FromMinutes(1));

        //Act
        var run = await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);

        //Assert
        Assert.Equal((LibrarySyncStatus.Succeeded, 3), (run.Status, run.WorksCount));
        var after = await ReadSnapshotAsync(cancellationToken);
        // Identifier carry-over: the same work, first seen when the old file was.
        Assert.Equal(before.Works.Select(work => (work.Id, work.CreatedUtc)), after.Works.Select(work => (work.Id, work.CreatedUtc)));
        Assert.Equal(district13Work, WorkOf(after, UpgradedId));
        Assert.Equal(firstSeen, after.Works.Single(work => work.Id == district13Work).CreatedUtc);
        // The old item's facts are gone; the new item's are new rows on that work.
        Assert.DoesNotContain(Provenanced(after), row => row.SourceItemId == JellyfinSamples.District13Id);
        var version = Assert.Single(after.Versions, row => row.SourceItemId == UpgradedId);
        Assert.Equal((district13Work, UpgradedId), (version.MediaWorkId, version.SourceVersionId));
    }

    [Fact]
    public async Task Resync_of_a_work_whose_items_split_leaves_it_to_the_group_holding_most_of_them()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var jellyfin = await CreateJellyfinAsync("Living room");
        // Three files of one movie, one work. The item that will leave sorts and is listed first, so only
        // "most of its items" can keep the work where the other two are.
        const string Leaving = "0000000000000000000000000000000c";
        const string Staying = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        const string AlsoStaying = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
        SetMovies(_jellyfin, District13Copy(Leaving), District13Copy(Staying), District13Copy(AlsoStaying));
        await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);
        var work = (await ReadSnapshotAsync(cancellationToken)).Works.Single();
        // Jellyfin re-identifies one of them as another movie.
        MovieOf(_jellyfin, Leaving)["ProviderIds"] = new JsonObject { ["Imdb"] = "tt9999999", ["Tmdb"] = "99999" };
        _factory.Time.Advance(TimeSpan.FromMinutes(1));
        var splitAt = _factory.Time.GetUtcNow().UtcDateTime;

        //Act
        var run = await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);

        //Assert
        Assert.Equal((LibrarySyncStatus.Succeeded, 2), (run.Status, run.WorksCount));
        var after = await ReadSnapshotAsync(cancellationToken);
        Assert.Equal(work.Id, WorkOf(after, Staying));
        Assert.Equal(work.Id, WorkOf(after, AlsoStaying));
        var newWork = WorkOf(after, Leaving);
        Assert.NotEqual(work.Id, newWork);
        Assert.Equal(
            [(work.Id, work.CreatedUtc), (newWork, splitAt)],
            after.Works.OrderBy(row => row.CreatedUtc).Select(row => (row.Id, row.CreatedUtc)));
    }

    [Fact]
    public async Task Resync_of_two_works_whose_items_now_share_an_identifier_merges_them_into_the_older()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var jellyfin = await CreateJellyfinAsync("Living room");
        const string TmdbOnly = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        const string ImdbOnly = "0000000000000000000000000000000b";
        var tmdbOnly = District13Copy(TmdbOnly);
        tmdbOnly["ProviderIds"]!.AsObject().Remove("Imdb");
        var imdbOnly = District13Copy(ImdbOnly);
        imdbOnly["ProviderIds"]!.AsObject().Remove("Tmdb");
        // One sync with the first movie, a later one with the second: two works, the first the older.
        SetMovies(_jellyfin, tmdbOnly);
        await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);
        _factory.Time.Advance(TimeSpan.FromMinutes(1));
        SetMovies(_jellyfin, tmdbOnly, imdbOnly);
        await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);
        var before = await ReadSnapshotAsync(cancellationToken);
        var older = WorkOf(before, TmdbOnly);
        var newer = WorkOf(before, ImdbOnly);
        // Jellyfin finds the second one's TMDb id: both now share one, and nothing conflicts.
        MovieOf(_jellyfin, ImdbOnly)["ProviderIds"]!["Tmdb"] = "10045";
        _factory.Time.Advance(TimeSpan.FromMinutes(1));

        //Act
        var run = await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);

        //Assert
        Assert.Equal((LibrarySyncStatus.Succeeded, 1, 2), (run.Status, run.WorksCount, run.VersionsCount));
        var after = await ReadSnapshotAsync(cancellationToken);
        // Equal claims on both works (one item each, one shared identifier each): the older one stays,
        // and the emptied newer one goes.
        Assert.NotEqual(older, newer);
        Assert.Equal(older, Assert.Single(after.Works).Id);
        Assert.All(Facts(after), fact => Assert.Equal(older, fact.MediaWorkId));
    }

    [Fact]
    public async Task Sync_of_two_libraries_with_the_same_movies_keeps_every_work_to_one_library()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var livingRoom = await CreateJellyfinAsync("Living room");
        var bedroom = await CreateJellyfinAsync("Bedroom", SecondJellyfinAddress);

        //Act
        var first = await _factory.SyncLibraryAsync(livingRoom.Id, _owner.AccessToken, cancellationToken);
        var second = await _factory.SyncLibraryAsync(bedroom.Id, _owner.AccessToken, cancellationToken);

        //Assert
        Assert.Equal((LibrarySyncStatus.Succeeded, 3), (first.Status, first.WorksCount));
        Assert.Equal((LibrarySyncStatus.Succeeded, 3), (second.Status, second.WorksCount));
        var snapshot = await ReadSnapshotAsync(cancellationToken);
        // Same ids in both, and still six works: a sync never joins a work another source reported.
        Assert.Equal(6, snapshot.Works.Count);
        var sourcesByWork = Facts(snapshot).GroupBy(fact => fact.MediaWorkId).ToDictionary(
            work => work.Key, work => work.Select(fact => fact.SourceConnectionId).Distinct().ToList());
        Assert.All(snapshot.Works, work => Assert.Single(sourcesByWork[work.Id]));
        Assert.Equal(3, sourcesByWork.Count(work => work.Value[0] == livingRoom.Id));
        Assert.Equal(3, sourcesByWork.Count(work => work.Value[0] == bedroom.Id));
        // Each file is in its own library's "Фільми", never the other's.
        Assert.Equal(10, snapshot.Locations.Count);
        Assert.All(snapshot.Files, file => Assert.Equal(
            file.SourceConnectionId,
            snapshot.Locations.Single(location => location.Id == file.LibraryLocationId).SourceConnectionId));
    }

    [Fact]
    public async Task Resync_of_one_library_leaves_every_row_of_the_other_untouched()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var livingRoom = await CreateJellyfinAsync("Living room");
        var bedroom = await CreateJellyfinAsync("Bedroom", SecondJellyfinAddress);
        await _factory.SyncLibraryAsync(livingRoom.Id, _owner.AccessToken, cancellationToken);
        await _factory.SyncLibraryAsync(bedroom.Id, _owner.AccessToken, cancellationToken);
        var before = await ReadSnapshotAsync(cancellationToken);
        _jellyfin.Movies.RemoveAll(movie => (string?)movie["Id"] == JellyfinSamples.District13Id);
        _factory.Time.Advance(TimeSpan.FromMinutes(1));

        //Act
        var run = await _factory.SyncLibraryAsync(livingRoom.Id, _owner.AccessToken, cancellationToken);

        //Assert
        Assert.Equal((LibrarySyncStatus.Succeeded, 2), (run.Status, run.WorksCount));
        var after = await ReadSnapshotAsync(cancellationToken);
        // Every column of the other library's rows — last-seen times included — and its works as they were.
        Assert.Equal(RowsOf(before, bedroom.Id), RowsOf(after, bedroom.Id));
        Assert.NotEmpty(RowsOf(after, bedroom.Id));
        Assert.Equal(WorksOf(before, bedroom.Id), WorksOf(after, bedroom.Id));
        Assert.DoesNotContain(Provenanced(after), row => row.SourceConnectionId == livingRoom.Id && row.SourceItemId == JellyfinSamples.District13Id);
        Assert.Contains(Provenanced(after), row => row.SourceConnectionId == bedroom.Id && row.SourceItemId == JellyfinSamples.District13Id);
    }

    private Task<IntegrationConnectionEntity> CreateJellyfinAsync(string name, string baseUrl = FakeJellyfinServer.DefaultBaseUrl) =>
        _integrations.Create("jellyfin", name, baseUrl, apiKey: ApiKey);

    private async Task<StoredLibrarySnapshot> ReadSnapshotAsync(CancellationToken cancellationToken)
    {
        StoredLibrarySnapshot? snapshot = null;
        await _factory.WithDbAsync(async db => snapshot = await StoredLibrarySnapshot.ReadAsync(db, cancellationToken));
        return snapshot!;
    }

    private static void SetMovies(FakeJellyfinServer server, params JsonObject[] movies)
    {
        server.Movies.Clear();
        server.Movies.AddRange(movies);
    }

    private static JsonObject MovieOf(FakeJellyfinServer server, string id) =>
        server.Movies.Single(movie => (string?)movie["Id"] == id);

    // Another file of "13-й район" as an item of its own: its id, its source's id and its path are
    // <paramref name="id"/>'s; its IMDb and TMDb ids are the sample's.
    private static JsonObject District13Copy(string id)
    {
        var movie = JellyfinSamples.Movie(JellyfinSamples.District13Id);
        movie["Id"] = id;
        movie["MediaSources"]![0]!["Id"] = id;
        movie["MediaSources"]![0]!["Path"] = $"/media/movies/Banlieue 13 (2004) {id}.mkv";
        return movie;
    }

    private static IEnumerable<ProvenancedEntity> Provenanced(StoredLibrarySnapshot snapshot) =>
        [.. snapshot.Titles, .. snapshot.Identifiers, .. snapshot.Versions, .. snapshot.Files, .. snapshot.Locations];

    // The rows that sit on a work, with the work they sit on.
    private static IEnumerable<(Guid MediaWorkId, Guid? SourceConnectionId)> Facts(StoredLibrarySnapshot snapshot) =>
    [
        .. snapshot.Titles.Select(row => (row.MediaWorkId, row.SourceConnectionId)),
        .. snapshot.Identifiers.Select(row => (row.MediaWorkId, row.SourceConnectionId)),
        .. snapshot.Versions.Select(row => (row.MediaWorkId, row.SourceConnectionId)),
    ];

    // The one work every fact of an item sits on.
    private static Guid WorkOf(StoredLibrarySnapshot snapshot, string itemId) =>
        new[]
            {
                snapshot.Titles.Where(row => row.SourceItemId == itemId).Select(row => row.MediaWorkId),
                snapshot.Identifiers.Where(row => row.SourceItemId == itemId).Select(row => row.MediaWorkId),
                snapshot.Versions.Where(row => row.SourceItemId == itemId).Select(row => row.MediaWorkId),
            }
            .SelectMany(works => works)
            .Distinct()
            .Single();

    // Every row line of one library's facts (each names its connection).
    private static IReadOnlyList<string> RowsOf(StoredLibrarySnapshot snapshot, Guid integrationId) =>
        [.. snapshot.Rows().Where(row => row.Contains(integrationId.ToString(), StringComparison.Ordinal))];

    private static IReadOnlyList<(Guid, DateTime)> WorksOf(StoredLibrarySnapshot snapshot, Guid integrationId)
    {
        var works = Facts(snapshot).Where(fact => fact.SourceConnectionId == integrationId).Select(fact => fact.MediaWorkId).ToHashSet();
        return [.. snapshot.Works.Where(work => works.Contains(work.Id)).Select(work => (work.Id, work.CreatedUtc))];
    }
}
