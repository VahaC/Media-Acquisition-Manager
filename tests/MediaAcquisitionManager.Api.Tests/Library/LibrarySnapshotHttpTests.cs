using System.Text.Json.Nodes;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Data;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Seeders;

namespace MediaAcquisitionManager.Api.Tests.Library;

// MAM-8, ADR 0008 — what a sync stores, row by row: the real samples as works, titles, identifiers,
// versions, files and libraries with their provenance; and what a sync over an earlier snapshot keeps
// (every id and first-seen time) and removes (every stored row it no longer reached). Syncs run over
// real HTTP and the real worker; only Jellyfin's network and the clock are faked.
public sealed class LibrarySnapshotHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string ApiKey = "0123456789abcdef0123456789abcdef";

    private const string ContinentalPath =
        "/media/movies/John Wick. Collection/[01] The Continental (2023) - (Season 1)/[01] The Continental (2023) - S01E01 - BDRip 1080p x265 [3xUKR_ENG] [Hurtom].mkv";

    private const string District13Path =
        "/media/movies/District 13. Dilogy (2004-2009) BDRip 1080p H.265 [2xUKR_FRE] [Hurtom]/[01] Banlieue 13 (2004) BDRip 1080p H.265 [2xUKR_FRE] [Hurtom].mkv";

    private const string AliensTheatricalPath =
        "/media/movies/Alien. Collection/[02.1] Aliens (1986) [Theatrical Cut, Fundamental Collection] BDRip 1080p H.265 [2xUKR_ENG] [Hurtom].mkv";

    private const string AliensSpecialEditionPath =
        "/media/movies/Alien. Collection/[02.2] Aliens (1986) [Special Edition] BDRip 1080p H.265 [2xUKR_ENG] [Hurtom, UTOPIA].mkv";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private IIntegrationConnectionSeeder _integrations = null!;
    private AuthorizedUser _owner = null!;
    private FakeJellyfinServer _jellyfin = null!;

    public LibrarySnapshotHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _integrations = new IntegrationConnectionSeeder(_factory.Scope);
        _owner = await new UserSeeder(_factory.Scope).CreateAuthorizedOwner("owner");
        _jellyfin = new FakeJellyfinServer { ApiKey = ApiKey };
        _jellyfin.Arm(_factory.JellyfinHttp);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Sync_of_the_samples_stores_three_works_with_every_fact_and_its_provenance()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var jellyfin = await CreateJellyfinAsync();
        var now = _factory.Time.GetUtcNow().UtcDateTime;

        //Act
        var run = await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);

        //Assert
        Assert.Equal(LibrarySyncStatus.Succeeded, run.Status);
        Assert.Equal((4, 4, 3, 4, 4), (run.ItemsRead, run.ItemsTotal, run.WorksCount, run.VersionsCount, run.FilesCount));
        var snapshot = await ReadSnapshotAsync(cancellationToken);

        // Titles as Jellyfin gave them, with the upper-case copy the search compares (SQLite folds only
        // ASCII) and the year; an item without an original title has none.
        Assert.Equal(
            [
                $"{JellyfinSamples.AliensTheatricalId} Display Чужі ЧУЖІ 1986",
                $"{JellyfinSamples.AliensTheatricalId} Original Aliens ALIENS 1986",
                $"{JellyfinSamples.ContinentalId} Display The Continental THE CONTINENTAL 2023",
                $"{JellyfinSamples.AliensSpecialEditionId} Display Чужі ЧУЖІ 1986",
                $"{JellyfinSamples.AliensSpecialEditionId} Original Aliens ALIENS 1986",
                $"{JellyfinSamples.District13Id} Display 13-й район 13-Й РАЙОН 2004",
                $"{JellyfinSamples.District13Id} Original Banlieue 13 BANLIEUE 13 2004",
            ],
            snapshot.Titles
                .OrderBy(title => title.SourceItemId, StringComparer.Ordinal)
                .ThenBy(title => title.Kind)
                .Select(title => $"{title.SourceItemId} {title.Kind} {title.Text} {title.NormalizedText} {title.Year}"));
        // IMDb and TMDb only: 13-й район's and Чужі's TmdbCollection ids name a collection, not a work.
        Assert.Equal(
            [
                $"{JellyfinSamples.AliensTheatricalId} Imdb tt0090605",
                $"{JellyfinSamples.AliensTheatricalId} Tmdb 679",
                $"{JellyfinSamples.ContinentalId} Imdb tt29669983",
                $"{JellyfinSamples.AliensSpecialEditionId} Imdb tt0090605",
                $"{JellyfinSamples.AliensSpecialEditionId} Tmdb 679",
                $"{JellyfinSamples.District13Id} Imdb tt0414852",
                $"{JellyfinSamples.District13Id} Tmdb 10045",
            ],
            snapshot.Identifiers
                .OrderBy(identifier => identifier.SourceItemId, StringComparer.Ordinal)
                .ThenBy(identifier => identifier.Scheme)
                .Select(identifier => $"{identifier.SourceItemId} {identifier.Scheme} {identifier.Value}"));
        // One version per media source, keyed by its id; one media source per item, so no edition label.
        Assert.Equal(
            [
                $"{JellyfinSamples.AliensTheatricalId} {JellyfinSamples.AliensTheatricalId} ∅",
                $"{JellyfinSamples.ContinentalId} {JellyfinSamples.ContinentalId} ∅",
                $"{JellyfinSamples.AliensSpecialEditionId} {JellyfinSamples.AliensSpecialEditionId} ∅",
                $"{JellyfinSamples.District13Id} {JellyfinSamples.District13Id} ∅",
            ],
            snapshot.Versions
                .OrderBy(version => version.SourceItemId, StringComparer.Ordinal)
                .Select(version => $"{version.SourceItemId} {version.SourceVersionId} {version.EditionLabel ?? "∅"}"));

        // The five libraries of the server, by their own ids; the files are all in "Фільми" (/media/movies).
        Assert.Equal(
            [
                "2e7d31f7c627f72ad0912eff1c00353d Мульфільми",
                $"{JellyfinSamples.MoviesLocationId} Фільми",
                "dea595f0747f8862151db0192e565150 Документалки",
                "f28915b8cd74d585b0c181975ec68c3a Колекції",
                "f97af1337db58125aaa9a3f3695ab163 Серіали",
            ],
            snapshot.Locations
                .OrderBy(location => location.SourceItemId, StringComparer.Ordinal)
                .Select(location => $"{location.SourceItemId} {location.Name}"));
        var movies = snapshot.Locations.Single(location => location.SourceItemId == JellyfinSamples.MoviesLocationId);
        // Collection folders stay where they are: every path verbatim, with its size, under its version.
        Assert.Equal(
            [
                (JellyfinSamples.AliensTheatricalId, AliensTheatricalPath, 14_845_708_473L),
                (JellyfinSamples.ContinentalId, ContinentalPath, 3_891_719_630L),
                (JellyfinSamples.AliensSpecialEditionId, AliensSpecialEditionPath, 19_620_073_340L),
                (JellyfinSamples.District13Id, District13Path, 4_670_197_619L),
            ],
            snapshot.Files
                .Select(file => (VersionOf(snapshot, file).SourceItemId!, file.ServicePath, file.SizeBytes!.Value))
                .OrderBy(file => file.Item1, StringComparer.Ordinal));
        Assert.All(snapshot.Files, file =>
        {
            Assert.Equal(file.ServicePath.ToUpperInvariant(), file.NormalizedServicePath);
            Assert.Equal(movies.Id, file.LibraryLocationId);
            // The app's own reading of the file (MAM-9); a library never says.
            Assert.Null(file.DeviceId);
            Assert.Null(file.Inode);
        });

        // Both "Чужі" items share their IMDb and TMDb ids: one work with two versions. Every item's facts
        // sit on one work, and nothing else shares a work.
        var workOf = WorksByItem(snapshot);
        Assert.Equal(3, snapshot.Works.Count);
        Assert.All(snapshot.Works, work => Assert.Equal((MediaWorkKind.Movie, now), (work.Kind, work.CreatedUtc)));
        Assert.Equal(workOf[JellyfinSamples.AliensTheatricalId], workOf[JellyfinSamples.AliensSpecialEditionId]);
        Assert.Equal(
            3,
            new[] { JellyfinSamples.ContinentalId, JellyfinSamples.District13Id, JellyfinSamples.AliensTheatricalId }
                .Select(item => workOf[item])
                .Distinct()
                .Count());

        // ADR 0006 — every row says who reported it, under which item, and when it was first and last seen.
        Assert.All(Provenanced(snapshot), row =>
        {
            Assert.Equal(ProvenanceSourceKind.MediaLibrary, row.SourceKind);
            Assert.Equal((jellyfin.Id, "Living room"), (row.SourceConnectionId, row.SourceName));
            Assert.Equal((now, now), (row.CreatedUtc, row.ObservedUtc));
        });
    }

    [Fact]
    public async Task Resync_keeps_every_id_and_first_seen_time_and_refreshes_last_seen_and_the_source_name()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var jellyfin = await CreateJellyfinAsync();
        var firstSeen = _factory.Time.GetUtcNow().UtcDateTime;
        await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);
        var before = await ReadSnapshotAsync(cancellationToken);
        _factory.Time.Advance(TimeSpan.FromMinutes(1));
        var lastSeen = _factory.Time.GetUtcNow().UtcDateTime;
        await _factory.PutHttpRequest(
            $"/api/settings/integrations/{jellyfin.Id}", _owner.AccessToken, new UpdateIntegrationRequest("Bedroom", jellyfin.BaseUrl));

        //Act
        var run = await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);

        //Assert
        Assert.Equal(LibrarySyncStatus.Succeeded, run.Status);
        Assert.Equal((3, 4, 4), (run.WorksCount, run.VersionsCount, run.FilesCount));
        var after = await ReadSnapshotAsync(cancellationToken);
        // Upserted by provenance keys, never re-created: the same rows, still first seen a minute ago.
        Assert.Equal(before.Works.Select(work => (work.Id, work.CreatedUtc)), after.Works.Select(work => (work.Id, work.CreatedUtc)));
        Assert.Equal(Keys(before), Keys(after));
        Assert.All(Provenanced(after), row =>
        {
            Assert.Equal((firstSeen, lastSeen), (row.CreatedUtc, row.ObservedUtc));
            // The integration's current name on every fact it reported again.
            Assert.Equal("Bedroom", row.SourceName);
        });
        // Nothing else moved: every column of every row is as before but for the name and the last-seen
        // time (every stored time was the first-seen one, and every new one is the last-seen one).
        Assert.Equal(
            before.Rows().Select(row => row.Replace("Living room", "Bedroom", StringComparison.Ordinal)),
            after.Rows().Select(row => row.Replace(
                StoredLibrarySnapshot.TimeText(lastSeen), StoredLibrarySnapshot.TimeText(firstSeen), StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Resync_without_an_item_removes_its_facts_and_its_emptied_work_and_keeps_the_rest()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var jellyfin = await CreateJellyfinAsync();
        await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);
        var before = await ReadSnapshotAsync(cancellationToken);
        var district13Work = WorksByItem(before)[JellyfinSamples.District13Id];
        _jellyfin.Movies.RemoveAll(movie => (string?)movie["Id"] == JellyfinSamples.District13Id);
        _factory.Time.Advance(TimeSpan.FromMinutes(1));

        //Act
        var run = await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);

        //Assert
        Assert.Equal((LibrarySyncStatus.Succeeded, 2, 3, 3), (run.Status, run.WorksCount, run.VersionsCount, run.FilesCount));
        var after = await ReadSnapshotAsync(cancellationToken);
        Assert.DoesNotContain(Provenanced(after), row => row.SourceItemId == JellyfinSamples.District13Id);
        Assert.DoesNotContain(after.Works, work => work.Id == district13Work);
        // Everything else is the same rows as before, found again.
        Assert.Equal(
            Keys(before).Where(key => !key.EndsWith(JellyfinSamples.District13Id, StringComparison.Ordinal)),
            Keys(after));
        Assert.Equal(before.Works.Select(work => work.Id).Where(work => work != district13Work), after.Works.Select(work => work.Id));
    }

    [Fact]
    public async Task Resync_of_an_item_without_one_of_its_identifiers_removes_that_identifier_only()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var jellyfin = await CreateJellyfinAsync();
        await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);
        var before = await ReadSnapshotAsync(cancellationToken);
        Movie(JellyfinSamples.District13Id)["ProviderIds"]!.AsObject().Remove("Tmdb");
        _factory.Time.Advance(TimeSpan.FromMinutes(1));

        //Act
        await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);

        //Assert
        var after = await ReadSnapshotAsync(cancellationToken);
        var dropped = before.Identifiers.Single(identifier =>
            identifier.SourceItemId == JellyfinSamples.District13Id && identifier.Scheme == MediaIdentifierScheme.Tmdb);
        Assert.Equal(Keys(before).Where(key => !key.StartsWith($"identifier {dropped.Id}", StringComparison.Ordinal)), Keys(after));
        // The item keeps its work: its other facts still hold it.
        Assert.Equal(WorksByItem(before)[JellyfinSamples.District13Id], WorksByItem(after)[JellyfinSamples.District13Id]);
    }

    [Fact]
    public async Task Resync_of_an_item_without_its_original_title_removes_that_title_only()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var jellyfin = await CreateJellyfinAsync();
        await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);
        var before = await ReadSnapshotAsync(cancellationToken);
        Movie(JellyfinSamples.District13Id).Remove("OriginalTitle");
        _factory.Time.Advance(TimeSpan.FromMinutes(1));

        //Act
        await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);

        //Assert
        var after = await ReadSnapshotAsync(cancellationToken);
        var cleared = before.Titles.Single(title =>
            title.SourceItemId == JellyfinSamples.District13Id && title.Kind == MediaTitleKind.Original);
        Assert.Equal(Keys(before).Where(key => !key.StartsWith($"title {cleared.Id}", StringComparison.Ordinal)), Keys(after));
    }

    [Fact]
    public async Task Resync_of_an_item_with_one_of_its_two_media_sources_removes_that_version_and_its_file()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var jellyfin = await CreateJellyfinAsync();
        // Jellyfin 12.1's merged versions: the theatrical item lists the special edition's source too.
        _jellyfin.Movies.Clear();
        _jellyfin.Movies.Add(JellyfinSamples.AliensWithBothSources());
        await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);
        var before = await ReadSnapshotAsync(cancellationToken);
        _jellyfin.Movies[0]["MediaSources"]!.AsArray().RemoveAt(1);
        _factory.Time.Advance(TimeSpan.FromMinutes(1));

        //Act
        var run = await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);

        //Assert
        Assert.Equal((LibrarySyncStatus.Succeeded, 1, 1), (run.Status, run.VersionsCount, run.FilesCount));
        var after = await ReadSnapshotAsync(cancellationToken);
        var removedVersion = before.Versions.Single(version => version.SourceVersionId == JellyfinSamples.AliensSpecialEditionId);
        var removedFile = before.Files.Single(file => file.MediaVersionId == removedVersion.Id);
        Assert.Equal(
            Keys(before).Where(key => !key.StartsWith($"version {removedVersion.Id}", StringComparison.Ordinal)
                && !key.StartsWith($"file {removedFile.Id}", StringComparison.Ordinal)),
            Keys(after));
        // A single source is just a file name, not an edition to choose: the kept version loses its label.
        Assert.Null(Assert.Single(after.Versions).EditionLabel);
    }

    [Fact]
    public async Task Resync_of_a_media_source_at_another_path_replaces_its_file_and_keeps_its_version()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var jellyfin = await CreateJellyfinAsync();
        await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);
        var before = await ReadSnapshotAsync(cancellationToken);
        const string MovedPath = "/media/movies/The Continental/[01] The Continental (2023) - S01E01.mkv";
        Movie(JellyfinSamples.ContinentalId)["MediaSources"]![0]!["Path"] = MovedPath;
        _factory.Time.Advance(TimeSpan.FromMinutes(1));

        //Act
        await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);

        //Assert
        var after = await ReadSnapshotAsync(cancellationToken);
        var version = before.Versions.Single(row => row.SourceVersionId == JellyfinSamples.ContinentalId);
        var oldFile = before.Files.Single(file => file.MediaVersionId == version.Id);
        // A file row is one name of a file: another path is another row, under the same version.
        var newFile = Assert.Single(after.Files, file => file.MediaVersionId == version.Id);
        Assert.NotEqual(oldFile.Id, newFile.Id);
        Assert.Equal((MovedPath, _factory.Time.GetUtcNow().UtcDateTime), (newFile.ServicePath, newFile.CreatedUtc));
        Assert.Equal(
            Keys(before).Where(key => !key.StartsWith($"file {oldFile.Id}", StringComparison.Ordinal)),
            Keys(after).Where(key => !key.StartsWith($"file {newFile.Id}", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Sync_of_an_item_that_reports_nothing_stores_no_work_for_it()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var jellyfin = await CreateJellyfinAsync();
        // A movie Jellyfin knows only by its id: no title, no provider ids, no media source.
        _jellyfin.Movies.Add(new JsonObject { ["Id"] = "ffffffffffffffffffffffffffffffff", ["Type"] = "Movie" });

        //Act
        var run = await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);

        //Assert
        // Read, but nothing to show: an empty work would only be a blank card.
        Assert.Equal((LibrarySyncStatus.Succeeded, 5, 5, 3), (run.Status, run.ItemsRead, run.ItemsTotal, run.WorksCount));
        var snapshot = await ReadSnapshotAsync(cancellationToken);
        Assert.Equal(3, snapshot.Works.Count);
        Assert.DoesNotContain(Provenanced(snapshot), row => row.SourceItemId == "ffffffffffffffffffffffffffffffff");
    }

    [Fact]
    public async Task Sync_of_a_stacked_movie_stores_each_part_as_a_file_of_its_one_version()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var jellyfin = await CreateJellyfinAsync();
        const string SecondPart = "/media/movies/District 13. Dilogy (2004-2009) BDRip 1080p H.265 [2xUKR_FRE] [Hurtom]/[01] Banlieue 13 (2004) part2.mkv";
        // Jellyfin lists only a stacked movie's first part among its media sources, and the rest on request.
        Movie(JellyfinSamples.District13Id)["PartCount"] = 2;
        _jellyfin.AdditionalParts[JellyfinSamples.District13Id] = [JellyfinSamples.Part("dddddddddddddddddddddddddddddddd", SecondPart, 1_000)];

        //Act
        var run = await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);

        //Assert
        Assert.Equal((LibrarySyncStatus.Succeeded, 3, 4, 5), (run.Status, run.WorksCount, run.VersionsCount, run.FilesCount));
        var snapshot = await ReadSnapshotAsync(cancellationToken);
        var version = snapshot.Versions.Single(row => row.SourceItemId == JellyfinSamples.District13Id);
        Assert.Equal(
            [(District13Path, 4_670_197_619L), (SecondPart, 1_000L)],
            snapshot.Files
                .Where(file => file.MediaVersionId == version.Id)
                .Select(file => (file.ServicePath, file.SizeBytes!.Value))
                .OrderBy(file => file.ServicePath, StringComparer.Ordinal));
    }

    private Task<IntegrationConnectionEntity> CreateJellyfinAsync() =>
        _integrations.Create("jellyfin", "Living room", FakeJellyfinServer.DefaultBaseUrl, apiKey: ApiKey);

    private JsonObject Movie(string id) => _jellyfin.Movies.Single(movie => (string?)movie["Id"] == id);

    private async Task<StoredLibrarySnapshot> ReadSnapshotAsync(CancellationToken cancellationToken)
    {
        StoredLibrarySnapshot? snapshot = null;
        await _factory.WithDbAsync(async db => snapshot = await StoredLibrarySnapshot.ReadAsync(db, cancellationToken));
        return snapshot!;
    }

    // Every provenanced row of the snapshot, whatever its table.
    private static IEnumerable<ProvenancedEntity> Provenanced(StoredLibrarySnapshot snapshot) =>
        [.. snapshot.Titles, .. snapshot.Identifiers, .. snapshot.Versions, .. snapshot.Files, .. snapshot.Locations];

    // "<table> <id> <item>" for every provenanced row, in one order: equal lists are the same rows,
    // reporting the same items.
    private static IEnumerable<string> Keys(StoredLibrarySnapshot snapshot) =>
    [
        .. snapshot.Titles.Select(row => $"title {row.Id} {row.SourceItemId}"),
        .. snapshot.Identifiers.Select(row => $"identifier {row.Id} {row.SourceItemId}"),
        .. snapshot.Versions.Select(row => $"version {row.Id} {row.SourceItemId}"),
        .. snapshot.Files.Select(row => $"file {row.Id} {row.SourceItemId}"),
        .. snapshot.Locations.Select(row => $"location {row.Id} {row.SourceItemId}"),
    ];

    // The work of each item, read off its titles.
    private static Dictionary<string, Guid> WorksByItem(StoredLibrarySnapshot snapshot) =>
        snapshot.Titles
            .GroupBy(title => title.SourceItemId!, StringComparer.Ordinal)
            .ToDictionary(item => item.Key, item => item.Select(title => title.MediaWorkId).Distinct().Single(), StringComparer.Ordinal);

    private static MediaVersionEntity VersionOf(StoredLibrarySnapshot snapshot, PhysicalFileEntity file) =>
        snapshot.Versions.Single(version => version.Id == file.MediaVersionId);
}
