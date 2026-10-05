using System.Net;
using System.Net.Http.Json;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Abstractions.Metadata;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Tests.Search;

// MAM-33, ADR 0020 — the ordinary search finds documentary and anime films too: Toloka files them in
// TV/Anime and TV/Documentary beside series, so the movie search asks those categories, and a release there
// that names no season or episode is a film, matched to the library's movies like any other. Read against
// the real Toloka listings of "Wild Yellowstone She Wolf" (2013), which its documentary section holds twice
// beside the BBC's series "Yellowstone (S1) (2009)".
public sealed class SearchReleasesFilmsInTvCategoriesHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/searches";
    private const string TolokaAddress = "http://prowlarr.test:9696/1/api";
    private const string ApiKey = "planted-torznab-key-0001";
    private const string JellyfinApiKey = "0123456789abcdef0123456789abcdef";

    private const string FilmItemId = "4d4d4d4d4d4d4d4d4d4d4d4d4d4d4d4d";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private AuthorizedUser _member = null!;

    public SearchReleasesFilmsInTvCategoriesHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        var users = new UserSeeder(_factory.Scope);
        var owner = await users.CreateAuthorizedOwner("owner");
        _member = await users.CreateAuthorized("member");

        var jellyfin = new FakeJellyfinServer { ApiKey = JellyfinApiKey };
        jellyfin.Movies.Clear();
        jellyfin.Movies.Add(JellyfinSamples.MovieWith(
            FilmItemId, "Wild Yellowstone She Wolf", null, 2013, "/media/documentary/Wild Yellowstone She Wolf (2013).mkv"));
        jellyfin.Arm(_factory.JellyfinHttp);
        var integrations = new IntegrationConnectionSeeder(_factory.Scope);
        await new LibrarySnapshotSeeder(_factory.Scope).Sync(await integrations.Create("jellyfin", "Living room", apiKey: JellyfinApiKey), owner.User);

        // The film's real listings, in Toloka's answer for "Yellowstone": its text search finds every
        // section whatever cat= says (ADR 0004), and the adapter keeps the categories asked.
        new FakeTorznabServer(TolokaAddress, ApiKey) { SearchXml = TorznabSamples.TvSearch(TolokaAddress, ApiKey) }.Arm(_factory.TorznabHttp);
        await integrations.Create("torznab", "Toloka", TolokaAddress, apiKey: ApiKey);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Create_asks_the_movie_search_in_tv_anime_and_tv_documentary_and_matches_the_films_there_to_the_librarys_movie()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _member.AccessToken, new SearchRequest("Wild Yellowstone She Wolf", "2013"));
        var search = (await response.Content.ReadFromJsonAsync<SearchResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
        var list = await _factory.GetHttpRequest($"{BaseUrl}/{search.Id}/releases?pageSize=100", _member.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(
            [
                $"{TolokaAddress}?t=caps&apikey={ApiKey}",
                $"{TolokaAddress}?t=movie&apikey={ApiKey}&q=Wild%20Yellowstone%20She%20Wolf%202013&cat=2000,2020,2040,2070,5070,5080&limit=100",
                $"{TolokaAddress}?t=tvsearch&apikey={ApiKey}&q=Wild%20Yellowstone%20She%20Wolf&cat=5000,5040,5050,5060,5070,5080&limit=100",
            ],
            _factory.TorznabHttp.Requests.Select(request => request.Uri.AbsoluteUri));
        var film = Assert.Single(search.Library);
        Assert.Equal((MediaWorkKind.Movie, "Wild Yellowstone She Wolf", MatchEvidence.TitleAndYear), (film.Work.Kind, film.Work.Title!, film.Evidence));
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        var releases = (await list.Content.ReadFromJsonAsync<SearchReleaseListResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!.Items;
        // TV/Documentary's four: the film's two releases are movies of its title and year, matched to it; a
        // film of another title matches nothing; the BBC's series names its season and stays TV.
        Assert.Equal(
            [
                ("Wild Yellowstone She Wolf (2013) HDTV", ReleaseKind.Movie, $"{film.Work.Id} TitleAndYear"),
                ("Wild Yellowstone She Wolf (2013) HDTV 1080i Ukr/Eng", ReleaseKind.Movie, $"{film.Work.Id} TitleAndYear"),
                ("World Natural Heritage USA: Yellowstone National Park (2013) IPTVRemux", ReleaseKind.Movie, ""),
                ("Yellowstone (S1) (2009) BDRip 1080p H.265 Ukr/Eng | Sub Eng (S1)", ReleaseKind.Tv, ""),
            ],
            releases
                .Select(release => (release.Title, release.Kind, string.Join(", ", release.LibraryMatches.Select(match => $"{match.WorkId} {match.Evidence}"))))
                .OrderBy(release => release.Title, StringComparer.Ordinal));
        await _factory.WithDbAsync(async db =>
            Assert.Equal(
                [
                    ("Yellowstone (S1) (2009) BDRip 1080p H.265 Ukr/Eng | Sub Eng (S1)", ReleaseKind.Tv),
                    ("World Natural Heritage USA: Yellowstone National Park (2013) IPTVRemux", ReleaseKind.Movie),
                    ("Wild Yellowstone She Wolf (2013) HDTV", ReleaseKind.Movie),
                    ("Wild Yellowstone She Wolf (2013) HDTV 1080i Ukr/Eng", ReleaseKind.Movie),
                ],
                (await db.Releases.AsNoTracking().OrderBy(release => release.Position).ToListAsync(cancellationToken))
                    .Select(release => (release.Title, release.Kind))));
    }
}
