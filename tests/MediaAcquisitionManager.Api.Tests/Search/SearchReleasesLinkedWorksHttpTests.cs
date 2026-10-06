using System.Net;
using System.Net.Http.Json;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Abstractions.Metadata;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Tests.Search;

// MAM-34, MAM-12 — a release is matched to the search's library works by the ids they count, an item's TMDb link
// included (ILibraryLinkReader): the IMDb or TMDb id a listing gives matches the linked id, or the IMDb id TMDb gave where
// the library holds none, as it would the library's own; the id a link replaces matches nothing while the link isn't
// confirmed in the library, and a listing naming it contradicts the linked work.
public sealed class SearchReleasesLinkedWorksHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/searches";
    private const string TolokaAddress = "http://prowlarr.test:9696/1/api";
    private const string ApiKey = "planted-torznab-key-0001";
    private const string JellyfinApiKey = "0123456789abcdef0123456789abcdef";

    /// <summary>A Tom &amp; Jerry short Jellyfin found no metadata for, linked to TMDb's 223021.</summary>
    private const string LandingStriplingItemId = "1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a";

    /// <summary>"Чужий" (1979), which Jellyfin gave a wrong TMDb id, 111, that its link to 348 replaces.</summary>
    private const string AlienItemId = "2b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2b";

    private const string ByLinkedTmdbId = "Something Else BDRip 720p";
    private const string ByTmdbsImdbId = "Another Thing DVDRip";
    private const string ByAliensLink = "Інше (2001) BDRip";
    private const string ByTheReplacedId = "Ще інше (2002) BDRip";
    private const string ByAliensTitleAndTheReplacedId = "Чужий (1979) BDRip";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private AuthorizedUser _member = null!;
    private Guid _landingStripling;
    private Guid _alien;

    public SearchReleasesLinkedWorksHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        var integrations = new IntegrationConnectionSeeder(_factory.Scope);
        var users = new UserSeeder(_factory.Scope);
        var owner = await users.CreateAuthorizedOwner("owner");
        _member = await users.CreateAuthorized("member");

        var jellyfin = new FakeJellyfinServer { ApiKey = JellyfinApiKey };
        jellyfin.Movies.Clear();
        jellyfin.Movies.Add(JellyfinSamples.MovieWith(
            LandingStriplingItemId, "120. Landing Stripling", null, 1962, "/media/cartoons/Tom & Jerry/120. Landing Stripling (1962).mkv"));
        jellyfin.Movies.Add(JellyfinSamples.MovieWith(AlienItemId, "Чужий", "Alien", 1979, "/media/movies/Alien (1979).mkv", tmdbId: "111"));
        jellyfin.Arm(_factory.JellyfinHttp);
        var livingRoom = await integrations.Create("jellyfin", "Living room", apiKey: JellyfinApiKey);
        await new LibrarySnapshotSeeder(_factory.Scope).Sync(livingRoom, owner.User);

        _landingStripling = await WorkOfAsync(LandingStriplingItemId);
        _alien = await WorkOfAsync(AlienItemId);
        var links = new LibraryItemLinkSeeder(_factory.Scope);
        await links.Create(
            livingRoom, LandingStriplingItemId, _landingStripling, MediaIdentifierScheme.Tmdb, "223021", setBy: owner.User,
            configure: link =>
            {
                link.ImdbId = "tt0056164";
                link.ImdbSourceName = "TMDb";
            });
        await links.Create(
            livingRoom, AlienItemId, _alien, MediaIdentifierScheme.Tmdb, "348", writeState: LibraryLinkWriteState.NotWritten, setBy: owner.User,
            configure: link => link.SupersededValue = "111");

        var toloka = new FakeTorznabServer(TolokaAddress, ApiKey)
        {
            SearchXml = TorznabSamples.Feed(
                Item(ByLinkedTmdbId, "t1", """<torznab:attr name="tmdbid" value="223021" />"""),
                Item(ByTmdbsImdbId, "t2", """<torznab:attr name="imdbid" value="tt0056164" />"""),
                Item(ByAliensLink, "t3", """<torznab:attr name="tmdbid" value="348" />"""),
                Item(ByTheReplacedId, "t4", """<torznab:attr name="tmdbid" value="111" />"""),
                Item(ByAliensTitleAndTheReplacedId, "t5", """<torznab:attr name="tmdbid" value="111" />""")),
        };
        toloka.Arm(_factory.TorznabHttp);
        await integrations.Create("torznab", "Toloka", TolokaAddress, apiKey: ApiKey);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Releases_match_a_work_by_its_linked_tmdb_id_or_the_imdb_id_tmdb_gave_its_link()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // A title no work has: the search finds the short by its linked id alone.
        var search = await CreateSearchAsync(new SearchRequest("Zzz", TmdbId: "223021"), cancellationToken);

        //Act
        var releases = await ReleasesAsync(search, cancellationToken);

        //Assert
        Assert.Equal([_landingStripling], search.Library.Select(match => match.Work.Id));
        Assert.Equal([(_landingStripling, MatchEvidence.Identifier)], MatchesOf(releases, ByLinkedTmdbId));
        Assert.Equal([(_landingStripling, MatchEvidence.Identifier)], MatchesOf(releases, ByTmdbsImdbId));
        Assert.Empty(Assert.Single(releases, release => release.Title == ByLinkedTmdbId).LibraryMatches.Single().ConflictingSchemes);
    }

    [Fact]
    public async Task Releases_match_a_work_by_its_link_never_by_the_id_the_link_replaces_while_the_library_has_not_taken_it()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var search = await CreateSearchAsync(new SearchRequest("Чужий", "1979"), cancellationToken);

        //Act
        var releases = await ReleasesAsync(search, cancellationToken);

        //Assert
        Assert.Equal([(_alien, MatchEvidence.Identifier)], MatchesOf(releases, ByAliensLink));
        Assert.Empty(MatchesOf(releases, ByTheReplacedId));
        // Its title and year match, and the listing's 111 contradicts the linked 348.
        var byTitle = Assert.Single(Assert.Single(releases, release => release.Title == ByAliensTitleAndTheReplacedId).LibraryMatches);
        Assert.Equal((_alien, MatchEvidence.TitleAndYear), (byTitle.WorkId, byTitle.Evidence));
        Assert.Equal([MediaIdentifierScheme.Tmdb], byTitle.ConflictingSchemes);
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    private static List<(Guid WorkId, MatchEvidence Evidence)> MatchesOf(IReadOnlyList<SearchReleaseDto> releases, string title) =>
        [.. Assert.Single(releases, release => release.Title == title).LibraryMatches.Select(match => (match.WorkId, match.Evidence))];

    /// <summary>The work the library item <paramref name="itemId"/> is a version of.</summary>
    private async Task<Guid> WorkOfAsync(string itemId)
    {
        var workId = Guid.Empty;
        await _factory.WithDbAsync(async db => workId = await db.MediaVersions
            .Where(version => version.SourceItemId == itemId)
            .Select(version => version.MediaWorkId)
            .SingleAsync());
        return workId;
    }

    /// <summary>One release Toloka lists, its page under <paramref name="topic"/>, with
    /// <paramref name="extra"/>'s attrs (its ids).</summary>
    private static string Item(string title, string topic, string extra = "") =>
        TorznabSamples.Item(
            title,
            $"{TorznabSamples.DownloadBaseFor(TolokaAddress)}download?apikey={ApiKey}&file={topic}",
            comments: $"https://toloka.to/{topic}",
            extra: "<category>2000</category>" + extra);

    /// <summary>A search by the member; throws unless it was created.</summary>
    private async Task<SearchResponse> CreateSearchAsync(SearchRequest request, CancellationToken cancellationToken)
    {
        var response = await _factory.PostHttpRequest(BaseUrl, _member.AccessToken, request);
        if (response.StatusCode != HttpStatusCode.Created)
            throw new InvalidOperationException($"The search answered {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync(cancellationToken)}");
        return (await response.Content.ReadFromJsonAsync<SearchResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
    }

    /// <summary>Every release of <paramref name="search"/>, on one page; throws unless it was listed.</summary>
    private async Task<IReadOnlyList<SearchReleaseDto>> ReleasesAsync(SearchResponse search, CancellationToken cancellationToken)
    {
        var response = await _factory.GetHttpRequest($"{BaseUrl}/{search.Id}/releases?pageSize=100", _member.AccessToken);
        if (response.StatusCode != HttpStatusCode.OK)
            throw new InvalidOperationException($"The list answered {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync(cancellationToken)}");
        return (await response.Content.ReadFromJsonAsync<SearchReleaseListResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!.Items;
    }
}
