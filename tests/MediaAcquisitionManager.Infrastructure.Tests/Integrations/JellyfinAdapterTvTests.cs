using System.Globalization;
using System.Net;
using System.Text.Json.Nodes;
using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Abstractions.Library;
using MediaAcquisitionManager.Core.Abstractions.Media;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Infrastructure.Integrations.Jellyfin;
using MediaAcquisitionManager.TestInfrastructure;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;

namespace MediaAcquisitionManager.Infrastructure.Tests.Integrations;

// MAM-32 — Jellyfin's series and episodes as the media library contract reads them (ADR 0017, ADR 0019):
// the real answers MAM-17 sampled from the reference library (VendorSamples/Jellyfin/series-page.json,
// seasons-page.json, episodes-page.json) — a series with every id, the id-less Penguins of Madagascar,
// the Tom & Jerry Kids item holding 65 files and no series, the two halves of one Mickey Mouse Clubhouse
// episode, a virtual special — every field read by name, and the library's word alone deciding what an
// item is.
public sealed class JellyfinAdapterTvTests : IDisposable
{
    private const string BaseUrl = "http://jellyfin.test:8096";
    private const string ApiKey = "4f1d0b9c2e7a4c3b8d6e5f7a9b0c1d2e";

    private const string RoadRallyAPath =
        "/media/cartoons/Mickey Mouse Clubhouse WEB-DL 1080p [Hurtom] by urij546724/Mickey Mouse Clubhouse (2010-2012) [S3] [ukr_eng] WEB-DL 1080p [Hurtom] by urij546724/Mickey Mouse Clubhouse_S03E09a_-_Road Rally [ukr_eng] WEB-DL 1080p [Hurtom].mkv";

    private const string RoadRallyBPath =
        "/media/cartoons/Mickey Mouse Clubhouse WEB-DL 1080p [Hurtom] by urij546724/Mickey Mouse Clubhouse (2010-2012) [S3] [ukr_eng] WEB-DL 1080p [Hurtom] by urij546724/Mickey Mouse Clubhouse_S03E09b_-_Road Rally [ukr_eng] WEB-DL 1080p [Hurtom].mkv";

    private static readonly IntegrationConnectionSettings Settings = new(null, BaseUrl, null, null, ApiKey);

    private static readonly LibraryPageRequest SeriesPage = new(MediaWorkKind.Series, 0, 50);

    private static readonly LibraryPageRequest EpisodesPage = new(MediaWorkKind.Episode, 0, 50);

    private readonly IntegrationAdapterHarness _harness = new();
    private readonly FakeJellyfinServer _server = new(BaseUrl);

    private IMediaLibraryProvider Library => _harness.Adapter<IMediaLibraryProvider>(JellyfinAdapter.TypeId);

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task ListItems_reads_the_sample_series_with_their_titles_years_and_the_total_of_the_library()
    {
        //Arrange
        _harness.JellyfinHttp.Handler = (_, _) => VendorResponses.Sample("Jellyfin/series-page.json", "application/json; charset=utf-8");

        //Act
        var result = await Library.ListItemsAsync(Settings, SeriesPage, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        var page = result.Value!;
        // The library's 10 series items, not the five in the file.
        Assert.Equal(JellyfinSamples.SampleSeriesTotalRecordCount, page.TotalCount);
        Assert.Equal(
            [JellyfinSamples.YellowstoneId, JellyfinSamples.HowTheUniverseWorksId, JellyfinSamples.MickeyMouseClubhouseId, JellyfinSamples.ClarksonsFarmId, JellyfinSamples.PenguinsId],
            page.Items.Select(item => item.SourceItemId));
        Assert.All(page.Items, item =>
        {
            Assert.Equal(MediaWorkKind.Series, item.Kind);
            // A series has no file of its own.
            Assert.Empty(item.Versions);
            Assert.Null(item.Episode);
            Assert.False(item.IsMissing);
        });
        var items = page.Items.ToDictionary(item => item.SourceItemId);
        Assert.Equal(
            [new ReportedTitle("Yellowstone", MediaTitleKind.Display, 2018), new ReportedTitle("Yellowstone", MediaTitleKind.Original, 2018)],
            items[JellyfinSamples.YellowstoneId].Titles);
        // Named after its folder, as Jellyfin found no metadata for it: the library's word, kept.
        Assert.Equal(
            [new ReportedTitle("The Penguins Of Madagascar (Season 1-3) (2008-2015) WEBDL 1080p [Ukr,Eng] Sub [Eng] [ Hurtom ] by Jatx 11", MediaTitleKind.Display, 2015)],
            items[JellyfinSamples.PenguinsId].Titles);
    }

    [Fact]
    public async Task ListItems_reads_a_series_imdb_tmdb_and_tvdb_ids_its_tmdb_id_as_a_shows()
    {
        //Arrange
        _harness.JellyfinHttp.Handler = (_, _) => VendorResponses.Sample("Jellyfin/series-page.json", "application/json; charset=utf-8");

        //Act
        var result = await Library.ListItemsAsync(Settings, SeriesPage, TestContext.Current.CancellationToken);

        //Assert
        var items = result.Value!.Items.ToDictionary(item => item.SourceItemId);
        // ADR 0017 — TMDb numbers shows apart from movies: 73586 is a show, whatever movie has that number.
        Assert.Equal(
            [
                new MediaIdentifier(MediaIdentifierScheme.Imdb, "tt4236770"),
                new MediaIdentifier(MediaIdentifierScheme.TmdbTv, "73586"),
                new MediaIdentifier(MediaIdentifierScheme.Tvdb, "341164"),
            ],
            items[JellyfinSamples.YellowstoneId].Identifiers);
        // TvMaze, TvRage and the TVDB slug are no scheme of this app.
        Assert.Equal(
            [
                new MediaIdentifier(MediaIdentifierScheme.Imdb, "tt0784896"),
                new MediaIdentifier(MediaIdentifierScheme.TmdbTv, "3934"),
                new MediaIdentifier(MediaIdentifierScheme.Tvdb, "79854"),
            ],
            items[JellyfinSamples.MickeyMouseClubhouseId].Identifiers);
        Assert.Empty(items[JellyfinSamples.PenguinsId].Identifiers);
    }

    [Fact]
    public async Task ListItems_reads_the_sample_episodes_with_the_total_of_the_library()
    {
        //Arrange
        _harness.JellyfinHttp.Handler = (_, _) => VendorResponses.Sample("Jellyfin/episodes-page.json", "application/json; charset=utf-8");

        //Act
        var result = await Library.ListItemsAsync(Settings, EpisodesPage, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        var page = result.Value!;
        Assert.Equal(JellyfinSamples.SampleEpisodesTotalRecordCount, page.TotalCount);
        Assert.Equal(
            [
                JellyfinSamples.YellowstoneS01E01Id,
                JellyfinSamples.TomAndJerryKidsId,
                JellyfinSamples.BigBangId,
                JellyfinSamples.RoadRallyAId,
                JellyfinSamples.RoadRallyBId,
                JellyfinSamples.FollowTheMouseId,
            ],
            page.Items.Select(item => item.SourceItemId));
        Assert.All(page.Items, item => Assert.Equal(MediaWorkKind.Episode, item.Kind));
    }

    [Fact]
    public async Task ListItems_reads_an_episode_with_its_series_item_numbers_own_ids_and_file()
    {
        //Arrange
        _harness.JellyfinHttp.Handler = (_, _) => VendorResponses.Sample("Jellyfin/episodes-page.json", "application/json; charset=utf-8");

        //Act
        var result = await Library.ListItemsAsync(Settings, EpisodesPage, TestContext.Current.CancellationToken);

        //Assert
        var episode = result.Value!.Items.Single(item => item.SourceItemId == JellyfinSamples.YellowstoneS01E01Id);
        Assert.Equal([new ReportedTitle("Daybreak", MediaTitleKind.Display, 2018)], episode.Titles);
        // Its own ids, never its series': IMDb and TVDB, as episodes carry them (ADR 0017).
        Assert.Equal(
            [new MediaIdentifier(MediaIdentifierScheme.Imdb, "tt7529124"), new MediaIdentifier(MediaIdentifierScheme.Tvdb, "6507316")],
            episode.Identifiers);
        Assert.Equal(new LibraryEpisode(JellyfinSamples.YellowstoneId, 1, 1), episode.Episode);
        Assert.False(episode.IsMissing);
        Assert.Equal(
            [$"{JellyfinSamples.YellowstoneS01E01Id} [] /media/tv-shows/Yellowstone/Yellowstone.2018.S01.WEBRip.1080p/Yellowstone.2018.S01E01.Daybreak.1080p.WEBRip.mkv 2905796131"],
            DescribeVersions(episode));
    }

    [Fact]
    public async Task ListItems_reads_the_tom_and_jerry_kids_item_as_one_episode_of_no_series_with_65_versions()
    {
        //Arrange
        _harness.JellyfinHttp.Handler = (_, _) => VendorResponses.Sample("Jellyfin/episodes-page.json", "application/json; charset=utf-8");

        //Act
        var result = await Library.ListItemsAsync(Settings, EpisodesPage, TestContext.Current.CancellationToken);

        //Assert
        // ADR 0017 — what the library groups stays grouped: 65 files, 65 different episodes, are one item,
        // so one episode with 65 versions, each labelled as Jellyfin's version picker shows it.
        var item = result.Value!.Items.Single(item => item.SourceItemId == JellyfinSamples.TomAndJerryKidsId);
        Assert.Equal(new LibraryEpisode(null, 1, 4), item.Episode);
        Assert.Empty(item.Identifiers);
        Assert.Equal(65, item.Versions.Count);
        Assert.Equal(65, item.Versions.Select(version => version.SourceVersionId).Distinct().Count());
        Assert.All(item.Versions, version =>
        {
            var file = Assert.Single(version.Files);
            Assert.StartsWith("/media/cartoons/Tom & Jerry Kids season 1-4 (1990-1993) [ukr] TVRip [Hurtom] by MasterYura/", file.ServicePath, StringComparison.Ordinal);
            Assert.NotNull(version.EditionLabel);
        });
        // The label is cleaned like any remote text: runs of spaces become one.
        Assert.Equal("02. Toys Will Be Toys Droopy Delivers My Pal", item.Versions[1].EditionLabel);
    }

    [Fact]
    public async Task ListItems_reads_the_two_halves_of_an_episode_with_the_same_numbers_and_ids()
    {
        //Arrange
        _harness.JellyfinHttp.Handler = (_, _) => VendorResponses.Sample("Jellyfin/episodes-page.json", "application/json; charset=utf-8");

        //Act
        var result = await Library.ListItemsAsync(Settings, EpisodesPage, TestContext.Current.CancellationToken);

        //Assert
        var items = result.Value!.Items.ToDictionary(item => item.SourceItemId);
        var halfA = items[JellyfinSamples.RoadRallyAId];
        var halfB = items[JellyfinSamples.RoadRallyBId];
        // Two items, as Jellyfin keeps them, each with the episode's numbers and ids; which work they are
        // is the core's decision.
        Assert.Equal(new LibraryEpisode(JellyfinSamples.MickeyMouseClubhouseId, 3, 9), halfA.Episode);
        Assert.Equal(halfA.Episode, halfB.Episode);
        Assert.Equal(
            [new MediaIdentifier(MediaIdentifierScheme.Imdb, "tt1715023"), new MediaIdentifier(MediaIdentifierScheme.Tvdb, "2804541")],
            halfA.Identifiers);
        Assert.Equal(halfA.Identifiers, halfB.Identifiers);
        Assert.Equal(
            [new ReportedTitle("Road Rally", MediaTitleKind.Display, 2010), new ReportedTitle("Road Rally Part 1", MediaTitleKind.Original, 2010)],
            halfA.Titles);
        Assert.Equal(RoadRallyAPath, Assert.Single(Assert.Single(halfA.Versions).Files).ServicePath);
        Assert.Equal(RoadRallyBPath, Assert.Single(Assert.Single(halfB.Versions).Files).ServicePath);
    }

    [Fact]
    public async Task ListItems_reads_a_virtual_special_as_missing_with_no_file()
    {
        //Arrange
        _harness.JellyfinHttp.Handler = (_, _) => VendorResponses.Sample("Jellyfin/episodes-page.json", "application/json; charset=utf-8");

        //Act
        var result = await Library.ListItemsAsync(Settings, EpisodesPage, TestContext.Current.CancellationToken);

        //Assert
        // Read, so the pages of the list add up, and marked missing: the core stores nothing of it. Its one
        // media source is a placeholder without a path, which is no file (ADR 0017).
        var special = result.Value!.Items.Single(item => item.SourceItemId == JellyfinSamples.FollowTheMouseId);
        Assert.True(special.IsMissing);
        Assert.Equal(new LibraryEpisode(JellyfinSamples.MickeyMouseClubhouseId, 0, 1), special.Episode);
        Assert.All(special.Versions, version => Assert.Empty(version.Files));
        Assert.All(result.Value!.Items.Where(item => item.SourceItemId != JellyfinSamples.FollowTheMouseId), item => Assert.False(item.IsMissing));
    }

    [Fact]
    public async Task ListItems_places_every_sampled_episode_in_the_season_jellyfin_lists_for_it()
    {
        //Arrange
        // Seasons come from the episodes themselves (MAM-32): each episode's ParentIndexNumber and SeriesId
        // are its season item's IndexNumber and SeriesId, so the seasons list adds nothing to read.
        _harness.JellyfinHttp.Handler = (_, _) => VendorResponses.Sample("Jellyfin/episodes-page.json", "application/json; charset=utf-8");
        var seasons = JellyfinSamples.Seasons().ToDictionary(season => (string)season["Id"]!);
        var episodes = JellyfinSamples.Episodes().ToDictionary(episode => (string)episode["Id"]!);

        //Act
        var result = await Library.ListItemsAsync(Settings, EpisodesPage, TestContext.Current.CancellationToken);

        //Assert
        var placed = result.Value!.Items
            .Where(item => (string?)episodes[item.SourceItemId]["SeasonId"] is not null)
            .ToList();
        Assert.Equal(5, placed.Count);
        Assert.All(placed, item =>
        {
            var season = seasons[(string)episodes[item.SourceItemId]["SeasonId"]!];
            Assert.Equal((int)season["IndexNumber"]!, item.Episode!.SeasonNumber);
            Assert.Equal((string)season["SeriesId"]!, item.Episode.SeriesSourceItemId);
        });
        Assert.Equal(JellyfinSamples.SampleSeasonsTotalRecordCount, (int)JsonNode.Parse(File.ReadAllText(Path.Combine(RepositoryPaths.VendorSamples, "Jellyfin", "seasons-page.json")))!["TotalRecordCount"]!);
    }

    [Fact]
    public async Task ListItems_reads_an_episode_tmdb_id_as_nothing_and_a_movie_tvdb_id_as_nothing()
    {
        //Arrange
        // An episode's TMDb id would be TMDb's id of the episode, a third numbering; a movie has no TVDB id
        // this app matches by. Neither is stored as something it isn't.
        var episode = JellyfinSamples.EpisodeWith("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "Pilot", null, 1, 1, "/media/tv-shows/x.mkv", "tt0000001", "1");
        episode["ProviderIds"]!["Tmdb"] = "62085";
        ArmEpisodes(episode);

        //Act
        var result = await Library.ListItemsAsync(Settings, EpisodesPage, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(
            [new MediaIdentifier(MediaIdentifierScheme.Imdb, "tt0000001"), new MediaIdentifier(MediaIdentifierScheme.Tvdb, "1")],
            Assert.Single(result.Value!.Items).Identifiers);
    }

    [Theory]
    [InlineData(MediaWorkKind.Series, "Series", "\"Movie\"", "Jellyfin listed an item of type “Movie” when asked for series only, so the app can't rely on the list. Check the address.")]
    [InlineData(MediaWorkKind.Episode, "Episode", "\"Season\"", "Jellyfin listed an item of type “Season” when asked for episodes only, so the app can't rely on the list. Check the address.")]
    [InlineData(MediaWorkKind.Episode, "Episode", "\"Movie\"", "Jellyfin listed an item of type “Movie” when asked for episodes only, so the app can't rely on the list. Check the address.")]
    public async Task ListItems_fails_the_page_on_an_item_of_another_type_than_the_list(MediaWorkKind kind, string type, string otherType, string expectedError)
    {
        //Arrange
        var item = type == "Series" ? JellyfinSamples.Series(JellyfinSamples.YellowstoneId) : JellyfinSamples.Episode(JellyfinSamples.YellowstoneS01E01Id);
        item["Type"] = JsonNode.Parse(otherType);
        ArmBody(new JsonObject { ["Items"] = new JsonArray(item), ["TotalRecordCount"] = 1 }.ToJsonString());

        //Act
        var result = await Library.ListItemsAsync(Settings, new LibraryPageRequest(kind, 0, 50), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.UnexpectedResponse, result.Status);
        Assert.Equal(expectedError, result.Error);
    }

    [Theory]
    [InlineData(MediaWorkKind.Series, "The service at this address listed a series without an id, which Jellyfin never does. Check the address.")]
    [InlineData(MediaWorkKind.Episode, "The service at this address listed an episode without an id, which Jellyfin never does. Check the address.")]
    public async Task ListItems_fails_the_page_on_an_item_without_an_id(MediaWorkKind kind, string expectedError)
    {
        //Arrange
        var item = kind == MediaWorkKind.Series ? JellyfinSamples.Series(JellyfinSamples.YellowstoneId) : JellyfinSamples.Episode(JellyfinSamples.YellowstoneS01E01Id);
        item.Remove("Id");
        ArmBody(new JsonObject { ["Items"] = new JsonArray(item), ["TotalRecordCount"] = 1 }.ToJsonString());

        //Act
        var result = await Library.ListItemsAsync(Settings, new LibraryPageRequest(kind, 0, 50), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal((IntegrationCallStatus.UnexpectedResponse, expectedError), (result.Status, result.Error));
    }

    [Theory]
    [InlineData(MediaWorkKind.Series, "Series", "ProviderIds,OriginalTitle")]
    [InlineData(MediaWorkKind.Episode, "Episode", "Path,MediaSources,ProviderIds,OriginalTitle")]
    public async Task ListItems_asks_for_one_page_of_the_list_with_its_fields(MediaWorkKind kind, string type, string fields)
    {
        //Arrange
        _server.Arm(_harness.JellyfinHttp);

        //Act
        await Library.ListItemsAsync(Settings, new LibraryPageRequest(kind, 49, 50), TestContext.Current.CancellationToken);

        //Assert
        var request = Assert.Single(_harness.JellyfinHttp.Requests);
        Assert.Equal(
            [
                "recursive=true",
                $"includeItemTypes={type}",
                "collapseBoxSetItems=false",
                $"fields={fields}",
                "sortBy=SortName,DateCreated",
                "sortOrder=Ascending",
                "startIndex=49",
                "limit=50",
                // MAM-35 — the primary image's tag and no other image's: it names the poster a card shows.
                "enableImages=true",
                "enableImageTypes=Primary",
                "imageTypeLimit=1",
                "enableUserData=false",
                "enableTotalRecordCount=true",
            ],
            request.Uri.Query.TrimStart('?').Split('&'));
    }

    [Fact]
    public async Task ListItems_reads_a_stacked_episodes_other_parts()
    {
        //Arrange
        var episode = JellyfinSamples.Episode(JellyfinSamples.YellowstoneS01E01Id);
        episode["PartCount"] = 2;
        ArmEpisodes(episode);
        _server.AdditionalParts[JellyfinSamples.YellowstoneS01E01Id] =
            [JellyfinSamples.Part("bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", "/media/tv-shows/Yellowstone/S01E01-part2.mkv", 1000)];

        //Act
        var result = await Library.ListItemsAsync(Settings, EpisodesPage, TestContext.Current.CancellationToken);

        //Assert
        var version = Assert.Single(Assert.Single(result.Value!.Items).Versions);
        Assert.Equal(
            [
                "/media/tv-shows/Yellowstone/Yellowstone.2018.S01.WEBRip.1080p/Yellowstone.2018.S01E01.Daybreak.1080p.WEBRip.mkv",
                "/media/tv-shows/Yellowstone/S01E01-part2.mkv",
            ],
            version.Files.Select(file => file.ServicePath));
    }

    [Fact]
    public async Task ListItems_of_a_stacked_episode_whose_parts_are_gone_names_an_episode()
    {
        //Arrange
        var episode = JellyfinSamples.Episode(JellyfinSamples.YellowstoneS01E01Id);
        episode["PartCount"] = 2;
        ArmEpisodes(episode);

        //Act
        var result = await Library.ListItemsAsync(Settings, EpisodesPage, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(
            "Jellyfin no longer finds the other parts of an episode it has just listed (404 Not Found); its library is changing. Try again once Jellyfin's library scan has finished.",
            result.Error);
    }

    // The server answering these episodes and nothing else, with their count as the total.
    private void ArmEpisodes(params JsonObject[] episodes)
    {
        _server.Episodes.Clear();
        _server.Episodes.AddRange(episodes);
        _server.Arm(_harness.JellyfinHttp);
    }

    private void ArmBody(string body) =>
        _harness.JellyfinHttp.Handler = (_, _) => VendorResponses.Text(body, HttpStatusCode.OK, "application/json");

    // Each version as "id [label] path size | path size …".
    private static List<string> DescribeVersions(LibraryItem item) =>
    [
        .. item.Versions.Select(version =>
            $"{version.SourceVersionId ?? "(no id)"} [{version.EditionLabel}] " +
            string.Join(" | ", version.Files.Select(file => $"{file.ServicePath} {file.SizeBytes?.ToString(CultureInfo.InvariantCulture) ?? "(unknown)"}"))),
    ];
}
