using System.Globalization;
using System.Text.Json.Nodes;

namespace MediaAcquisitionManager.TestInfrastructure.Integrations;

/// <summary>
/// The real Jellyfin answers of <c>VendorSamples/Jellyfin</c> (MAM-6) as editable JSON, and variants
/// synthesized from them for what the reference library doesn't show (MAM-8): merged versions, a
/// library of any size, a movie without metadata, a stacked movie's parts. MAM-32 adds the series,
/// seasons and episodes MAM-17 sampled, and series and episodes of any facts. MAM-34 adds a movie and a series item
/// read whole, the users, and — from the live check of Jellyfin's Identify (2026-10-06) — its search results and Apply
/// answers, the three items read again after they were identified, and an episode Jellyfin lists as a film. MAM-35 adds the
/// sampled movies, series and episodes read again with their primary image's tag (2026-10-07), and the image route's answers.
/// </summary>
/// <remarks>
/// Every call returns fresh nodes, parsed anew, so a test can change what it got (a path, a media
/// source's protocol, a provider id) without touching another test's copy.
/// </remarks>
public static class JellyfinSamples
{
    /// <summary>"The Continental" S01E01: an episode in the movie library, in a season folder inside
    /// the <c>John Wick. Collection</c> folder, with an IMDb id only.</summary>
    public const string ContinentalId = "0d6e8ae1b5acb7a98486f5dfc099d94c";

    /// <summary>"13-й район" (2004) in the <c>District 13. Dilogy</c> folder: IMDb, TMDb and a TMDb
    /// collection id.</summary>
    public const string District13Id = "e3d063df4b73d12c0d4cd71725e5e3eb";

    /// <summary>"Чужі" (1986), the theatrical cut, in the <c>Alien. Collection</c> folder: IMDb tt0090605,
    /// TMDb 679.</summary>
    public const string AliensTheatricalId = "00a49b2db2200514168431dc028f0a0b";

    /// <summary>"Чужі" (1986), the special edition: a second item with the same ids as the theatrical
    /// cut.</summary>
    public const string AliensSpecialEditionId = "436a68097ee85c654c2e696bcf2fa8d6";

    /// <summary>The library "Фільми", rooted at <c>/media/movies</c>, which holds all four movies.</summary>
    public const string MoviesLocationId = "78ee018b5034640ba414833f0fe9d6b3";

    /// <summary>The total <c>movies-page.json</c> reports: the reference library's 870 movies, not the
    /// four in the file.</summary>
    public const int SampleTotalRecordCount = 870;

    /// <summary>The library "Мульфільми" (mixed), rooted at <c>/media/cartoons</c>.</summary>
    public const string CartoonsLocationId = "2e7d31f7c627f72ad0912eff1c00353d";

    /// <summary>The library "Документалки" (mixed), rooted at <c>/media/documentary</c>.</summary>
    public const string DocumentaryLocationId = "dea595f0747f8862151db0192e565150";

    /// <summary>The library "Серіали" (tvshows), rooted at <c>/media/tv-shows</c>.</summary>
    public const string TvShowsLocationId = "f97af1337db58125aaa9a3f3695ab163";

    /// <summary>Yellowstone (2018) in the TV library: IMDb tt4236770, TMDb 73586, TVDB 341164.</summary>
    public const string YellowstoneId = "bad3e58c7b81156c7bccd6235599accd";

    /// <summary>How the Universe Works (2010) in the documentary library: IMDb, TMDb and TVDB ids.</summary>
    public const string HowTheUniverseWorksId = "d0d8035e1431be7c3631b3cf6e1e266b";

    /// <summary>Mickey Mouse Clubhouse (2006) in the cartoon library: IMDb tt0784896, TMDb 3934, TVDB
    /// 79854.</summary>
    public const string MickeyMouseClubhouseId = "5d35cbe1d774979d932ee85791c466db";

    /// <summary>Clarkson's Farm (2021) in the TV library: IMDb, TMDb and TVDB ids.</summary>
    public const string ClarksonsFarmId = "d35b5d642dc60a0898a2a54bee8d9f0b";

    /// <summary>The Penguins of Madagascar: a series Jellyfin named after its folder and gave no ids.</summary>
    public const string PenguinsId = "9ac821b3ddea72fd14ba1835b4153e50";

    /// <summary>Yellowstone S01E01 "Daybreak": an ordinary episode, IMDb tt7529124 and TVDB 6507316 of its
    /// own.</summary>
    public const string YellowstoneS01E01Id = "9dce0fa40765e5104b1c0a123a281280";

    /// <summary>Tom &amp; Jerry Kids: one episode item (season 1, episode 4) holding the 65 files of the
    /// folder, with no series and no ids.</summary>
    public const string TomAndJerryKidsId = "4f28ab262a69cb4a9eb85ea904a6dc5d";

    /// <summary>How the Universe Works S01E01 "Big Bang": a documentary episode in a mixed library, IMDb
    /// only.</summary>
    public const string BigBangId = "9bfab0cb16adc69d065ea37bd803c52a";

    /// <summary>Mickey Mouse Clubhouse S03E09, the a half: same numbers and ids as the b half.</summary>
    public const string RoadRallyAId = "8d461b4546ae6a601e34e85ff4e4a27b";

    /// <summary>Mickey Mouse Clubhouse S03E09, the b half.</summary>
    public const string RoadRallyBId = "ec5d83bbc45a035bc8cde248be189cb8";

    /// <summary>Mickey Mouse Clubhouse S00E01 "Follow the Mouse": a virtual special, its one media source
    /// a placeholder without a path.</summary>
    public const string FollowTheMouseId = "1728f2c0514906e2c522eb130d401193";

    /// <summary>The totals the TV samples report: the reference library's 10 series items, 25 seasons and
    /// 471 episodes.</summary>
    public const int SampleSeriesTotalRecordCount = 10;

    /// <inheritdoc cref="SampleSeriesTotalRecordCount"/>
    public const int SampleSeasonsTotalRecordCount = 25;

    /// <inheritdoc cref="SampleSeriesTotalRecordCount"/>
    public const int SampleEpisodesTotalRecordCount = 471;

    /// <summary>MAM-34 — "120. Landing Stripling" (1962), a Tom &amp; Jerry short in the cartoon library, read whole by
    /// <c>GET /Items/{id}?userId=</c> (<c>item.json</c>): named after its file, no ids, before its Identify.</summary>
    public const string LandingStriplingId = "750eaa45b0836d08e4ae4b35750b93ca";

    /// <summary>MAM-34 — the TMDb film the live check identified <see cref="LandingStriplingId"/> as.</summary>
    public const string LandingStriplingTmdbId = "223021";

    /// <summary>MAM-34 — the IMDb id TMDb gives for it.</summary>
    public const string LandingStriplingImdbId = "tt0056164";

    /// <summary>MAM-34 — the TMDb show the live check identified the Penguins series item (<see cref="PenguinsId"/>,
    /// <c>series-item.json</c>) as, under Jellyfin's <c>Tmdb</c> key.</summary>
    public const string PenguinsTmdbTvId = "7869";

    /// <summary>MAM-34 — the enabled administrator of <c>users.json</c>, whom an item is read as.</summary>
    public const string AdministratorUserId = "06119359f9364accb7fb3ea32e531f1d";

    /// <summary>MAM-34 — the other user of <c>users.json</c>, enabled and no administrator.</summary>
    public const string MemberUserId = "692951d9e4c24fc0a9691ca9e02c6324";

    /// <summary>MAM-34 — what <c>GET /Items/{id}?userId=</c> answered an item that doesn't exist: 404 problem
    /// details.</summary>
    public const string ItemReadUnknownItemResponseFile = "Jellyfin/item-read-unknown-item-response.txt";

    /// <summary>MAM-34 — what <c>GET /Items/{id}</c> answered the API key without a user: 400, "Error processing
    /// request."</summary>
    public const string ItemReadWithoutUserResponseFile = "Jellyfin/item-read-without-user-response.txt";

    /// <summary>MAM-34 — "Greatest Events of World War II in Colour" S01E01, which Jellyfin lists as a film: a Movie item in
    /// the documentary library holding the episode's IMDb id only (<see cref="BlitzkriegImdbId"/>). The live check
    /// identified it through the Open Movie Database.</summary>
    public const string BlitzkriegId = "0329939301cb93b1c47eea6efe4975c2";

    /// <summary>MAM-34 — the episode's own IMDb id, the one an episode listed as a film is identified by.</summary>
    public const string BlitzkriegImdbId = "tt9103946";

    /// <summary>MAM-34 — the name TMDb gives <see cref="LandingStriplingId"/> (the item is named after its file until it is
    /// identified).</summary>
    public const string LandingStriplingName = "Landing Stripling";

    /// <summary>MAM-34 — the root of the library "Фільми" (language <c>uk</c>, country <c>UA</c>).</summary>
    public const string MoviesRoot = "/media/movies";

    /// <summary>MAM-34 — the root of the library "Мульфільми" (no language).</summary>
    public const string CartoonsRoot = "/media/cartoons";

    /// <summary>MAM-34 — the root of the library "Документалки" (no language).</summary>
    public const string DocumentaryRoot = "/media/documentary";

    /// <summary>MAM-34 — the root of the library "Серіали" (language <c>en</c>, no country).</summary>
    public const string TvShowsRoot = "/media/tv-shows";

    /// <summary>MAM-34 — the root of the library "Колекції", a box-set library that saves beside the media: the one library
    /// of the reference installation an Identify is refused in.</summary>
    public const string CollectionsRoot = "/config/data/collections";

    /// <summary>MAM-34 — what <c>POST /Items/RemoteSearch/Apply/{id}</c> answered the film's match (recorded live on
    /// 2026-10-06): 204, no body, after 1.8 s.</summary>
    public const string ItemRemoteSearchApplyResponseFile = "Jellyfin/item-remote-search-apply-response.txt";

    /// <summary>MAM-34 — what the same call answered the Penguins series' match: 204 after 159.3 s.</summary>
    public const string SeriesRemoteSearchApplyResponseFile = "Jellyfin/series-remote-search-apply-response.txt";

    /// <summary>MAM-34 — what the same call answered the episode listed as a film, matched through the Open Movie Database:
    /// 204 after 21.3 s.</summary>
    public const string EpisodeMovieRemoteSearchApplyResponseFile = "Jellyfin/episode-movie-remote-search-apply-response.txt";

    /// <summary>MAM-34 — what the same call answered an item that doesn't exist: 404 problem details (the all-zero id is 400,
    /// so the recording used another).</summary>
    public const string RemoteSearchApplyUnknownItemResponseFile = "Jellyfin/remote-search-apply-unknown-item-response.txt";

    /// <summary>MAM-34 — what the same call answered a wrong key: 401, no body.</summary>
    public const string RemoteSearchApplyWrongKeyResponseFile = "Jellyfin/remote-search-apply-wrong-key-response.txt";

    /// <summary>MAM-34 — what <c>POST /Items/RemoteSearch/Movie</c> answered a wrong key: 401, no body, in 0.03 s.</summary>
    public const string RemoteSearchWrongKeyResponseFile = "Jellyfin/remote-search-wrong-key-response.txt";

    /// <summary>MAM-35 — the tag of Yellowstone's (<see cref="YellowstoneId"/>) primary image, the poster
    /// <c>image-primary-response.txt</c> was recorded for.</summary>
    public const string YellowstonePosterTag = "7060953c5bc967a2e947ecc514b77a37";

    /// <summary>
    /// MAM-35 — what <c>GET /Items/{id}/Images/Primary?tag=…&amp;format=Jpg&amp;maxWidth=342&amp;quality=90</c> answered for
    /// Yellowstone without a key (recorded live on 2026-10-07): 200, <c>image/jpeg</c>, 34,972 bytes, an immutable year-long
    /// <c>Cache-Control</c> and the asked tag as its <c>ETag</c>. The headers only: the poster's bytes are not kept, so a test
    /// answers the test JPEG under them (<see cref="FakeJellyfinServer.RecordedPoster"/>).
    /// </summary>
    public const string ImagePrimaryResponseFile = "Jellyfin/image-primary-response.txt";

    /// <summary>MAM-35 — what the same route answered for an item without a primary image (the virtual special
    /// <see cref="FollowTheMouseId"/>): 404, <c>application/json</c>, the JSON string "Follow the Mouse does not have an
    /// image of type Primary".</summary>
    public const string ImagePrimaryMissingResponseFile = "Jellyfin/image-primary-missing-response.txt";

    /// <summary>MAM-35 — what the same route answered for an id Jellyfin doesn't know (the all-zero one): 400,
    /// <c>text/plain</c>, "Error processing request."</summary>
    public const string ImagePrimaryUnknownItemResponseFile = "Jellyfin/image-primary-unknown-item-response.txt";

    /// <summary>MAM-35 — the whole answer of <c>movies-page-with-images.json</c>, <c>series-page-with-images.json</c> or
    /// <c>episodes-page-with-images.json</c> as recorded, for a test that answers the adapter with it byte for byte.</summary>
    public const string MoviesWithImagesPageFile = "Jellyfin/movies-page-with-images.json";

    /// <inheritdoc cref="MoviesWithImagesPageFile"/>
    public const string SeriesWithImagesPageFile = "Jellyfin/series-page-with-images.json";

    /// <inheritdoc cref="MoviesWithImagesPageFile"/>
    public const string EpisodesWithImagesPageFile = "Jellyfin/episodes-page-with-images.json";

    private static readonly Lazy<string> MoviesPageText = new(() => Read("movies-page.json"));
    private static readonly Lazy<string> SeriesPageText = new(() => Read("series-page.json"));
    private static readonly Lazy<string> SeasonsPageText = new(() => Read("seasons-page.json"));
    private static readonly Lazy<string> EpisodesPageText = new(() => Read("episodes-page.json"));
    private static readonly Lazy<string> SystemInfoText = new(() => Read("system-info.json"));
    private static readonly Lazy<string> VirtualFoldersText = new(() => Read("virtual-folders.json"));
    private static readonly Lazy<string> UsersText = new(() => Read("users.json"));
    private static readonly Lazy<string> ItemText = new(() => Read("item.json"));
    private static readonly Lazy<string> ItemAfterIdentifyText = new(() => Read("item-after-identify.json"));
    private static readonly Lazy<string> SeriesItemText = new(() => Read("series-item.json"));
    private static readonly Lazy<string> SeriesItemAfterIdentifyText = new(() => Read("series-item-after-identify.json"));
    private static readonly Lazy<string> EpisodeMovieAfterIdentifyText = new(() => Read("episode-movie-after-identify.json"));
    private static readonly Lazy<string> MoviesWithImagesPageText = new(() => Read("movies-page-with-images.json"));
    private static readonly Lazy<string> SeriesWithImagesPageText = new(() => Read("series-page-with-images.json"));
    private static readonly Lazy<string> EpisodesWithImagesPageText = new(() => Read("episodes-page-with-images.json"));

    /// <summary>The answer of <c>GET /System/Info</c>: Jellyfin 12.1.0, "VahaC media".</summary>
    public static JsonObject SystemInfo() => JsonNode.Parse(SystemInfoText.Value)!.AsObject();

    /// <summary>The answer of <c>GET /Library/VirtualFolders</c>: five libraries.</summary>
    public static JsonArray VirtualFolders() => JsonNode.Parse(VirtualFoldersText.Value)!.AsArray();

    /// <summary>MAM-34 — the answer of <c>GET /Users</c>, cut to the fields the app reads (name, id, whether
    /// administrator, whether disabled): a member first, then the administrator.</summary>
    public static JsonArray Users() => JsonNode.Parse(UsersText.Value)!.AsArray();

    /// <summary>MAM-34 — <see cref="LandingStriplingId"/> as <c>GET /Items/{id}?userId=</c> answered it: the whole
    /// item, 47 fields, no ids.</summary>
    public static JsonObject Item() => JsonNode.Parse(ItemText.Value)!.AsObject();

    /// <summary>MAM-34 — <see cref="LandingStriplingId"/> read again after Jellyfin's Identify applied TMDb's result
    /// (<c>item-after-identify.json</c>, recorded live on 2026-10-06): TMDb's name, overview, poster and backdrop, a new
    /// Etag, and the ids as the other providers re-added them.</summary>
    public static JsonObject ItemAfterIdentify() => JsonNode.Parse(ItemAfterIdentifyText.Value)!.AsObject();

    /// <summary>MAM-34 — the Penguins series item (<see cref="PenguinsId"/>) as <c>GET /Items/{id}?userId=</c> answered
    /// it: 41 fields, no ids, named after its folder, under <see cref="CartoonsRoot"/>. The live check's earlier write and
    /// removal of its TMDb id are recorded beside it (<c>series-item-after-update.json</c>, history).</summary>
    public static JsonObject SeriesItem() => JsonNode.Parse(SeriesItemText.Value)!.AsObject();

    /// <summary>MAM-34 — the Penguins series item read again after its Identify (<c>series-item-after-identify.json</c>):
    /// TMDb's overview, all five images with new tags, a new Etag, its 149 episodes untouched.</summary>
    public static JsonObject SeriesItemAfterIdentify() => JsonNode.Parse(SeriesItemAfterIdentifyText.Value)!.AsObject();

    /// <summary>MAM-34 — <see cref="BlitzkriegId"/> read again after its Identify through the Open Movie Database
    /// (<c>episode-movie-after-identify.json</c>): the name and year corrected, the primary image replaced, the overview and
    /// the ids kept.</summary>
    public static JsonObject EpisodeMovieAfterIdentify() => JsonNode.Parse(EpisodeMovieAfterIdentifyText.Value)!.AsObject();

    /// <summary>MAM-34 — the film search by <c>Tmdb</c> 223021 as the live check recorded it: one row, "Landing
    /// Stripling".</summary>
    public static JsonArray RemoteSearchMovieByTmdbRows() => RecordedJson("Jellyfin/remote-search-movie-by-tmdb-response.txt");

    /// <summary>MAM-34 — the series search by <c>Tmdb</c> 7869: one row, "The Penguins of Madagascar", with IMDb and TVDB
    /// ids.</summary>
    public static JsonArray RemoteSearchSeriesByTmdbRows() => RecordedJson("Jellyfin/remote-search-series-by-tmdb-response.txt");

    /// <summary>MAM-34 — the film search by <c>Imdb</c> tt9103946 through the Open Movie Database: one row, "Blitzkrieg",
    /// with an IMDb id and no overview.</summary>
    public static JsonArray RemoteSearchOmdbRows() => RecordedJson("Jellyfin/remote-search-movie-by-imdb-omdb-response.txt");

    /// <summary>MAM-34 — the film search by the TV id 7869: the unrelated film "The Fest" the live check saw (the namespace
    /// trap).</summary>
    public static JsonArray RemoteSearchMovieByTvIdRows() => RecordedJson("Jellyfin/remote-search-movie-by-tv-id-response.txt");

    /// <summary>
    /// MAM-34 — an episode Jellyfin lists as a film (<see cref="BlitzkriegId"/>) as it was before its Identify: named after
    /// its file, year 2025, the episode's IMDb id only, in the documentary library, with the overview an earlier lookup had
    /// filled in (Identify through the Open Movie Database leaves it, <c>episode-movie-after-identify.json</c>).
    /// </summary>
    public static JsonObject EpisodeMovieItem()
    {
        var item = Item();
        var path = $"{DocumentaryRoot}/Greatest Events of World War II in Colour S01 2019 WEB-DL 1080p/Greatest Events of World War II in Colour.S01E01.Blitzkrieg 2019 WEB-DL 1080p.mkv";
        item["Id"] = BlitzkriegId;
        item["Name"] = "Greatest Events of World War II in Colour.S01E01.Blitzkrieg";
        item["SortName"] = "greatest events of world war ii in colour.s01e01.blitzkrieg";
        item["ProductionYear"] = 2025;
        item["ProviderIds"] = new JsonObject { ["Imdb"] = BlitzkriegImdbId };
        item["Path"] = path;
        item["MediaSources"]![0]!["Id"] = BlitzkriegId;
        item["MediaSources"]![0]!["Path"] = path;
        item["Overview"] = EpisodeMovieAfterIdentify()["Overview"]!.DeepClone();
        return item;
    }

    /// <summary>The four movies of <c>movies-page.json</c>, in its order: The Continental, 13-й район,
    /// then the two "Чужі".</summary>
    public static List<JsonObject> Movies()
    {
        var items = JsonNode.Parse(MoviesPageText.Value)!["Items"]!.AsArray();
        var movies = items.Select(item => item!.AsObject()).ToList();
        // Detached from the parsed page, so each can go into another answer.
        items.Clear();
        return movies;
    }

    /// <summary>The sample movie <paramref name="id"/>.</summary>
    public static JsonObject Movie(string id) => Movies().Single(movie => string.Equals((string?)movie["Id"], id, StringComparison.Ordinal));

    /// <summary>The five series of <c>series-page.json</c>, in its order (MAM-32): Yellowstone, How the
    /// Universe Works, Mickey Mouse Clubhouse, Clarkson's Farm, The Penguins of Madagascar.</summary>
    public static List<JsonObject> Series() => ItemsOf(SeriesPageText.Value);

    /// <summary>The sample series <paramref name="id"/>.</summary>
    public static JsonObject Series(string id) => Series().Single(series => string.Equals((string?)series["Id"], id, StringComparison.Ordinal));

    /// <summary>The four seasons of <c>seasons-page.json</c>, in its order (MAM-32): Yellowstone's season
    /// 1, How the Universe Works' season 1, Mickey Mouse Clubhouse's season 3 and its virtual
    /// specials.</summary>
    public static List<JsonObject> Seasons() => ItemsOf(SeasonsPageText.Value);

    /// <summary>The six episodes of <c>episodes-page.json</c>, in its order (MAM-32): Yellowstone S01E01,
    /// the Tom &amp; Jerry Kids item, How the Universe Works S01E01, the two halves of Mickey Mouse
    /// Clubhouse S03E09, and the virtual special.</summary>
    public static List<JsonObject> Episodes() => ItemsOf(EpisodesPageText.Value);

    /// <summary>The sample episode <paramref name="id"/>.</summary>
    public static JsonObject Episode(string id) => Episodes().Single(episode => string.Equals((string?)episode["Id"], id, StringComparison.Ordinal));

    /// <summary>
    /// MAM-35 — the four movies of <c>movies-page.json</c> read again on 2026-10-07 with the sync's query that asks for the
    /// primary image's tag (<c>movies-page-with-images.json</c>), asked for by their ids, in the order Jellyfin listed them:
    /// 13-й район, The Continental, then the two "Чужі", each with its own <c>ImageTags.Primary</c>.
    /// </summary>
    public static List<JsonObject> MoviesWithImages() => ItemsOf(MoviesWithImagesPageText.Value);

    /// <summary>MAM-35 — the five series of <c>series-page.json</c> read the same way (<c>series-page-with-images.json</c>):
    /// every one with a primary image, the Penguins of Madagascar named and pictured as its Identify (MAM-34) left it.</summary>
    public static List<JsonObject> SeriesWithImages() => ItemsOf(SeriesWithImagesPageText.Value);

    /// <summary>
    /// MAM-35 — the six episodes of <c>episodes-page.json</c> read the same way (<c>episodes-page-with-images.json</c>):
    /// each with its own primary image but the virtual special, whose <c>ImageTags</c> is empty; every episode of a series
    /// also carries its series' and season's tags (<c>SeriesPrimaryImageTag</c>, <c>ParentPrimaryImageTag</c>).
    /// </summary>
    public static List<JsonObject> EpisodesWithImages() => ItemsOf(EpisodesWithImagesPageText.Value);

    /// <summary>
    /// A series of its own with these facts (MAM-32): Yellowstone with its id, title, year, provider ids
    /// and path replaced. An id that is <c>null</c> is left out, the way Jellyfin leaves out what it
    /// doesn't know; <paramref name="tmdbId"/> is a show's.
    /// </summary>
    public static JsonObject SeriesWith(
        string id, string title, int year, string path, string? imdbId = null, string? tmdbId = null, string? tvdbId = null)
    {
        var series = Series(YellowstoneId);
        series["Id"] = id;
        series["Name"] = title;
        series["OriginalTitle"] = title;
        series["ProductionYear"] = year;
        series["Path"] = path;
        series["ProviderIds"] = ProviderIds(imdbId, tmdbId, tvdbId);
        return series;
    }

    /// <summary>
    /// An episode of its own with these facts (MAM-32): Yellowstone S01E01 with its id, title, series,
    /// numbers, provider ids and file replaced; a series, a number or an id that is <c>null</c> is left
    /// out, as Jellyfin leaves out what it doesn't know.
    /// </summary>
    public static JsonObject EpisodeWith(
        string id,
        string title,
        string? seriesId,
        int? season,
        int? episode,
        string path,
        string? imdbId = null,
        string? tvdbId = null)
    {
        var item = Episode(YellowstoneS01E01Id);
        item["Id"] = id;
        item["Name"] = title;
        item["Path"] = path;
        item["SeriesId"] = seriesId;
        item["SeasonId"] = null;
        item.Remove("ParentIndexNumber");
        item.Remove("IndexNumber");
        if (season is not null)
            item["ParentIndexNumber"] = season;
        if (episode is not null)
            item["IndexNumber"] = episode;
        item["ProviderIds"] = ProviderIds(imdbId, null, tvdbId);
        item["MediaSources"]![0]!["Id"] = id;
        item["MediaSources"]![0]!["Path"] = path;
        return item;
    }

    /// <summary>
    /// The theatrical "Чужі" listing the special edition's media source as well, labelled by its name:
    /// what Jellyfin 12.1 answers for two versions it merged into one item.
    /// </summary>
    public static JsonObject AliensWithBothSources()
    {
        var theatrical = Movie(AliensTheatricalId);
        theatrical["MediaSources"]!.AsArray().Add(Movie(AliensSpecialEditionId)["MediaSources"]![0]!.DeepClone());
        return theatrical;
    }

    /// <summary>
    /// Both "Чужі" items, each listing both media sources in the same order: how Jellyfin 10.9 to 10.11
    /// answer for two versions they merged.
    /// </summary>
    public static List<JsonObject> AliensEachListingBothSources()
    {
        var theatrical = Movie(AliensTheatricalId);
        var specialEdition = Movie(AliensSpecialEditionId);
        var theatricalSource = theatrical["MediaSources"]![0]!;
        var specialEditionSource = specialEdition["MediaSources"]![0]!;
        theatrical["MediaSources"]!.AsArray().Add(specialEditionSource.DeepClone());
        specialEdition["MediaSources"]!.AsArray().Insert(0, theatricalSource.DeepClone());
        return [theatrical, specialEdition];
    }

    /// <summary>A copy of <paramref name="movie"/> without provider ids: Jellyfin found no metadata for
    /// it.</summary>
    public static JsonObject WithoutProviderIds(JsonObject movie)
    {
        var copy = movie.DeepClone().AsObject();
        copy.Remove("ProviderIds");
        return copy;
    }

    /// <summary>
    /// A copy of <paramref name="movie"/> as another movie, number <paramref name="number"/> (1 or more):
    /// its id, its media sources' ids, its paths and its IMDb and TMDb ids renumbered, so it is another
    /// item with another file and other identifiers. Copies of one number of the two "Чужі" items still
    /// share their identifiers, like the originals.
    /// </summary>
    public static JsonObject Renumbered(JsonObject movie, int number)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(number, 1);

        var copy = movie.DeepClone().AsObject();
        var prefix = number.ToString("x8", CultureInfo.InvariantCulture);
        copy["Id"] = RenumberId(copy["Id"]!.GetValue<string>(), prefix);
        if (copy["Path"] is JsonValue path)
            copy["Path"] = RenumberPath(path.GetValue<string>(), number);

        if (copy["MediaSources"] is JsonArray sources)
        {
            foreach (var source in sources.OfType<JsonObject>())
            {
                if (source["Id"] is JsonValue sourceId)
                    source["Id"] = RenumberId(sourceId.GetValue<string>(), prefix);
                if (source["Path"] is JsonValue sourcePath)
                    source["Path"] = RenumberPath(sourcePath.GetValue<string>(), number);
            }
        }

        // Fixed-width prefixes, so no two numbers can make one id; the forms stay valid (tt and
        // digits, digits only).
        var digits = number.ToString("D6", CultureInfo.InvariantCulture);
        if (copy["ProviderIds"] is JsonObject providerIds)
        {
            if (providerIds["Imdb"] is JsonValue imdb)
                providerIds["Imdb"] = $"tt{digits}{imdb.GetValue<string>()[2..]}";
            if (providerIds["Tmdb"] is JsonValue tmdb)
                providerIds["Tmdb"] = $"{digits}{tmdb.GetValue<string>()}";
        }

        return copy;
    }

    /// <summary>
    /// A library of <paramref name="count"/> movies: renumbered copies of the four samples in turn, the
    /// first four numbered 1, the next four 2, and so on. Every four movies make three works (the two
    /// "Чужі" of a number share their ids), four versions and four files.
    /// </summary>
    public static List<JsonObject> Library(int count)
    {
        var samples = Movies();
        return [.. Enumerable.Range(0, count).Select(index => Renumbered(samples[index % samples.Count], index / samples.Count + 1))];
    }

    /// <summary>
    /// A movie of its own with these facts (MAM-12): "13-й район" with its id, titles, year, provider ids
    /// and path replaced, for the titles and ids the reference library doesn't hold — a search page's
    /// "Інтерстеллар", a remake of another year. An original title or id that is <c>null</c> is left out,
    /// the way Jellyfin leaves out what it doesn't know.
    /// </summary>
    public static JsonObject MovieWith(
        string id, string title, string? originalTitle, int year, string path, string? imdbId = null, string? tmdbId = null)
    {
        var movie = Movie(District13Id);
        movie["Id"] = id;
        movie["Name"] = title;
        movie.Remove("OriginalTitle");
        if (originalTitle is not null)
            movie["OriginalTitle"] = originalTitle;
        movie["ProductionYear"] = year;
        var providerIds = new JsonObject();
        if (imdbId is not null)
            providerIds["Imdb"] = imdbId;
        if (tmdbId is not null)
            providerIds["Tmdb"] = tmdbId;
        movie["ProviderIds"] = providerIds;
        movie["Path"] = path;
        movie["MediaSources"]![0]!["Id"] = id;
        movie["MediaSources"]![0]!["Path"] = path;
        return movie;
    }

    /// <summary>
    /// One further part of a stacked movie as <c>GET /Videos/{id}/AdditionalParts</c> lists it: an item
    /// of its own with the part's path, and its size on its media source.
    /// </summary>
    public static JsonObject Part(string id, string path, long? sizeBytes) => new()
    {
        ["Id"] = id,
        ["Type"] = "Movie",
        ["Path"] = path,
        ["MediaSources"] = new JsonArray(new JsonObject
        {
            ["Protocol"] = "File",
            ["Id"] = id,
            ["Path"] = path,
            ["Size"] = sizeBytes,
            ["IsRemote"] = false,
            ["VideoType"] = "VideoFile",
        }),
    };

    private static string Read(string fileName) =>
        File.ReadAllText(Path.Combine(RepositoryPaths.VendorSamples, "Jellyfin", fileName));

    // A recorded answer is its status line and headers, a blank line, then the body, which here is a JSON array.
    private static JsonArray RecordedJson(string relativePath)
    {
        var lines = File.ReadAllLines(Path.Combine(RepositoryPaths.VendorSamples, relativePath));
        var blank = Array.FindIndex(lines, line => line.Length == 0);
        return JsonNode.Parse(string.Join('\n', lines[(blank + 1)..]))!.AsArray();
    }

    // Detached from the parsed page, so each can go into another answer.
    private static List<JsonObject> ItemsOf(string pageText)
    {
        var items = JsonNode.Parse(pageText)!["Items"]!.AsArray();
        var list = items.Select(item => item!.AsObject()).ToList();
        items.Clear();
        return list;
    }

    private static JsonObject ProviderIds(string? imdbId, string? tmdbId, string? tvdbId)
    {
        var providerIds = new JsonObject();
        if (imdbId is not null)
            providerIds["Imdb"] = imdbId;
        if (tmdbId is not null)
            providerIds["Tmdb"] = tmdbId;
        if (tvdbId is not null)
            providerIds["Tvdb"] = tvdbId;
        return providerIds;
    }

    private static string RenumberId(string id, string prefix) => id.Length > prefix.Length ? prefix + id[prefix.Length..] : prefix + id;

    // "…/Aliens (1986) … [Hurtom].mkv" becomes "…/Aliens (1986) … [Hurtom] #7.mkv".
    private static string RenumberPath(string path, int number)
    {
        var name = path.LastIndexOf('/') + 1;
        var extension = path.LastIndexOf('.');
        var at = extension > name ? extension : path.Length;
        return string.Create(CultureInfo.InvariantCulture, $"{path[..at]} #{number}{path[at..]}");
    }
}
