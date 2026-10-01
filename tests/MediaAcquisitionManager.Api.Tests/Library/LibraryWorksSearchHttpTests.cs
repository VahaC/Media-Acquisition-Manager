using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Tests.Library;

// MAM-8 — the Library page's search and filters, applied by the server: a search finds a work by any
// title, identifier or file path in any letter case — Cyrillic too, which SQLite's upper() doesn't fold —
// or by a year; '%' and '_' are text, not patterns. The filters are the Jellyfin library a file is in and
// "missing an IMDb or TMDb id".
public sealed class LibraryWorksSearchHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/library/works";
    private const string ApiKey = "0123456789abcdef0123456789abcdef";
    private const string CartoonsLocationId = "2e7d31f7c627f72ad0912eff1c00353d";
    private const string ShowsLocationId = "f97af1337db58125aaa9a3f3695ab163";

    /// <summary>A movie whose path names none of its facts, so a search can only find it by the fact
    /// searched for.</summary>
    private const string Shadows = "Тіні забутих предків";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private AuthorizedUser _owner = null!;
    private FakeJellyfinServer _jellyfin = null!;

    public LibraryWorksSearchHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    /// <summary>A search term and the titles of the works it finds, in title order.</summary>
    public static TheoryData<string, string[]> Searches => new()
    {
        // A displayed title in another letter case, Cyrillic both ways, and the term trimmed.
        { "чужі", ["Чужі"] },
        { "  ЧУЖІ  ", ["Чужі"] },
        { "тіні", [Shadows] },
        // An original title.
        { "FORGOTTEN", [Shadows] },
        // An IMDb id in another case, and a TMDb id.
        { "TT0058642", [Shadows] },
        { "38800", [Shadows] },
        // A year.
        { "1965", [Shadows] },
        // Part of a file path: a folder and a file name, or a collection folder as it is named.
        { "x/film", [Shadows] },
        { "Alien. Collection", ["Чужі"] },
        { "collection", ["Чужі", "The Continental"] },
        // Nothing, and characters a LIKE pattern would read as wildcards: every path has a "1080p".
        { "Ghostbusters", [] },
        { "%", [] },
        { "1_80p", [] },
    };

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _owner = await new UserSeeder(_factory.Scope).CreateAuthorizedOwner("owner");
        _jellyfin = new FakeJellyfinServer { ApiKey = ApiKey };
        _jellyfin.Arm(_factory.JellyfinHttp);
        // The four samples, the clean movie, a cartoon in the cartoon library, and a movie Jellyfin found
        // no metadata for.
        _jellyfin.Movies.Add(ShadowsMovie());
        _jellyfin.Movies.Add(Movie("c0c0c0c0c0c0c0c0c0c0c0c0c0c0c0c0", "Коко", 2017, "/media/cartoons/Coco (2017)/Coco.mkv", "tt2380307", "354912"));
        _jellyfin.Movies.Add(JellyfinSamples.WithoutProviderIds(
            Movie("0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f", "Без ідентифікаторів", 2020, "/media/movies/unknown.mkv", "tt0000001", "1")));
        var jellyfin = await new IntegrationConnectionSeeder(_factory.Scope).Create("jellyfin", "Living room", apiKey: ApiKey);
        await new LibrarySnapshotSeeder(_factory.Scope).Sync(jellyfin, _owner.User);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Theory]
    [MemberData(nameof(Searches))]
    public async Task Works_search_finds_the_works_with_a_matching_title_identifier_year_or_path(string search, string[] titles)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}?search={Uri.EscapeDataString(search)}", _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var list = await ReadListAsync(response, cancellationToken);
        Assert.Equal(titles, list.Items.Select(work => work.Title));
        Assert.Equal(titles.Length, list.TotalCount);
    }

    [Fact]
    public async Task Works_in_a_library_lists_only_the_works_with_a_file_in_it()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var movies = await LocationIdAsync(JellyfinSamples.MoviesLocationId, cancellationToken);
        var cartoons = await LocationIdAsync(CartoonsLocationId, cancellationToken);
        var shows = await LocationIdAsync(ShowsLocationId, cancellationToken);

        //Act
        var inMovies = await ReadListAsync(await _factory.GetHttpRequest($"{BaseUrl}?locationId={movies}", _owner.AccessToken), cancellationToken);
        var inCartoons = await ReadListAsync(await _factory.GetHttpRequest($"{BaseUrl}?locationId={cartoons}", _owner.AccessToken), cancellationToken);
        var inShows = await ReadListAsync(await _factory.GetHttpRequest($"{BaseUrl}?locationId={shows}", _owner.AccessToken), cancellationToken);

        //Assert
        Assert.Equal(["13-й район", "Без ідентифікаторів", Shadows, "Чужі", "The Continental"], inMovies.Items.Select(work => work.Title));
        Assert.Equal(5, inMovies.TotalCount);
        Assert.Equal(["Коко"], inCartoons.Items.Select(work => work.Title));
        // A library that holds nothing lists nothing.
        Assert.Equal(0, inShows.TotalCount);
    }

    [Fact]
    public async Task Works_in_a_library_nobody_has_is_an_empty_page()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}?locationId={Guid.NewGuid()}", _owner.AccessToken);

        //Assert
        // A library a sync has since removed, in a page loaded before it: nothing, not an error.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var list = await ReadListAsync(response, cancellationToken);
        Assert.Equal(0, list.TotalCount);
        Assert.Empty(list.Items);
    }

    [Theory]
    [InlineData("abc", "“abc” is not a library.")]
    [InlineData(" 12345 ", "“12345” is not a library.")]
    public async Task Works_in_a_library_that_is_not_an_id_is_400_with_its_sentence(string locationId, string error)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}?locationId={Uri.EscapeDataString(locationId)}", _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(error, await response.ReadErrorAsync(cancellationToken));
    }

    [Fact]
    public async Task Works_in_a_library_that_is_a_long_text_echoes_only_its_start()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var pasted = new string('x', 500);

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}?locationId={pasted}", _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal($"“{new string('x', 64)}…” is not a library.", await response.ReadErrorAsync(cancellationToken));
    }

    [Theory]
    [InlineData("Missing")]
    [InlineData("missing")]
    [InlineData(" MISSING ")]
    public async Task Works_missing_an_identifier_lists_the_works_without_an_imdb_or_a_tmdb_id(string identifiers)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}?identifiers={Uri.EscapeDataString(identifiers)}", _owner.AccessToken);

        //Assert
        // "Без ідентифікаторів" has neither; The Continental has an IMDb id only.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var list = await ReadListAsync(response, cancellationToken);
        Assert.Equal(["Без ідентифікаторів", "The Continental"], list.Items.Select(work => work.Title));
        Assert.Equal(
            [[MediaIdentifierScheme.Imdb, MediaIdentifierScheme.Tmdb], [MediaIdentifierScheme.Tmdb]],
            list.Items.Select(work => work.MissingIdentifiers));
    }

    [Fact]
    public async Task Works_with_an_unknown_identifier_filter_is_400_with_its_sentence()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}?identifiers=Present", _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("“Present” is not an identifier filter. Use Missing.", await response.ReadErrorAsync(cancellationToken));
    }

    [Fact]
    public async Task Works_search_and_filters_apply_together()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var cartoons = await LocationIdAsync(CartoonsLocationId, cancellationToken);

        //Act
        var missing = await ReadListAsync(
            await _factory.GetHttpRequest($"{BaseUrl}?search=collection&identifiers=Missing", _owner.AccessToken), cancellationToken);
        var elsewhere = await ReadListAsync(
            await _factory.GetHttpRequest($"{BaseUrl}?search=collection&locationId={cartoons}", _owner.AccessToken), cancellationToken);

        //Assert
        Assert.Equal(["The Continental"], missing.Items.Select(work => work.Title));
        Assert.Equal(0, elsewhere.TotalCount);
    }

    // The stored id of the library Jellyfin calls sourceLocationId.
    private async Task<Guid> LocationIdAsync(string sourceLocationId, CancellationToken cancellationToken)
    {
        var id = Guid.Empty;
        await _factory.WithDbAsync(async db => id = await db.LibraryLocations
            .Where(location => location.SourceItemId == sourceLocationId)
            .Select(location => location.Id)
            .SingleAsync(cancellationToken));
        return id;
    }

    // "Тіні забутих предків" (1965), IMDb tt0058642, TMDb 38800, at a path that names none of it.
    private static JsonObject ShadowsMovie()
    {
        var movie = Movie("5ade5ade5ade5ade5ade5ade5ade5ade", Shadows, 1965, "/media/movies/x/film.mkv", "tt0058642", "38800");
        movie["OriginalTitle"] = "Shadows of Forgotten Ancestors";
        return movie;
    }

    // A movie of its own with these facts: a "13-й район" with every one of them replaced.
    private static JsonObject Movie(string id, string title, int year, string path, string imdb, string tmdb)
    {
        var movie = JellyfinSamples.Movie(JellyfinSamples.District13Id);
        movie["Id"] = id;
        movie["Name"] = title;
        movie.Remove("OriginalTitle");
        movie["ProductionYear"] = year;
        movie["ProviderIds"] = new JsonObject { ["Imdb"] = imdb, ["Tmdb"] = tmdb };
        movie["Path"] = path;
        movie["MediaSources"]![0]!["Id"] = id;
        movie["MediaSources"]![0]!["Path"] = path;
        return movie;
    }

    private static async Task<LibraryWorkListResponse> ReadListAsync(HttpResponseMessage response, CancellationToken cancellationToken) =>
        (await response.Content.ReadFromJsonAsync<LibraryWorkListResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
}
