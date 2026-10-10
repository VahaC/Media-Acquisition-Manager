using System.Net;
using System.Text.Json.Nodes;
using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Abstractions.Library;
using MediaAcquisitionManager.Core.Abstractions.Media;
using MediaAcquisitionManager.Infrastructure.Integrations.Jellyfin;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;

namespace MediaAcquisitionManager.Infrastructure.Tests.Integrations;

// MAM-25 — what Jellyfin holds at a new file's path (IMediaLibraryFileLookup.ReadFilesAsync), read by the id the path
// computes as a film's, an episode's, a plain video's and a trailer's (ADR 0035; live on 12.2.0, 2026-10-10: 880 of 880
// movies, 440 of 440 episode files), as the administrator: the file's own item — the hidden alternate version recorded live included —, its
// kind by Jellyfin's word, its ids by the sync's rules, an episode's numbers with the end number and its series' ids, and
// whether Jellyfin has finished reading the file. The sampled items carry the ids their paths compute, so the fake answers
// them where the adapter looks. Every call is a GET; failures are JellyfinAdapterFilesReadFailureTests'.
public sealed class JellyfinAdapterFilesReadTests : IDisposable
{
    private const string BaseUrl = "http://jellyfin.test:8096";
    private const string ApiKey = "4f1d0b9c2e7a4c3b8d6e5f7a9b0c1d2e";

    private const string FilmPath = "/media/movies/Alien (1979)/Alien.1979.2160p.UHD.BDRemux.mkv";
    private const string ShowSeriesId = "6f9619ff8b86d011b42d00c04fc964ff";
    private const string ShowPath = "/media/tv-shows/Show";

    private static readonly IntegrationConnectionSettings Settings = new(null, BaseUrl, null, null, ApiKey);

    private readonly IntegrationAdapterHarness _harness = new();
    private readonly FakeJellyfinServer _server = new(BaseUrl) { ApiKey = ApiKey };

    public JellyfinAdapterFilesReadTests() => _server.Arm(_harness.JellyfinHttp);

    private IMediaLibraryFileLookup Lookup => _harness.Adapter<IMediaLibraryFileLookup>(JellyfinAdapter.TypeId);

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task ReadFiles_finds_a_film_by_the_id_its_path_computes_as_a_movie_with_its_ids()
    {
        //Arrange
        var path = (string)JellyfinSamples.Movie(JellyfinSamples.AliensTheatricalId)["Path"]!;

        //Act
        var result = await Lookup.ReadFilesAsync(Settings, [path], TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        var reading = Assert.Single(result.Value!);
        Assert.Equal(
            new Facts(path, true, JellyfinSamples.AliensTheatricalId, LibraryFileKind.Movie, true, ["Imdb tt0090605", "Tmdb 679"], null, []),
            Facts.Of(reading));
        // The administrator first, then the one id: a film is found at the first type tried. Reads only.
        Assert.Equal(
            ["GET /Users", $"GET /Items/{JellyfinSamples.AliensTheatricalId}?userId={JellyfinSamples.AdministratorUserId}"],
            Lines());
        Assert.All(_harness.JellyfinHttp.Requests, request => Assert.Equal([$"MediaBrowser Token=\"{ApiKey}\""], request.HeaderValues("Authorization")));
    }

    [Fact]
    public async Task ReadFiles_finds_an_episode_by_the_id_its_path_computes_as_an_episode_with_its_numbers_and_its_series_ids()
    {
        //Arrange
        var episode = JellyfinSamples.Episode(JellyfinSamples.YellowstoneS01E01Id);
        _server.Episodes.Add(episode);
        _server.Series.Add(JellyfinSamples.Series(JellyfinSamples.YellowstoneId));
        var path = (string)episode["Path"]!;

        //Act
        var result = await Lookup.ReadFilesAsync(Settings, [path], TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        // The episode's own ids (IMDb, TVDB), and its show's from the series item — TMDb as a show's (ADR 0017).
        Assert.Equal(
            new Facts(
                path,
                true,
                JellyfinSamples.YellowstoneS01E01Id,
                LibraryFileKind.Episode,
                true,
                ["Imdb tt7529124", "Tvdb 6507316"],
                new LibraryEpisodePlace(JellyfinSamples.YellowstoneId, 1, 1, null),
                ["Imdb tt4236770", "TmdbTv 73586", "Tvdb 341164"]),
            Facts.Of(Assert.Single(result.Value!)));
        Assert.Equal(
            [
                "GET /Users",
                $"GET /Items/{JellyfinItemIds.Of(JellyfinItemIds.MovieType, path)}?userId={JellyfinSamples.AdministratorUserId}",
                $"GET /Items/{JellyfinSamples.YellowstoneS01E01Id}?userId={JellyfinSamples.AdministratorUserId}",
                $"GET /Items?ids={JellyfinSamples.YellowstoneId}&fields=ProviderIds&enableImages=false&enableUserData=false",
            ],
            Lines());
    }

    [Fact]
    public async Task ReadFiles_reads_the_end_number_of_an_episode_whose_file_covers_several()
    {
        //Arrange
        _server.Series.Add(JellyfinSamples.SeriesWith(ShowSeriesId, "Show", 2020, ShowPath, imdbId: "tt1000001", tmdbId: "1001", tvdbId: "2001"));
        var path = $"{ShowPath}/Season 02/Show.S02E05-E06.mkv";
        _server.PlantEpisode(path, "Two Parts", ShowSeriesId, 2, 5, episodeEnd: 6, tvdbId: "3005");

        //Act
        var result = await Lookup.ReadFilesAsync(Settings, [path], TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        var reading = Assert.Single(result.Value!);
        Assert.Equal(new LibraryEpisodePlace(ShowSeriesId, 2, 5, 6), reading.Episode);
        Assert.True(reading.Refreshed);
        Assert.Equal(["Imdb tt1000001", "TmdbTv 1001", "Tvdb 2001"], Describe(reading.SeriesIdentifiers));
    }

    [Fact]
    public async Task ReadFiles_reads_each_series_once_however_many_of_its_episodes_are_asked_about()
    {
        //Arrange
        // The two halves of Mickey Mouse Clubhouse S03E09, one series item.
        var halfA = JellyfinSamples.Episode(JellyfinSamples.RoadRallyAId);
        var halfB = JellyfinSamples.Episode(JellyfinSamples.RoadRallyBId);
        _server.Episodes.AddRange([halfA, halfB]);
        _server.Series.Add(JellyfinSamples.Series(JellyfinSamples.MickeyMouseClubhouseId));

        //Act
        var result = await Lookup.ReadFilesAsync(Settings, [(string)halfA["Path"]!, (string)halfB["Path"]!], TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        Assert.Equal([JellyfinSamples.RoadRallyAId, JellyfinSamples.RoadRallyBId], result.Value!.Select(reading => reading.ItemId));
        Assert.All(result.Value!, reading =>
        {
            Assert.Equal(new LibraryEpisodePlace(JellyfinSamples.MickeyMouseClubhouseId, 3, 9, null), reading.Episode);
            Assert.Equal(["Imdb tt0784896", "TmdbTv 3934", "Tvdb 79854"], Describe(reading.SeriesIdentifiers));
        });
        Assert.Single(Lines(), line => line.StartsWith("GET /Items?ids=", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ReadFiles_reads_a_hidden_alternate_version_as_jellyfin_answered_it()
    {
        //Arrange
        // Recorded live on 12.2.0: a file Jellyfin holds as an alternate version of the Tom & Jerry Kids item, which no list
        // shows; its id is its own path's as an episode.
        var alternate = JellyfinSamples.EpisodeAlternateVersionItem();
        _server.Episodes.Add(alternate);
        var path = (string)alternate["Path"]!;

        //Act
        var result = await Lookup.ReadFilesAsync(Settings, [path], TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        // Its own numbers and running time; no series and no ids in that orphan folder, so no series is read.
        Assert.Equal(
            new Facts(path, true, JellyfinSamples.TomAndJerryKidsAlternateId, LibraryFileKind.Episode, true, [], new LibraryEpisodePlace(null, 1, 4, null), []),
            Facts.Of(Assert.Single(result.Value!)));
        Assert.Equal(
            [
                "GET /Users",
                $"GET /Items/{JellyfinItemIds.Of(JellyfinItemIds.MovieType, path)}?userId={JellyfinSamples.AdministratorUserId}",
                $"GET /Items/{JellyfinSamples.TomAndJerryKidsAlternateId}?userId={JellyfinSamples.AdministratorUserId}",
            ],
            Lines());
    }

    [Fact]
    public async Task ReadFiles_of_files_jellyfin_has_not_finished_reading_are_listed_and_not_refreshed()
    {
        //Arrange
        _server.Series.Add(JellyfinSamples.SeriesWith(ShowSeriesId, "Show", 2020, ShowPath, tvdbId: "2001"));
        var episodePath = $"{ShowPath}/Season 01/Show.S01E01.mkv";
        var unnumberedPath = $"{ShowPath}/Season 01/Show.S01E02.mkv";
        _server.PlantFilm(FilmPath, "Alien", 1979, imdbId: "tt0078748", tmdbId: "348", refreshed: false);
        _server.PlantEpisode(episodePath, "Pilot", ShowSeriesId, 1, 1, tvdbId: "3001", refreshed: false);
        // Its running time measured, its number not yet: not finished either.
        _server.PlantEpisode(unnumberedPath, "Second", ShowSeriesId, 1, 2, tvdbId: "3002").Remove("IndexNumber");

        //Act
        var result = await Lookup.ReadFilesAsync(Settings, [FilmPath, episodePath, unnumberedPath], TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        Assert.Equal(
            [
                new Facts(FilmPath, true, JellyfinItemIds.Of(JellyfinItemIds.MovieType, FilmPath), LibraryFileKind.Movie, false, [], null, []),
                new Facts(
                    episodePath, true, JellyfinItemIds.Of(JellyfinItemIds.EpisodeType, episodePath), LibraryFileKind.Episode, false, [],
                    new LibraryEpisodePlace(ShowSeriesId, null, null, null), ["Tvdb 2001"]),
                new Facts(
                    unnumberedPath, true, JellyfinItemIds.Of(JellyfinItemIds.EpisodeType, unnumberedPath), LibraryFileKind.Episode, false, ["Tvdb 3002"],
                    new LibraryEpisodePlace(ShowSeriesId, 1, null, null), ["Tvdb 2001"]),
            ],
            result.Value!.Select(Facts.Of));
    }

    [Fact]
    public async Task ReadFiles_after_jellyfin_finished_reading_a_file_gives_its_ids()
    {
        //Arrange
        _server.PlantFilm(FilmPath, "Alien", 1979, refreshed: false);
        var before = await Lookup.ReadFilesAsync(Settings, [FilmPath], TestContext.Current.CancellationToken);
        _server.PlantFilm(FilmPath, "Alien", 1979, imdbId: "tt0078748", tmdbId: "348");

        //Act
        var after = await Lookup.ReadFilesAsync(Settings, [FilmPath], TestContext.Current.CancellationToken);

        //Assert
        Assert.False(Assert.Single(before.Value!).Refreshed);
        var reading = Assert.Single(after.Value!);
        Assert.True(reading.Refreshed);
        Assert.Equal(["Imdb tt0078748", "Tmdb 348"], Describe(reading.Identifiers));
    }

    [Fact]
    public async Task ReadFiles_of_a_path_jellyfin_holds_no_item_for_is_not_listed_after_asking_as_each_type()
    {
        //Arrange
        const string path = "/media/movies/Nothing (2000)/Nothing.2000.mkv";

        //Act
        var result = await Lookup.ReadFilesAsync(Settings, [path], TestContext.Current.CancellationToken);

        //Assert
        // Not listed is a reading, not a failure: Jellyfin may not have looked yet.
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        Assert.Equal(new Facts(path, false, null, null, false, [], null, []), Facts.Of(Assert.Single(result.Value!)));
        Assert.Equal(
            [
                "GET /Users",
                $"GET /Items/{JellyfinItemIds.Of(JellyfinItemIds.MovieType, path)}?userId={JellyfinSamples.AdministratorUserId}",
                $"GET /Items/{JellyfinItemIds.Of(JellyfinItemIds.EpisodeType, path)}?userId={JellyfinSamples.AdministratorUserId}",
                $"GET /Items/{JellyfinItemIds.Of(JellyfinItemIds.VideoType, path)}?userId={JellyfinSamples.AdministratorUserId}",
                $"GET /Items/{JellyfinItemIds.Of(JellyfinItemIds.TrailerType, path)}?userId={JellyfinSamples.AdministratorUserId}",
            ],
            Lines());
    }

    [Fact]
    public async Task ReadFiles_never_takes_an_item_of_another_path_for_the_file()
    {
        //Arrange
        // A film answered under the id the path computes, but at another path: were the formula to change, the app would rather
        // not find the file than take another one for it.
        var movieId = JellyfinItemIds.Of(JellyfinItemIds.MovieType, FilmPath);
        var other = JellyfinSamples.Movie(JellyfinSamples.AliensTheatricalId);
        _server.Intercept = request => request.RequestUri!.AbsolutePath.EndsWith($"/Items/{movieId}", StringComparison.Ordinal)
            ? VendorResponses.Text(other.ToJsonString(), HttpStatusCode.OK, "application/json")
            : null;

        //Act
        var result = await Lookup.ReadFilesAsync(Settings, [FilmPath], TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        Assert.Equal(new Facts(FilmPath, false, null, null, false, [], null, []), Facts.Of(Assert.Single(result.Value!)));
        Assert.Equal(5, Lines().Count);
    }

    [Fact]
    public async Task ReadFiles_takes_a_path_only_in_jellyfins_exact_spelling()
    {
        //Arrange
        _server.PlantFilm(FilmPath, "Alien", 1979, imdbId: "tt0078748", tmdbId: "348");

        //Act
        var result = await Lookup.ReadFilesAsync(Settings, [FilmPath.ToUpperInvariant()], TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        Assert.False(Assert.Single(result.Value!).Listed);
    }

    [Fact]
    public async Task ReadFiles_reads_a_plain_video_as_another_kind_with_no_ids()
    {
        //Arrange
        // Jellyfin's Video type: a home video or an extra other than a trailer, never a film or an episode, whatever ids it holds.
        const string path = "/media/movies/Alien (1979)/extras/Alien.1979.Featurette.mkv";
        var videoId = JellyfinItemIds.Of(JellyfinItemIds.VideoType, path);
        var video = JellyfinSamples.FilmAt(path, "Featurette", 1979, imdbId: "tt0078748", tmdbId: "348");
        video["Type"] = "Video";
        video["Id"] = videoId;
        video["MediaSources"]![0]!["Id"] = videoId;
        _server.Movies.Add(video);

        //Act
        var result = await Lookup.ReadFilesAsync(Settings, [path], TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        Assert.Equal(new Facts(path, true, videoId, LibraryFileKind.Other, true, [], null, []), Facts.Of(Assert.Single(result.Value!)));
        Assert.Equal(4, Lines().Count);
    }

    [Fact]
    public async Task ReadFiles_finds_a_trailer_by_its_own_type_and_reads_it_as_another_kind_with_no_ids()
    {
        //Arrange
        // Jellyfin 12.2 resolves a trailer extra as its own Trailer type, so its id is its path's as a trailer, not as a plain
        // video: a trailer a pack brings is listed, and holds nothing up, like every other extra.
        const string path = "/media/movies/Alien (1979)/trailers/Alien.1979.Trailer.mkv";
        var trailerId = JellyfinItemIds.Of(JellyfinItemIds.TrailerType, path);
        var trailer = JellyfinSamples.FilmAt(path, "Trailer", 1979, imdbId: "tt0078748", tmdbId: "348");
        trailer["Type"] = "Trailer";
        trailer["Id"] = trailerId;
        trailer["MediaSources"]![0]!["Id"] = trailerId;
        _server.Movies.Add(trailer);

        //Act
        var result = await Lookup.ReadFilesAsync(Settings, [path], TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        Assert.Equal(new Facts(path, true, trailerId, LibraryFileKind.Other, true, [], null, []), Facts.Of(Assert.Single(result.Value!)));
        // Tried last: the administrator, then a film's, an episode's and a plain video's ids, each a 404, then a trailer's.
        Assert.Equal(5, Lines().Count);
        Assert.Equal($"GET /Items/{trailerId}?userId={JellyfinSamples.AdministratorUserId}", Lines()[^1]);
    }

    [Fact]
    public async Task ReadFiles_gives_one_reading_per_path_in_the_order_asked()
    {
        //Arrange
        var episode = JellyfinSamples.Episode(JellyfinSamples.YellowstoneS01E01Id);
        _server.Episodes.Add(episode);
        _server.Series.Add(JellyfinSamples.Series(JellyfinSamples.YellowstoneId));
        _server.PlantFilm(FilmPath, "Alien", 1979, imdbId: "tt0078748", tmdbId: "348");
        string[] paths = [(string)episode["Path"]!, "/media/movies/Nothing (2000)/Nothing.mkv", FilmPath];

        //Act
        var result = await Lookup.ReadFilesAsync(Settings, paths, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        Assert.Equal(paths, result.Value!.Select(reading => reading.ServicePath));
        Assert.Equal([LibraryFileKind.Episode, null, LibraryFileKind.Movie], result.Value!.Select(reading => reading.Kind));
        Assert.All(_harness.JellyfinHttp.Requests, request => Assert.Equal(HttpMethod.Get, request.Method));
    }

    [Fact]
    public async Task ReadFiles_takes_the_most_paths_the_contract_allows()
    {
        //Arrange
        // MAM-25 — the contract's bound is the adapter's: the verifier reads in batches of exactly this many.
        _server.PlantFilm(FilmPath, "Alien", 1979, imdbId: "tt0078748", tmdbId: "348");
        string[] paths = [.. JellyfinAdapterFilesScanTests.Films(IMediaLibraryFileLookup.MaximumPaths - 1), FilmPath];

        //Act
        var result = await Lookup.ReadFilesAsync(Settings, paths, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        Assert.Equal(paths, result.Value!.Select(reading => reading.ServicePath));
        Assert.Equal(LibraryFileKind.Movie, result.Value![^1].Kind);
    }

    [Fact]
    public async Task ReadFiles_of_an_episode_whose_series_jellyfin_no_longer_lists_has_no_series_ids()
    {
        //Arrange
        var path = $"{ShowPath}/Season 01/Show.S01E01.mkv";
        _server.PlantEpisode(path, "Pilot", ShowSeriesId, 1, 1, tvdbId: "3001");

        //Act
        var result = await Lookup.ReadFilesAsync(Settings, [path], TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        var reading = Assert.Single(result.Value!);
        Assert.Equal(ShowSeriesId, reading.Episode!.SeriesItemId);
        Assert.Empty(reading.SeriesIdentifiers);
    }

    [Fact]
    public async Task ReadFiles_never_takes_the_ids_of_an_item_that_is_not_a_series_for_a_shows()
    {
        //Arrange
        // Under the episode's series id Jellyfin lists a film: a film's TMDb number is not a show's (ADR 0017).
        var path = $"{ShowPath}/Season 01/Show.S01E01.mkv";
        _server.PlantEpisode(path, "Pilot", ShowSeriesId, 1, 1, tvdbId: "3001");
        _server.Movies.Add(JellyfinSamples.MovieWith(ShowSeriesId, "Not a Show", null, 2020, "/media/movies/Not a Show.mkv", "tt1000001", "1001"));

        //Act
        var result = await Lookup.ReadFilesAsync(Settings, [path], TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        Assert.Empty(Assert.Single(result.Value!).SeriesIdentifiers);
    }

    [Fact]
    public async Task ReadFiles_never_asks_for_a_series_by_an_id_jellyfin_never_gives()
    {
        //Arrange
        var path = $"{ShowPath}/Season 01/Show.S01E01.mkv";
        _server.PlantEpisode(path, "Pilot", "../Users", 1, 1, tvdbId: "3001");

        //Act
        var result = await Lookup.ReadFilesAsync(Settings, [path], TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        var reading = Assert.Single(result.Value!);
        Assert.Equal(new LibraryEpisodePlace(null, 1, 1, null), reading.Episode);
        Assert.DoesNotContain(Lines(), line => line.StartsWith("GET /Items?ids=", StringComparison.Ordinal));
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    /// <summary>Every request received, oldest first, as <c>METHOD /path?query</c>.</summary>
    private List<string> Lines() => [.. _harness.JellyfinHttp.Requests.Select(request => $"{request.Method} {request.Uri.PathAndQuery}")];

    /// <summary>Each id as <c>Scheme value</c>.</summary>
    private static List<string> Describe(IReadOnlyList<MediaIdentifier> identifiers) =>
        [.. identifiers.Select(identifier => $"{identifier.Scheme} {identifier.Value}")];

    /// <summary>A reading with its ids as text, so two compare by value.</summary>
    private sealed record Facts(
        string ServicePath,
        bool Listed,
        string? ItemId,
        LibraryFileKind? Kind,
        bool Refreshed,
        List<string> Identifiers,
        LibraryEpisodePlace? Episode,
        List<string> SeriesIdentifiers)
    {
        public static Facts Of(LibraryFileReading reading) =>
            new(
                reading.ServicePath,
                reading.Listed,
                reading.ItemId,
                reading.Kind,
                reading.Refreshed,
                Describe(reading.Identifiers),
                reading.Episode,
                Describe(reading.SeriesIdentifiers));

        public bool Equals(Facts? other) =>
            other is not null
            && (ServicePath, Listed, ItemId, Kind, Refreshed, Episode) == (other.ServicePath, other.Listed, other.ItemId, other.Kind, other.Refreshed, other.Episode)
            && Identifiers.SequenceEqual(other.Identifiers)
            && SeriesIdentifiers.SequenceEqual(other.SeriesIdentifiers);

        public override int GetHashCode() => HashCode.Combine(ServicePath, ItemId);
    }
}
