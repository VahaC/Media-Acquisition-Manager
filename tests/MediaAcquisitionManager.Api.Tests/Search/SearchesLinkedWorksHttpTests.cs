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

// MAM-34, ADR 0012 — the search page's "In your library" counts an item's TMDb link like the library's own ids, as the
// Library page does (ILibraryLinkReader): a search by a linked id, or by the IMDb id TMDb gave where the library holds
// none, finds the work on identifier evidence, with the person who set it as the matched id's source — the name for the
// owner only. A linked id beside a different library id of its scheme is two values, flagged as a conflict and never
// settled; the id a link replaces finds nothing while the link isn't confirmed.
public sealed class SearchesLinkedWorksHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/searches";
    private const string JellyfinApiKey = "0123456789abcdef0123456789abcdef";

    /// <summary>A Tom &amp; Jerry short Jellyfin found no metadata for.</summary>
    private const string LandingStriplingItemId = "1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a";

    /// <summary>"Чужий" (1979), which Jellyfin gave a wrong TMDb id, 111.</summary>
    private const string AlienItemId = "2b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2b";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private ILibraryItemLinkSeeder _links = null!;
    private AuthorizedUser _owner = null!;
    private AuthorizedUser _member = null!;
    private IntegrationConnectionEntity _livingRoom = null!;

    public SearchesLinkedWorksHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _links = new LibraryItemLinkSeeder(_factory.Scope);
        var users = new UserSeeder(_factory.Scope);
        _owner = await users.CreateAuthorizedOwner("owner");
        _member = await users.CreateAuthorized("member");
        var jellyfin = new FakeJellyfinServer { ApiKey = JellyfinApiKey };
        jellyfin.Movies.Clear();
        jellyfin.Movies.Add(JellyfinSamples.MovieWith(
            LandingStriplingItemId, "120. Landing Stripling", null, 1962, "/media/cartoons/Tom & Jerry/120. Landing Stripling (1962).mkv"));
        jellyfin.Movies.Add(JellyfinSamples.MovieWith(AlienItemId, "Чужий", "Alien", 1979, "/media/movies/Alien (1979).mkv", tmdbId: "111"));
        jellyfin.Arm(_factory.JellyfinHttp);
        _livingRoom = await new IntegrationConnectionSeeder(_factory.Scope).Create("jellyfin", "Living room", apiKey: JellyfinApiKey);
        await new LibrarySnapshotSeeder(_factory.Scope).Sync(_livingRoom, _owner.User);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Create_finds_a_work_by_its_linked_tmdb_id_with_the_owner_who_set_it_as_its_source()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var link = await LinkLandingStriplingAsync(cancellationToken);

        //Act
        // A title no work has: only the linked id can find it.
        var library = await LibraryOfAsync(new SearchRequest("Zzz", TmdbId: "223021"), _owner, cancellationToken);

        //Assert
        var match = Assert.Single(library);
        Assert.Equal((link.MediaWorkId!.Value, MatchEvidence.Identifier), (match.Work.Id, match.Evidence));
        Assert.Equal(
            [new LibraryIdentifierDto(MediaIdentifierScheme.Tmdb, "223021", "owner", ProvenanceSourceKind.Person, link.SetUtc)],
            match.MatchedIdentifiers);
        Assert.Empty(match.ConflictingSchemes);
        Assert.Equal("223021", match.Work.Link!.CatalogId);
    }

    [Fact]
    public async Task Create_as_a_member_shows_a_linked_ids_source_without_the_name_of_who_set_it()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var link = await LinkLandingStriplingAsync(cancellationToken);

        //Act
        var library = await LibraryOfAsync(new SearchRequest("Zzz", TmdbId: "223021"), _member, cancellationToken);

        //Assert
        // The owner's sign-in name is the owner's only: everyone else reads "set by hand".
        var match = Assert.Single(library);
        Assert.Equal(
            [new LibraryIdentifierDto(MediaIdentifierScheme.Tmdb, "223021", null, ProvenanceSourceKind.Person, link.SetUtc)],
            match.MatchedIdentifiers);
        Assert.Null(match.Work.Link!.SetByUserName);
    }

    [Fact]
    public async Task Create_finds_a_work_by_the_imdb_id_tmdb_gave_its_link_with_the_catalog_as_its_source()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var link = await LinkLandingStriplingAsync(cancellationToken);

        //Act
        var library = await LibraryOfAsync(new SearchRequest("Zzz", ImdbId: "TT0056164"), _member, cancellationToken);

        //Assert
        var match = Assert.Single(library);
        Assert.Equal((link.MediaWorkId!.Value, MatchEvidence.Identifier), (match.Work.Id, match.Evidence));
        Assert.Equal(
            [new LibraryIdentifierDto(MediaIdentifierScheme.Imdb, "tt0056164", "TMDb", ProvenanceSourceKind.Metadata)],
            match.MatchedIdentifiers);
    }

    [Theory]
    [InlineData("348")]
    [InlineData("111")]
    public async Task Create_flags_a_linked_id_beside_another_library_id_of_its_scheme_as_a_conflict(string tmdbId)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // Written: Jellyfin took the link's 348, and someone has set its 111 again since — neither wins.
        var link = await LinkAlienAsync(LibraryLinkWriteState.Written, cancellationToken);

        //Act
        var library = await LibraryOfAsync(new SearchRequest("Zzz", TmdbId: tmdbId), _member, cancellationToken);

        //Assert
        var match = Assert.Single(library);
        Assert.Equal((link.MediaWorkId!.Value, MatchEvidence.Identifier), (match.Work.Id, match.Evidence));
        Assert.Equal([MediaIdentifierScheme.Tmdb], match.ConflictingSchemes);
        Assert.Equal("111", match.Work.Link!.ConflictingValue);
    }

    [Fact]
    public async Task Create_never_finds_a_work_by_the_id_its_link_replaces_while_the_library_has_not_taken_the_link()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await LinkAlienAsync(LibraryLinkWriteState.NotWritten, cancellationToken);

        //Act
        var byReplaced = await LibraryOfAsync(new SearchRequest("Zzz", TmdbId: "111"), _member, cancellationToken);
        var byLink = await LibraryOfAsync(new SearchRequest("Zzz", TmdbId: "348"), _member, cancellationToken);

        //Assert
        Assert.Empty(byReplaced);
        Assert.Empty(Assert.Single(byLink).ConflictingSchemes);
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>The short linked to TMDb's 223021 with the IMDb id TMDb gave, written into Jellyfin.</summary>
    private async Task<LibraryItemLinkEntity> LinkLandingStriplingAsync(CancellationToken cancellationToken) =>
        await _links.Create(
            _livingRoom, LandingStriplingItemId, await WorkOfAsync(LandingStriplingItemId, cancellationToken), MediaIdentifierScheme.Tmdb, "223021",
            writeState: LibraryLinkWriteState.Written, setBy: _owner.User,
            configure: link =>
            {
                link.ImdbId = "tt0056164";
                link.ImdbSourceName = "TMDb";
            });

    /// <summary>"Чужий" linked to TMDb's 348 in <paramref name="state"/>, replacing Jellyfin's 111.</summary>
    private async Task<LibraryItemLinkEntity> LinkAlienAsync(LibraryLinkWriteState state, CancellationToken cancellationToken) =>
        await _links.Create(
            _livingRoom, AlienItemId, await WorkOfAsync(AlienItemId, cancellationToken), MediaIdentifierScheme.Tmdb, "348",
            writeState: state, setBy: _owner.User, configure: link => link.SupersededValue = "111");

    /// <summary>The work the library item <paramref name="itemId"/> is a version of.</summary>
    private async Task<Guid> WorkOfAsync(string itemId, CancellationToken cancellationToken)
    {
        var workId = Guid.Empty;
        await _factory.WithDbAsync(async db => workId = await db.MediaVersions
            .Where(version => version.SourceItemId == itemId)
            .Select(version => version.MediaWorkId)
            .SingleAsync(cancellationToken));
        return workId;
    }

    /// <summary>What a search for <paramref name="request"/> by <paramref name="user"/> found in the library; throws
    /// unless it was created.</summary>
    private async Task<IReadOnlyList<LibraryMatchDto>> LibraryOfAsync(SearchRequest request, AuthorizedUser user, CancellationToken cancellationToken)
    {
        var response = await _factory.PostHttpRequest(BaseUrl, user.AccessToken, request);
        if (response.StatusCode != HttpStatusCode.Created)
            throw new InvalidOperationException($"The search answered {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync(cancellationToken)}");
        return (await response.Content.ReadFromJsonAsync<SearchResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!.Library;
    }
}
