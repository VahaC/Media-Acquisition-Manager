using System.Net;
using System.Net.Http.Json;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Abstractions.Downloads;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Paths;
using MediaAcquisitionManager.Infrastructure.Persistence;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Data;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using MediaAcquisitionManager.TestInfrastructure.Texts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MediaAcquisitionManager.Api.Tests.Downloads;

// MAM-30, D19 — following a job of a client whose job ids never change (Transmission keeps a torrent's v1 hash for
// good): it is found by that hash, and one the client no longer lists is missing at once, with why, without the fallback
// that reads every torrent of its destination for a job renamed past its hashes — for such a client that would read
// every torrent it has on every pass while the job is missing (ADR 0013, ADR 0014).
public sealed class DownloadJobTrackingFolderClientHttpTests : IClassFixture<FolderClientApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/downloads";
    private const string TolokaAddress = "http://prowlarr.test:9696/1/api";
    private const string ApiKey = "planted-torznab-key-0030";
    private const string MoviesFolder = "/downloads/movies";
    private const string Hash = "0123456789abcdef0123456789abcdef01234567";
    private const string MissingReason =
        "“Transmission” doesn't list this download any more. It is still followed in case it comes back; to send the release again, cancel this job first.";

    private readonly FolderClientApiFactory _factory;
    private IReleaseSeeder _releases = null!;
    private AuthorizedUser _member = null!;
    private IntegrationConnectionEntity _toloka = null!;
    private IntegrationConnectionEntity _client = null!;
    private DestinationMappingEntity _movies = null!;

    public DownloadJobTrackingFolderClientHttpTests(FolderClientApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        var integrations = new IntegrationConnectionSeeder(_factory.Scope);
        _releases = new ReleaseSeeder(_factory.Scope);
        _member = await new UserSeeder(_factory.Scope).CreateAuthorized("member");
        _toloka = await integrations.Create("torznab", "Toloka", TolokaAddress, apiKey: ApiKey);
        _client = await integrations.Create(FakeFolderDownloadClient.TypeId, "Transmission", pathMappings: [new PathMappingPair("/downloads", "/media")]);
        _movies = await new DestinationMappingSeeder(_factory.Scope).Create(_client, "Фільми", MoviesFolder, isDefault: true);
        _factory.FolderClient.Arm();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Poll_follows_a_folder_clients_torrent_by_its_hash_and_reads_its_files()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var job = await PlantAsync();
        _factory.FolderClient.AddTorrent(new FakeFolderTorrent(
            Hash, "Film.2001.mkv", MoviesFolder, [new ClientJobFile("Film.2001.mkv", 1_000, 0.4, true)], ClientJobState.Downloading, 0.4));

        //Act
        await PassAsync(cancellationToken);

        //Assert
        var shown = await ReadAsync(job.Id, cancellationToken);
        Assert.Equal((DownloadJobStatus.Downloading, (double?)0.4, (string?)null), (shown.Status, shown.Progress, shown.BlockReason));
        Assert.Equal(["FindJobs", $"ListJobFiles {Hash}"], _factory.FolderClient.Calls);
    }

    [Fact]
    public async Task Poll_of_a_torrent_the_client_no_longer_lists_shows_why_and_never_reads_every_torrent_of_its_folder()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // Sent knowing only its v1 hash, into a destination that can be listed: what qBittorrent's rename fallback reads.
        var job = await PlantAsync();
        _factory.FolderClient.AddTorrent(new FakeFolderTorrent(Hash[..^1] + "8", "Another", MoviesFolder, []));

        //Act
        await PassAsync(cancellationToken);
        await PassAsync(cancellationToken);

        //Assert
        // D19 — missing at once, still followed, and only ever looked up by its hash.
        var row = await RowAsync(job.Id, cancellationToken);
        Assert.Equal(
            (DownloadJobStatus.Submitted, MissingReason, (DownloadJobErrorOrigin?)DownloadJobErrorOrigin.App),
            (row.Status, TestTexts.En(row.BlockReason), row.BlockReasonOrigin));
        Assert.Equal(["FindJobs", "FindJobs"], _factory.FolderClient.Calls);
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>A Submitted job of the member's to Фільми, taken by the client as its v1 hash.</summary>
    private async Task<DownloadJobEntity> PlantAsync()
    {
        var release = await _releases.CreateFound(_member.User, _toloka);
        var jobs = new DownloadJobDataFactory(_factory.Scope.GetRequiredService<ApplicationDbContext>(), _factory.Time);
        return await jobs.Create(
            release.Id,
            _client.Id,
            _client.Name,
            _movies.Id,
            DownloadJobStatus.Submitted,
            _member.User.Id,
            infoHashV1: Hash,
            clientJobId: Hash,
            destinationKind: DownloadDestinationKind.Folder);
    }

    private async Task PassAsync(CancellationToken cancellationToken)
    {
        await _factory.PollDownloadJobsAsync(cancellationToken);
        Assert.DoesNotContain(_factory.Logs.Entries, entry => entry.Level >= LogLevel.Error);
    }

    private async Task<DownloadJobEntity> RowAsync(Guid jobId, CancellationToken cancellationToken)
    {
        DownloadJobEntity? row = null;
        await _factory.WithDbAsync(async db => row = await db.DownloadJobs.AsNoTracking().SingleAsync(job => job.Id == jobId, cancellationToken));
        return row!;
    }

    private async Task<DownloadJobDto> ReadAsync(Guid jobId, CancellationToken cancellationToken)
    {
        var response = await _factory.GetHttpRequest($"{BaseUrl}/{jobId}", _member.AccessToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<DownloadJobDto>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
    }
}
