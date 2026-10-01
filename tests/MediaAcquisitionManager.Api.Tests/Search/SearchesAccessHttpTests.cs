using System.Net;
using System.Net.Http.Json;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MediaAcquisitionManager.Api.Tests.Search;

// MAM-12 — who may read a stored search, and for how long. Every signed-in user may search (MAM-10's
// choice), and a search is its creator's: another user's — the installation owner's reach included — and
// one that never was answer the same bare 404, so nothing confirms a search exists. A search lives as
// long as its releases, a day (ReleaseStore.UnusedReleaseLifetime): older, it is gone at once, and the
// next search's prune removes it with its sources; a release a download job sent stays as the job's
// evidence, unlinked. Deleting a user removes their searches.
public sealed class SearchesAccessHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/searches";
    private const string TolokaAddress = "http://prowlarr.test:9696/1/api";
    private const string ApiKey = "planted-torznab-key-0001";

    private static readonly int SampleMovieCount = TorznabSamples.ItemCount - TorznabSamples.NonMovieItemCount;

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private IIntegrationConnectionSeeder _integrations = null!;
    private IUserSeeder _users = null!;
    private AuthorizedUser _owner = null!;
    private AuthorizedUser _member = null!;
    private AuthorizedUser _otherMember = null!;

    public SearchesAccessHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _integrations = new IntegrationConnectionSeeder(_factory.Scope);
        _users = new UserSeeder(_factory.Scope);
        _owner = await _users.CreateAuthorizedOwner("owner");
        _member = await _users.CreateAuthorized("member");
        _otherMember = await _users.CreateAuthorized("other");
        new FakeTorznabServer(TolokaAddress, ApiKey).Arm(_factory.TorznabHttp);
        await _integrations.Create("torznab", "Toloka", TolokaAddress, apiKey: ApiKey);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Theory]
    [InlineData("")]
    [InlineData("/releases")]
    [InlineData("/releases?sort=largest&page=2")]
    public async Task Reads_without_a_token_are_401(string suffix)
    {
        //Arrange
        var search = await CreateAsync(_member, TestContext.Current.CancellationToken);

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}/{search.Id}{suffix}");

        //Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_of_another_users_search_is_a_bare_404_even_for_the_installation_owner()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var search = await CreateAsync(_member, cancellationToken);

        //Act
        var forOtherMember = await _factory.GetHttpRequest($"{BaseUrl}/{search.Id}", _otherMember.AccessToken);
        var forOwner = await _factory.GetHttpRequest($"{BaseUrl}/{search.Id}", _owner.AccessToken);

        //Assert
        await AssertBare404Async(forOtherMember, cancellationToken);
        await AssertBare404Async(forOwner, cancellationToken);
        // Still there for its creator.
        Assert.Equal(HttpStatusCode.OK, (await _factory.GetHttpRequest($"{BaseUrl}/{search.Id}", _member.AccessToken)).StatusCode);
    }

    [Theory]
    [InlineData("")]
    // Whose search is decided first: a broken filter on someone else's search confirms nothing either.
    [InlineData("?sort=popular&page=0&language=xx")]
    [InlineData("?pageSize=100")]
    public async Task Releases_of_another_users_search_is_a_bare_404_whatever_the_query(string query)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var search = await CreateAsync(_member, cancellationToken);

        //Act
        var forOtherMember = await _factory.GetHttpRequest($"{BaseUrl}/{search.Id}/releases{query}", _otherMember.AccessToken);
        var forOwner = await _factory.GetHttpRequest($"{BaseUrl}/{search.Id}/releases{query}", _owner.AccessToken);

        //Assert
        await AssertBare404Async(forOtherMember, cancellationToken);
        await AssertBare404Async(forOwner, cancellationToken);
    }

    [Theory]
    [InlineData("")]
    [InlineData("/releases")]
    [InlineData("/releases?sort=popular")]
    public async Task Reads_of_a_search_that_never_was_are_a_bare_404(string suffix)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await CreateAsync(_member, cancellationToken);

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}/{Guid.NewGuid()}{suffix}", _member.AccessToken);

        //Assert
        await AssertBare404Async(response, cancellationToken);
    }

    [Fact]
    public async Task Get_with_an_id_that_is_not_one_is_404_naming_no_endpoint()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}/abc", _member.AccessToken);

        //Assert
        // The route takes only an id, so this is the API's answer for no endpoint at all.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("No API endpoint matches GET /api/searches/abc.", await response.ReadErrorAsync(cancellationToken));
    }

    [Fact]
    public async Task Reads_of_a_search_a_day_old_still_answer()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var search = await CreateAsync(_member, cancellationToken);
        _factory.Time.Advance(TimeSpan.FromHours(24));
        // A day later the morning's access token has long expired: the member signs in again.
        var member = await _users.CreateSession(_member.User);

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}/{search.Id}", member.AccessToken);
        var releases = await _factory.GetHttpRequest($"{BaseUrl}/{search.Id}/releases", member.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, releases.StatusCode);
        var list = await releases.Content.ReadFromJsonAsync<SearchReleaseListResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken);
        Assert.Equal(SampleMovieCount, list!.TotalCount);
    }

    [Fact]
    public async Task Reads_of_a_search_older_than_a_day_are_a_bare_404_before_any_prune()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var search = await CreateAsync(_member, cancellationToken);
        _factory.Time.Advance(TimeSpan.FromHours(24) + TimeSpan.FromSeconds(1));
        var member = await _users.CreateSession(_member.User);

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}/{search.Id}", member.AccessToken);
        var releases = await _factory.GetHttpRequest($"{BaseUrl}/{search.Id}/releases", member.AccessToken);

        //Assert
        // Gone as its releases may be at any moment now, whether or not a later search pruned it yet.
        await AssertBare404Async(response, cancellationToken);
        await AssertBare404Async(releases, cancellationToken);
        await _factory.WithDbAsync(async db => Assert.Equal(search.Id, (await db.Searches.AsNoTracking().SingleAsync(cancellationToken)).Id));
    }

    [Fact]
    public async Task Create_removes_every_search_older_than_a_day_with_its_sources_and_releases()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await CreateAsync(_member, cancellationToken);
        await CreateAsync(_otherMember, cancellationToken);
        _factory.Time.Advance(TimeSpan.FromHours(25));
        var member = await _users.CreateSession(_member.User);
        _factory.Logs.Clear();

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, member.AccessToken, new SearchRequest("Interstellar"));

        //Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var fresh = (await response.Content.ReadFromJsonAsync<SearchResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
        await _factory.WithDbAsync(async db =>
        {
            // Whoever made them: the prune is the installation's, not the searcher's.
            Assert.Equal([fresh.Id], await db.Searches.AsNoTracking().Select(search => search.Id).ToListAsync(cancellationToken));
            Assert.Equal([fresh.Sources[0].Id], await db.SearchSources.AsNoTracking().Select(source => source.Id).ToListAsync(cancellationToken));
            Assert.All(await db.Releases.AsNoTracking().ToListAsync(cancellationToken), release => Assert.Equal(fresh.Sources[0].Id, release.SearchSourceId));
            Assert.Equal(SampleMovieCount, await db.Releases.CountAsync(cancellationToken));
        });
        Assert.Contains(_factory.Logs.Entries, entry => entry.Level == LogLevel.Information && entry.Message == "Removed 2 searches made more than a day ago.");
    }

    [Fact]
    public async Task Create_keeps_a_release_a_download_job_sent_after_its_search_is_removed()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var old = await CreateAsync(_member, cancellationToken);
        var sentId = Guid.Empty;
        await _factory.WithDbAsync(async db => sentId = await db.Releases
            .Where(release => release.SearchSourceId == old.Sources[0].Id && release.Position == 0)
            .Select(release => release.Id)
            .SingleAsync(cancellationToken));
        var client = await _integrations.Create("qbittorrent", "Downloads", "http://qbittorrent.test:8080");
        var job = await new DownloadJobSeeder(_factory.Scope).Create(client, sentId);
        _factory.Time.Advance(TimeSpan.FromHours(25));
        var member = await _users.CreateSession(_member.User);

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, member.AccessToken, new SearchRequest("Interstellar"));

        //Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        await _factory.WithDbAsync(async db =>
        {
            Assert.False(await db.Searches.AnyAsync(search => search.Id == old.Id, cancellationToken));
            Assert.False(await db.SearchSources.AnyAsync(source => source.Id == old.Sources[0].Id, cancellationToken));
            // The job's evidence of what it sent (Restrict, MAM-7) stays, no longer part of any search.
            var sent = await db.Releases.AsNoTracking().SingleAsync(release => release.Id == sentId, cancellationToken);
            Assert.Null(sent.SearchSourceId);
            Assert.Equal(sentId, (await db.DownloadJobs.AsNoTracking().SingleAsync(row => row.Id == job.Id, cancellationToken)).ReleaseId);
            // The old search's other releases went with it.
            Assert.Equal(SampleMovieCount + 1, await db.Releases.CountAsync(cancellationToken));
        });
    }

    [Fact]
    public async Task Create_keeps_a_search_less_than_a_day_old()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var earlier = await CreateAsync(_member, cancellationToken);
        _factory.Time.Advance(TimeSpan.FromHours(23));
        var member = await _users.CreateSession(_member.User);

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, member.AccessToken, new SearchRequest("Interstellar"));

        //Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _factory.GetHttpRequest($"{BaseUrl}/{earlier.Id}", member.AccessToken)).StatusCode);
        await _factory.WithDbAsync(async db =>
        {
            Assert.Equal(2, await db.Searches.CountAsync(cancellationToken));
            Assert.Equal(2 * SampleMovieCount, await db.Releases.CountAsync(cancellationToken));
        });
    }

    [Fact]
    public async Task Release_search_removes_the_searches_older_than_a_day_too()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await CreateAsync(_member, cancellationToken);
        _factory.Time.Advance(TimeSpan.FromHours(25));
        var member = await _users.CreateSession(_member.User);

        //Act
        // MAM-10's search: one store, one prune for both (Vasyl's decision 3).
        var response = await _factory.PostHttpRequest("/api/releases/search", member.AccessToken, new ReleaseSearchRequest("Interstellar"));

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await _factory.WithDbAsync(async db =>
        {
            Assert.Equal(0, await db.Searches.CountAsync(cancellationToken));
            Assert.Equal(0, await db.SearchSources.CountAsync(cancellationToken));
            // Only the plain search's own releases are left, linked to no search.
            Assert.Equal(SampleMovieCount, await db.Releases.CountAsync(release => release.SearchSourceId == null, cancellationToken));
            Assert.Equal(SampleMovieCount, await db.Releases.CountAsync(cancellationToken));
        });
    }

    [Fact]
    public async Task Create_whose_prune_of_old_searches_fails_still_answers_and_logs_why()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var old = await CreateAsync(_member, cancellationToken);
        _factory.Time.Advance(TimeSpan.FromHours(25));
        var member = await _users.CreateSession(_member.User);
        _factory.DbCommandHook.BeforeNextCommand(
            command => command.CommandText.StartsWith("DELETE FROM \"Searches\"", StringComparison.Ordinal),
            _ => throw new InvalidOperationException("The database is busy."));
        _factory.Logs.Clear();

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, member.AccessToken, new SearchRequest("Interstellar"));

        //Assert
        // The prune is housekeeping: its failure costs the search nothing, and the next search tries again.
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Contains(
            _factory.Logs.Entries,
            entry => entry.Level == LogLevel.Warning && entry.Message == "Removing the searches made more than a day ago failed; the next search tries again.");
        await _factory.WithDbAsync(async db =>
        {
            Assert.True(await db.Searches.AnyAsync(search => search.Id == old.Id, cancellationToken));
            Assert.Equal(2, await db.Searches.CountAsync(cancellationToken));
        });
    }

    [Fact]
    public async Task Create_whose_release_prune_fails_still_removes_the_old_searches()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var old = await CreateAsync(_member, cancellationToken);
        _factory.Time.Advance(TimeSpan.FromHours(25));
        var member = await _users.CreateSession(_member.User);
        _factory.DbCommandHook.BeforeNextCommand(
            command => command.CommandText.StartsWith("DELETE FROM \"Releases\"", StringComparison.Ordinal),
            _ => throw new InvalidOperationException("The database is busy."));
        _factory.Logs.Clear();

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, member.AccessToken, new SearchRequest("Interstellar"));

        //Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Contains(
            _factory.Logs.Entries,
            entry => entry.Level == LogLevel.Warning && entry.Message == "Removing the releases no download job used failed; the next search tries again.");
        await _factory.WithDbAsync(async db =>
        {
            Assert.False(await db.Searches.AnyAsync(search => search.Id == old.Id, cancellationToken));
            // The old releases wait for the next prune, unlinked from the search that went.
            Assert.Equal(SampleMovieCount, await db.Releases.CountAsync(release => release.SearchSourceId == null, cancellationToken));
        });
    }

    [Fact]
    public async Task Deleting_a_user_removes_their_searches_and_their_sources()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var members = await CreateAsync(_member, cancellationToken);
        var others = await CreateAsync(_otherMember, cancellationToken);

        //Act
        var response = await _factory.DeleteHttpRequest($"/api/settings/users/{_member.User.Id}", _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await _factory.WithDbAsync(async db =>
        {
            Assert.Equal([others.Id], await db.Searches.AsNoTracking().Select(search => search.Id).ToListAsync(cancellationToken));
            Assert.Equal([others.Sources[0].Id], await db.SearchSources.AsNoTracking().Select(source => source.Id).ToListAsync(cancellationToken));
            // The releases themselves are the prune's, a day after they were found; they belong to no search now.
            Assert.Equal(SampleMovieCount, await db.Releases.CountAsync(release => release.SearchSourceId == null, cancellationToken));
            Assert.False(await db.SearchSources.AnyAsync(source => source.SearchId == members.Id, cancellationToken));
        });
        Assert.Equal(HttpStatusCode.OK, (await _factory.GetHttpRequest($"{BaseUrl}/{others.Id}", _otherMember.AccessToken)).StatusCode);
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>A search for "Interstellar" by <paramref name="user"/>; throws unless it was created.</summary>
    private async Task<SearchResponse> CreateAsync(AuthorizedUser user, CancellationToken cancellationToken)
    {
        var response = await _factory.PostHttpRequest(BaseUrl, user.AccessToken, new SearchRequest("Interstellar"));
        if (response.StatusCode != HttpStatusCode.Created)
            throw new InvalidOperationException($"The search answered {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync(cancellationToken)}");
        return (await response.Content.ReadFromJsonAsync<SearchResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
    }

    /// <summary>A 404 with nothing in it: the same answer a search that never was gets (house rule 6).</summary>
    private static async Task AssertBare404Async(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("", await response.Content.ReadAsStringAsync(cancellationToken));
    }
}
