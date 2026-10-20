using System.Net;
using System.Net.Http.Json;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Texts;
using MediaAcquisitionManager.Infrastructure.Persistence;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Data;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;

namespace MediaAcquisitionManager.Api.Tests.Downloads;

// MAM-47 WP14 (ADR 0042, design §8.6) — the Jobs page and the job tools in Ukrainian, as a person reads them: a job's
// stored reason is rendered in the reader's language when it is read — a feed's own words nested verbatim, a reason an
// older build stored in English converted at start and then read in Ukrainian, a reason kept as its words shown as they
// are —, a refused filter quotes its value, and get_job counts a job's files in the noun's own form on a connection that
// asks for uk. The same request without the header answers English. Each answer spelled here, never read from the files.
public sealed class DownloadsUkrainianHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/downloads";
    private const string TolokaAddress = "http://prowlarr.test:9696/1/api";
    private const string ReleaseTitle = "Interstellar (2014) BDRip 1080p";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private IReleaseSeeder _releases = null!;
    private AuthorizedUser _owner = null!;
    private IntegrationConnectionEntity _toloka = null!;
    private IntegrationConnectionEntity _client = null!;
    private DestinationMappingEntity _movies = null!;

    public DownloadsUkrainianHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        var integrations = new IntegrationConnectionSeeder(_factory.Scope);
        _releases = new ReleaseSeeder(_factory.Scope);
        _owner = await new UserSeeder(_factory.Scope).CreateAuthorizedOwner("owner");
        _toloka = await integrations.Create("torznab", "Toloka", TolokaAddress, apiKey: "planted-torznab-key-0047");
        // Nothing answers it: reading the jobs never asks the client.
        _client = await integrations.Create(
            "qbittorrent", "qBittorrent", FakeQBittorrentServer.DefaultBaseUrl, userName: FakeQBittorrentServer.DefaultUserName, password: "secret");
        _movies = await new DestinationMappingSeeder(_factory.Scope).Create(_client, "Movies", "Movies", isDefault: true);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task List_asked_in_ukrainian_reads_a_failed_jobs_reason_with_the_feeds_own_words_nested()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var job = await PlantJobAsync(DownloadJobStatus.Failed);
        await SetErrorAsync(job.Id, Message.Of(
            "torznab.download.failed",
            ("error", Message.Of("torznab.error.described-clause", ("code", 500), ("description", VendorText.Clean("Download failed", 300))))));

        //Act
        var ukrainian = await ListAsync("uk", cancellationToken);
        var english = await ListAsync(null, cancellationToken);

        //Assert
        Assert.Equal("Джерелу Torznab не вдалося віддати реліз (помилка 500: Download failed).", Assert.Single(ukrainian.Items).Error);
        Assert.Equal("The Torznab feed failed to hand over the release (error 500: Download failed).", Assert.Single(english.Items).Error);
    }

    [Fact]
    public async Task List_asked_in_ukrainian_reads_a_block_reason_an_older_build_stored_in_english()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var job = await PlantJobAsync(DownloadJobStatus.Downloading);
        await _factory.ConvertStoredEnglishAsync(
            "DownloadJobs",
            "BlockReason",
            job.Id,
            "“qBittorrent” doesn't list this download any more. It is still followed in case it comes back; to send the release again, cancel this job first.");

        //Act
        var list = await ListAsync("uk", cancellationToken);

        //Assert
        Assert.Equal(
            "Клієнт «qBittorrent» більше не показує цього завантаження. Воно й далі відстежується на випадок, якщо з’явиться знову; щоб надіслати реліз ще раз, спершу скасуйте це завдання.",
            Assert.Single(list.Items).BlockReason);
    }

    [Fact]
    public async Task List_asked_in_ukrainian_shows_a_reason_kept_as_its_words_as_they_are()
    {
        //Arrange
        // What the startup converter couldn't read back stays as the words it found, in every language.
        var cancellationToken = TestContext.Current.CancellationToken;
        var job = await PlantJobAsync(DownloadJobStatus.Failed);
        await SetErrorAsync(job.Id, Message.Legacy("The tracker banned this account for its ratio."));

        //Act
        var list = await ListAsync("uk", cancellationToken);

        //Assert
        Assert.Equal("The tracker banned this account for its ratio.", Assert.Single(list.Items).Error);
    }

    [Fact]
    public async Task List_with_a_status_it_lacks_asked_in_ukrainian_is_400_quoting_the_value()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}?status=Stalled", _owner.AccessToken, language: "uk");

        //Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            """{"error":"Значення «Stalled» не є статусом завантаження. Вкажіть Created, Submitted, Downloading, Paused, Downloaded, Failed, NeedsLinking або Cancelled.","code":"downloads.jobs.status-unknown"}""",
            await response.Content.ReadAsStringAsync(cancellationToken));
    }

    [Theory]
    [InlineData(1, "наведено повний список його файлів: 1 файл")]
    [InlineData(3, "наведено повний список його файлів: 3 файли")]
    [InlineData(5, "наведено повний список його файлів: 5 файлів")]
    public async Task GetJob_on_a_connection_asking_for_ukrainian_counts_the_files_in_their_own_form(int files, string listed)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var job = await PlantJobAsync(DownloadJobStatus.Downloading);
        await Jobs().AddFiles(
            job.Id,
            [.. Enumerable.Range(1, files).Select(n => new DownloadJobFileEntity { RelativePath = $"Pack/{n:00}.mkv", SizeBytes = 1_000 + n, Progress = 0.5, IsWanted = true })]);
        var token = await new PersonalAccessTokenSeeder(_factory.Scope).Create(_owner.User);
        await using var client = await _factory.ConnectMcpAsync(token.Secret, cancellationToken: cancellationToken, language: "uk");

        //Act
        var result = await client.CallToolAsync("get_job", new Dictionary<string, object?> { ["jobId"] = job.Id }, cancellationToken: cancellationToken);

        //Assert
        Assert.False(result.IsError);
        Assert.Equal($"Реліз «Interstellar (2014) BDRip 1080p» у клієнті «qBittorrent» має статус Downloading; {listed}.", result.ReadRawText());
    }

    [Fact]
    public async Task GetJob_on_a_connection_asking_for_ukrainian_adds_the_jobs_reason_after_its_summary()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var job = await PlantJobAsync(DownloadJobStatus.Downloading);
        await SetBlockReasonAsync(job.Id, Message.Of("downloads.follow.missing", ("client", "qBittorrent")));
        var token = await new PersonalAccessTokenSeeder(_factory.Scope).Create(_owner.User);
        await using var client = await _factory.ConnectMcpAsync(token.Secret, cancellationToken: cancellationToken, language: "uk");

        //Act
        var result = await client.CallToolAsync("get_job", new Dictionary<string, object?> { ["jobId"] = job.Id }, cancellationToken: cancellationToken);

        //Assert
        Assert.Equal(
            "Реліз «Interstellar (2014) BDRip 1080p» у клієнті «qBittorrent» має статус Downloading; клієнт завантажень ще не показує його файлів. Клієнт «qBittorrent» більше не показує цього завантаження. Воно й далі відстежується на випадок, якщо з’явиться знову; щоб надіслати реліз ще раз, спершу скасуйте це завдання.",
            result.ReadRawText());
    }

    private async Task<DownloadJobEntity> PlantJobAsync(DownloadJobStatus status)
    {
        var release = await _releases.CreateFound(_owner.User, _toloka, ReleaseTitle);
        return await Jobs().Create(release.Id, _client.Id, _client.Name, _movies.Id, status, _owner.User.Id);
    }

    private Task SetErrorAsync(Guid jobId, Message error) =>
        _factory.WithDbAsync(async db =>
        {
            var job = await db.DownloadJobs.SingleAsync(row => row.Id == jobId);
            job.Error = error;
            job.ErrorOrigin = DownloadJobErrorOrigin.ReleaseSource;
            await db.SaveChangesAsync();
        });

    private Task SetBlockReasonAsync(Guid jobId, Message reason) =>
        _factory.WithDbAsync(async db =>
        {
            var job = await db.DownloadJobs.SingleAsync(row => row.Id == jobId);
            job.BlockReason = reason;
            job.BlockReasonOrigin = DownloadJobErrorOrigin.App;
            await db.SaveChangesAsync();
        });

    private async Task<DownloadJobListResponse> ListAsync(string? language, CancellationToken cancellationToken)
    {
        var response = await _factory.GetHttpRequest(BaseUrl, _owner.AccessToken, language: language);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // A list asked in a language reads as it came; one asked in none reads through the pass's own checks.
        var options = language is null ? MediaAcquisitionManagerApiFactory.JsonOptions : MediaAcquisitionManagerApiFactory.PlainJsonOptions;
        return (await response.Content.ReadFromJsonAsync<DownloadJobListResponse>(options, cancellationToken))!;
    }

    private IDownloadJobDataFactory Jobs() => new DownloadJobDataFactory(_factory.Scope.GetRequiredService<ApplicationDbContext>(), _factory.Time);
}
