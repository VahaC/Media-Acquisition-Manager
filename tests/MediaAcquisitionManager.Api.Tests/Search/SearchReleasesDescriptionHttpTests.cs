using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security;
using System.Text.Json.Nodes;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Abstractions.Metadata;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Seeders;

namespace MediaAcquisitionManager.Api.Tests.Search;

// MAM-30, Vasyl's decision 6, D15 — the Search page reads a release's description too, as Recommendations do: Toloka puts
// its topic's whole title there, Ukrainian title first, where a gateway's title keeps the English part alone or, for a
// Ukrainian-only release, nothing but punctuation. The description adds a match only by an id or by its title and year;
// the title keeps every level (ADR 0012). A work's match is the strongest of the two, a tie going to the title, and a
// TV match counts the episodes the claim that matched names, or the other's when it names none. The page says what each
// text was read as.
public sealed class SearchReleasesDescriptionHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/searches";
    private const string TolokaAddress = "http://prowlarr.test:9696/1/api";
    private const string ApiKey = "planted-torznab-key-0001";
    private const string JellyfinApiKey = "0123456789abcdef0123456789abcdef";

    private const string InterstellarItemId = "1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a";
    private const string Interstellar1999ItemId = "2b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2b";

    private const string TitleWithoutAYear = "Interstellar BDRip 1080p";
    private const string PunctuationTitle = "« » (2014) BDRip";
    private const string TheOtherYearsTitle = "Interstellar (1999) DVDRip";
    private const string TitleOfNeither = "Interstellar Remastered BDRip";
    private const string DescriptionWithoutAYear = "Something Else BDRip";
    private const string TitleWithoutADescription = "Interstellar (2014) BDRip 1080p";
    private const string TitleWithAnId = "Something Else BDRip 720p";

    /// <summary>A Ukrainian-only show among Prowlarr's newest TV (VendorSamples, prowlarr-toloka-latest-tv.xml): its title
    /// cut to its marker, its description whole.</summary>
    private const string UkrainianOnlyShowTitle = "(2026) WEB-DL 1080p (S1E1-25 of ?)";

    private const string UkrainianOnlyShowDescription = "Розслідування з Андрієм Данілевичем (Сезон 1, серії 1-25 з ?) (2026) WEB-DL 1080p";

    private const string LanternsOfItsYear = "Lanterns (S1) (2026) WEB-DL 1080p (S1)";
    private const string LanternsOfAnotherYear = "Lanterns (S1) (2025) WEB-DL 1080p (S1)";

    /// <summary>A description that disagrees with both Lanterns titles about the seasons: what tells whose episodes count.</summary>
    private const string LanternsDescription = "Ліхтарі (Сезони 1-2) / Lanterns (Seasons 1-2) (2026) WEB-DL 1080p";

    /// <summary>A season and no year: its title alone, which its description's title and year outrank.</summary>
    private const string LanternsOfNoYear = "Lanterns (S2) WEB-DL 1080p";

    /// <summary>The show's title and year, and no season marker: a title that names no season.</summary>
    private const string LanternsOfItsYearAlone = "Lanterns (2026) WEB-DL 1080p";

    private const string LanternsDescriptionWithoutASeason = "Ліхтарі / Lanterns (2026) WEB-DL 1080p";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private AuthorizedUser _member = null!;
    private FakeTorznabServer _toloka = null!;

    public SearchReleasesDescriptionHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    /// <summary>
    /// A release's title, its description and the works it matches, strongest first, as "work evidence": A is
    /// "Інтерстеллар" (2014, originally "Interstellar", IMDb tt0816692), B "Interstellar" (1999).
    /// </summary>
    public static TheoryData<string, string[]> Matches => new()
    {
        // The title names no year, so it claims no title; the description's title and year find A, its title alone
        // B, which a description never adds.
        { TitleWithoutAYear, ["A TitleAndYear"] },
        // Jackett's and Prowlarr's Ukrainian-only title: punctuation and a year. Its description names the film.
        { PunctuationTitle, ["A TitleAndYear"] },
        // The title's 1999 film by its title and year, A by its title alone; the description's title and year make A
        // as strong, B keeps the title's evidence.
        { TheOtherYearsTitle, ["A TitleAndYear", "B TitleAndYear"] },
        // The description names the 1999 film with its year: that one alone.
        { TitleOfNeither, ["B TitleAndYear"] },
        // A description that names no year claims no title either: nothing.
        { DescriptionWithoutAYear, [] },
        // No description: the title alone, at every level (ADR 0012).
        { TitleWithoutADescription, ["A TitleAndYear", "B TitleOnly"] },
        // The listing's id is the title's and the description's alike: one match, the title's.
        { TitleWithAnId, ["A Identifier"] },
    };

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        var users = new UserSeeder(_factory.Scope);
        var owner = await users.CreateAuthorizedOwner("owner");
        _member = await users.CreateAuthorized("member");

        var jellyfin = new FakeJellyfinServer { ApiKey = JellyfinApiKey };
        jellyfin.Movies.Clear();
        jellyfin.Movies.Add(JellyfinSamples.MovieWith(
            InterstellarItemId, "Інтерстеллар", "Interstellar", 2014, "/media/movies/Interstellar (2014).mkv", "tt0816692", "157336"));
        jellyfin.Movies.Add(JellyfinSamples.MovieWith(Interstellar1999ItemId, "Interstellar", null, 1999, "/media/movies/Interstellar (1999).avi"));
        // The Ukrainian-only show, titled in Ukrainian alone: three episodes of its first season.
        jellyfin.Series.Add(JellyfinSamples.SeriesWith(ItemId('r'), "Розслідування з Андрієм Данілевичем", 2026, "/media/tv-shows/Rozsliduvannia"));
        jellyfin.Episodes.Add(Episode('s', 'r', 1, 1));
        jellyfin.Episodes.Add(Episode('t', 'r', 1, 2));
        jellyfin.Episodes.Add(Episode('u', 'r', 1, 30));
        // A show of 2026: two episodes of season 1 and one of season 2.
        jellyfin.Series.Add(JellyfinSamples.SeriesWith(ItemId('l'), "Lanterns", 2026, "/media/tv-shows/Lanterns"));
        jellyfin.Episodes.Add(Episode('m', 'l', 1, 1));
        jellyfin.Episodes.Add(Episode('n', 'l', 1, 2));
        jellyfin.Episodes.Add(Episode('o', 'l', 2, 1));
        jellyfin.Arm(_factory.JellyfinHttp);
        var integrations = new IntegrationConnectionSeeder(_factory.Scope);
        await new LibrarySnapshotSeeder(_factory.Scope).Sync(await integrations.Create("jellyfin", "Living room", apiKey: JellyfinApiKey), owner.User);

        _toloka = new FakeTorznabServer(TolokaAddress, ApiKey)
        {
            SearchXml = TorznabSamples.Feed(
                Item(TitleWithoutAYear, "t1", "2000", "Інтерстеллар / Interstellar (2014) BDRip 1080p"),
                Item(PunctuationTitle, "t2", "2000", "Інтерстеллар (2014) BDRip"),
                Item(TheOtherYearsTitle, "t3", "2000", "Інтерстеллар / Interstellar (2014) DVDRip"),
                Item(TitleOfNeither, "t4", "2000", "Interstellar (1999) BDRip"),
                Item(DescriptionWithoutAYear, "t5", "2000", "Інтерстеллар BDRip"),
                Item(TitleWithoutADescription, "t6", "2000"),
                Item(TitleWithAnId, "t7", "2000", "Інтерстеллар (2014) BDRip 720p", """<torznab:attr name="imdbid" value="tt0816692" />""")),
            TvSearchXml = TorznabSamples.Feed(
                Item(UkrainianOnlyShowTitle, "r1", "5080", UkrainianOnlyShowDescription),
                Item(LanternsOfItsYear, "l1", "5000", LanternsDescription),
                Item(LanternsOfAnotherYear, "l2", "5000", LanternsDescription),
                Item(LanternsOfNoYear, "l3", "5000", LanternsDescriptionWithoutASeason),
                Item(LanternsOfItsYearAlone, "l4", "5000", LanternsDescription)),
        };
        _toloka.Arm(_factory.TorznabHttp);
        await integrations.Create("torznab", "Toloka", TolokaAddress, apiKey: ApiKey);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Theory]
    [MemberData(nameof(Matches))]
    public async Task Releases_adds_what_a_description_matches_by_its_title_and_year_to_what_the_title_matches(string title, string[] expected)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var search = await CreateSearchAsync("Interstellar", "2014", cancellationToken);
        var works = WorkNames(search);

        //Act
        var releases = await ReleasesAsync(search, cancellationToken);

        //Assert
        Assert.Equal(["A TitleAndYear", "B TitleOnly"], search.Library.Select(match => $"{works[match.Work.Id]} {match.Evidence}"));
        var release = Assert.Single(releases, release => release.Title == title);
        Assert.Equal(expected, release.LibraryMatches.Select(match => $"{works[match.WorkId]} {match.Evidence}"));
    }

    [Theory]
    [InlineData(PunctuationTitle, new[] { "« »" }, 2014, new[] { "Інтерстеллар" }, 2014)]
    [InlineData(TitleWithoutAYear, new string[0], null, new[] { "Інтерстеллар", "Interstellar" }, 2014)]
    [InlineData(DescriptionWithoutAYear, new string[0], null, new string[0], null)]
    public async Task Releases_says_what_each_title_and_each_description_was_read_as(
        string title, string[] titles, int? year, string[] descriptionTitles, int? descriptionYear)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var search = await CreateSearchAsync("Interstellar", "2014", cancellationToken);

        //Act
        var releases = await ReleasesAsync(search, cancellationToken);

        //Assert
        // The page shows both readings, so a match the title doesn't explain is seen to come from the description.
        var release = Assert.Single(releases, release => release.Title == title);
        Assert.Equal(titles, release.ClaimedWork.Titles);
        Assert.Equal(year, release.ClaimedWork.Year);
        Assert.NotNull(release.DescriptionWork);
        Assert.Equal(descriptionTitles, release.DescriptionWork.Titles);
        Assert.Equal(descriptionYear, release.DescriptionWork.Year);
    }

    [Fact]
    public async Task Releases_reads_no_description_where_the_listing_gave_none()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var search = await CreateSearchAsync("Interstellar", "2014", cancellationToken);

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}/{search.Id}/releases?pageSize=100", _member.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var items = JsonNode.Parse(await response.Content.ReadAsStringAsync(cancellationToken))!["items"]!.AsArray();
        var withoutOne = Assert.Single(items, item => (string?)item!["title"] == TitleWithoutADescription)!;
        // Sent as null, not left out: the page tells "no description" from an older server.
        Assert.True(withoutOne.AsObject().ContainsKey("descriptionWork"));
        Assert.Null(withoutOne["descriptionWork"]);
        Assert.Null(withoutOne["description"]);
    }

    [Fact]
    public async Task Releases_matches_a_ukrainian_only_show_by_its_description_counting_the_episodes_it_names()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var search = await CreateSearchAsync("Розслідування з Андрієм Данілевичем", null, cancellationToken);

        //Act
        var releases = await ReleasesAsync(search, cancellationToken);

        //Assert
        var show = Assert.Single(search.Library);
        var release = Assert.Single(releases, release => release.Title == UkrainianOnlyShowTitle);
        Assert.Equal(ReleaseKind.Tv, release.Kind);
        // The title claims a season and no name; the description the show, its year and the episodes of season 1 — its
        // title without the bracket its marker opens, though the total of episodes isn't known ("з ?").
        Assert.Equal(new ClaimedEpisodesDto(1, 1, 1, 25), release.ClaimedWork.Episodes);
        Assert.Equal(["Розслідування з Андрієм Данілевичем"], release.DescriptionWork!.Titles);
        Assert.Equal(2026, release.DescriptionWork.Year);
        Assert.Equal(new ClaimedEpisodesDto(1, 1, 1, 25), release.DescriptionWork.Episodes);
        // Episodes 1 and 2 of season 1 are among the 25; episode 30 isn't.
        var match = Assert.Single(release.LibraryMatches);
        Assert.Equal((show.Work.Id, MatchEvidence.TitleAndYear, (int?)2), (match.WorkId, match.Evidence, match.HeldEpisodeCount));
    }

    [Theory]
    // Both match by title and year: the tie goes to the title, whose season 1 holds two episodes.
    [InlineData(LanternsOfItsYear, "TitleAndYear held 2")]
    // The title's year isn't the show's: its title alone. The description's title and year are stronger, and its
    // seasons 1 and 2 hold all three.
    [InlineData(LanternsOfAnotherYear, "TitleAndYear held 3")]
    // The description's title and year are stronger than the title alone, but it names no season: the title's season 2,
    // as when the title alone was read, holds one.
    [InlineData(LanternsOfNoYear, "TitleAndYear held 1")]
    // A tie, and the title names no season: the description's seasons 1 and 2 hold all three.
    [InlineData(LanternsOfItsYearAlone, "TitleAndYear held 3")]
    public async Task Releases_counts_the_episodes_the_claim_that_matched_names_or_the_others_when_it_names_none(string title, string expected)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var search = await CreateSearchAsync("Lanterns", null, cancellationToken);

        //Act
        var releases = await ReleasesAsync(search, cancellationToken);

        //Assert
        var match = Assert.Single(Assert.Single(releases, release => release.Title == title).LibraryMatches);
        Assert.Equal(Assert.Single(search.Library).Work.Id, match.WorkId);
        Assert.Equal(expected, $"{match.Evidence} held {match.HeldEpisodeCount}");
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>The search's movies by the names the theory data uses: A the film of 2014, B the one of 1999.</summary>
    private static Dictionary<Guid, string> WorkNames(SearchResponse search) =>
        search.Library.ToDictionary(match => match.Work.Id, match => match.Work.Year == 2014 ? "A" : "B");

    private static string ItemId(char letter) => new(letter, 32);

    /// <summary>An episode of a series of the test, its file named after its place.</summary>
    private static JsonObject Episode(char letter, char seriesLetter, int season, int episode) =>
        JellyfinSamples.EpisodeWith(
            ItemId(letter),
            string.Create(CultureInfo.InvariantCulture, $"Episode {episode}"),
            ItemId(seriesLetter),
            season,
            episode,
            string.Create(CultureInfo.InvariantCulture, $"/media/tv-shows/{seriesLetter}/S{season:00}E{episode:00}.mkv"));

    /// <summary>One release Toloka lists in <paramref name="category"/>, its page under <paramref name="topic"/>, with
    /// <paramref name="description"/> when given and <paramref name="extra"/>'s attrs.</summary>
    private static string Item(string title, string topic, string category, string? description = null, string extra = "") =>
        TorznabSamples.Item(
            title,
            $"{TorznabSamples.DownloadBaseFor(TolokaAddress)}download?apikey={ApiKey}&file={topic}",
            comments: $"https://toloka.to/{topic}",
            extra: $"<category>{category}</category>"
                + (description is null ? "" : $"<description>{SecurityElement.Escape(description)}</description>")
                + extra);

    /// <summary>A search by the member; throws unless it was created.</summary>
    private async Task<SearchResponse> CreateSearchAsync(string text, string? year, CancellationToken cancellationToken)
    {
        var response = await _factory.PostHttpRequest(BaseUrl, _member.AccessToken, new SearchRequest(text, year));
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
