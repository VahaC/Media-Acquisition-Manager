using System.Net;
using System.Net.Http.Json;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Tests.Recommendations;

// MAM-33 — what the household's download jobs do to a recommendation list, read live: a job of the work — any user's —
// that a client is still working on (Created, Submitted, Downloading, Paused) shows it as downloading; one that ended
// (Downloaded, NeedsLinking, Cancelled) leaves it out; a failed one changes nothing. A job's release is the work's when
// it is of the work's kind (a film in TV/Documentary stored as a movie, ADR 0020) and its listing's id or its title and
// year — from the title or the description — match the work (ADR 0012); never by a title alone.
public sealed class RecommendationListDownloadsHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/recommendations";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private ICatalogWorkSeeder _works = null!;
    private IRecommendationCandidateSeeder _candidates = null!;
    private IIntegrationConnectionSeeder _integrations = null!;
    private IReleaseSeeder _releases = null!;
    private IDownloadJobSeeder _jobs = null!;
    private AuthorizedUser _anna = null!;
    private UserEntity _bohdan = null!;
    private IntegrationConnectionEntity _toloka = null!;

    public RecommendationListDownloadsHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _works = new CatalogWorkSeeder(_factory.Scope);
        _candidates = new RecommendationCandidateSeeder(_factory.Scope);
        _integrations = new IntegrationConnectionSeeder(_factory.Scope);
        _releases = new ReleaseSeeder(_factory.Scope);
        _jobs = new DownloadJobSeeder(_factory.Scope);
        var users = new UserSeeder(_factory.Scope);
        _anna = await users.CreateAuthorized("anna");
        // Every job here is his and every list hers: a download serves the household, as the library does.
        _bohdan = await users.Create("bohdan");
        _toloka = await _integrations.Create("torznab", "Toloka");
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task List_shows_a_film_as_downloading_when_a_followed_jobs_release_gives_its_tmdb_id()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var martian = await RecommendAsync("286217", "Марсіянин", "The Martian", 2015);
        var other = await RecommendAsync("157336", "Інтерстеллар", "Interstellar", 2014);
        // A title that claims nothing of the work: the id alone is the evidence.
        await DownloadAsync("Mars.Rover.Collection.1080p", tmdbId: "286217");

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}?tab=Movies", _anna.AccessToken);

        //Assert
        var items = (await ReadListAsync(response, cancellationToken)).Items.ToDictionary(item => item.Id);
        Assert.True(items[martian.Id].Downloading);
        Assert.False(items[other.Id].Downloading);
    }

    [Theory]
    // The original title and the year, as Prowlarr's title keeps it…
    [InlineData("The Martian (2015) BDRip 1080p H.265 Ukr/Eng", null)]
    // …or the catalog's Ukrainian title from Toloka's description, where the title has only another one.
    [InlineData("Mars Mission Untitled (2015) WEB-DL 1080p", "Марсіянин / The Martian (2015) WEB-DL 1080p")]
    public async Task List_shows_a_film_as_downloading_when_its_jobs_release_claims_its_title_and_year(string title, string? description)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var martian = await RecommendAsync("286217", "Марсіянин", "The Martian", 2015);
        await DownloadAsync(title, description: description);

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}?tab=Movies", _anna.AccessToken);

        //Assert
        Assert.True(Assert.Single((await ReadListAsync(response, cancellationToken)).Items, item => item.Id == martian.Id).Downloading);
    }

    [Theory]
    // Another year is another film; a title without a year is a title alone — a look-alike, never a match.
    [InlineData("The Martian (2016) BDRip 1080p")]
    [InlineData("The Martian BDRip 1080p")]
    [InlineData("The Martian Chronicles (2015) 1080p")]
    public async Task List_doesnt_take_a_release_for_a_film_on_a_title_alone(string title)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var martian = await RecommendAsync("286217", "Марсіянин", "The Martian", 2015);
        await DownloadAsync(title, DownloadJobStatus.Downloaded);

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}?tab=Movies", _anna.AccessToken);

        //Assert
        Assert.False(Assert.Single((await ReadListAsync(response, cancellationToken)).Items, item => item.Id == martian.Id).Downloading);
    }

    [Fact]
    public async Task List_matches_a_film_released_in_a_tv_documentary_category_which_is_stored_as_a_movie()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var schumacher = await RecommendAsync(
            "1530001", "Шумахер 1994: Народження легенди", "Schumacher '94 - Die Geburt einer Legende", 2026, tab: RecommendationTab.Documentaries);
        // ADR 0020 as amended: a TV/Documentary release naming no season is a film, and is stored as one.
        await DownloadAsync("Schumacher '94 - Die Geburt einer Legende (2026) WEB-DL 1080p", categoryIds: "5080");

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}?tab=Documentaries", _anna.AccessToken);

        //Assert
        Assert.True(Assert.Single((await ReadListAsync(response, cancellationToken)).Items, item => item.Id == schumacher.Id).Downloading);
    }

    [Fact]
    public async Task List_matches_a_tv_release_only_to_a_show()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // The BBC's documentary film and a TV release of the same name and year: a series release is never a film's.
        var film = await RecommendAsync("1007", "Єллоустоун", "Yellowstone", 2009, tab: RecommendationTab.Documentaries);
        await DownloadAsync("Yellowstone (S1) (2009) BDRip 1080p", kind: ReleaseKind.Tv, categoryIds: "5080");

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}?tab=Documentaries", _anna.AccessToken);

        //Assert
        Assert.False(Assert.Single((await ReadListAsync(response, cancellationToken)).Items, item => item.Id == film.Id).Downloading);
    }

    [Fact]
    public async Task List_matches_a_show_by_the_tmdb_tv_id_and_never_a_film_of_that_number()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var show = await RecommendAsync("73586", "Єллоустоун", "Yellowstone", 2018, MediaWorkKind.Series, RecommendationTab.Series);
        // The catalog numbers films and shows apart (ADR 0017): this film's number is the show's.
        var film = await RecommendAsync("73586", "Інший фільм", "Another Film", 1999);
        await DownloadAsync("Season.Pack.WEB-DL.1080p", DownloadJobStatus.Downloading, ReleaseKind.Tv, tmdbId: "73586");

        //Act
        var series = await _factory.GetHttpRequest($"{BaseUrl}?tab=Series", _anna.AccessToken);
        var movies = await _factory.GetHttpRequest($"{BaseUrl}?tab=Movies", _anna.AccessToken);

        //Assert
        Assert.True(Assert.Single((await ReadListAsync(series, cancellationToken)).Items, item => item.Id == show.Id).Downloading);
        Assert.False(Assert.Single((await ReadListAsync(movies, cancellationToken)).Items, item => item.Id == film.Id).Downloading);
    }

    [Theory]
    // A season the library has says nothing about the ones it lacks…
    [InlineData("Yellowstone (S1) (2018) WEB-DL 1080p", DownloadJobStatus.Downloaded, true, false)]
    [InlineData("Yellowstone (S1-3) (2018-2020) WEB-DL 1080p", DownloadJobStatus.Downloading, true, false)]
    // …a missing one does, and so does a release that names no season, which may hold them all.
    [InlineData("Yellowstone (S4) (2021) WEB-DL 1080p", DownloadJobStatus.Downloading, true, true)]
    [InlineData("Yellowstone (S5) (2022-2024) WEB-DL 1080p", DownloadJobStatus.Downloaded, false, false)]
    [InlineData("Yellowstone (2018) WEB-DL 1080p", DownloadJobStatus.Downloaded, false, false)]
    public async Task List_counts_only_a_job_of_a_missing_season_for_a_held_series(
        string title, DownloadJobStatus status, bool listed, bool downloading)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var yellowstone = await _works.Create(MediaWorkKind.Series, "73586", "Єллоустоун", new DateOnly(2018, 6, 20), originalTitle: "Yellowstone");
        await _candidates.Create(
            yellowstone,
            RecommendationTab.Series,
            missingSeasons: "4,5",
            reasons: [new RecommendationReasonEntity { Kind = RecommendationReasonKind.MissingSeasons, Seasons = "4,5" }]);
        await DownloadAsync(title, status, ReleaseKind.Tv);

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}?tab=Series", _anna.AccessToken);

        //Assert
        // The availability check's rule: the card is about seasons 4 and 5.
        var items = (await ReadListAsync(response, cancellationToken)).Items;
        Assert.Equal(listed, items.Any(item => item.Id == yellowstone.Id));
        Assert.Equal(downloading, items.Any(item => item.Id == yellowstone.Id && item.Downloading));
    }

    [Theory]
    [InlineData(DownloadJobStatus.Created)]
    [InlineData(DownloadJobStatus.Submitted)]
    [InlineData(DownloadJobStatus.Downloading)]
    [InlineData(DownloadJobStatus.Paused)]
    public async Task List_shows_a_work_a_followed_job_is_of_as_downloading(DownloadJobStatus status)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var martian = await RecommendAsync("286217", "Марсіянин", "The Martian", 2015);
        await DownloadAsync("The Martian (2015) 1080p", status);

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}?tab=Movies", _anna.AccessToken);

        //Assert
        var list = await ReadListAsync(response, cancellationToken);
        Assert.True(Assert.Single(list.Items, item => item.Id == martian.Id).Downloading);
        Assert.Equal(1, list.TotalCount);
    }

    [Theory]
    [InlineData(DownloadJobStatus.Downloaded)]
    [InlineData(DownloadJobStatus.NeedsLinking)]
    [InlineData(DownloadJobStatus.Cancelled)]
    public async Task List_leaves_out_a_work_a_job_of_which_ended_and_counts_it_nowhere(DownloadJobStatus status)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await RecommendAsync("286217", "Марсіянин", "The Martian", 2015);
        var job = await DownloadAsync("The Martian (2015) 1080p", status);

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}?tab=Movies", _anna.AccessToken);

        //Assert
        // The household dealt with it: downloaded, waiting to be linked, or stopped by whoever sent it.
        var list = await ReadListAsync(response, cancellationToken);
        Assert.Empty(list.Items);
        Assert.Equal(0, list.TotalCount);
        Assert.Empty(list.Facets.Reasons);
        Assert.Empty(list.Facets.Decades);
        // Only read: the job and the candidate stay as they were.
        await _factory.WithDbAsync(async db =>
        {
            Assert.Equal(status, (await db.DownloadJobs.SingleAsync(row => row.Id == job.Id, cancellationToken)).Status);
            Assert.Equal(1, await db.RecommendationCandidates.CountAsync(cancellationToken));
        });
    }

    [Fact]
    public async Task List_shows_a_work_a_failed_job_is_of_as_if_there_were_none()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var martian = await RecommendAsync("286217", "Марсіянин", "The Martian", 2015);
        await DownloadAsync("The Martian (2015) 1080p", DownloadJobStatus.Failed);

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}?tab=Movies", _anna.AccessToken);

        //Assert
        Assert.False(Assert.Single((await ReadListAsync(response, cancellationToken)).Items, item => item.Id == martian.Id).Downloading);
    }

    [Fact]
    public async Task List_leaves_out_a_work_one_job_of_which_ended_though_another_is_under_way()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await RecommendAsync("286217", "Марсіянин", "The Martian", 2015);
        await DownloadAsync("The Martian (2015) 2160p", DownloadJobStatus.Downloading);
        await DownloadAsync("The Martian (2015) 1080p", DownloadJobStatus.Downloaded);

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}?tab=Movies", _anna.AccessToken);

        //Assert
        // Whatever else is still downloading, the work is in the library now.
        Assert.Empty((await ReadListAsync(response, cancellationToken)).Items);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    /// <summary>A catalog work recommended in <paramref name="tab"/> (Movies unless told).</summary>
    private async Task<CatalogWorkEntity> RecommendAsync(
        string catalogId,
        string title,
        string originalTitle,
        int year,
        MediaWorkKind kind = MediaWorkKind.Movie,
        RecommendationTab tab = RecommendationTab.Movies)
    {
        var work = await _works.Create(kind, catalogId, title, new DateOnly(year, 6, 1), originalTitle: originalTitle);
        await _candidates.Create(work, tab);
        return work;
    }

    /// <summary>
    /// A release Toloka listed as <paramref name="kind"/>, sent by Bohdan to a client of its own — a client takes one
    /// destination of a name — and standing at <paramref name="status"/>.
    /// </summary>
    private async Task<DownloadJobEntity> DownloadAsync(
        string title,
        DownloadJobStatus status = DownloadJobStatus.Submitted,
        ReleaseKind kind = ReleaseKind.Movie,
        string? description = null,
        string? tmdbId = null,
        string? categoryIds = null)
    {
        var release = await _releases.Create(_toloka, title, kind, description, tmdbId, categoryIds);
        var client = await _integrations.Create("qbittorrent");
        return await _jobs.Create(client, release.Id, status, _bohdan);
    }

    private static async Task<RecommendationListResponse> ReadListAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<RecommendationListResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
    }
}
