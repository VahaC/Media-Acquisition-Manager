using System.Globalization;
using System.IO.Pipelines;
using System.Net;
using System.Text.Json.Nodes;
using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Abstractions.Library;
using MediaAcquisitionManager.Core.Abstractions.Media;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Infrastructure.Integrations.Jellyfin;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;

namespace MediaAcquisitionManager.Infrastructure.Tests.Integrations;

// MAM-8 — Jellyfin's movies as the media library contract reads them: the real answer of the reference
// library (VendorSamples/Jellyfin/movies-page.json) and variants synthesized from it (JellyfinSamples),
// every field read by name, and every failure a status and a sentence with the key masked.
public sealed class JellyfinAdapterItemsTests : IDisposable
{
    private const string BaseUrl = "http://jellyfin.test:8096";
    private const string ApiKey = "4f1d0b9c2e7a4c3b8d6e5f7a9b0c1d2e";

    private const string NotJellyfinError =
        "The service at this address answered, but not the way Jellyfin does. Check the address.";

    private const string MovieWithoutIdError =
        "The service at this address listed a movie without an id, which Jellyfin never does. Check the address.";

    private const string ContinentalPath =
        "/media/movies/John Wick. Collection/[01] The Continental (2023) - (Season 1)/[01] The Continental (2023) - S01E01 - BDRip 1080p x265 [3xUKR_ENG] [Hurtom].mkv";

    private const string District13Path =
        "/media/movies/District 13. Dilogy (2004-2009) BDRip 1080p H.265 [2xUKR_FRE] [Hurtom]/[01] Banlieue 13 (2004) BDRip 1080p H.265 [2xUKR_FRE] [Hurtom].mkv";

    private const string AliensTheatricalPath =
        "/media/movies/Alien. Collection/[02.1] Aliens (1986) [Theatrical Cut, Fundamental Collection] BDRip 1080p H.265 [2xUKR_ENG] [Hurtom].mkv";

    private const string AliensSpecialEditionPath =
        "/media/movies/Alien. Collection/[02.2] Aliens (1986) [Special Edition] BDRip 1080p H.265 [2xUKR_ENG] [Hurtom, UTOPIA].mkv";

    // MAM-43 — a Blu-ray remux as production's Фільми holds it: a folder with BDMV inside.
    private const string DirtyDozenFolder = "/media/movies/DIRTY_DOZEN_1967_RMX";

    private static readonly IntegrationConnectionSettings Settings = new(null, BaseUrl, null, null, ApiKey);

    private static readonly LibraryPageRequest FirstPage = new(MediaWorkKind.Movie, 0, 50);

    private readonly IntegrationAdapterHarness _harness = new();
    private readonly FakeJellyfinServer _server = new(BaseUrl);

    private IMediaLibraryProvider Library => _harness.Adapter<IMediaLibraryProvider>(JellyfinAdapter.TypeId);

    /// <summary>A media source that isn't a local video file: each change of the theatrical cut's
    /// source, as a property and its JSON value (<c>null</c> removes the property).</summary>
    public static TheoryData<string, string?> SourcesWithoutAFile => new()
    {
        { "Protocol", "\"Http\"" },
        { "Protocol", "7" },
        { "IsRemote", "true" },
        // A DVD or Blu-ray folder: its path names a folder, not a file.
        { "VideoType", "\"Dvd\"" },
        { "VideoType", "\"BluRay\"" },
        { "VideoType", "4" },
        // A .strm pointer, as 12.1 reports it (10.9 reports it as remote).
        { "Path", "\"/media/movies/Alien. Collection/Aliens (1986).strm\"" },
        { "Path", "\"/media/movies/Alien. Collection/ALIENS (1986).STRM\"" },
        { "Path", "\"  \"" },
        { "Path", "\"/media/movies/Alien. Collection/Ali\\u0007ens (1986).mkv\"" },
        { "Path", "\"/media/movies/Alien. Collection/Aliens (1986).mkv\\n\"" },
        { "Path", $"\"/media/movies/{new string('x', 4097 - "/media/movies/".Length)}\"" },
        { "Path", "12" },
        { "Path", "null" },
        { "Path", null },
    };

    public void Dispose() => _harness.Dispose();

    [Fact]
    public void Capabilities_say_one_item_can_hold_several_versions()
    {
        //Arrange
        var library = Library;

        //Act
        var capabilities = library.Capabilities;

        //Assert
        Assert.True(capabilities.GroupsVersionsUnderOneItem);
    }

    [Fact]
    public async Task ListItems_reads_the_four_sample_movies_and_the_total_of_the_library()
    {
        //Arrange
        _harness.JellyfinHttp.Handler = (_, _) => VendorResponses.Sample("Jellyfin/movies-page.json", "application/json; charset=utf-8");

        //Act
        var result = await Library.ListItemsAsync(Settings, FirstPage, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        Assert.Null(result.Error);
        var page = result.Value!;
        // The library's 870, not the four in the file: the total is what the sync pages to.
        Assert.Equal(870, page.TotalCount);
        Assert.Equal(
            [JellyfinSamples.ContinentalId, JellyfinSamples.District13Id, JellyfinSamples.AliensTheatricalId, JellyfinSamples.AliensSpecialEditionId],
            page.Items.Select(item => item.SourceItemId));
        Assert.All(page.Items, item => Assert.Equal(MediaWorkKind.Movie, item.Kind));
    }

    [Fact]
    public async Task ListItems_reads_the_displayed_and_the_original_title_with_the_year()
    {
        //Arrange
        _harness.JellyfinHttp.Handler = (_, _) => VendorResponses.Sample("Jellyfin/movies-page.json", "application/json; charset=utf-8");

        //Act
        var result = await Library.ListItemsAsync(Settings, FirstPage, TestContext.Current.CancellationToken);

        //Assert
        var items = result.Value!.Items.ToDictionary(item => item.SourceItemId);
        Assert.Equal(
            [new ReportedTitle("Чужі", MediaTitleKind.Display, 1986), new ReportedTitle("Aliens", MediaTitleKind.Original, 1986)],
            items[JellyfinSamples.AliensTheatricalId].Titles);
        Assert.Equal(
            [new ReportedTitle("13-й район", MediaTitleKind.Display, 2004), new ReportedTitle("Banlieue 13", MediaTitleKind.Original, 2004)],
            items[JellyfinSamples.District13Id].Titles);
        // Jellyfin gives the episode no original title, and none is made up.
        Assert.Equal([new ReportedTitle("The Continental", MediaTitleKind.Display, 2023)], items[JellyfinSamples.ContinentalId].Titles);
    }

    [Fact]
    public async Task ListItems_reads_the_imdb_and_tmdb_ids_and_never_a_collection_id()
    {
        //Arrange
        _harness.JellyfinHttp.Handler = (_, _) => VendorResponses.Sample("Jellyfin/movies-page.json", "application/json; charset=utf-8");

        //Act
        var result = await Library.ListItemsAsync(Settings, FirstPage, TestContext.Current.CancellationToken);

        //Assert
        // ADR 0006: TmdbCollection names a box set; as a work id it would make a collection one work.
        Assert.Equal(
            [
                "0d6e8ae1b5acb7a98486f5dfc099d94c: Imdb tt29669983",
                "e3d063df4b73d12c0d4cd71725e5e3eb: Imdb tt0414852, Tmdb 10045",
                "00a49b2db2200514168431dc028f0a0b: Imdb tt0090605, Tmdb 679",
                "436a68097ee85c654c2e696bcf2fa8d6: Imdb tt0090605, Tmdb 679",
            ],
            result.Value!.Items.Select(item => $"{item.SourceItemId}: {string.Join(", ", item.Identifiers.Select(id => $"{id.Scheme} {id.Value}"))}"));
    }

    [Fact]
    public async Task ListItems_reads_each_media_source_as_a_version_with_its_path_as_sent_and_its_size()
    {
        //Arrange
        _harness.JellyfinHttp.Handler = (_, _) => VendorResponses.Sample("Jellyfin/movies-page.json", "application/json; charset=utf-8");

        //Act
        var result = await Library.ListItemsAsync(Settings, FirstPage, TestContext.Current.CancellationToken);

        //Assert
        // One source each, so no edition label: alone, a source's name is only its file name. The
        // collection folders stay in the paths exactly as Jellyfin sees them.
        Assert.Equal(
            [
                $"{JellyfinSamples.ContinentalId}: {JellyfinSamples.ContinentalId} [] {ContinentalPath} 3891719630",
                $"{JellyfinSamples.District13Id}: {JellyfinSamples.District13Id} [] {District13Path} 4670197619",
                $"{JellyfinSamples.AliensTheatricalId}: {JellyfinSamples.AliensTheatricalId} [] {AliensTheatricalPath} 14845708473",
                $"{JellyfinSamples.AliensSpecialEditionId}: {JellyfinSamples.AliensSpecialEditionId} [] {AliensSpecialEditionPath} 19620073340",
            ],
            result.Value!.Items.SelectMany(item => DescribeVersions(item).Select(version => $"{item.SourceItemId}: {version}")));
    }

    [Fact]
    public async Task ListItems_labels_the_versions_of_a_movie_with_two_media_sources_by_their_names()
    {
        //Arrange
        ArmMovies(JellyfinSamples.AliensWithBothSources());

        //Act
        var result = await Library.ListItemsAsync(Settings, FirstPage, TestContext.Current.CancellationToken);

        //Assert
        // What Jellyfin's version picker shows for each.
        var item = Assert.Single(result.Value!.Items);
        Assert.Equal(
            [
                $"{JellyfinSamples.AliensTheatricalId} [[02.1] Aliens (1986) [Theatrical Cut, Fundamental Collection] BDRip 1080p H.265 [2xUKR_ENG] [Hurtom]] {AliensTheatricalPath} 14845708473",
                $"{JellyfinSamples.AliensSpecialEditionId} [[02.2] Aliens (1986) [Special Edition] BDRip 1080p H.265 [2xUKR_ENG] [Hurtom, UTOPIA]] {AliensSpecialEditionPath} 19620073340",
            ],
            DescribeVersions(item));
    }

    [Fact]
    public async Task ListItems_reads_two_items_that_each_list_both_sources_as_they_come()
    {
        //Arrange
        // Jellyfin 10.9 to 10.11: which item owns which source is the core's decision
        // (LibraryVersionOwnership), not the adapter's.
        ArmMovies([.. JellyfinSamples.AliensEachListingBothSources()]);

        //Act
        var result = await Library.ListItemsAsync(Settings, FirstPage, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(
            [
                $"{JellyfinSamples.AliensTheatricalId}: {JellyfinSamples.AliensTheatricalId} {JellyfinSamples.AliensSpecialEditionId}",
                $"{JellyfinSamples.AliensSpecialEditionId}: {JellyfinSamples.AliensTheatricalId} {JellyfinSamples.AliensSpecialEditionId}",
            ],
            result.Value!.Items.Select(item => $"{item.SourceItemId}: {string.Join(' ', item.Versions.Select(version => version.SourceVersionId))}"));
    }

    [Fact]
    public async Task ListItems_keeps_the_first_of_two_media_sources_with_one_id()
    {
        //Arrange
        var movie = JellyfinSamples.AliensWithBothSources();
        movie["MediaSources"]![1]!["Id"] = JellyfinSamples.AliensTheatricalId;
        ArmMovies(movie);

        //Act
        var result = await Library.ListItemsAsync(Settings, FirstPage, TestContext.Current.CancellationToken);

        //Assert
        var version = Assert.Single(Assert.Single(result.Value!.Items).Versions);
        Assert.Equal(AliensTheatricalPath, Assert.Single(version.Files).ServicePath);
        // Alone after all: no label.
        Assert.Null(version.EditionLabel);
    }

    [Fact]
    public async Task ListItems_asks_for_one_page_of_every_movie_with_the_key_in_the_media_browser_header()
    {
        //Arrange
        _server.Arm(_harness.JellyfinHttp);

        //Act
        await Library.ListItemsAsync(Settings, new LibraryPageRequest(MediaWorkKind.Movie, 49, 50), TestContext.Current.CancellationToken);

        //Assert
        var request = Assert.Single(_harness.JellyfinHttp.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("http://jellyfin.test:8096/Items", request.Uri.GetLeftPart(UriPartial.Path));
        // collapseBoxSetItems=false above all: without it Jellyfin answers the movies of a collection
        // as one box set (670 items instead of 870, VendorSamples README).
        Assert.Equal(
            [
                "recursive=true",
                "includeItemTypes=Movie",
                "collapseBoxSetItems=false",
                "fields=Path,MediaSources,ProviderIds,OriginalTitle",
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
        Assert.Equal([$"MediaBrowser Token=\"{ApiKey}\""], request.HeaderValues("Authorization"));
        Assert.Equal(["application/json"], request.HeaderValues("Accept"));
        Assert.Empty(request.HeaderValues("X-Emby-Token"));
        Assert.DoesNotContain(ApiKey, request.Uri.OriginalString, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ListItems_keeps_a_base_path_of_the_address()
    {
        //Arrange
        var settings = Settings with { BaseUrl = "http://jellyfin.test/jellyfin" };
        new FakeJellyfinServer(settings.BaseUrl).Arm(_harness.JellyfinHttp);

        //Act
        var result = await Library.ListItemsAsync(settings, FirstPage, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        Assert.Equal(4, result.Value!.Items.Count);
        Assert.Equal("http://jellyfin.test/jellyfin/Items", Assert.Single(_harness.JellyfinHttp.Requests).Uri.GetLeftPart(UriPartial.Path));
    }

    [Theory]
    [InlineData(0, 2, new[] { JellyfinSamples.ContinentalId, JellyfinSamples.District13Id })]
    [InlineData(1, 2, new[] { JellyfinSamples.District13Id, JellyfinSamples.AliensTheatricalId })]
    [InlineData(2, 50, new[] { JellyfinSamples.AliensTheatricalId, JellyfinSamples.AliensSpecialEditionId })]
    [InlineData(4, 50, new string[0])]
    public async Task ListItems_answers_the_page_asked_for_and_the_total(int startIndex, int limit, string[] expectedIds)
    {
        //Arrange
        _server.Arm(_harness.JellyfinHttp);

        //Act
        var result = await Library.ListItemsAsync(Settings, new LibraryPageRequest(MediaWorkKind.Movie, startIndex, limit), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        Assert.Equal(expectedIds, result.Value!.Items.Select(item => item.SourceItemId));
        Assert.Equal(4, result.Value.TotalCount);
    }

    [Fact]
    public async Task ListItems_pages_through_a_library_of_120_synthesized_movies()
    {
        //Arrange
        _server.Movies.Clear();
        _server.Movies.AddRange(JellyfinSamples.Library(120));
        _server.Arm(_harness.JellyfinHttp);
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        // Overlapping by one, the way a sync reads (ADR 0008).
        var pages = new List<LibraryItemPage>();
        foreach (var startIndex in (int[])[0, 49, 98])
            pages.Add((await Library.ListItemsAsync(Settings, new LibraryPageRequest(MediaWorkKind.Movie, startIndex, 50), cancellationToken)).Value!);

        //Assert
        Assert.Equal([50, 50, 22], pages.Select(page => page.Items.Count));
        Assert.All(pages, page => Assert.Equal(120, page.TotalCount));
        Assert.Equal(pages[0].Items[^1].SourceItemId, pages[1].Items[0].SourceItemId);
        Assert.Equal(pages[1].Items[^1].SourceItemId, pages[2].Items[0].SourceItemId);
        var items = pages.SelectMany(page => page.Items).DistinctBy(item => item.SourceItemId).ToList();
        // Every copy another item with another file and well-formed ids of its own, like the sample it
        // was made from: four items make three works (the two "Чужі" of a number share their ids).
        Assert.Equal(120, items.Count);
        Assert.Equal(120, items.SelectMany(item => item.Versions).SelectMany(version => version.Files).Select(file => file.ServicePath).Distinct().Count());
        Assert.Equal(120, items.SelectMany(item => item.Versions).Select(version => version.SourceVersionId).Distinct().Count());
        Assert.Equal(90, items.Select(item => string.Join(",", item.Identifiers.Select(id => id.Value))).Distinct().Count());
        Assert.All(items, item => Assert.NotEmpty(item.Identifiers));
    }

    [Fact]
    public async Task ListItems_reads_each_integration_from_its_own_address()
    {
        //Arrange
        // One client serves every connection of the type; each call goes to its own connection's address.
        var first = new FakeJellyfinServer("http://127.0.0.1:9");
        var second = new FakeJellyfinServer("http://127.0.0.2:9");
        second.Movies.RemoveAll(movie => (string?)movie["Id"] != JellyfinSamples.ContinentalId);
        FakeJellyfinServer.Arm(_harness.JellyfinHttp, first, second);
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var fromFirst = await Library.ListItemsAsync(Settings with { BaseUrl = first.BaseUrl }, FirstPage, cancellationToken);
        var fromSecond = await Library.ListItemsAsync(Settings with { BaseUrl = second.BaseUrl }, FirstPage, cancellationToken);

        //Assert
        Assert.Equal(4, fromFirst.Value!.Items.Count);
        Assert.Equal([JellyfinSamples.ContinentalId], fromSecond.Value!.Items.Select(item => item.SourceItemId));
        Assert.Equal(["127.0.0.1", "127.0.0.2"], _harness.JellyfinHttp.Requests.Select(request => request.Uri.Host));
    }

    [Fact]
    public async Task ListItems_of_an_empty_page_is_an_empty_page_with_the_total()
    {
        //Arrange
        ArmBody("""{"Items":[],"TotalRecordCount":870,"StartIndex":900}""");

        //Act
        var result = await Library.ListItemsAsync(Settings, new LibraryPageRequest(MediaWorkKind.Movie, 900, 50), TestContext.Current.CancellationToken);

        //Assert
        // Whether that is the end or a library that shrank is the sync's decision.
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        Assert.Empty(result.Value!.Items);
        Assert.Equal(870, result.Value.TotalCount);
    }

    [Fact]
    public async Task ListItems_leaves_what_jellyfin_leaves_out_unknown()
    {
        //Arrange
        ArmMovies(
            Movie("""{"Id":"a"}"""),
            Movie("""{"Id":"b","Type":null,"Name":"Чужі","OriginalTitle":null,"ProductionYear":null,"ProviderIds":null,"MediaSources":null}"""),
            Movie("""{"Id":"c","Type":"Movie","MediaSources":[{"Path":"/media/movies/c.mkv"}]}"""));

        //Act
        var result = await Library.ListItemsAsync(Settings, FirstPage, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        var items = result.Value!.Items;
        Assert.Equal(["a", "b", "c"], items.Select(item => item.SourceItemId));
        Assert.Empty(items[0].Titles);
        Assert.Empty(items[0].Identifiers);
        Assert.Empty(items[0].Versions);
        Assert.Equal([new ReportedTitle("Чужі", MediaTitleKind.Display, null)], items[1].Titles);
        Assert.Empty(items[1].Identifiers);
        Assert.Empty(items[1].Versions);
        // A source without an id or a size: a version all the same, its file of unknown size.
        Assert.Equal(["(no id) [] /media/movies/c.mkv (unknown)"], DescribeVersions(items[2]));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("10000")]
    [InlineData("-1986")]
    [InlineData("1986.5")]
    [InlineData("\"1986\"")]
    [InlineData("99999999999")]
    public async Task ListItems_leaves_a_year_it_can_not_trust_unknown(string productionYear)
    {
        //Arrange
        ArmMovies(Movie($$"""{"Id":"a","Name":"Чужі","OriginalTitle":"Aliens","ProductionYear":{{productionYear}}}"""));

        //Act
        var result = await Library.ListItemsAsync(Settings, FirstPage, TestContext.Current.CancellationToken);

        //Assert
        Assert.All(Assert.Single(result.Value!.Items).Titles, title => Assert.Null(title.Year));
    }

    [Theory]
    [InlineData("""{"Imdb":"0090605","Tmdb":"tt679"}""")]
    [InlineData("""{"Imdb":"tt1234","Tmdb":""}""")]
    [InlineData("""{"Imdb":"tt00906O5","Tmdb":"679a"}""")]
    [InlineData("""{"Imdb":"nm0000244","Tmdb":"1234567890123"}""")]
    [InlineData("""{"Imdb":90605,"Tmdb":679}""")]
    [InlineData("""{"TmdbCollection":"8091","Tvdb":"81189","Imdb.":"tt0090605"}""")]
    [InlineData("""{"Imdb":"t t0090605","Tmdb":"６７９"}""")]
    [InlineData("""[]""")]
    [InlineData("""null""")]
    public async Task ListItems_ignores_provider_ids_that_are_not_an_imdb_or_a_tmdb_id(string providerIds)
    {
        //Arrange
        ArmMovies(Movie($$"""{"Id":"a","ProviderIds":{{providerIds}}}"""));

        //Act
        var result = await Library.ListItemsAsync(Settings, FirstPage, TestContext.Current.CancellationToken);

        //Assert
        // An id that isn't well formed matches nothing reliably; the item stays a work of its own.
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        Assert.Empty(Assert.Single(result.Value!.Items).Identifiers);
    }

    [Fact]
    public async Task ListItems_reads_provider_ids_in_any_letter_case_and_trimmed()
    {
        //Arrange
        ArmMovies(Movie("""{"Id":"a","ProviderIds":{"IMDB":" TT0090605 ","tmdb":" 679 "}}"""));

        //Act
        var result = await Library.ListItemsAsync(Settings, FirstPage, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(
            [new MediaIdentifier(MediaIdentifierScheme.Imdb, "TT0090605"), new MediaIdentifier(MediaIdentifierScheme.Tmdb, "679")],
            Assert.Single(result.Value!.Items).Identifiers);
    }

    [Fact]
    public async Task ListItems_takes_the_first_well_formed_value_of_each_scheme()
    {
        //Arrange
        ArmMovies(Movie("""{"Id":"a","ProviderIds":{"Imdb":"bad","IMDB":"tt0090605","imdb":"tt0078748","Tmdb":"679","TMDB":"348"}}"""));

        //Act
        var result = await Library.ListItemsAsync(Settings, FirstPage, TestContext.Current.CancellationToken);

        //Assert
        // One value per scheme, so an item never contradicts itself.
        Assert.Equal(
            [new MediaIdentifier(MediaIdentifierScheme.Imdb, "tt0090605"), new MediaIdentifier(MediaIdentifierScheme.Tmdb, "679")],
            Assert.Single(result.Value!.Items).Identifiers);
    }

    [Theory]
    [MemberData(nameof(SourcesWithoutAFile))]
    public async Task ListItems_gives_a_media_source_that_is_not_a_local_video_file_a_version_without_a_file(string property, string? value)
    {
        //Arrange
        ArmMovies(TheatricalWith(property, value));

        //Act
        var result = await Library.ListItemsAsync(Settings, FirstPage, TestContext.Current.CancellationToken);

        //Assert
        // A file row is one name of a file (ADR 0006): never a folder, a link or a stream.
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        var version = Assert.Single(Assert.Single(result.Value!.Items).Versions);
        Assert.Equal(JellyfinSamples.AliensTheatricalId, version.SourceVersionId);
        Assert.Empty(version.Files);
    }

    [Theory]
    [InlineData("VideoType", "\"VideoFile\"")]
    [InlineData("VideoType", "\"Iso\"")]
    [InlineData("VideoType", "null")]
    [InlineData("VideoType", null)]
    [InlineData("Protocol", "null")]
    [InlineData("Protocol", null)]
    [InlineData("IsRemote", "false")]
    [InlineData("IsRemote", null)]
    public async Task ListItems_keeps_the_file_of_a_local_video_file_or_iso(string property, string? value)
    {
        //Arrange
        ArmMovies(TheatricalWith(property, value));

        //Act
        var result = await Library.ListItemsAsync(Settings, FirstPage, TestContext.Current.CancellationToken);

        //Assert
        var version = Assert.Single(Assert.Single(result.Value!.Items).Versions);
        Assert.Equal(new LibraryFile(AliensTheatricalPath, 14845708473), Assert.Single(version.Files));
    }

    [Theory]
    [InlineData("\"BluRay\"")]
    [InlineData("\"Dvd\"")]
    public async Task ListItems_keeps_the_folder_of_a_local_blu_ray_or_dvd_as_its_disc_folder(string videoType)
    {
        //Arrange
        // Jellyfin's own path of a disc: the folder that holds BDMV (or VIDEO_TS), never a file in it.
        ArmMovies(TheatricalWith(("VideoType", videoType), ("Path", $"\"{DirtyDozenFolder}\"")));

        //Act
        var result = await Library.ListItemsAsync(Settings, FirstPage, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        var version = Assert.Single(Assert.Single(result.Value!.Items).Versions);
        Assert.Equal(JellyfinSamples.AliensTheatricalId, version.SourceVersionId);
        Assert.Equal(DirtyDozenFolder, version.DiscFolderPath);
        // The clips are the sync's to list (LibraryDiscClips): the library names none.
        Assert.Empty(version.Files);
    }

    [Theory]
    [InlineData("Protocol", "\"Http\"")]
    [InlineData("Protocol", "7")]
    [InlineData("IsRemote", "true")]
    [InlineData("Path", "\"  \"")]
    [InlineData("Path", "\"/media/movies/DIRTY_DOZEN_1967_RMX.strm\"")]
    [InlineData("Path", "12")]
    [InlineData("Path", null)]
    public async Task ListItems_gives_a_blu_ray_that_is_not_a_local_folder_no_disc_folder(string property, string? value)
    {
        //Arrange
        ArmMovies(TheatricalWith(("VideoType", "\"BluRay\""), ("Path", $"\"{DirtyDozenFolder}\""), (property, value)));

        //Act
        var result = await Library.ListItemsAsync(Settings, FirstPage, TestContext.Current.CancellationToken);

        //Assert
        var version = Assert.Single(Assert.Single(result.Value!.Items).Versions);
        Assert.Null(version.DiscFolderPath);
        Assert.Empty(version.Files);
    }

    [Theory]
    [InlineData("\"VideoFile\"")]
    [InlineData("\"Iso\"")]
    [InlineData("3")]
    [InlineData("null")]
    [InlineData(null)]
    public async Task ListItems_gives_a_media_source_of_any_other_video_type_no_disc_folder(string? videoType)
    {
        //Arrange
        ArmMovies(TheatricalWith("VideoType", videoType));

        //Act
        var result = await Library.ListItemsAsync(Settings, FirstPage, TestContext.Current.CancellationToken);

        //Assert
        Assert.Null(Assert.Single(Assert.Single(result.Value!.Items).Versions).DiscFolderPath);
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("\"14845708473\"")]
    [InlineData("14845708473.5")]
    [InlineData("1e30")]
    [InlineData("null")]
    [InlineData(null)]
    public async Task ListItems_leaves_a_size_it_can_not_trust_unknown(string? size)
    {
        //Arrange
        ArmMovies(TheatricalWith("Size", size));

        //Act
        var result = await Library.ListItemsAsync(Settings, FirstPage, TestContext.Current.CancellationToken);

        //Assert
        var file = Assert.Single(Assert.Single(Assert.Single(result.Value!.Items).Versions).Files);
        Assert.Equal(AliensTheatricalPath, file.ServicePath);
        Assert.Null(file.SizeBytes);
    }

    [Fact]
    public async Task ListItems_takes_a_path_exactly_as_jellyfin_sent_it()
    {
        //Arrange
        // A path names a file: never cleaned, trimmed or joined up, whatever it looks like.
        const string path = " /media/movies/Чужі  (1986)/[02.1] Aliens\u00A0(1986) \u202E.mkv ";
        ArmMovies(TheatricalWith("Path", $"\"{path}\""));

        //Act
        var result = await Library.ListItemsAsync(Settings, FirstPage, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(path, Assert.Single(Assert.Single(Assert.Single(result.Value!.Items).Versions).Files).ServicePath);
    }

    [Fact]
    public async Task ListItems_cleans_the_titles_and_masks_the_key_in_them()
    {
        //Arrange
        var original = new string('x', 600);
        ArmMovies(Movie($$"""{"Id":"a","Name":"Чужі\u0007\r\n  \u202E{{ApiKey}}","OriginalTitle":"{{original}}"}"""));

        //Act
        var result = await Library.ListItemsAsync(Settings, FirstPage, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(
            [new ReportedTitle("Чужі ***", MediaTitleKind.Display, null), new ReportedTitle(new string('x', 499) + "…", MediaTitleKind.Original, null)],
            Assert.Single(result.Value!.Items).Titles);
    }

    [Fact]
    public async Task ListItems_reads_the_further_parts_of_a_stacked_movie_as_files_of_its_version()
    {
        //Arrange
        var movie = JellyfinSamples.Movie(JellyfinSamples.District13Id);
        movie["PartCount"] = 3;
        _server.Movies.Clear();
        _server.Movies.Add(movie);
        _server.AdditionalParts[JellyfinSamples.District13Id] =
        [
            JellyfinSamples.Part("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "/media/movies/District 13/Banlieue 13 - part2.mkv", 2_000_000_000),
            JellyfinSamples.Part("bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", "/media/movies/District 13/Banlieue 13 - part3.mkv", null),
        ];
        _server.Arm(_harness.JellyfinHttp);

        //Act
        var result = await Library.ListItemsAsync(Settings, FirstPage, TestContext.Current.CancellationToken);

        //Assert
        // Jellyfin lists only the first part in the movie's media source.
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        var version = Assert.Single(Assert.Single(result.Value!.Items).Versions);
        Assert.Equal(
            [
                new LibraryFile(District13Path, 4670197619),
                new LibraryFile("/media/movies/District 13/Banlieue 13 - part2.mkv", 2_000_000_000),
                new LibraryFile("/media/movies/District 13/Banlieue 13 - part3.mkv", null),
            ],
            version.Files);
        var partsRequest = _harness.JellyfinHttp.Requests[1];
        Assert.Equal(HttpMethod.Get, partsRequest.Method);
        Assert.Equal($"{BaseUrl}/Videos/{JellyfinSamples.District13Id}/AdditionalParts", partsRequest.Uri.AbsoluteUri);
        Assert.Equal([$"MediaBrowser Token=\"{ApiKey}\""], partsRequest.HeaderValues("Authorization"));
        Assert.Equal(2, _harness.JellyfinHttp.Requests.Count);
    }

    [Fact]
    public async Task ListItems_adds_the_parts_to_the_movies_own_media_source()
    {
        //Arrange
        // The movie's own source second: the parts belong to it, not to whichever comes first.
        var movie = JellyfinSamples.AliensWithBothSources();
        var sources = movie["MediaSources"]!.AsArray();
        var own = sources[0]!;
        sources.RemoveAt(0);
        sources.Add(own);
        movie["PartCount"] = 2;
        ArmMovies(movie);
        _server.AdditionalParts[JellyfinSamples.AliensTheatricalId] =
        [
            JellyfinSamples.Part("cccccccccccccccccccccccccccccccc", "/media/movies/Alien. Collection/Aliens (1986) - part2.mkv", 1_000),
            // A part that repeats a path it already has is not a second file.
            JellyfinSamples.Part("dddddddddddddddddddddddddddddddd", AliensTheatricalPath, 1_000),
            JellyfinSamples.Part("eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee", "/media/movies/Alien. Collection/Aliens (1986) - part3.strm", 1_000),
        ];

        //Act
        var result = await Library.ListItemsAsync(Settings, FirstPage, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(
            [
                $"{JellyfinSamples.AliensSpecialEditionId} [[02.2] Aliens (1986) [Special Edition] BDRip 1080p H.265 [2xUKR_ENG] [Hurtom, UTOPIA]] {AliensSpecialEditionPath} 19620073340",
                $"{JellyfinSamples.AliensTheatricalId} [[02.1] Aliens (1986) [Theatrical Cut, Fundamental Collection] BDRip 1080p H.265 [2xUKR_ENG] [Hurtom]] {AliensTheatricalPath} 14845708473 | /media/movies/Alien. Collection/Aliens (1986) - part2.mkv 1000",
            ],
            DescribeVersions(Assert.Single(result.Value!.Items)));
    }

    [Theory]
    [InlineData(404, IntegrationCallStatus.UnexpectedResponse,
        "Jellyfin no longer finds the other parts of a movie it has just listed (404 Not Found); its library is changing. Try again once Jellyfin's library scan has finished.")]
    [InlineData(401, IntegrationCallStatus.AuthenticationFailed,
        "Jellyfin refused the API key. Create one in Jellyfin under Dashboard → API Keys and enter it here.")]
    [InlineData(500, IntegrationCallStatus.ServiceError, "Jellyfin answered 500 Internal Server Error. Its own log says why.")]
    [InlineData(200, IntegrationCallStatus.UnexpectedResponse, NotJellyfinError)]
    public async Task ListItems_fails_the_page_when_the_parts_of_a_stacked_movie_can_not_be_read(
        int statusCode, IntegrationCallStatus expectedStatus, string expectedMessage)
    {
        //Arrange
        var movie = JellyfinSamples.Movie(JellyfinSamples.District13Id);
        movie["PartCount"] = 2;
        ArmMovies(movie);
        _server.Intercept = request => request.RequestUri!.AbsolutePath.EndsWith("/AdditionalParts", StringComparison.Ordinal)
            ? VendorResponses.Text("[]", (HttpStatusCode)statusCode, "application/json")
            : null;

        //Act
        var result = await Library.ListItemsAsync(Settings, FirstPage, TestContext.Current.CancellationToken);

        //Assert
        // Never the movie with its first part only: a sync would take that for the whole movie.
        Assert.Equal(expectedStatus, result.Status);
        Assert.Equal(expectedMessage, result.Error);
        Assert.Null(result.Value);
    }

    [Theory]
    // Resolved as dot segments, these would send the keyed GET to {base}/AdditionalParts and
    // {base}/Videos/AdditionalParts.
    [InlineData("..")]
    [InlineData(".")]
    [InlineData("district-13")]
    public async Task ListItems_of_a_stacked_movie_whose_id_is_not_a_guid_fails_without_asking_for_its_parts(string id)
    {
        //Arrange
        var movie = JellyfinSamples.Movie(JellyfinSamples.District13Id);
        movie["Id"] = id;
        movie["PartCount"] = 2;
        ArmMovies(movie);

        //Act
        var result = await Library.ListItemsAsync(Settings, FirstPage, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.UnexpectedResponse, result.Status);
        Assert.Equal(
            "The service at this address listed a movie in several parts under an id Jellyfin never gives, so the app won't ask for its other parts. Check the address.",
            result.Error);
        Assert.Null(result.Value);
        // The page and nothing else: the key went to no other path.
        Assert.Equal([$"{BaseUrl}/Items"], _harness.JellyfinHttp.Requests.Select(request => request.Uri.GetLeftPart(UriPartial.Path)));
    }

    [Fact]
    public async Task ListItems_asks_for_the_parts_of_a_stacked_movie_by_its_id_in_jellyfins_own_form()
    {
        //Arrange
        // The same Guid as the sample's id, hyphenated and in capitals.
        var movie = JellyfinSamples.Movie(JellyfinSamples.District13Id);
        movie["Id"] = "E3D063DF-4B73-D12C-0D4C-D71725E5E3EB";
        movie["PartCount"] = 2;
        ArmMovies(movie);
        _server.AdditionalParts[JellyfinSamples.District13Id] =
            [JellyfinSamples.Part("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "/media/movies/District 13/Banlieue 13 - part2.mkv", 1_000)];

        //Act
        var result = await Library.ListItemsAsync(Settings, FirstPage, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        Assert.Equal(
            [$"{BaseUrl}/Items", $"{BaseUrl}/Videos/{JellyfinSamples.District13Id}/AdditionalParts"],
            _harness.JellyfinHttp.Requests.Select(request => request.Uri.GetLeftPart(UriPartial.Path)));
        Assert.Equal(2, Assert.Single(Assert.Single(result.Value!.Items).Versions).Files.Count);
    }

    [Theory]
    [InlineData(401, IntegrationCallStatus.AuthenticationFailed,
        "Jellyfin refused the API key. Create one in Jellyfin under Dashboard → API Keys and enter it here.")]
    [InlineData(403, IntegrationCallStatus.AuthenticationFailed,
        "Jellyfin accepted the key but won't let it read the library (403 Forbidden). Use a key from Dashboard → API Keys, not a user's access token.")]
    [InlineData(404, IntegrationCallStatus.UnexpectedResponse,
        "Jellyfin wasn't found at this address (404 Not Found for /Items). If Jellyfin runs under a base path, include it, for example http://192.168.1.10:8096/jellyfin.")]
    [InlineData(500, IntegrationCallStatus.ServiceError, "Jellyfin answered 500 Internal Server Error. Its own log says why.")]
    [InlineData(503, IntegrationCallStatus.ServiceError, "Jellyfin answered 503 Service Unavailable. Its own log says why.")]
    [InlineData(204, IntegrationCallStatus.UnexpectedResponse,
        "Jellyfin answered 204 No Content, which it doesn't send for this request. Check the address.")]
    [InlineData(418, IntegrationCallStatus.UnexpectedResponse,
        "Jellyfin answered 418, which it doesn't send for this request. Check the address.")]
    public async Task ListItems_answers_each_failure_with_its_status_and_sentence(
        int statusCode, IntegrationCallStatus expectedStatus, string expectedMessage)
    {
        //Arrange
        _harness.JellyfinHttp.Handler = (_, _) =>
            new HttpResponseMessage((HttpStatusCode)statusCode) { ReasonPhrase = "Made ‮up by the server" };

        //Act
        var result = await Library.ListItemsAsync(Settings, FirstPage, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(expectedStatus, result.Status);
        Assert.Equal(expectedMessage, result.Error);
        Assert.Null(result.Value);
    }

    [Fact]
    public async Task ListItems_reports_a_redirect_with_the_key_it_carries_masked()
    {
        //Arrange
        // The key in the path, where the redirect sentence repeats it (the query is left out anyway).
        _harness.JellyfinHttp.Handler = (_, _) => VendorResponses.Redirect($"https://media.test/{ApiKey}/web/index.html?api_key={ApiKey}");

        //Act
        var result = await Library.ListItemsAsync(Settings, FirstPage, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.UnexpectedResponse, result.Status);
        Assert.Equal(
            "The address answered with a redirect to https://media.test/***/web/index.html. Enter the address the service is really at: the app doesn't follow redirects, so your credentials never go anywhere else.",
            result.Error);
        Assert.DoesNotContain(ApiKey, result.Error!, StringComparison.Ordinal);
        // Never followed: the key went nowhere but the address the owner entered.
        Assert.Single(_harness.JellyfinHttp.Requests);
    }

    [Theory]
    [MemberData(nameof(JellyfinAdapterTests.TransportFailures), MemberType = typeof(JellyfinAdapterTests))]
    public async Task ListItems_maps_each_transport_error_to_its_sentence_without_the_exception_text(
        HttpRequestError error, IntegrationCallStatus expectedStatus, string expectedMessage)
    {
        //Arrange
        _harness.JellyfinHttp.Handler = (request, _) =>
            throw new HttpRequestException(error, $"Socket failure on {request.RequestUri} with the key {ApiKey}");

        //Act
        var result = await Library.ListItemsAsync(Settings, FirstPage, TestContext.Current.CancellationToken);

        //Assert
        // The connection test's sentences, from one source.
        Assert.Equal(expectedStatus, result.Status);
        Assert.Equal(expectedMessage, result.Error);
    }

    [Fact]
    public async Task ListItems_that_outlives_the_timeout_is_timed_out()
    {
        //Arrange
        using var harness = new IntegrationAdapterHarness(timeout: TimeSpan.FromMilliseconds(100));
        harness.JellyfinHttp.Handler = (_, _) => StalledAnswer();

        //Act
        var result = await harness.Adapter<IMediaLibraryProvider>(JellyfinAdapter.TypeId)
            .ListItemsAsync(Settings, FirstPage, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.TimedOut, result.Status);
        Assert.Equal("Jellyfin at http://jellyfin.test:8096 didn't answer within 0.1 seconds.", result.Error);
    }

    [Fact]
    public async Task ListItems_cancelled_by_the_caller_throws()
    {
        //Arrange
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        _harness.JellyfinHttp.Handler = (_, _) =>
        {
            cancellation.Cancel();
            return StalledAnswer();
        };

        //Act
        var exception = await Record.ExceptionAsync(() => Library.ListItemsAsync(Settings, FirstPage, cancellation.Token));

        //Assert
        // Not a "timed out" result: nobody is waiting for one.
        Assert.IsAssignableFrom<OperationCanceledException>(exception);
    }

    [Theory]
    [InlineData("")]
    [InlineData("<!DOCTYPE html><html><body>Welcome to nginx!</body></html>")]
    [InlineData("""{"Items":[""")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("""{"Items":[]}""")]
    [InlineData("""{"TotalRecordCount":4}""")]
    [InlineData("""{"Items":{},"TotalRecordCount":4}""")]
    [InlineData("""{"Items":null,"TotalRecordCount":4}""")]
    [InlineData("""{"Items":[],"TotalRecordCount":-1}""")]
    [InlineData("""{"Items":[],"TotalRecordCount":"4"}""")]
    [InlineData("""{"Items":[],"TotalRecordCount":4.5}""")]
    [InlineData("""{"Items":[1],"TotalRecordCount":1}""")]
    [InlineData("""{"Items":[{"Id":"a","Type":7}],"TotalRecordCount":1}""")]
    [InlineData("""{"Items":[{"Id":"a","MediaSources":{}}],"TotalRecordCount":1}""")]
    [InlineData("""{"Items":[{"Id":"a","MediaSources":"x"}],"TotalRecordCount":1}""")]
    public async Task ListItems_of_a_body_that_is_not_a_page_of_movies_is_an_unexpected_response(string body)
    {
        //Arrange
        ArmBody(body);

        //Act
        var result = await Library.ListItemsAsync(Settings, FirstPage, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.UnexpectedResponse, result.Status);
        Assert.Equal(NotJellyfinError, result.Error);
    }

    [Theory]
    [InlineData("""{"Type":"Movie","Name":"Чужі"}""")]
    [InlineData("""{"Id":null}""")]
    [InlineData("""{"Id":""}""")]
    [InlineData("""{"Id":"   "}""")]
    [InlineData("""{"Id":12}""")]
    [InlineData("""{"Id":"00a49b2d\u0007b2200514168431dc028f0a0b"}""")]
    public async Task ListItems_of_a_movie_without_a_usable_id_is_an_unexpected_response(string item)
    {
        //Arrange
        // Skipped, it would look deleted to the sync; failed, the stored snapshot stays as it is.
        ArmBody($$"""{"Items":[{"Id":"fine","Type":"Movie"},{{item}}],"TotalRecordCount":2}""");

        //Act
        var result = await Library.ListItemsAsync(Settings, FirstPage, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.UnexpectedResponse, result.Status);
        Assert.Equal(MovieWithoutIdError, result.Error);
    }

    [Fact]
    public async Task ListItems_of_a_movie_whose_id_is_longer_than_an_id_column_is_an_unexpected_response()
    {
        //Arrange
        ArmBody($$"""{"Items":[{"Id":"{{new string('a', 257)}}"}],"TotalRecordCount":1}""");

        //Act
        var result = await Library.ListItemsAsync(Settings, FirstPage, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.UnexpectedResponse, result.Status);
        Assert.Equal(MovieWithoutIdError, result.Error);
    }

    [Theory]
    [InlineData("BoxSet", "BoxSet")]
    [InlineData("Episode", "Episode")]
    // A JSON-escaped bell, which the cleaning drops, and the key, which it masks.
    [InlineData(@"Folder\u0007 " + ApiKey, "Folder ***")]
    public async Task ListItems_of_an_item_that_is_not_a_movie_is_an_unexpected_response(string type, string expectedType)
    {
        //Arrange
        // A box set among the movies would be imported as a work; the whole list is suspect instead.
        ArmMovies(JellyfinSamples.Movie(JellyfinSamples.ContinentalId), Movie($$"""{"Id":"a","Type":"{{type}}"}"""));

        //Act
        var result = await Library.ListItemsAsync(Settings, FirstPage, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.UnexpectedResponse, result.Status);
        Assert.Equal(
            $"Jellyfin listed an item of type “{expectedType}” when asked for movies only, so the app can't rely on the list. Check the address.",
            result.Error);
    }

    [Theory]
    [InlineData(-1, 50)]
    [InlineData(0, 0)]
    [InlineData(0, -5)]
    [InlineData(0, 201)]
    public async Task ListItems_refuses_a_page_outside_its_limits_without_sending_anything(int startIndex, int limit)
    {
        //Arrange
        _server.Arm(_harness.JellyfinHttp);

        //Act
        var exception = await Record.ExceptionAsync(() =>
            Library.ListItemsAsync(Settings, new LibraryPageRequest(MediaWorkKind.Movie, startIndex, limit), TestContext.Current.CancellationToken));

        //Assert
        Assert.IsType<ArgumentOutOfRangeException>(exception);
        Assert.Empty(_harness.JellyfinHttp.Requests);
    }

    [Fact]
    public async Task ListItems_accepts_the_largest_page()
    {
        //Arrange
        _server.Arm(_harness.JellyfinHttp);

        //Act
        var result = await Library.ListItemsAsync(Settings, new LibraryPageRequest(MediaWorkKind.Movie, 0, JellyfinAdapter.MaximumPageSize), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        Assert.Contains("limit=200", Assert.Single(_harness.JellyfinHttp.Requests).Uri.Query, StringComparison.Ordinal);
    }

    // The server answering these movies and nothing else, with their count as the total.
    private void ArmMovies(params JsonObject[] movies)
    {
        _server.Movies.Clear();
        _server.Movies.AddRange(movies);
        _server.Arm(_harness.JellyfinHttp);
    }

    private void ArmBody(string body) =>
        _harness.JellyfinHttp.Handler = (_, _) => VendorResponses.Text(body, HttpStatusCode.OK, "application/json");

    private static JsonObject Movie(string json) => JsonNode.Parse(json)!.AsObject();

    // The theatrical "Чужі" with one property of its media source set to a JSON value, or removed.
    private static JsonObject TheatricalWith(string property, string? value)
    {
        var movie = JellyfinSamples.Movie(JellyfinSamples.AliensTheatricalId);
        var source = movie["MediaSources"]![0]!.AsObject();
        if (value is null)
            source.Remove(property);
        else
            source[property] = JsonNode.Parse(value);
        return movie;
    }

    // The theatrical "Чужі" with several properties of its media source set, each in turn, the last of one name winning.
    private static JsonObject TheatricalWith(params (string Property, string? Value)[] changes)
    {
        var movie = JellyfinSamples.Movie(JellyfinSamples.AliensTheatricalId);
        var source = movie["MediaSources"]![0]!.AsObject();
        foreach (var (property, value) in changes)
        {
            if (value is null)
                source.Remove(property);
            else
                source[property] = JsonNode.Parse(value);
        }

        return movie;
    }

    // Each version as "id [label] path size | path size …".
    private static List<string> DescribeVersions(LibraryItem item) =>
    [
        .. item.Versions.Select(version =>
            $"{version.SourceVersionId ?? "(no id)"} [{version.EditionLabel}] " +
            string.Join(" | ", version.Files.Select(file => $"{file.ServicePath} {file.SizeBytes?.ToString(CultureInfo.InvariantCulture) ?? "(unknown)"}"))),
    ];

    // A 200 whose body never finishes arriving: the pipe is never written to or completed.
    private static HttpResponseMessage StalledAnswer() =>
        new(HttpStatusCode.OK) { Content = new StreamContent(new Pipe().Reader.AsStream()) };
}
