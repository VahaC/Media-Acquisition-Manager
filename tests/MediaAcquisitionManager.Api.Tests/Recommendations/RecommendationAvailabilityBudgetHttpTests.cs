using MediaAcquisitionManager.Api.Services.Recommendations;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Tests.Recommendations;

// MAM-33, ADR 0024, Vasyl's decision 5 — how many searches the availability check runs: each user's daily limit,
// counted over the last 24 hours, split evenly over the tabs they have on (60 over four tabs is 15 each), a tab past
// its share waiting while another tab has works due and taking the share of tabs with nothing due; none past the
// limit, none at all with a limit of 0; at most four a pass, the users served in turn; a search freed 24 hours after
// it ran. Earlier searches are planted in the budget log; each pass is the app's own checker against Toloka faked at
// the network, answering nothing found.
public sealed class RecommendationAvailabilityBudgetHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string FeedAddress = "http://prowlarr.test:9696/1/api";
    private const string ApiKey = "planted-torznab-key-0001";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private ICatalogWorkSeeder _works = null!;
    private IRecommendationCandidateSeeder _candidates = null!;
    private IRecommendationSearchSeeder _searches = null!;
    private IUserRecommendationSettingsSeeder _settings = null!;
    private IUserSeeder _users = null!;
    private UserEntity _anna = null!;
    private int _nextCatalogId = 1000;

    public RecommendationAvailabilityBudgetHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _works = new CatalogWorkSeeder(_factory.Scope);
        _candidates = new RecommendationCandidateSeeder(_factory.Scope);
        _searches = new RecommendationSearchSeeder(_factory.Scope);
        _settings = new UserRecommendationSettingsSeeder(_factory.Scope);
        _users = new UserSeeder(_factory.Scope);
        _anna = await _users.Create("anna");
        await new IntegrationConnectionSeeder(_factory.Scope).Create("torznab", "Toloka", FeedAddress, apiKey: ApiKey);
        new FakeTorznabServer(FeedAddress, ApiKey) { SearchXml = TorznabSamples.Feed() }.Arm(_factory.TorznabHttp);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Check_keeps_a_tab_at_its_share_waiting_while_another_tab_has_works_due()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // The default limit, 60 over the four tabs: Movies has spent its 15.
        await _searches.Log(_anna, RecommendationTab.Movies, 15);
        await PlantAsync(RecommendationTab.Movies, 1, score: 9);
        await PlantAsync(RecommendationTab.Series, 5);

        //Act
        await _factory.CheckRecommendationAvailabilityAsync(cancellationToken);

        //Assert
        Assert.Equal([RecommendationTab.Series, RecommendationTab.Series, RecommendationTab.Series, RecommendationTab.Series], await NewSearchTabsAsync(cancellationToken));
    }

    [Fact]
    public async Task Check_gives_the_share_of_tabs_with_nothing_due_to_the_tabs_that_have_works_due()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // Movies and Series have spent their 15 each; Cartoons and Documentaries have nothing due.
        await _searches.Log(_anna, RecommendationTab.Movies, 15);
        await _searches.Log(_anna, RecommendationTab.Series, 15);
        await PlantAsync(RecommendationTab.Movies, 3);
        await PlantAsync(RecommendationTab.Series, 3);

        //Act
        await _factory.CheckRecommendationAvailabilityAsync(cancellationToken);

        //Assert
        // The unused 30 go to the tabs with works due, in turn.
        Assert.Equal([RecommendationTab.Movies, RecommendationTab.Series, RecommendationTab.Movies, RecommendationTab.Series], await NewSearchTabsAsync(cancellationToken));
    }

    [Fact]
    public async Task Check_runs_no_search_past_the_daily_limit()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _searches.Log(_anna, RecommendationTab.Movies, 15);
        await _searches.Log(_anna, RecommendationTab.Series, 15);
        await _searches.Log(_anna, RecommendationTab.Cartoons, 15);
        await _searches.Log(_anna, RecommendationTab.Documentaries, 13);
        await PlantAsync(RecommendationTab.Movies, 5);

        //Act
        await _factory.CheckRecommendationAvailabilityAsync(cancellationToken);
        await _factory.CheckRecommendationAvailabilityAsync(cancellationToken);

        //Assert
        // 58 of 60 spent: two more, then none, however much is due.
        Assert.Equal(2, (await NewSearchTabsAsync(cancellationToken)).Count);
        Assert.Equal(2, CapabilitiesReads());
    }

    [Fact]
    public async Task Check_runs_no_search_at_all_for_a_user_whose_limit_is_0()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _settings.Save(_anna, [RecommendationTab.Movies, RecommendationTab.Series], dailySearchLimit: 0);
        await PlantAsync(RecommendationTab.Movies, 2);

        //Act
        await _factory.CheckRecommendationAvailabilityAsync(cancellationToken);

        //Assert
        Assert.Empty(_factory.TorznabHttp.Requests);
        Assert.Empty(await NewSearchTabsAsync(cancellationToken));
    }

    [Fact]
    public async Task Check_runs_at_most_four_searches_a_pass_serving_the_users_in_turn()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var borys = await _users.Create("borys");
        await _settings.Save(_anna, [RecommendationTab.Movies]);
        await _settings.Save(borys, [RecommendationTab.Series]);
        await PlantAsync(RecommendationTab.Movies, 3);
        await PlantAsync(RecommendationTab.Series, 3);

        //Act
        await _factory.CheckRecommendationAvailabilityAsync(cancellationToken);
        var firstPass = await SearchesByUserAsync(cancellationToken);
        await _factory.CheckRecommendationAvailabilityAsync(cancellationToken);

        //Assert
        Assert.Equal(RecommendationAvailabilityPass.SearchesPerPass, firstPass.Values.Sum());
        Assert.Equal((2, 2), (firstPass[_anna.Id], firstPass[borys.Id]));
        var secondPass = await SearchesByUserAsync(cancellationToken);
        Assert.Equal((3, 3), (secondPass[_anna.Id], secondPass[borys.Id]));
    }

    [Fact]
    public async Task Check_frees_a_search_of_the_budget_24_hours_after_it_ran()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _settings.Save(_anna, [RecommendationTab.Movies], dailySearchLimit: 2);
        await PlantAsync(RecommendationTab.Movies, 3);
        await _factory.CheckRecommendationAvailabilityAsync(cancellationToken);

        //Act
        _factory.Time.Advance(RecommendationAvailabilityPass.BudgetWindow - TimeSpan.FromMinutes(1));
        await _factory.CheckRecommendationAvailabilityAsync(cancellationToken);
        var searchesWithinTheDay = CapabilitiesReads();
        _factory.Time.Advance(TimeSpan.FromMinutes(1));
        await _factory.CheckRecommendationAvailabilityAsync(cancellationToken);

        //Assert
        // Two the first day, the limit; the third once the first two are a day old.
        Assert.Equal(2, searchesWithinTheDay);
        Assert.Equal(3, CapabilitiesReads());
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    /// <summary><paramref name="count"/> films (or shows, for Series) recommended in <paramref name="tab"/>, never
    /// checked.</summary>
    private async Task PlantAsync(RecommendationTab tab, int count, double score = 1)
    {
        for (var index = 0; index < count; index++)
        {
            var catalogId = (_nextCatalogId++).ToString(System.Globalization.CultureInfo.InvariantCulture);
            var kind = tab == RecommendationTab.Series ? MediaWorkKind.Series : MediaWorkKind.Movie;
            var work = await _works.Create(kind, catalogId, $"Work {catalogId}", new DateOnly(2000, 6, 1));
            await _candidates.Create(work, tab, score);
        }
    }

    /// <summary>The searches each pass sent: each reads the source's capabilities first.</summary>
    private int CapabilitiesReads() => _factory.TorznabHttp.Requests.Count(request => request.Uri.Query.StartsWith("?t=caps", StringComparison.Ordinal));

    /// <summary>The tabs of the searches the passes ran — the planted log's are of catalog works that don't exist — in
    /// the order they ran.</summary>
    private async Task<List<RecommendationTab>> NewSearchTabsAsync(CancellationToken cancellationToken)
    {
        List<RecommendationTab> tabs = [];
        await _factory.WithDbAsync(async db =>
        {
            var searched = await db.RecommendationSearches.AsNoTracking()
                .Where(search => db.CatalogWorks.Any(work => work.Id == search.CatalogWorkId))
                .Join(db.CatalogWorks, search => search.CatalogWorkId, work => work.Id, (search, work) => new { search.Tab, work.CatalogId })
                .ToListAsync(cancellationToken);
            // The catalog ids were planted in increasing order within a tab, and each tab's works are searched by that
            // order; the order across tabs is the requests'.
            var order = SearchedTitles();
            tabs = [.. searched.OrderBy(search => order.IndexOf($"Work {search.CatalogId}")).Select(search => search.Tab)];
        });
        return tabs;
    }

    /// <summary>The titles searched for, in the order searched: the text of each search's first query, which carries
    /// the film's year (a show's has none).</summary>
    private List<string> SearchedTitles() =>
        [
            .. _factory.TorznabHttp.Requests
                .Select(request => request.Uri.Query)
                .Where(query => !query.StartsWith("?t=caps", StringComparison.Ordinal))
                .Select(query => Uri.UnescapeDataString(query.Split("q=")[1].Split('&')[0]))
                .Select(text => text.EndsWith(" 2000", StringComparison.Ordinal) ? text[..^" 2000".Length] : text)
                .Distinct(),
        ];

    private async Task<Dictionary<Guid, int>> SearchesByUserAsync(CancellationToken cancellationToken)
    {
        Dictionary<Guid, int> counts = [];
        await _factory.WithDbAsync(async db => counts = await db.RecommendationSearches.AsNoTracking()
            .GroupBy(search => search.UserId)
            .Select(group => new { group.Key, Count = group.Count() })
            .ToDictionaryAsync(group => group.Key, group => group.Count, cancellationToken));
        return counts;
    }
}
