using System.Net;
using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Abstractions.Search;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Search;
using MediaAcquisitionManager.Infrastructure.Integrations.Torznab;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;

namespace MediaAcquisitionManager.Infrastructure.Tests.Integrations;

// MAM-33 — a feed's newest releases (ReleaseSearchMode.Latest): asked as Torznab's general search with no q,
// in the categories ReleaseSearchPlanning.ForLatest picks from the feed's caps, and read like any search
// answer — the real answers of Toloka through Prowlarr to cat=2000 and cat=5000 (VendorSamples/Torznab),
// newest first, each with Toloka's own topic title as its description and its kind told by its categories
// (ADR 0020). A query no core code builds is TorznabAdapterSearchTests'.
public sealed class TorznabAdapterSearchLatestTests : IDisposable
{
    private const string BaseUrl = "http://prowlarr.test:9696/1/api";
    private const string ApiKey = "0f9e8d7c6b5a49382716a5b4c3d2e1f0";

    private static readonly IntegrationConnectionSettings Settings = new(null, BaseUrl, null, null, ApiKey);

    /// <summary>The caps sample's movie categories with TV/Anime and TV/Documentary, which hold films too,
    /// as <c>ReleaseSearchPlanning.ForLatest</c> asks for films.</summary>
    private static readonly string[] MovieCategories = ["2000", "2020", "2040", "2070", "5070", "5080"];

    /// <summary>The caps sample's TV categories, as <c>ReleaseSearchPlanning.ForLatest</c> asks for shows.</summary>
    private static readonly string[] TvCategories = ["5000", "5040", "5050", "5060", "5070", "5080"];

    private readonly IntegrationAdapterHarness _harness = new();

    private IReleaseSearchProvider Search => _harness.Adapter<IReleaseSearchProvider>(TorznabAdapter.TypeId);

    public void Dispose() => _harness.Dispose();

    [Theory]
    [InlineData(true, false, "2000,2020,2040,2070,5070,5080")]
    [InlineData(false, true, "5000,5040,5050,5060,5070,5080")]
    [InlineData(true, true, "2000,2020,2040,2070,5000,5040,5050,5060,5070,5080")]
    public async Task Search_of_the_newest_releases_sends_a_general_search_with_no_text_in_the_categories_planned_from_the_caps(
        bool movies, bool tv, string expectedCategories)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        new FakeTorznabServer(BaseUrl, ApiKey) { SearchXml = TorznabSamples.LatestMovies(BaseUrl, ApiKey) }.Arm(_harness.TorznabHttp);
        var caps = await Search.GetCapabilitiesAsync(Settings, cancellationToken);
        var query = ReleaseSearchPlanning.ForLatest(movies, tv, caps.Value!)!;

        //Act
        var result = await Search.SearchAsync(Settings, query, cancellationToken);

        //Assert
        // No q, no offset: Toloka's gateway answers its latest 90 to one request, and answered offset=50 and
        // offset=100 with no release at all (VendorSamples, 2026-10-05).
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        Assert.Equal(
            [
                $"{BaseUrl}?t=caps&apikey={ApiKey}",
                $"{BaseUrl}?t=search&apikey={ApiKey}&cat={expectedCategories}&limit=100",
            ],
            _harness.TorznabHttp.Requests.Select(request => request.Uri.AbsoluteUri));
    }

    [Fact]
    public async Task Search_of_the_newest_releases_sends_only_the_parameters_it_has()
    {
        //Arrange
        _harness.TorznabHttp.Handler = (_, _) => RssXml(TorznabSamples.Feed());
        var query = new ReleaseSearchQuery(ReleaseSearchMode.Latest, null, null, [], ["2000", "5070"], 90);

        //Act
        await Search.SearchAsync(Settings with { ApiKey = null }, query, TestContext.Current.CancellationToken);

        //Assert
        var request = Assert.Single(_harness.TorznabHttp.Requests);
        Assert.Equal("http://prowlarr.test:9696/1/api?t=search&cat=2000,5070&limit=90", request.Uri.AbsoluteUri);
    }

    [Fact]
    public async Task Search_of_the_newest_movies_reads_the_sample_newest_first_with_each_description()
    {
        //Arrange
        _harness.TorznabHttp.Handler = (_, _) => RssXml(TorznabSamples.LatestMovies(BaseUrl, ApiKey));

        //Act
        var result = await Search.SearchAsync(Settings, Latest(MovieCategories), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        var hits = result.Value!;
        Assert.Equal(TorznabSamples.LatestMoviesItemCount, hits.Count);
        // The feed's order, which is newest first.
        Assert.Equal(hits.Select(hit => hit.PublishedUtc).OrderDescending(), hits.Select(hit => hit.PublishedUtc));
        var first = hits[0];
        Assert.Equal("https://toloka.to/t700691", first.SourceItemId);
        Assert.Equal("Those Who Wish Me Dead (2021) BDRip 1080p 2xUkr/Eng | Sub Ukr/Eng", first.Title);
        Assert.Equal("Ті, хто бажають моєї смерті / Those Who Wish Me Dead (2021) BDRip 1080p 2xUkr/Eng | Sub Ukr/Eng", first.Description);
        // "Mon, 05 Oct 2026 00:00:00 +0300".
        Assert.Equal(new DateTime(2026, 10, 4, 21, 0, 0, DateTimeKind.Utc), first.PublishedUtc);
        Assert.Equal(["2040", "100096"], first.CategoryIds);
        Assert.Equal("Airplane! (1980) BDRemux 1080p Ukr/Eng | Sub Eng", hits[^1].Title);
        // Toloka's topic title for every one; its double space read as one.
        Assert.All(hits, hit => Assert.False(string.IsNullOrEmpty(hit.Description)));
        Assert.Contains(hits, hit => hit.Description == "Охоронці та найманці / Shi yue wei cheng (2009) BDRip-AVC Chi | sub Ukr");
        Assert.All(hits, hit => Assert.Equal(ReleaseKind.Movie, hit.Kind));
    }

    [Fact]
    public async Task Search_of_the_newest_tv_releases_reads_each_as_tv_by_its_categories_and_its_season_or_episode_markers()
    {
        //Arrange
        _harness.TorznabHttp.Handler = (_, _) => RssXml(TorznabSamples.LatestTv(BaseUrl, ApiKey));

        //Act
        var result = await Search.SearchAsync(Settings, Latest(TvCategories), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        var hits = result.Value!;
        Assert.Equal(TorznabSamples.LatestTvItemCount, hits.Count);
        Assert.Equal("Lanterns (S1) (2026) WEB-DL 1080p H.265 (S1)", hits[0].Title);
        Assert.Equal("Ліхтарі (Сезон 1) / Lanterns (Season 1) (2026) WEB-DL 1080p H.265", hits[0].Description);
        // Every TV category of the sample: TV, TV/HD and TV/Sport by the category alone, the anime of TV/Anime
        // and the documentary series of TV/Documentary by the seasons or episodes each names.
        Assert.Equal(["5000", "5040", "5060", "5070", "5080"], hits.Select(hit => hit.CategoryIds[0]).Distinct().Order(StringComparer.Ordinal));
        Assert.All(hits, hit => Assert.Equal(ReleaseKind.Tv, hit.Kind));
        Assert.All(
            hits.Where(hit => hit.CategoryIds.Contains("5070") || hit.CategoryIds.Contains("5080")),
            hit => Assert.True(ReleaseWorkClaims.ClaimsSeasonsOrEpisodes(hit.Title)));
        // A Ukrainian-only show: Prowlarr's title kept its marker and nothing else, the description all of it.
        var documentary = Assert.Single(hits, hit => hit.CategoryIds.Contains("5080"));
        Assert.Equal("(2026) WEB-DL 1080p (S1E1-25 of ?)", documentary.Title);
        Assert.Equal("Розслідування з Андрієм Данілевичем (Сезон 1, серії 1-25 з ?) (2026) WEB-DL 1080p", documentary.Description);
    }

    [Theory]
    // A listing in no category is what the asked categories hold, as a general search's: TV in TV's alone,
    // TV/Anime and TV/Documentary included — nothing in the listing says it is a film — and a movie when
    // any other is asked beside them.
    [InlineData("5000,5040", ReleaseKind.Tv)]
    [InlineData("5000,5040,5050,5060,5070,5080", ReleaseKind.Tv)]
    [InlineData("2000,2020,2040,2070,5070,5080", ReleaseKind.Movie)]
    [InlineData("2000,2040", ReleaseKind.Movie)]
    [InlineData("", ReleaseKind.Movie)]
    public async Task Search_of_the_newest_releases_gives_a_listing_in_no_category_the_kind_its_categories_ask(
        string askedCategoryIds, ReleaseKind expected)
    {
        //Arrange
        _harness.TorznabHttp.Handler = (_, _) => RssXml(TorznabSamples.Feed(
            TorznabSamples.Item("Akira (1988) BDRemux 1080p", "http://prowlarr.test:9696/1/download?file=x")));

        //Act
        var result = await Search.SearchAsync(
            Settings, Latest(askedCategoryIds.Split(',', StringSplitOptions.RemoveEmptyEntries)), TestContext.Current.CancellationToken);

        //Assert
        var hit = Assert.Single(result.Value!);
        Assert.Empty(hit.CategoryIds);
        Assert.Equal(expected, hit.Kind);
    }

    private static ReleaseSearchQuery Latest(IReadOnlyList<string> categories) =>
        new(ReleaseSearchMode.Latest, null, null, [], categories, ReleaseSearchPlanning.MaximumResults);

    private static HttpResponseMessage RssXml(string body) => VendorResponses.Text(body, HttpStatusCode.OK, "application/rss+xml");
}
