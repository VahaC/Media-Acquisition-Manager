using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Downloads;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Infrastructure.Persistence;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Data;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MediaAcquisitionManager.Api.Tests.Downloads;

// MAM-14 — a job's files on its details page: the caller's own job's, as qBittorrent last listed them for the
// poller — in qBittorrent's order, each path exactly as qBittorrent names it, sizes past 4 GiB, a file skipped
// in qBittorrent not wanted; an empty page until qBittorrent has listed them; paged by the server. Another
// user's job — the installation owner's reach included — and one that never was answer the same bare 404,
// whatever the page asked for; a page or a page size out of range is 400 with the sentence. A read asks no
// client.
public sealed class DownloadJobFilesHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/downloads";
    private const string TolokaAddress = "http://prowlarr.test:9696/1/api";
    private const string ApiKey = "planted-torznab-key-0001";
    private const string Hash = "1ec461458feffe9f27ae17e30d0a804e2ab0a88f";

    // As qBittorrent names the trilogy pack's files (VendorSamples/QBittorrent/torrent-files-multi.json): the root
    // folder first, a double space kept.
    private const string TakenFolder = "Taken. Trilogy  (2008-2014) BDRip 1080p";

    // Spelled out rather than read from the product code: these are what the job's page shows.
    private const string PageError = "Page must be 1 or more.";
    private const string PageSizeError = "Page size must be between 1 and 100.";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private IReleaseSeeder _releases = null!;
    private AuthorizedUser _owner = null!;
    private AuthorizedUser _member = null!;
    private FakeQBittorrentServer _qbittorrent = null!;
    private IntegrationConnectionEntity _toloka = null!;
    private IntegrationConnectionEntity _client = null!;
    private DestinationMappingEntity _movies = null!;

    public DownloadJobFilesHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        var integrations = new IntegrationConnectionSeeder(_factory.Scope);
        _releases = new ReleaseSeeder(_factory.Scope);
        var users = new UserSeeder(_factory.Scope);
        _owner = await users.CreateAuthorizedOwner("owner");
        _member = await users.CreateAuthorized("member");
        _toloka = await integrations.Create("torznab", "Toloka", TolokaAddress, apiKey: ApiKey);
        _qbittorrent = new FakeQBittorrentServer();
        _qbittorrent.Arm(_factory.QBittorrentHttp);
        _client = await integrations.Create(
            "qbittorrent",
            "qBittorrent",
            _qbittorrent.BaseUrl,
            userName: FakeQBittorrentServer.DefaultUserName,
            password: FakeQBittorrentServer.DefaultPassword);
        _movies = await new DestinationMappingSeeder(_factory.Scope).Create(_client, "Movies", "Movies", isDefault: true);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Files_of_the_callers_job_are_200_in_qBittorrents_order_as_the_poller_read_them()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var job = await PlantAsync(_member);
        _qbittorrent.AddTorrent(FakeQBittorrentTorrent.Of(new TorrentInfoHashes(Hash, null), TakenFolder, "Movies", "downloading", 0.4) with
        {
            Files =
            [
                new($"{TakenFolder}/Taken (2008) BDRip 1080p.mkv", 9_659_032_478, 1),
                new($"{TakenFolder}/Taken 2 (2012) BDRip 1080p.mkv", 8_589_934_592, 0.2, Priority: 6),
                // Skipped in qBittorrent: "do not download".
                new($"{TakenFolder}/Taken 3 (2014) BDRip 1080p.mkv", 10_737_418_240, 0, Priority: 0),
            ],
        });
        await _factory.PollDownloadJobsAsync(cancellationToken);
        var pollErrors = _factory.Logs.Entries.Where(entry => entry.Level >= LogLevel.Error).ToList();
        var before = _factory.QBittorrentHttp.Requests.Count;

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}/{job.Id}/files", _member.AccessToken);

        //Assert
        // The pass that stored them threw nothing: one that threw is logged and would read as one that stored none.
        Assert.Empty(pollErrors);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var list = await ReadListAsync(response, cancellationToken);
        Assert.Equivalent(
            new DownloadJobFileListResponse(
                [
                    new DownloadJobFileDto(0, $"{TakenFolder}/Taken (2008) BDRip 1080p.mkv", 9_659_032_478, 1, true),
                    new DownloadJobFileDto(1, $"{TakenFolder}/Taken 2 (2012) BDRip 1080p.mkv", 8_589_934_592, 0.2, true),
                    new DownloadJobFileDto(2, $"{TakenFolder}/Taken 3 (2014) BDRip 1080p.mkv", 10_737_418_240, 0, false),
                ],
                3,
                1,
                25),
            list,
            strict: true);
        // Read from the rows: the page asks qBittorrent nothing.
        Assert.Equal(before, _factory.QBittorrentHttp.Requests.Count);
    }

    [Theory]
    [InlineData(1, new[] { 0, 1 })]
    [InlineData(2, new[] { 2, 3 })]
    [InlineData(3, new[] { 4 })]
    // Past the last page: nothing, with the real count, not an error.
    [InlineData(4, new int[0])]
    [InlineData(int.MaxValue, new int[0])]
    public async Task Files_page_in_qBittorrents_order(int page, int[] expected)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var job = await PlantAsync(_member);
        await Jobs().AddFiles(job.Id, [.. Enumerable.Range(0, 5).Select(n => new DownloadJobFileEntity { RelativePath = $"Pack/{n}.mkv", SizeBytes = n })]);

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}/{job.Id}/files?page={page}&pageSize=2", _member.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var list = await ReadListAsync(response, cancellationToken);
        Assert.Equal((5, page, 2), (list.TotalCount, list.Page, list.PageSize));
        Assert.Equal(expected, list.Items.Select(file => file.Position));
        Assert.Equal(expected.Select(n => $"Pack/{n}.mkv"), list.Items.Select(file => file.Path));
    }

    [Fact]
    public async Task Files_of_a_job_qBittorrent_has_not_listed_files_for_are_an_empty_page()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // A magnet still fetching its metadata, say.
        var job = await PlantAsync(_member);

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}/{job.Id}/files", _member.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var list = await ReadListAsync(response, cancellationToken);
        Assert.Equal((0, 1, 25), (list.TotalCount, list.Page, list.PageSize));
        Assert.Empty(list.Items);
    }

    [Fact]
    public async Task Files_of_another_users_job_are_a_bare_404_even_for_the_installation_owner()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var job = await PlantAsync(_member);
        await Jobs().AddFiles(job.Id, [new DownloadJobFileEntity { RelativePath = "Pack/0.mkv" }]);
        var other = await new UserSeeder(_factory.Scope).CreateAuthorized("other");

        //Act
        var forOwner = await _factory.GetHttpRequest($"{BaseUrl}/{job.Id}/files", _owner.AccessToken);
        var forOther = await _factory.GetHttpRequest($"{BaseUrl}/{job.Id}/files", other.AccessToken);
        // Whatever the page: the job is checked first, so a bad page confirms nothing either.
        var withBadPage = await _factory.GetHttpRequest($"{BaseUrl}/{job.Id}/files?page=0", _owner.AccessToken);

        //Assert
        await AssertBare404Async(forOwner, cancellationToken);
        await AssertBare404Async(forOther, cancellationToken);
        await AssertBare404Async(withBadPage, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, (await _factory.GetHttpRequest($"{BaseUrl}/{job.Id}/files", _member.AccessToken)).StatusCode);
    }

    [Fact]
    public async Task Files_of_a_job_that_never_was_are_a_bare_404()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}/{Guid.NewGuid()}/files", _member.AccessToken);

        //Assert
        await AssertBare404Async(response, cancellationToken);
    }

    [Theory]
    [InlineData(0, 25, PageError)]
    [InlineData(1, 0, PageSizeError)]
    [InlineData(1, 101, PageSizeError)]
    public async Task Files_with_a_page_or_page_size_out_of_range_are_400_with_why(int page, int pageSize, string expected)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var job = await PlantAsync(_member);

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}/{job.Id}/files?page={page}&pageSize={pageSize}", _member.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(expected, await response.ReadErrorAsync(cancellationToken));
    }

    [Fact]
    public async Task Files_answer_camel_case()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var job = await PlantAsync(_member);
        await Jobs().AddFiles(job.Id, [new DownloadJobFileEntity { RelativePath = "Pack/0.mkv", SizeBytes = 1, Progress = 0.5, IsWanted = true }]);

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}/{job.Id}/files", _member.AccessToken);

        //Assert
        // Exactly these properties: a path under the job's folder, never where it is on any disk.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        Assert.Equal(["items", "totalCount", "page", "pageSize"], document.RootElement.EnumerateObject().Select(property => property.Name));
        Assert.Equal(
            ["position", "path", "sizeBytes", "progress", "isWanted"],
            document.RootElement.GetProperty("items")[0].EnumerateObject().Select(property => property.Name));
    }

    [Fact]
    public async Task Files_without_a_token_are_401()
    {
        //Arrange
        var job = await PlantAsync(_member);

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}/{job.Id}/files");

        //Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>A job of <paramref name="user"/>'s downloading the trilogy pack in qBittorrent's Movies.</summary>
    private async Task<DownloadJobEntity> PlantAsync(AuthorizedUser user)
    {
        var release = await _releases.CreateFound(user.User, _toloka, TakenFolder);
        return await Jobs().Create(
            release.Id, _client.Id, _client.Name, _movies.Id, DownloadJobStatus.Submitted, user.User.Id, infoHashV1: Hash, clientJobId: Hash);
    }

    private IDownloadJobDataFactory Jobs() => new DownloadJobDataFactory(_factory.Scope.GetRequiredService<ApplicationDbContext>(), _factory.Time);

    private static async Task<DownloadJobFileListResponse> ReadListAsync(HttpResponseMessage response, CancellationToken cancellationToken) =>
        (await response.Content.ReadFromJsonAsync<DownloadJobFileListResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;

    /// <summary>A 404 with nothing in it: the same answer a job that never was gets (house rule 6).</summary>
    private static async Task AssertBare404Async(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("", await response.Content.ReadAsStringAsync(cancellationToken));
    }
}
