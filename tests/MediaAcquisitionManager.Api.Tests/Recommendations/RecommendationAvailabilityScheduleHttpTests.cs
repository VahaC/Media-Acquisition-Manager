using MediaAcquisitionManager.Api.Services.Recommendations;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Tests.Recommendations;

// MAM-33, ADR 0024 — when the availability check searches a work, and for whom: a work found is searched again a day
// later, one not found a week later, never before; a check is shared, so a work checked for one user is not searched
// for another; a user's "Not interested" works and the works of a tab they have off are never searched for them; a
// top-rated work listed only once found is searched like any other; and the order — never checked first, then by
// score. Each pass is the app's own checker, against Toloka faked at the network.
public sealed class RecommendationAvailabilityScheduleHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string FeedAddress = "http://prowlarr.test:9696/1/api";
    private const string ApiKey = "planted-torznab-key-0001";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private ICatalogWorkSeeder _works = null!;
    private IRecommendationCandidateSeeder _candidates = null!;
    private IUserSeeder _users = null!;
    private UserEntity _anna = null!;
    private FakeTorznabServer _toloka = null!;

    public RecommendationAvailabilityScheduleHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _works = new CatalogWorkSeeder(_factory.Scope);
        _candidates = new RecommendationCandidateSeeder(_factory.Scope);
        _users = new UserSeeder(_factory.Scope);
        _anna = await _users.Create("anna");
        await new IntegrationConnectionSeeder(_factory.Scope).Create("torznab", "Toloka", FeedAddress, apiKey: ApiKey);
        _toloka = new FakeTorznabServer(FeedAddress, ApiKey) { SearchXml = TorznabSamples.Feed() };
        _toloka.Arm(_factory.TorznabHttp);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Check_searches_a_work_found_again_a_day_later_and_not_before()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        _toloka.SearchXml = TorznabSamples.MovieSearch(FeedAddress, ApiKey);
        var film = await Film("157336", "Interstellar", 2014);
        await _candidates.Create(film, RecommendationTab.Movies);
        await _factory.CheckRecommendationAvailabilityAsync(cancellationToken);
        var firstCheckUtc = (await AvailabilityAsync(film.Id, cancellationToken))!.CheckedUtc;

        //Act
        _factory.Time.Advance(RecommendationAvailabilityPass.FoundRecheck - TimeSpan.FromMinutes(1));
        await _factory.CheckRecommendationAvailabilityAsync(cancellationToken);
        var searchesBeforeADay = SearchCount();
        var checkedBeforeADayUtc = (await AvailabilityAsync(film.Id, cancellationToken))!.CheckedUtc;
        _factory.Time.Advance(TimeSpan.FromMinutes(1));
        await _factory.CheckRecommendationAvailabilityAsync(cancellationToken);

        //Assert
        Assert.Equal(1, searchesBeforeADay);
        Assert.Equal(firstCheckUtc, checkedBeforeADayUtc);
        Assert.Equal(2, SearchCount());
        Assert.Equal(_factory.Time.GetUtcNow().UtcDateTime, (await AvailabilityAsync(film.Id, cancellationToken))!.CheckedUtc);
    }

    [Fact]
    public async Task Check_searches_a_work_not_found_again_a_week_later_and_not_before()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var film = await Film("157336", "Interstellar", 2014);
        await _candidates.Create(film, RecommendationTab.Movies);
        await _factory.CheckRecommendationAvailabilityAsync(cancellationToken);
        var searchesOfTheFirstCheck = SearchCount();

        //Act
        _factory.Time.Advance(RecommendationAvailabilityPass.NotFoundRecheck - TimeSpan.FromMinutes(1));
        await _factory.CheckRecommendationAvailabilityAsync(cancellationToken);
        var searchesBeforeAWeek = SearchCount();
        _factory.Time.Advance(TimeSpan.FromMinutes(1));
        await _factory.CheckRecommendationAvailabilityAsync(cancellationToken);

        //Assert
        Assert.Equal(searchesOfTheFirstCheck, searchesBeforeAWeek);
        Assert.Equal(2 * searchesOfTheFirstCheck, SearchCount());
        var availability = await AvailabilityAsync(film.Id, cancellationToken);
        Assert.Equal((false, _factory.Time.GetUtcNow().UtcDateTime), (availability!.Found, availability.CheckedUtc));
    }

    [Fact]
    public async Task Check_of_a_work_for_one_user_is_shared_with_every_other()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _users.Create("borys");
        var film = await Film("157336", "Interstellar", 2014);
        await _candidates.Create(film, RecommendationTab.Movies);

        //Act
        await _factory.CheckRecommendationAvailabilityAsync(cancellationToken);
        await _factory.CheckRecommendationAvailabilityAsync(cancellationToken);

        //Assert
        // Searched once, for the first user served; the second user's list shows the same check.
        var search = Assert.Single(await SearchesAsync(cancellationToken));
        Assert.Equal((_anna.Id, film.Id), (search.UserId, search.CatalogWorkId));
        Assert.Equal(1, SearchCount());
    }

    [Fact]
    public async Task Check_never_searches_a_work_for_a_user_who_is_not_interested_but_does_for_another()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var borys = await _users.Create("borys");
        var dismissed = await Film("157336", "Interstellar", 2014);
        var other = await Film("27205", "Inception", 2010);
        // The dismissed one ranks first: Anna, served first, would take it if it were due for her.
        await _candidates.Create(dismissed, RecommendationTab.Movies, score: 2);
        await _candidates.Create(other, RecommendationTab.Movies, score: 1);
        await new RecommendationDismissalSeeder(_factory.Scope).Dismiss(_anna, dismissed);

        //Act
        await _factory.CheckRecommendationAvailabilityAsync(cancellationToken);

        //Assert
        var searches = await SearchesAsync(cancellationToken);
        Assert.Equal(
            [(_anna.Id, other.Id), (borys.Id, dismissed.Id)],
            searches.Select(search => (search.UserId, search.CatalogWorkId)).OrderBy(search => search.UserId == _anna.Id ? 0 : 1));
    }

    [Fact]
    public async Task Check_never_searches_a_work_of_a_tab_the_user_has_off()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await new UserRecommendationSettingsSeeder(_factory.Scope).Save(_anna, [RecommendationTab.Movies]);
        var show = await _works.Create(MediaWorkKind.Series, "73586", "Єллоустоун", new DateOnly(2018, 6, 20), originalTitle: "Yellowstone");
        await _candidates.Create(show, RecommendationTab.Series);

        //Act
        await _factory.CheckRecommendationAvailabilityAsync(cancellationToken);

        //Assert
        Assert.Empty(_factory.TorznabHttp.Requests);
        Assert.Empty(await SearchesAsync(cancellationToken));
    }

    [Fact]
    public async Task Check_searches_a_work_listed_only_once_found_like_any_other()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        _toloka.SearchXml = TorznabSamples.MovieSearch(FeedAddress, ApiKey);
        var film = await Film("157336", "Interstellar", 2014);
        await _candidates.Create(
            film,
            RecommendationTab.Movies,
            requiresAvailability: true,
            reasons: [new RecommendationReasonEntity { Kind = RecommendationReasonKind.TopRated }]);

        //Act
        await _factory.CheckRecommendationAvailabilityAsync(cancellationToken);

        //Assert
        Assert.True((await AvailabilityAsync(film.Id, cancellationToken))?.Found);
    }

    [Fact]
    public async Task Check_searches_the_works_never_checked_first_then_by_score()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        string[] titles = ["Alpha", "Bravo", "Charlie", "Delta", "Echo"];
        foreach (var (position, title) in titles.Index())
        {
            var film = await Film($"{100 + position}", title, 2000);
            await _candidates.Create(film, RecommendationTab.Movies, score: titles.Length - position);
        }

        //Act
        await _factory.CheckRecommendationAvailabilityAsync(cancellationToken);
        var firstPass = SearchedTitles();
        _factory.TorznabHttp.Reset();
        _toloka.Arm(_factory.TorznabHttp);
        // Long enough for the four checked to be due again; Echo has never been checked.
        _factory.Time.Advance(RecommendationAvailabilityPass.NotFoundRecheck);
        await _factory.CheckRecommendationAvailabilityAsync(cancellationToken);

        //Assert
        Assert.Equal(["Alpha", "Bravo", "Charlie", "Delta"], firstPass);
        Assert.Equal(["Echo", "Alpha", "Bravo", "Charlie"], SearchedTitles());
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>A film whose catalog title is Ukrainian-free, so it is searched by it as it is.</summary>
    private Task<CatalogWorkEntity> Film(string catalogId, string title, int year) =>
        _works.Create(MediaWorkKind.Movie, catalogId, title, new DateOnly(year, 6, 1));

    /// <summary>The searches sent: each reads the source's capabilities first.</summary>
    private int SearchCount() => _factory.TorznabHttp.Requests.Count(request => Parameter(request.Uri, "t") == "caps");

    /// <summary>The titles of the works searched, in the order searched: the text of each search's first movie
    /// query — the title and the year — without the year.</summary>
    private List<string> SearchedTitles() =>
        [
            .. _factory.TorznabHttp.Requests
                .Where(request => Parameter(request.Uri, "t") == "movie")
                .Select(request => Parameter(request.Uri, "q")!)
                .Where(text => text.EndsWith(" 2000", StringComparison.Ordinal))
                .Select(text => text[..^" 2000".Length]),
        ];

    /// <summary>The value of the query parameter <paramref name="name"/>, unescaped; <c>null</c> without one.</summary>
    private static string? Parameter(Uri uri, string name) =>
        uri.Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('=', 2))
            .Where(pair => string.Equals(pair[0], name, StringComparison.Ordinal))
            .Select(pair => pair.Length > 1 ? Uri.UnescapeDataString(pair[1]) : "")
            .FirstOrDefault();

    private async Task<ReleaseAvailabilityEntity?> AvailabilityAsync(Guid catalogWorkId, CancellationToken cancellationToken)
    {
        ReleaseAvailabilityEntity? availability = null;
        await _factory.WithDbAsync(async db => availability = await db.ReleaseAvailabilities.AsNoTracking()
            .SingleOrDefaultAsync(row => row.CatalogWorkId == catalogWorkId, cancellationToken));
        return availability;
    }

    private async Task<List<RecommendationSearchEntity>> SearchesAsync(CancellationToken cancellationToken)
    {
        List<RecommendationSearchEntity> searches = [];
        await _factory.WithDbAsync(async db => searches = await db.RecommendationSearches.AsNoTracking().ToListAsync(cancellationToken));
        return searches;
    }
}
