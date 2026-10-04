using System.Text.Json.Nodes;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Data;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Seeders;

namespace MediaAcquisitionManager.Api.Tests.Library;

// MAM-32, ADR 0017, ADR 0019 — what a sync stores of a library's series and episodes, row by row: the real
// samples as series works, episode works with their versions, files and positions, every fact with its
// provenance; a show known by several library series items is one series; a movie and a series sharing a
// TMDb number stay apart; a virtual episode is stored as nothing; and a sync over an earlier snapshot keeps
// every work it can. Syncs run over real HTTP and the real worker; only Jellyfin's network and the clock are
// faked.
public sealed class LibrarySnapshotTvHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string ApiKey = "0123456789abcdef0123456789abcdef";

    private const string GrandTourS01Id = "11111111111111111111111111111111";
    private const string GrandTourS02Id = "22222222222222222222222222222222";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private IIntegrationConnectionSeeder _integrations = null!;
    private AuthorizedUser _owner = null!;
    private FakeJellyfinServer _jellyfin = null!;

    public LibrarySnapshotTvHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _integrations = new IntegrationConnectionSeeder(_factory.Scope);
        _owner = await new UserSeeder(_factory.Scope).CreateAuthorizedOwner("owner");
        _jellyfin = new FakeJellyfinServer { ApiKey = ApiKey };
        _jellyfin.Movies.Clear();
        _jellyfin.Arm(_factory.JellyfinHttp);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Sync_of_the_tv_samples_stores_series_and_episodes_and_leaves_the_virtual_special_out()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var jellyfin = await CreateJellyfinAsync();
        _jellyfin.Series.AddRange(JellyfinSamples.Series());
        _jellyfin.Episodes.AddRange(JellyfinSamples.Episodes());

        //Act
        var run = await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);

        //Assert
        // 5 series and 6 episodes read; the virtual special stored as nothing, the two halves of Road Rally
        // one work: 4 episode works with 1 + 65 + 1 + 2 versions, one file each.
        Assert.Equal(LibrarySyncStatus.Succeeded, run.Status);
        Assert.Equal((11, 11, 4, 5, 69, 69), (run.ItemsRead, run.ItemsTotal, run.WorksCount, run.SeriesCount, run.VersionsCount, run.FilesCount));
        var snapshot = await ReadSnapshotAsync(cancellationToken);
        var workOf = WorksByItem(snapshot);
        Assert.Equal(
            [
                (JellyfinSamples.TomAndJerryKidsId, MediaWorkKind.Episode),
                (JellyfinSamples.RoadRallyAId, MediaWorkKind.Episode),
                (JellyfinSamples.BigBangId, MediaWorkKind.Episode),
                (JellyfinSamples.YellowstoneS01E01Id, MediaWorkKind.Episode),
                (JellyfinSamples.RoadRallyBId, MediaWorkKind.Episode),
                (JellyfinSamples.MickeyMouseClubhouseId, MediaWorkKind.Series),
                (JellyfinSamples.PenguinsId, MediaWorkKind.Series),
                (JellyfinSamples.YellowstoneId, MediaWorkKind.Series),
                (JellyfinSamples.HowTheUniverseWorksId, MediaWorkKind.Series),
                (JellyfinSamples.ClarksonsFarmId, MediaWorkKind.Series),
            ],
            workOf
                .Select(item => (item.Key, snapshot.Works.Single(work => work.Id == item.Value).Kind))
                .OrderBy(item => item.Item2)
                .ThenBy(item => item.Key, StringComparer.Ordinal));
        Assert.Equal(workOf[JellyfinSamples.RoadRallyAId], workOf[JellyfinSamples.RoadRallyBId]);
        Assert.Equal(9, snapshot.Works.Count);
        Assert.DoesNotContain(Provenanced(snapshot), row => row.SourceItemId == JellyfinSamples.FollowTheMouseId);
        Assert.DoesNotContain(snapshot.Positions, row => row.SourceItemId == JellyfinSamples.FollowTheMouseId);
    }

    [Fact]
    public async Task Sync_of_the_tv_samples_places_each_episode_in_its_series_work_with_its_numbers_and_provenance()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var jellyfin = await CreateJellyfinAsync();
        _jellyfin.Series.AddRange(JellyfinSamples.Series());
        _jellyfin.Episodes.AddRange(JellyfinSamples.Episodes());
        var now = _factory.Time.GetUtcNow().UtcDateTime;

        //Act
        await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);

        //Assert
        var snapshot = await ReadSnapshotAsync(cancellationToken);
        var workOf = WorksByItem(snapshot);
        // One position per library item: the halves of Road Rally each place their one work; Tom & Jerry
        // Kids has numbers and no series.
        Assert.Equal(
            [
                (JellyfinSamples.TomAndJerryKidsId, workOf[JellyfinSamples.TomAndJerryKidsId], (Guid?)null, 1, 4),
                (JellyfinSamples.RoadRallyAId, workOf[JellyfinSamples.RoadRallyAId], workOf[JellyfinSamples.MickeyMouseClubhouseId], 3, 9),
                (JellyfinSamples.BigBangId, workOf[JellyfinSamples.BigBangId], workOf[JellyfinSamples.HowTheUniverseWorksId], 1, 1),
                (JellyfinSamples.YellowstoneS01E01Id, workOf[JellyfinSamples.YellowstoneS01E01Id], workOf[JellyfinSamples.YellowstoneId], 1, 1),
                (JellyfinSamples.RoadRallyBId, workOf[JellyfinSamples.RoadRallyAId], workOf[JellyfinSamples.MickeyMouseClubhouseId], 3, 9),
            ],
            snapshot.Positions
                .Select(position => (position.SourceItemId!, position.MediaWorkId, position.SeriesWorkId, position.SeasonNumber!.Value, position.EpisodeNumber!.Value))
                .OrderBy(position => position.Item1, StringComparer.Ordinal));
        Assert.All(snapshot.Positions, position =>
        {
            Assert.Equal(ProvenanceSourceKind.MediaLibrary, position.SourceKind);
            Assert.Equal((jellyfin.Id, "Living room"), (position.SourceConnectionId!.Value, position.SourceName));
            Assert.Equal(now, position.ObservedUtc);
            Assert.Equal(now, position.CreatedUtc);
        });
    }

    [Fact]
    public async Task Sync_of_the_tv_samples_stores_each_series_ids_its_tmdb_id_as_a_shows()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var jellyfin = await CreateJellyfinAsync();
        _jellyfin.Series.AddRange(JellyfinSamples.Series());
        _jellyfin.Episodes.AddRange(JellyfinSamples.Episodes());

        //Act
        await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);

        //Assert
        var snapshot = await ReadSnapshotAsync(cancellationToken);
        var workOf = WorksByItem(snapshot);
        Assert.Equal(
            ["Imdb tt4236770", "TmdbTv 73586", "Tvdb 341164"],
            IdentifiersOf(snapshot, workOf[JellyfinSamples.YellowstoneId]));
        // Episodes keep their own ids, never their series'.
        Assert.Equal(["Imdb tt7529124", "Tvdb 6507316"], IdentifiersOf(snapshot, workOf[JellyfinSamples.YellowstoneS01E01Id]));
        Assert.Equal(["Imdb tt1715023", "Tvdb 2804541"], IdentifiersOf(snapshot, workOf[JellyfinSamples.RoadRallyAId]));
        Assert.Empty(IdentifiersOf(snapshot, workOf[JellyfinSamples.PenguinsId]));
        Assert.Empty(IdentifiersOf(snapshot, workOf[JellyfinSamples.TomAndJerryKidsId]));
    }

    [Fact]
    public async Task Sync_of_the_tv_samples_puts_each_episode_file_in_its_library()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var jellyfin = await CreateJellyfinAsync();
        _jellyfin.Series.AddRange(JellyfinSamples.Series());
        _jellyfin.Episodes.AddRange(JellyfinSamples.Episodes());

        //Act
        await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);

        //Assert
        // Every library at once: the TV library, and series in the mixed cartoon and documentary ones.
        var snapshot = await ReadSnapshotAsync(cancellationToken);
        var libraryOf = snapshot.Locations.ToDictionary(location => location.Id, location => location.SourceItemId);
        Assert.Equal(
            [
                (JellyfinSamples.TomAndJerryKidsId, JellyfinSamples.CartoonsLocationId, 65),
                (JellyfinSamples.RoadRallyAId, JellyfinSamples.CartoonsLocationId, 1),
                (JellyfinSamples.BigBangId, JellyfinSamples.DocumentaryLocationId, 1),
                (JellyfinSamples.YellowstoneS01E01Id, JellyfinSamples.TvShowsLocationId, 1),
                (JellyfinSamples.RoadRallyBId, JellyfinSamples.CartoonsLocationId, 1),
            ],
            snapshot.Files
                .GroupBy(file => (file.SourceItemId!, libraryOf[file.LibraryLocationId!.Value]))
                .Select(item => (item.Key.Item1, item.Key.Item2!, item.Count()))
                .OrderBy(item => item.Item1, StringComparer.Ordinal));
    }

    [Fact]
    public async Task Sync_makes_one_series_of_a_show_the_library_knows_as_two_series_items()
    {
        //Arrange
        // The Grand Tour is four series items, one per season folder, all with the show's ids (ADR 0017).
        var cancellationToken = TestContext.Current.CancellationToken;
        var jellyfin = await CreateJellyfinAsync();
        _jellyfin.Series.Add(JellyfinSamples.SeriesWith(GrandTourS01Id, "The Grand Tour", 2016, "/media/tv-shows/The Grand Tour S01", "tt5712554", "68004", "314614"));
        _jellyfin.Series.Add(JellyfinSamples.SeriesWith(GrandTourS02Id, "The Grand Tour", 2016, "/media/tv-shows/The Grand Tour S02", "tt5712554", "68004", "314614"));
        _jellyfin.Episodes.Add(Episode("a", GrandTourS01Id, 1, 1));
        _jellyfin.Episodes.Add(Episode("b", GrandTourS02Id, 2, 1));

        //Act
        var run = await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);

        //Assert
        Assert.Equal((2, 1), (run.WorksCount, run.SeriesCount));
        var snapshot = await ReadSnapshotAsync(cancellationToken);
        var workOf = WorksByItem(snapshot);
        Assert.Equal(workOf[GrandTourS01Id], workOf[GrandTourS02Id]);
        Assert.Equal([workOf[GrandTourS01Id], workOf[GrandTourS01Id]], snapshot.Positions.Select(position => position.SeriesWorkId!.Value));
        Assert.NotEqual(workOf[ItemId("a")], workOf[ItemId("b")]);
    }

    [Fact]
    public async Task Sync_keeps_a_movie_and_a_series_sharing_a_tmdb_number_apart()
    {
        //Arrange
        // ADR 0017 — 3934 is Mickey Mouse Clubhouse in TMDb's shows and another film in its movies.
        var cancellationToken = TestContext.Current.CancellationToken;
        var jellyfin = await CreateJellyfinAsync();
        _jellyfin.Movies.Add(JellyfinSamples.MovieWith(ItemId("m"), "A film", null, 1999, "/media/movies/A film (1999).mkv", tmdbId: "3934"));
        _jellyfin.Series.Add(JellyfinSamples.Series(JellyfinSamples.MickeyMouseClubhouseId));

        //Act
        var run = await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);

        //Assert
        Assert.Equal((1, 1), (run.WorksCount, run.SeriesCount));
        var snapshot = await ReadSnapshotAsync(cancellationToken);
        var workOf = WorksByItem(snapshot);
        Assert.NotEqual(workOf[ItemId("m")], workOf[JellyfinSamples.MickeyMouseClubhouseId]);
        Assert.Equal(["Tmdb 3934"], IdentifiersOf(snapshot, workOf[ItemId("m")]));
        Assert.Contains("TmdbTv 3934", IdentifiersOf(snapshot, workOf[JellyfinSamples.MickeyMouseClubhouseId]));
    }

    [Fact]
    public async Task Sync_keeps_a_movie_and_an_episode_sharing_an_imdb_id_apart()
    {
        //Arrange
        // The library says what an item is (ADR 0017): an episode the movie list holds stays a movie, and
        // an episode item with the same id is an episode, never the same work.
        var cancellationToken = TestContext.Current.CancellationToken;
        var jellyfin = await CreateJellyfinAsync();
        _jellyfin.Movies.Add(JellyfinSamples.MovieWith(ItemId("m"), "The Continental", null, 2023, "/media/movies/The Continental S01E01.mkv", imdbId: "tt29669983"));
        _jellyfin.Episodes.Add(JellyfinSamples.EpisodeWith(ItemId("e"), "Night 1", null, 1, 1, "/media/tv-shows/The Continental S01E01.mkv", imdbId: "tt29669983"));

        //Act
        var run = await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);

        //Assert
        Assert.Equal(2, run.WorksCount);
        var snapshot = await ReadSnapshotAsync(cancellationToken);
        var workOf = WorksByItem(snapshot);
        Assert.NotEqual(workOf[ItemId("m")], workOf[ItemId("e")]);
        Assert.Equal(MediaWorkKind.Movie, snapshot.Works.Single(work => work.Id == workOf[ItemId("m")]).Kind);
        Assert.Equal(MediaWorkKind.Episode, snapshot.Works.Single(work => work.Id == workOf[ItemId("e")]).Kind);
    }

    [Fact]
    public async Task Sync_makes_one_episode_of_items_at_one_position_of_a_series_with_ids_and_two_of_one_episodes_ids_at_two_positions()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var jellyfin = await CreateJellyfinAsync();
        _jellyfin.Series.Add(JellyfinSamples.Series(JellyfinSamples.MickeyMouseClubhouseId));
        // Two files of S03E10 with no ids of their own: known by the series' ids with their numbers.
        _jellyfin.Episodes.Add(Episode("a", JellyfinSamples.MickeyMouseClubhouseId, 3, 10));
        _jellyfin.Episodes.Add(Episode("b", JellyfinSamples.MickeyMouseClubhouseId, 3, 10));
        // "The Golden Boo Boo" (S03E31) carries the ids of "Donald Hatches an Egg" (S03E33).
        _jellyfin.Episodes.Add(Episode("c", JellyfinSamples.MickeyMouseClubhouseId, 3, 31, imdbId: "tt1720001"));
        _jellyfin.Episodes.Add(Episode("d", JellyfinSamples.MickeyMouseClubhouseId, 3, 33, imdbId: "tt1720001"));

        //Act
        await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);

        //Assert
        var workOf = WorksByItem(await ReadSnapshotAsync(cancellationToken));
        Assert.Equal(workOf[ItemId("a")], workOf[ItemId("b")]);
        Assert.NotEqual(workOf[ItemId("c")], workOf[ItemId("d")]);
    }

    [Fact]
    public async Task Sync_keeps_apart_episodes_at_one_position_of_a_series_without_ids()
    {
        //Arrange
        // The library's series item is never the show's identity (ADR 0017): with no ids, two files at one
        // position of the Penguins are only possible matches, each a work of its own.
        var cancellationToken = TestContext.Current.CancellationToken;
        var jellyfin = await CreateJellyfinAsync();
        _jellyfin.Series.Add(JellyfinSamples.Series(JellyfinSamples.PenguinsId));
        _jellyfin.Episodes.Add(Episode("a", JellyfinSamples.PenguinsId, 1, 1));
        _jellyfin.Episodes.Add(Episode("b", JellyfinSamples.PenguinsId, 1, 1));

        //Act
        await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);

        //Assert
        var workOf = WorksByItem(await ReadSnapshotAsync(cancellationToken));
        Assert.NotEqual(workOf[ItemId("a")], workOf[ItemId("b")]);
    }

    [Fact]
    public async Task Sync_of_an_unchanged_tv_library_keeps_every_row_and_moves_only_the_last_seen_time()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var jellyfin = await CreateJellyfinAsync();
        _jellyfin.Series.AddRange(JellyfinSamples.Series());
        _jellyfin.Episodes.AddRange(JellyfinSamples.Episodes());
        var firstSeen = _factory.Time.GetUtcNow().UtcDateTime;
        await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);
        var before = await ReadSnapshotAsync(cancellationToken);
        _factory.Time.Advance(TimeSpan.FromMinutes(1));
        var lastSeen = _factory.Time.GetUtcNow().UtcDateTime;

        //Act
        await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);

        //Assert
        // Every series, episode and position upserted by its provenance keys, never re-created: every
        // column of every row as before but for the last-seen time.
        var after = await ReadSnapshotAsync(cancellationToken);
        Assert.Equal(
            before.Rows(),
            after.Rows().Select(row => row.Replace(
                StoredLibrarySnapshot.TimeText(lastSeen), StoredLibrarySnapshot.TimeText(firstSeen), StringComparison.Ordinal)));
        Assert.All(after.Positions, position => Assert.Equal((firstSeen, lastSeen), (position.CreatedUtc, position.ObservedUtc)));
    }

    [Fact]
    public async Task Sync_keeps_an_episodes_work_when_its_file_comes_back_under_a_new_item_at_its_position()
    {
        //Arrange
        // ADR 0017 — an episode is known by its series' ids with its numbers: an upgraded file, a new library
        // item with no ids of its own, is still the same episode.
        var cancellationToken = TestContext.Current.CancellationToken;
        var jellyfin = await CreateJellyfinAsync();
        _jellyfin.Series.Add(JellyfinSamples.Series(JellyfinSamples.YellowstoneId));
        _jellyfin.Episodes.Add(Episode("a", JellyfinSamples.YellowstoneId, 1, 2));
        await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);
        var before = WorksByItem(await ReadSnapshotAsync(cancellationToken))[ItemId("a")];
        _jellyfin.Episodes.Clear();
        _jellyfin.Episodes.Add(Episode("b", JellyfinSamples.YellowstoneId, 1, 2));

        //Act
        await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);

        //Assert
        var snapshot = await ReadSnapshotAsync(cancellationToken);
        Assert.Equal(before, WorksByItem(snapshot)[ItemId("b")]);
        Assert.Single(snapshot.Works, work => work.Kind == MediaWorkKind.Episode);
    }

    [Fact]
    public async Task Sync_after_a_series_item_is_gone_keeps_its_episodes_without_a_series()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var jellyfin = await CreateJellyfinAsync();
        _jellyfin.Series.Add(JellyfinSamples.Series(JellyfinSamples.YellowstoneId));
        _jellyfin.Episodes.Add(JellyfinSamples.Episode(JellyfinSamples.YellowstoneS01E01Id));
        await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);
        _jellyfin.Series.Clear();

        //Act
        var run = await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);

        //Assert
        Assert.Equal((1, 0), (run.WorksCount, run.SeriesCount));
        var snapshot = await ReadSnapshotAsync(cancellationToken);
        Assert.Equal([MediaWorkKind.Episode], snapshot.Works.Select(work => work.Kind));
        var position = Assert.Single(snapshot.Positions);
        Assert.Equal(((Guid?)null, (int?)1, (int?)1), (position.SeriesWorkId, position.SeasonNumber, position.EpisodeNumber));
    }

    [Fact]
    public async Task Sync_puts_an_item_the_library_now_calls_an_episode_on_a_new_work()
    {
        //Arrange
        // A work of another kind is never a candidate: the movie it was goes, the episode is new.
        var cancellationToken = TestContext.Current.CancellationToken;
        var jellyfin = await CreateJellyfinAsync();
        _jellyfin.Movies.Add(JellyfinSamples.MovieWith(ItemId("x"), "Pilot", null, 2020, "/media/tv-shows/Pilot.mkv", imdbId: "tt0000077"));
        await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);
        var movieWork = WorksByItem(await ReadSnapshotAsync(cancellationToken))[ItemId("x")];
        _jellyfin.Movies.Clear();
        _jellyfin.Episodes.Add(JellyfinSamples.EpisodeWith(ItemId("x"), "Pilot", null, 1, 1, "/media/tv-shows/Pilot.mkv", imdbId: "tt0000077"));

        //Act
        await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);

        //Assert
        var snapshot = await ReadSnapshotAsync(cancellationToken);
        var work = Assert.Single(snapshot.Works);
        Assert.Equal(MediaWorkKind.Episode, work.Kind);
        Assert.NotEqual(movieWork, work.Id);
    }

    [Theory]
    [InlineData("Series", "Jellyfin's list of series shifted between two pages, so its library changed while it was being read. Nothing was changed here; sync again once Jellyfin's library scan has finished.")]
    [InlineData("Episode", "Jellyfin's list of episodes shifted between two pages, so its library changed while it was being read. Nothing was changed here; sync again once Jellyfin's library scan has finished.")]
    public async Task Sync_whose_tv_list_changes_while_it_is_read_fails_and_stores_nothing(string type, string expectedError)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var jellyfin = await CreateJellyfinAsync();
        _jellyfin.Series.Add(JellyfinSamples.Series(JellyfinSamples.YellowstoneId));
        _jellyfin.Episodes.Add(JellyfinSamples.Episode(JellyfinSamples.YellowstoneS01E01Id));
        await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);
        var before = await ReadSnapshotAsync(cancellationToken);
        // The list's first page holds one item of the two its total claims: one went missing from under the
        // read (ADR 0008).
        _jellyfin.TotalRecordCountOf = (listType, _) => listType == type ? 2 : null;

        //Act
        var run = await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);

        //Assert
        Assert.Equal((LibrarySyncStatus.Failed, expectedError), (run.Status, run.Error));
        Assert.Equal(before.Rows(), (await ReadSnapshotAsync(cancellationToken)).Rows());
    }

    private Task<IntegrationConnectionEntity> CreateJellyfinAsync() =>
        _integrations.Create("jellyfin", "Living room", FakeJellyfinServer.DefaultBaseUrl, apiKey: ApiKey);

    // A 32-hex-digit item id made of one letter, as Jellyfin's ids are.
    private static string ItemId(string letter) => new(letter[0], 32);

    private static JsonObject Episode(string letter, string seriesId, int season, int episode, string? imdbId = null) =>
        JellyfinSamples.EpisodeWith(ItemId(letter), $"Episode {letter}", seriesId, season, episode, $"/media/tv-shows/{letter}.mkv", imdbId);

    private async Task<StoredLibrarySnapshot> ReadSnapshotAsync(CancellationToken cancellationToken)
    {
        StoredLibrarySnapshot? snapshot = null;
        await _factory.WithDbAsync(async db => snapshot = await StoredLibrarySnapshot.ReadAsync(db, cancellationToken));
        return snapshot!;
    }

    // Every provenanced row of the snapshot, whatever its table.
    private static IEnumerable<ProvenancedEntity> Provenanced(StoredLibrarySnapshot snapshot) =>
        [.. snapshot.Titles, .. snapshot.Identifiers, .. snapshot.Versions, .. snapshot.Files, .. snapshot.Positions];

    // The work of each item, read off its titles.
    private static Dictionary<string, Guid> WorksByItem(StoredLibrarySnapshot snapshot) =>
        snapshot.Titles
            .GroupBy(title => title.SourceItemId!, StringComparer.Ordinal)
            .ToDictionary(item => item.Key, item => item.Select(title => title.MediaWorkId).Distinct().Single(), StringComparer.Ordinal);

    // "<scheme> <value>" for each id the work holds, once, in scheme then value order.
    private static List<string> IdentifiersOf(StoredLibrarySnapshot snapshot, Guid workId) =>
    [
        .. snapshot.Identifiers
            .Where(identifier => identifier.MediaWorkId == workId)
            .Select(identifier => $"{identifier.Scheme} {identifier.Value}")
            .Distinct()
            .Order(StringComparer.Ordinal),
    ];
}
