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

// MAM-12, Vasyl's decision 2 — every release is matched to the library copies the search found: by the
// title and year its own title claims, and by the IMDb or TMDb id its listing gives (the Torznab attrs,
// ADR 0004's question for MAM-12). Every work a release matches is listed, strongest first; an id the
// listing gives that a work contradicts is flagged. A title without a year claims no title at all — it
// can't be told from its quality tail — so such a release matches only by an id. A release is matched
// against the search's works only, never the whole library, and against the library as it is now.
public sealed class SearchReleasesMatchingHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/searches";
    private const string TolokaAddress = "http://prowlarr.test:9696/1/api";
    private const string ApiKey = "planted-torznab-key-0001";
    private const string JellyfinApiKey = "0123456789abcdef0123456789abcdef";

    private const string InterstellarItemId = "1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a";
    private const string Interstellar1999ItemId = "2b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2b";
    private const string InterstellarWarsItemId = "3c3c3c3c3c3c3c3c3c3c3c3c3c3c3c3c";

    private const string ByTitleAndYear = "Interstellar (2014) BDRip 1080p";
    private const string ByBothTitles = "Інтерстеллар / Interstellar (2014) BDRip";
    private const string ByTheOtherYear = "Interstellar (1999) DVDRip";
    private const string ByImdbIdAlone = "Something Else BDRip 720p";
    private const string WithoutAYear = "Interstellar BDRip 1080p";
    private const string WithAConflictingImdbId = "Interstellar (2014) 2160p";
    private const string ByBothIds = "Interstellar.2014.1080p.BluRay.x264-SPARKS";
    private const string ByTmdbId = "Interstellar (2014) WEB-DL";
    private const string ByImdbNumber = "Interstellar (2014) Remux";
    private const string WithAConflictingTmdbId = "Interstellar (2014) HDTV";
    private const string OfAnotherFilm = "Interstellar Wars (2014) 1080p";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private IIntegrationConnectionSeeder _integrations = null!;
    private AuthorizedUser _owner = null!;
    private AuthorizedUser _member = null!;
    private FakeJellyfinServer _livingRoom = null!;
    private IntegrationConnectionEntity _livingRoomIntegration = null!;
    private FakeTorznabServer _toloka = null!;

    public SearchReleasesMatchingHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    /// <summary>
    /// A release title and the works it matches, strongest first, each as "work evidence conflicts":
    /// A is "Інтерстеллар" (2014, originally "Interstellar", IMDb tt0816692, TMDb 157336), B "Interstellar"
    /// (1999, no ids).
    /// </summary>
    public static TheoryData<string, string[]> Matches => new()
    {
        // By the title and year the title claims; the 1999 film by its title only.
        { ByTitleAndYear, ["A TitleAndYear", "B TitleOnly"] },
        // Toloka's "localized / original (year)": either alternative can match.
        { ByBothTitles, ["A TitleAndYear", "B TitleOnly"] },
        // The other year's film first: strongest first, whatever the search found first.
        { ByTheOtherYear, ["B TitleAndYear", "A TitleOnly"] },
        // By the listing's IMDb id alone (the title names another film).
        { ByImdbIdAlone, ["A Identifier"] },
        // No year: no title can be read, so no title match.
        { WithoutAYear, [] },
        // The listing's id contradicts A's: listed on its title's evidence, with the conflict.
        { WithAConflictingImdbId, ["A TitleAndYear Imdb", "B TitleOnly"] },
        { ByBothIds, ["A Identifier", "B TitleOnly"] },
        { ByTmdbId, ["A Identifier", "B TitleOnly"] },
        // Newznab's number without its leading zero is the same id.
        { ByImdbNumber, ["A Identifier", "B TitleOnly"] },
        // One id agrees, the other doesn't: the id matches, and the disagreement is flagged.
        { WithAConflictingTmdbId, ["A Identifier Tmdb", "B TitleOnly"] },
        // A film the library holds but the search didn't find: not offered.
        { OfAnotherFilm, [] },
    };

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _integrations = new IntegrationConnectionSeeder(_factory.Scope);
        var users = new UserSeeder(_factory.Scope);
        _owner = await users.CreateAuthorizedOwner("owner");
        _member = await users.CreateAuthorized("member");

        _livingRoom = new FakeJellyfinServer { ApiKey = JellyfinApiKey };
        _livingRoom.Movies.Clear();
        _livingRoom.Movies.Add(JellyfinSamples.MovieWith(
            InterstellarItemId, "Інтерстеллар", "Interstellar", 2014, "/media/movies/Interstellar (2014).mkv", "tt0816692", "157336"));
        _livingRoom.Movies.Add(JellyfinSamples.MovieWith(Interstellar1999ItemId, "Interstellar", null, 1999, "/media/movies/Interstellar (1999).avi"));
        _livingRoom.Movies.Add(JellyfinSamples.MovieWith(
            InterstellarWarsItemId, "Interstellar Wars", null, 2014, "/media/movies/Interstellar Wars (2014).mkv", "tt5083736", "374205"));
        _livingRoom.Arm(_factory.JellyfinHttp);
        _livingRoomIntegration = await _integrations.Create("jellyfin", "Living room", apiKey: JellyfinApiKey);
        await new LibrarySnapshotSeeder(_factory.Scope).Sync(_livingRoomIntegration, _owner.User);

        _toloka = new FakeTorznabServer(TolokaAddress, ApiKey)
        {
            SearchXml = TorznabSamples.Feed(
                Item(ByTitleAndYear, "t1"),
                Item(ByBothTitles, "t2"),
                Item(ByTheOtherYear, "t3"),
                Item(ByImdbIdAlone, "t4", """<torznab:attr name="imdbid" value="0816692" />"""),
                Item(WithoutAYear, "t5"),
                Item(WithAConflictingImdbId, "t6", """<torznab:attr name="imdbid" value="tt9999999" />"""),
                Item(ByBothIds, "t7", """<torznab:attr name="imdbid" value="tt0816692" /><torznab:attr name="tmdbid" value="157336" />"""),
                Item(ByTmdbId, "t8", """<torznab:attr name="tmdbid" value="157336" />"""),
                Item(ByImdbNumber, "t9", """<torznab:attr name="imdb" value="816692" />"""),
                Item(WithAConflictingTmdbId, "t10", """<torznab:attr name="imdbid" value="tt0816692" /><torznab:attr name="tmdbid" value="999" />"""),
                Item(OfAnotherFilm, "t11")),
        };
        _toloka.Arm(_factory.TorznabHttp);
        await _integrations.Create("torznab", "Toloka", TolokaAddress, apiKey: ApiKey);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Theory]
    [MemberData(nameof(Matches))]
    public async Task Releases_lists_every_work_of_the_search_a_release_matches_strongest_first(string title, string[] expected)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var search = await CreateSearchAsync("Interstellar", "2014", cancellationToken);
        var works = await WorkNamesAsync(cancellationToken);

        //Act
        var releases = await ReleasesAsync(search, cancellationToken);

        //Assert
        // The search found A and B, and not "Interstellar Wars".
        Assert.Equal([MatchEvidence.TitleAndYear, MatchEvidence.TitleOnly], search.Library.Select(match => match.Evidence));
        var release = Assert.Single(releases, release => release.Title == title);
        Assert.Equal(
            expected,
            release.LibraryMatches.Select(match => $"{works[match.WorkId]} {match.Evidence}" + string.Concat(match.ConflictingSchemes.Select(scheme => $" {scheme}"))));
    }

    [Theory]
    [InlineData(ByTitleAndYear, new[] { "Interstellar" }, 2014)]
    [InlineData(ByBothTitles, new[] { "Інтерстеллар", "Interstellar" }, 2014)]
    [InlineData(ByBothIds, new[] { "Interstellar" }, 2014)]
    [InlineData(ByImdbIdAlone, new string[0], null)]
    [InlineData(WithoutAYear, new string[0], null)]
    [InlineData(OfAnotherFilm, new[] { "Interstellar Wars" }, 2014)]
    public async Task Releases_says_what_each_title_was_read_as(string title, string[] titles, int? year)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var search = await CreateSearchAsync("Interstellar", "2014", cancellationToken);

        //Act
        var releases = await ReleasesAsync(search, cancellationToken);

        //Assert
        var claimedWork = Assert.Single(releases, release => release.Title == title).ClaimedWork;
        Assert.Equal(titles, claimedWork.Titles);
        Assert.Equal(year, claimedWork.Year);
    }

    [Fact]
    public async Task Releases_lists_the_ids_each_listing_gives_and_keeps_them_with_the_release()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var search = await CreateSearchAsync("Interstellar", "2014", cancellationToken);

        //Act
        var releases = await ReleasesAsync(search, cancellationToken);

        //Assert
        string IdsOf(string title) => string.Join(", ", Assert.Single(releases, release => release.Title == title).Identifiers.Select(id => $"{id.Scheme} {id.Value}"));
        // IMDb first; an IMDb number padded to its seven digits with its "tt".
        Assert.Equal("Imdb tt0816692, Tmdb 157336", IdsOf(ByBothIds));
        Assert.Equal("Imdb tt0816692", IdsOf(ByImdbIdAlone));
        Assert.Equal("Imdb tt0816692", IdsOf(ByImdbNumber));
        Assert.Equal("Tmdb 157336", IdsOf(ByTmdbId));
        Assert.Equal("", IdsOf(ByTitleAndYear));
        await _factory.WithDbAsync(async db =>
        {
            var stored = await db.Releases.AsNoTracking().ToDictionaryAsync(release => release.Title, cancellationToken);
            Assert.Equal(("tt0816692", "157336"), (stored[ByBothIds].ImdbId, stored[ByBothIds].TmdbId));
            Assert.Equal(("tt0816692", (string?)null), (stored[ByImdbNumber].ImdbId, stored[ByImdbNumber].TmdbId));
            Assert.Equal(("tt9999999", (string?)null), (stored[WithAConflictingImdbId].ImdbId, stored[WithAConflictingImdbId].TmdbId));
        });
    }

    [Fact]
    public async Task Releases_names_each_matched_work_by_its_id_in_the_search()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var search = await CreateSearchAsync("Interstellar", "2014", cancellationToken);

        //Act
        var releases = await ReleasesAsync(search, cancellationToken);

        //Assert
        // The page finds the card a release matches among the search's own.
        var libraryIds = search.Library.Select(match => match.Work.Id).ToHashSet();
        Assert.All(releases.SelectMany(release => release.LibraryMatches), match => Assert.Contains(match.WorkId, libraryIds));
        Assert.Equal(
            search.Library.Select(match => match.Work.Id),
            Assert.Single(releases, release => release.Title == ByTitleAndYear).LibraryMatches.Select(match => match.WorkId));
    }

    [Fact]
    public async Task Releases_of_a_search_by_an_id_still_match_each_release_on_its_own_evidence()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var search = await CreateSearchAsync("Interstellar", "2014", cancellationToken, imdbId: "tt0816692");
        var works = await WorkNamesAsync(cancellationToken);

        //Act
        var releases = await ReleasesAsync(search, cancellationToken);

        //Assert
        // The search found A by its id; a release without one matches it by its title only.
        Assert.Equal(MatchEvidence.Identifier, search.Library[0].Evidence);
        Assert.Equal(
            ["A TitleAndYear", "B TitleOnly"],
            Assert.Single(releases, release => release.Title == ByTitleAndYear).LibraryMatches.Select(match => $"{works[match.WorkId]} {match.Evidence}"));
    }

    [Theory]
    [InlineData("TT0816692", null)]
    [InlineData(null, "157336")]
    public async Task Releases_of_a_search_by_an_id_alone_are_matched_to_the_work_that_id_found(string? imdbId, string? tmdbId)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // A title no work has: the list reads the search's works again from the id its row keeps.
        var search = await CreateSearchAsync("Zzz", null, cancellationToken, imdbId, tmdbId);
        var works = await WorkNamesAsync(cancellationToken);

        //Act
        var releases = await ReleasesAsync(search, cancellationToken);

        //Assert
        Assert.Equal(["A Identifier"], search.Library.Select(match => $"{works[match.Work.Id]} {match.Evidence}"));
        string[] MatchesOf(string title) =>
            [.. Assert.Single(releases, release => release.Title == title).LibraryMatches.Select(match => $"{works[match.WorkId]} {match.Evidence}")];
        Assert.Equal(["A Identifier"], MatchesOf(ByImdbIdAlone));
        Assert.Equal(["A TitleAndYear"], MatchesOf(ByTitleAndYear));
        Assert.Equal(["A Identifier"], MatchesOf(ByTmdbId));
        // "Zzz" doesn't find the 1999 film, so no release is matched to it: the works came from the id.
        Assert.DoesNotContain(releases.SelectMany(release => release.LibraryMatches), match => works[match.WorkId] != "A");
    }

    [Fact]
    public async Task Releases_of_a_search_that_found_nothing_in_the_library_match_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // The feed answers the same whatever is asked; the library holds no "Zzz".
        var search = await CreateSearchAsync("Zzz", null, cancellationToken);

        //Act
        var releases = await ReleasesAsync(search, cancellationToken);

        //Assert
        Assert.Empty(search.Library);
        Assert.Equal(11, releases.Count);
        Assert.All(releases, release => Assert.Empty(release.LibraryMatches));
    }

    [Fact]
    public async Task Releases_match_the_library_as_it_is_now()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var search = await CreateSearchAsync("Interstellar", "2014", cancellationToken);
        var works = await WorkNamesAsync(cancellationToken);
        // A sync after the search: the 1999 film is gone from the library.
        _livingRoom.Movies.RemoveAll(movie => (string?)movie["Id"] == Interstellar1999ItemId);
        await new LibrarySnapshotSeeder(_factory.Scope).Sync(_livingRoomIntegration, _owner.User);

        //Act
        var releases = await ReleasesAsync(search, cancellationToken);

        //Assert
        Assert.Equal(
            ["A TitleAndYear"],
            Assert.Single(releases, release => release.Title == ByTitleAndYear).LibraryMatches.Select(match => $"{works[match.WorkId]} {match.Evidence}"));
        Assert.Equal(
            ["A TitleOnly"],
            Assert.Single(releases, release => release.Title == ByTheOtherYear).LibraryMatches.Select(match => $"{works[match.WorkId]} {match.Evidence}"));
    }

    [Fact]
    public async Task Releases_match_a_scene_name_without_the_apostrophe_the_library_title_keeps()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        const string oceansItemId = "4d4d4d4d4d4d4d4d4d4d4d4d4d4d4d4d";
        const string sceneName = "Oceans.Eleven.2001.1080p.BluRay";
        // MAM-12 — a scene name drops the apostrophe, and a title key does too, so the two are one title.
        _livingRoom.Movies.Add(JellyfinSamples.MovieWith(oceansItemId, "Ocean's Eleven", null, 2001, "/media/movies/Ocean's Eleven (2001).mkv"));
        await new LibrarySnapshotSeeder(_factory.Scope).Sync(_livingRoomIntegration, _owner.User);
        _toloka.SearchXml = TorznabSamples.Feed(Item(sceneName, "o1"));
        var search = await CreateSearchAsync("Oceans Eleven", "2001", cancellationToken);
        var oceans = Guid.Empty;
        await _factory.WithDbAsync(async db => oceans = await db.MediaVersions
            .Where(version => version.SourceItemId == oceansItemId)
            .Select(version => version.MediaWorkId)
            .SingleAsync(cancellationToken));

        //Act
        var releases = await ReleasesAsync(search, cancellationToken);

        //Assert
        // The search typed without it finds the work, and the release named without it matches it.
        Assert.Equal([(oceans, MatchEvidence.TitleAndYear)], search.Library.Select(match => (match.Work.Id, match.Evidence)));
        var release = Assert.Single(releases);
        Assert.Equal((sceneName, "Oceans Eleven", (int?)2001), (release.Title, Assert.Single(release.ClaimedWork.Titles), release.ClaimedWork.Year));
        Assert.Equal([(oceans, MatchEvidence.TitleAndYear)], release.LibraryMatches.Select(match => (match.WorkId, match.Evidence)));
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>One release Toloka lists, its page under <paramref name="topic"/>, with
    /// <paramref name="extra"/>'s attrs (its ids).</summary>
    private static string Item(string title, string topic, string extra = "") =>
        TorznabSamples.Item(
            title,
            $"{TorznabSamples.DownloadBaseFor(TolokaAddress)}download?apikey={ApiKey}&file={topic}",
            comments: $"https://toloka.to/{topic}",
            extra: "<category>2000</category>" + extra);

    /// <summary>The works of the library by the names the theory data uses: A, B and W.</summary>
    private async Task<Dictionary<Guid, string>> WorkNamesAsync(CancellationToken cancellationToken)
    {
        var names = new Dictionary<Guid, string>();
        await _factory.WithDbAsync(async db =>
        {
            foreach (var (itemId, name) in new[] { (InterstellarItemId, "A"), (Interstellar1999ItemId, "B"), (InterstellarWarsItemId, "W") })
            {
                var workId = await db.MediaVersions.Where(version => version.SourceItemId == itemId).Select(version => version.MediaWorkId).SingleAsync(cancellationToken);
                names[workId] = name;
            }
        });
        return names;
    }

    /// <summary>A search by the member; throws unless it was created.</summary>
    private async Task<SearchResponse> CreateSearchAsync(
        string text, string? year, CancellationToken cancellationToken, string? imdbId = null, string? tmdbId = null)
    {
        var response = await _factory.PostHttpRequest(BaseUrl, _member.AccessToken, new SearchRequest(text, year, imdbId, tmdbId));
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
