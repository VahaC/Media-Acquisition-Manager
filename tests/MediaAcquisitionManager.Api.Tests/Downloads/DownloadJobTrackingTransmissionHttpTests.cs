using System.Net;
using System.Net.Http.Json;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Downloads;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Paths;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using MediaAcquisitionManager.TestInfrastructure.Texts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MediaAcquisitionManager.Api.Tests.Downloads;

// MAM-30, ADR 0014 — following a download sent to a real Transmission, end to end: the real adapter over HTTP to a
// FakeTransmissionServer answering as 4.1.3 does, the poller's passes run by hand. A torrent is found by the hashString
// the add answered, each pass a torrent-get naming it and only the fields a job is read from; its status is the job's —
// downloading with its progress, stopped as paused, queued as waiting, complete only once nothing wanted is left (D10:
// only a local error stops it, a tracker's warning or error doesn't) — and its files are the job's. One Transmission no
// longer lists is missing at once, without the renamed-job search that would read every torrent on every pass (D19).
// The Api's own rules over a stand-in client are DownloadJobTrackingFolderClientHttpTests'.
public sealed class DownloadJobTrackingTransmissionHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/downloads";
    private const string TolokaAddress = "http://prowlarr.test:9696/1/api";
    private const string ApiKey = "planted-torznab-key-0030";
    private const string MoviesFolder = "/downloads/movies";
    private const string FileName = "Film.2001.mkv";
    private const int FileLength = (3 * TestTorrent.PieceLength) + 5;

    // Sentences the Jobs page shows, spelled out rather than read from product code.
    private const string ErrorReason =
        "“Transmission” reports an error for this download, so it isn't moving on. Check it in “Transmission”: the job goes on by itself once the error clears.";
    private const string MissingReason =
        "“Transmission” doesn't list this download any more. It is still followed in case it comes back; to send the release again, cancel this job first.";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private AuthorizedUser _member = null!;
    private FakeTransmissionServer _transmission = null!;
    private IntegrationConnectionEntity _toloka = null!;
    private IntegrationConnectionEntity _client = null!;
    private DestinationMappingEntity _movies = null!;
    private string _hash = null!;

    public DownloadJobTrackingTransmissionHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        var integrations = new IntegrationConnectionSeeder(_factory.Scope);
        _member = await new UserSeeder(_factory.Scope).CreateAuthorized("member");
        var torrent = TestTorrent.V1(FileName, TestTorrent.Bytes(FileLength, seed: 1));
        _hash = TorrentInfoHashReader.FromTorrentFile(torrent).V1!;
        new FakeTorznabServer(TolokaAddress, ApiKey) { TorrentFile = torrent }.Arm(_factory.TorznabHttp);
        _toloka = await integrations.Create("torznab", "Toloka", TolokaAddress, apiKey: ApiKey);
        _transmission = new FakeTransmissionServer();
        _transmission.AddFolder(MoviesFolder);
        _transmission.Arm(_factory.TransmissionHttp);
        _client = await integrations.Create(
            "transmission",
            "Transmission",
            _transmission.BaseUrl,
            userName: FakeTransmissionServer.DefaultUserName,
            password: FakeTransmissionServer.DefaultPassword,
            pathMappings: [new PathMappingPair("/downloads", "/media")]);
        _movies = await new DestinationMappingSeeder(_factory.Scope).Create(_client, "Фільми", MoviesFolder, isDefault: true);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Theory]
    // 4.0 writes a whole fraction 1, 4.1 writes it 1.0, and lists each file's pieces too.
    [InlineData("4.0.6")]
    [InlineData("4.1.3")]
    public async Task Poll_follows_the_torrent_transmission_took_by_its_hash_from_downloading_to_downloaded_with_its_files(string version)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        _transmission.Version = version == "4.0.6" ? FakeTransmissionVersion.V4_0_6 : FakeTransmissionVersion.V4_1_3;
        var job = await SendAsync(cancellationToken);
        var mark = _transmission.Calls.Count;

        //Act
        await PassAsync(cancellationToken);
        var started = await ReadAsync(job.Id, cancellationToken);
        _transmission.UpdateTorrent(_hash, torrent => torrent.WithFiles([new FakeTransmissionFile(FileName, FileLength, FileLength / 2)]));
        await PassAsync(cancellationToken);
        var halfway = await ReadAsync(job.Id, cancellationToken);
        _transmission.Complete(_hash);
        await PassAsync(cancellationToken);
        var done = await ReadAsync(job.Id, cancellationToken);
        var files = await _factory.GetHttpRequest($"{BaseUrl}/{job.Id}/files", _member.AccessToken);

        //Assert
        Assert.Equal((DownloadJobStatus.Downloading, (double?)0, (string?)null), (started.Status, started.Progress, started.BlockReason));
        Assert.Equal(DownloadJobStatus.Downloading, halfway.Status);
        Assert.Equal(0.5, halfway.Progress!.Value, precision: 3);
        Assert.Equal((DownloadJobStatus.Downloaded, (double?)1, (string?)null), (done.Status, done.Progress, done.BlockReason));
        Assert.Equal(HttpStatusCode.OK, files.StatusCode);
        var listed = (await files.Content.ReadFromJsonAsync<DownloadJobFileListResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
        Assert.Equivalent(new[] { new DownloadJobFileDto(0, FileName, FileLength, 1, true) }, listed.Items, strict: true);
        // Every read names the torrent by its hash and asks for no more than a job or its files are read from: never its
        // id, its labels, its trackers or its magnet link.
        var reads = _transmission.Calls.Skip(mark).Where(call => call.Method == "torrent-get").ToList();
        Assert.NotEmpty(reads);
        Assert.All(reads, read => Assert.Equal($"[\"{_hash}\"]", read.Arguments["ids"]?.ToJsonString()));
        Assert.Equal(
            [
                """["hashString","name","status","error","percentDone","leftUntilDone","metadataPercentComplete","downloadDir"]""",
                """["hashString","metadataPercentComplete","files","fileStats"]""",
            ],
            reads.Select(read => read.Arguments["fields"]!.ToJsonString()).Distinct());
        Assert.Equal(["torrent-add"], _transmission.Writes.Select(call => call.Method));
    }

    [Fact]
    public async Task Poll_of_a_magnet_still_fetching_its_metadata_never_reads_it_as_downloaded_until_its_files_are_there()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        const string Hash = "c9e15763f722f23e98a29decdfae341b98d53056";
        var release = await new ReleaseSeeder(_factory.Scope).CreateFound(
            _member.User, _toloka, "Film (2001) WEBRip", $"magnet:?xt=urn:btih:{Hash}&dn=Film.2001", payloadKind: ReleasePayloadKind.Magnet);
        var sent = await _factory.PostHttpRequest(BaseUrl, _member.AccessToken, new SendDownloadRequest(Guid.NewGuid(), release.Id, _client.Id, _movies.Id));
        var job = (await sent.Content.ReadFromJsonAsync<DownloadJobDto>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;

        //Act
        // Nothing left to download at 0 %: Transmission's answer while it has no metadata.
        await PassAsync(cancellationToken);
        var fetching = await ReadAsync(job.Id, cancellationToken);
        var noFiles = await FilesAsync(job.Id, cancellationToken);
        _transmission.GetMetadata(Hash, [new FakeTransmissionFile("Film.2001/Film.2001.mkv", FileLength, 0)]);
        _transmission.Complete(Hash);
        await PassAsync(cancellationToken);
        var done = await ReadAsync(job.Id, cancellationToken);
        var files = await FilesAsync(job.Id, cancellationToken);

        //Assert
        Assert.Equal((DownloadJobStatus.Downloading, (string?)Hash), (fetching.Status, fetching.ClientJobId));
        Assert.Empty(noFiles);
        Assert.Equal((DownloadJobStatus.Downloaded, (double?)1), (done.Status, done.Progress));
        Assert.Equal([("Film.2001/Film.2001.mkv", (long?)FileLength)], files.Select(file => (file.Path, file.SizeBytes)));
    }

    [Theory]
    // Stopped short of done: paused; stopped once done: downloaded.
    [InlineData(0, 0, false, DownloadJobStatus.Paused, false)]
    [InlineData(0, 0, true, DownloadJobStatus.Downloaded, false)]
    // Queued to verify or to download before it ever started: still only taken.
    [InlineData(1, 0, false, DownloadJobStatus.Submitted, false)]
    [InlineData(3, 0, false, DownloadJobStatus.Submitted, false)]
    // Seeding, or queued to seed, once complete.
    [InlineData(5, 0, true, DownloadJobStatus.Downloaded, false)]
    [InlineData(6, 0, true, DownloadJobStatus.Downloaded, false)]
    // D10 — a tracker's warning (1) or error (2) leaves the download going; a local error (3) stops it, said so.
    [InlineData(4, 1, false, DownloadJobStatus.Downloading, false)]
    [InlineData(4, 2, false, DownloadJobStatus.Downloading, false)]
    [InlineData(4, 3, false, DownloadJobStatus.Submitted, true)]
    public async Task Poll_reads_transmissions_status_and_error_as_the_jobs(int status, int error, bool complete, DownloadJobStatus expected, bool blocked)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var job = await SendAsync(cancellationToken);
        _transmission.UpdateTorrent(_hash, torrent =>
            torrent.WithFiles([new FakeTransmissionFile(FileName, FileLength, complete ? FileLength : FileLength / 4)]) with
            {
                Status = status,
                Error = error,
                ErrorString = error == 0 ? "" : "planted error",
            });

        //Act
        await PassAsync(cancellationToken);

        //Assert
        var shown = await ReadAsync(job.Id, cancellationToken);
        Assert.Equal((expected, blocked ? ErrorReason : null), (shown.Status, shown.BlockReason));
    }

    [Fact]
    public async Task Poll_of_a_torrent_removed_in_transmission_is_missing_at_once_and_never_reads_every_torrent()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var job = await SendAsync(cancellationToken);
        // Another torrent in the same folder: what a search for a renamed job would read.
        _transmission.AddTorrent(new TorrentInfoHashes("0123456789abcdef0123456789abcdef01234567", null), "Another", MoviesFolder);
        _transmission.RemoveTorrent(_hash);
        var mark = _transmission.Calls.Count;

        //Act
        await PassAsync(cancellationToken);
        await PassAsync(cancellationToken);

        //Assert
        // D19 — Transmission names a torrent by its v1 hash for good: still followed, and only ever looked up by it.
        DownloadJobEntity? row = null;
        await _factory.WithDbAsync(async db => row = await db.DownloadJobs.AsNoTracking().SingleAsync(stored => stored.Id == job.Id, cancellationToken));
        Assert.Equal(
            (DownloadJobStatus.Submitted, (string?)MissingReason, (DownloadJobErrorOrigin?)DownloadJobErrorOrigin.App),
            (row!.Status, TestTexts.En(row.BlockReason), row.BlockReasonOrigin));
        var reads = _transmission.Calls.Skip(mark).Where(call => call.Method == "torrent-get").ToList();
        Assert.Equal(2, reads.Count);
        Assert.All(reads, read => Assert.Equal($"[\"{_hash}\"]", read.Arguments["ids"]?.ToJsonString()));
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>The member's send of the film to Фільми through the real route: Transmission adds it, downloading.</summary>
    private async Task<DownloadJobDto> SendAsync(CancellationToken cancellationToken)
    {
        var release = await new ReleaseSeeder(_factory.Scope).CreateFound(_member.User, _toloka, "Film (2001) BDRip 1080p");
        var response = await _factory.PostHttpRequest(BaseUrl, _member.AccessToken, new SendDownloadRequest(Guid.NewGuid(), release.Id, _client.Id, _movies.Id));
        var job = (await response.Content.ReadFromJsonAsync<DownloadJobDto>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
        if (job.Status != DownloadJobStatus.Submitted)
            throw new InvalidOperationException($"The send ended {job.Status}: {job.Error}");
        return job;
    }

    /// <summary>One pass of the poller, a moment after the last, which must not have logged an error.</summary>
    private async Task PassAsync(CancellationToken cancellationToken)
    {
        _factory.Time.Advance(TimeSpan.FromMinutes(1));
        await _factory.PollDownloadJobsAsync(cancellationToken);
        if (_factory.Logs.Entries.FirstOrDefault(entry => entry.Level >= LogLevel.Error) is { } logged)
            throw new InvalidOperationException($"A pass logged an error: {logged.Category}: {logged.Message}");
    }

    private async Task<IReadOnlyList<DownloadJobFileDto>> FilesAsync(Guid jobId, CancellationToken cancellationToken)
    {
        var response = await _factory.GetHttpRequest($"{BaseUrl}/{jobId}/files", _member.AccessToken);
        if (response.StatusCode != HttpStatusCode.OK)
            throw new InvalidOperationException($"The job's files answered {(int)response.StatusCode}.");
        return (await response.Content.ReadFromJsonAsync<DownloadJobFileListResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!.Items;
    }

    private async Task<DownloadJobDto> ReadAsync(Guid jobId, CancellationToken cancellationToken)
    {
        var response = await _factory.GetHttpRequest($"{BaseUrl}/{jobId}", _member.AccessToken);
        if (response.StatusCode != HttpStatusCode.OK)
            throw new InvalidOperationException($"The job's read answered {(int)response.StatusCode}.");
        return (await response.Content.ReadFromJsonAsync<DownloadJobDto>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
    }
}
