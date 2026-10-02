using System.Net;
using System.Net.Http.Json;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Api.Services.Downloads;
using MediaAcquisitionManager.Core.Downloads;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Paths;
using MediaAcquisitionManager.Infrastructure.Persistence;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Data;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MediaAcquisitionManager.Api.Tests.Downloads;

// MAM-14 — following a job from what its client says (ADR 0014), each pass of the app's own tracker against the
// stateful fake qBittorrent: every row of the mapping from a client's state onto the job — Downloaded only from a
// finished state at a progress of 1, a re-check and a state this build doesn't know moving nothing — with the
// progress stored and the status's time changed only when the status is; a downloaded job never asked about
// again; its files stored and read again only when the job moved, a magnet's until it lists some, and kept when
// the torrent goes before they are read; its files read before the job is written and written with it, so it
// never reads as ahead of them; a client that stops answering after the lookup asked nothing more that pass; an
// error the client reports shown as a reason that goes once the error does (MAM-14's decision 2); nothing
// written for a job nothing happened to; every job of a client looked up in batches of fifty ids; a log of ids
// and statuses only; a pass that throws logged, the next one going on, and one a shutdown stops left unlogged;
// and the tracker's own loop, a pass right after it starts and a stop no error.
// What the person sees is read back over HTTP. Restarts are DownloadJobRecoveryHttpTests', torrents the client
// doesn't list and clients that can't be asked DownloadJobMissingHttpTests'.
public sealed class DownloadJobTrackingHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/downloads";
    private const string TolokaAddress = "http://prowlarr.test:9696/1/api";
    private const string ApiKey = "planted-torznab-key-0001";
    private const string ReleaseTitle = "Taken. Trilogy (2008-2014) BDRip 1080p";

    /// <summary>The v1 info hash of <see cref="TorznabSamples.TorrentFile"/>, which the feed's download links
    /// answer: qBittorrent's id for the torrent a send adds.</summary>
    private const string SampleInfoHash = "72d6069eff259a0800f7afdb2500d5de591a9f01";

    private const string SignIn = "POST /api/v2/auth/login";
    private const string Version = "GET /api/v2/app/version";

    // Spelled out rather than read from the product code: this is what the Jobs page shows.
    private const string ClientErrorReason =
        "“qBittorrent” reports an error for this download, so it isn't moving on. Check it in “qBittorrent”: the job goes on by itself once the error clears.";
    private const string UnreachableReason =
        "Couldn't reach qBittorrent at http://qbittorrent.test:8080: nothing accepted the connection. Check the address and port, and that qBittorrent is running.";

    private const string FilesPath = "/api/v2/torrents/files";

    // The trilogy pack of VendorSamples/QBittorrent/torrent-files-multi.json, as qBittorrent names its files.
    private const string TakenFolder = "Taken. Trilogy (2008-2014) BDRip 1080p";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private IReleaseSeeder _releases = null!;
    private AuthorizedUser _member = null!;
    private FakeQBittorrentServer _qbittorrent = null!;
    private IntegrationConnectionEntity _toloka = null!;
    private IntegrationConnectionEntity _client = null!;
    private DestinationMappingEntity _movies = null!;

    public DownloadJobTrackingHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public static TheoryData<DownloadJobStatus, string, double, DownloadJobStatus, double> ClientStates => new()
    {
        { DownloadJobStatus.Submitted, "downloading", 0.25, DownloadJobStatus.Downloading, 0.25 },
        { DownloadJobStatus.Submitted, "stalledDL", 0.3, DownloadJobStatus.Downloading, 0.3 },
        // A magnet fetching its metadata is under way.
        { DownloadJobStatus.Submitted, "metaDL", 0, DownloadJobStatus.Downloading, 0 },
        // Queued before it started stays taken; queued or checked again after it started is still under way.
        { DownloadJobStatus.Submitted, "queuedDL", 0.1, DownloadJobStatus.Submitted, 0.1 },
        { DownloadJobStatus.Downloading, "queuedDL", 0.6, DownloadJobStatus.Downloading, 0.6 },
        { DownloadJobStatus.Paused, "checkingDL", 0.6, DownloadJobStatus.Downloading, 0.6 },
        { DownloadJobStatus.Downloading, "stoppedDL", 0.7, DownloadJobStatus.Paused, 0.7 },
        { DownloadJobStatus.Downloading, "pausedDL", 0.7, DownloadJobStatus.Paused, 0.7 },
        { DownloadJobStatus.Paused, "downloading", 0.8, DownloadJobStatus.Downloading, 0.8 },
        // Finished at 1: downloaded, however it got there.
        { DownloadJobStatus.Downloading, "stalledUP", 1, DownloadJobStatus.Downloaded, 1 },
        { DownloadJobStatus.Submitted, "uploading", 1, DownloadJobStatus.Downloaded, 1 },
        { DownloadJobStatus.Paused, "stoppedUP", 1, DownloadJobStatus.Downloaded, 1 },
        // A re-check of a finished torrent reports the check's progress under its finished state: nothing moves,
        // and the stored progress stays.
        { DownloadJobStatus.Downloading, "checkingUP", 0.4, DownloadJobStatus.Downloading, 0.5 },
        // A state this build doesn't know (moving, one a later qBittorrent adds) is never guessed into a status.
        { DownloadJobStatus.Downloading, "moving", 1, DownloadJobStatus.Downloading, 0.5 },
        { DownloadJobStatus.Paused, "teleporting", 1, DownloadJobStatus.Paused, 0.5 },
    };

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        var integrations = new IntegrationConnectionSeeder(_factory.Scope);
        _releases = new ReleaseSeeder(_factory.Scope);
        var users = new UserSeeder(_factory.Scope);
        await users.CreateAuthorizedOwner("owner");
        _member = await users.CreateAuthorized("member");
        new FakeTorznabServer(TolokaAddress, ApiKey).Arm(_factory.TorznabHttp);
        _toloka = await integrations.Create("torznab", "Toloka", TolokaAddress, apiKey: ApiKey);
        _qbittorrent = new FakeQBittorrentServer();
        _qbittorrent.Arm(_factory.QBittorrentHttp);
        _client = await integrations.Create(
            "qbittorrent",
            "qBittorrent",
            _qbittorrent.BaseUrl,
            userName: FakeQBittorrentServer.DefaultUserName,
            password: FakeQBittorrentServer.DefaultPassword,
            pathMappings: [new PathMappingPair("/downloads", "/media")]);
        _movies = await new DestinationMappingSeeder(_factory.Scope).Create(_client, "Movies", "Movies", isDefault: true);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Poll_moves_a_sent_job_on_as_qBittorrent_downloads_it_and_the_job_reads_so()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var sent = await SendAsync(cancellationToken);
        _qbittorrent.UpdateTorrent(SampleInfoHash, torrent => torrent with { State = "downloading", Progress = 0.25 });
        _factory.Time.Advance(TimeSpan.FromMinutes(1));
        var movedUtc = _factory.Time.GetUtcNow().UtcDateTime;
        var before = _factory.QBittorrentHttp.Requests.Count;

        //Act
        await PassAsync(cancellationToken);

        //Assert
        // On the session the send opened: the lookup by the id qBittorrent gave, then — the job moved — its files.
        Assert.Equal([Version, Lookup(SampleInfoHash), Version, Files(SampleInfoHash)], QBittorrentCalls()[before..]);
        var response = await _factory.GetHttpRequest($"{BaseUrl}/{sent.Id}", _member.AccessToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var job = (await response.Content.ReadFromJsonAsync<DownloadJobDto>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
        Assert.Equivalent(sent with { Status = DownloadJobStatus.Downloading, Progress = 0.25, StatusChangedUtc = movedUtc }, job, strict: true);
        var row = await RowAsync(sent.Id, cancellationToken);
        Assert.Equal(
            (DownloadJobStatus.Downloading, (double?)0.25, SampleInfoHash, movedUtc, (string?)null),
            (row.Status, row.Progress, row.ClientJobId, row.StatusChangedUtc, row.BlockReason));
    }

    [Theory]
    [MemberData(nameof(ClientStates))]
    public async Task Poll_maps_what_qBittorrent_reports_onto_the_job(
        DownloadJobStatus current, string state, double clientProgress, DownloadJobStatus expectedStatus, double expectedProgress)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var job = await PlantAsync(current, Hash(1), state, clientProgress, storedProgress: 0.5);
        var plantedUtc = _factory.Time.GetUtcNow().UtcDateTime;
        _factory.Time.Advance(TimeSpan.FromMinutes(1));
        var passUtc = _factory.Time.GetUtcNow().UtcDateTime;

        //Act
        await PassAsync(cancellationToken);

        //Assert
        // The status's time moves only with the status; nothing blocks a job the client reports no error for.
        var row = await RowAsync(job.Id, cancellationToken);
        Assert.Equal(
            (expectedStatus, (double?)expectedProgress, expectedStatus == current ? plantedUtc : passUtc, (string?)null, Hash(1)),
            (row.Status, row.Progress, row.StatusChangedUtc, row.BlockReason, row.ClientJobId));
        var read = await ReadAsync(job.Id, cancellationToken);
        Assert.Equal((expectedStatus, (double?)expectedProgress), (read.Status, read.Progress));
    }

    [Fact]
    public async Task Poll_changes_a_jobs_status_time_only_when_its_status_changes()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var job = await PlantAsync(DownloadJobStatus.Submitted, Hash(1), "downloading", 0.2);
        _factory.Time.Advance(TimeSpan.FromMinutes(1));
        var startedUtc = _factory.Time.GetUtcNow().UtcDateTime;
        await PassAsync(cancellationToken);
        _qbittorrent.UpdateTorrent(Hash(1), torrent => torrent with { Progress = 0.6 });
        _factory.Time.Advance(TimeSpan.FromMinutes(1));

        //Act
        await PassAsync(cancellationToken);

        //Assert
        // Still downloading since the first pass, further on.
        var row = await RowAsync(job.Id, cancellationToken);
        Assert.Equal((DownloadJobStatus.Downloading, (double?)0.6, startedUtc), (row.Status, row.Progress, row.StatusChangedUtc));
    }

    [Fact]
    public async Task Poll_of_a_finished_download_ends_its_following_and_never_asks_about_it_again()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var job = await PlantAsync(DownloadJobStatus.Downloading, Hash(1), "stalledUP", 1, files: TakenFiles(1));
        await PassAsync(cancellationToken);
        var afterFirst = _factory.QBittorrentHttp.Requests.Count;

        //Act
        await PassAsync(cancellationToken);

        //Assert
        // Downloaded ends following: its final files were read once, on the pass that finished it, and the next
        // pass has nothing to ask qBittorrent at all.
        Assert.Equal([SignIn, Version, Lookup(Hash(1)), Version, Files(Hash(1))], QBittorrentCalls());
        Assert.Equal(afterFirst, _factory.QBittorrentHttp.Requests.Count);
        var row = await RowAsync(job.Id, cancellationToken);
        Assert.Equal((DownloadJobStatus.Downloaded, (double?)1), (row.Status, row.Progress));
        Assert.Equal(TakenRows(1), await StoredFilesAsync(job.Id, cancellationToken));
    }

    [Fact]
    public async Task Poll_stores_a_jobs_files_and_reads_them_again_only_when_the_job_moved()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var job = await PlantAsync(DownloadJobStatus.Downloading, Hash(1), "downloading", 0.1, storedProgress: 0.1, files: TakenFiles(0.1));
        await PassAsync(cancellationToken);
        var stored = await StoredFilesAsync(job.Id, cancellationToken);
        var afterFirst = _factory.QBittorrentHttp.Requests.Count;
        // Nothing moved in qBittorrent.
        await PassAsync(cancellationToken);
        var calledIdle = QBittorrentCalls()[afterFirst..];
        // Then it moves on, and lists one file fewer — a file renamed out of the torrent in qBittorrent.
        _qbittorrent.UpdateTorrent(Hash(1), torrent => torrent with { Progress = 0.5, Files = [.. TakenFiles(0.5).Take(2)] });
        var afterIdle = _factory.QBittorrentHttp.Requests.Count;

        //Act
        await PassAsync(cancellationToken);

        //Assert
        // Read on the first pass, as the job had none stored; not on the idle one; again once it moved, and the
        // stored list follows qBittorrent's, file for file.
        Assert.Equal(TakenRows(0.1), stored);
        Assert.Equal([Version, Lookup(Hash(1))], calledIdle);
        Assert.Equal([Version, Lookup(Hash(1)), Version, Files(Hash(1))], QBittorrentCalls()[afterIdle..]);
        Assert.Equal(TakenRows(0.5)[..2], await StoredFilesAsync(job.Id, cancellationToken));
        Assert.Equal((double?)0.5, (await RowAsync(job.Id, cancellationToken)).Progress);
    }

    [Fact]
    public async Task Poll_of_a_magnet_still_fetching_its_metadata_reads_its_files_until_it_lists_some()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var job = await PlantAsync(DownloadJobStatus.Downloading, Hash(1), "metaDL", 0, storedProgress: 0);
        await PassAsync(cancellationToken);
        var afterFirst = _factory.QBittorrentHttp.Requests.Count;
        await PassAsync(cancellationToken);
        var calledWhileFetching = QBittorrentCalls()[afterFirst..];
        // The metadata arrives: the files are known, and nothing has been downloaded yet.
        _qbittorrent.UpdateTorrent(Hash(1), torrent => torrent with { Files = TakenFiles(0) });

        //Act
        await PassAsync(cancellationToken);

        //Assert
        // No files listed is no files stored, so each pass reads them again, though nothing else moved.
        Assert.Equal([Version, Lookup(Hash(1)), Version, Files(Hash(1))], calledWhileFetching);
        Assert.Equal(TakenRows(0), await StoredFilesAsync(job.Id, cancellationToken));
        Assert.Equal(DownloadJobStatus.Downloading, (await RowAsync(job.Id, cancellationToken)).Status);
    }

    [Fact]
    public async Task Poll_keeps_the_stored_files_when_the_torrent_goes_between_the_lookup_and_the_files_read()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var job = await PlantAsync(DownloadJobStatus.Downloading, Hash(1), "downloading", 0.2, storedProgress: 0.2, files: TakenFiles(0.2));
        await PassAsync(cancellationToken);
        _qbittorrent.UpdateTorrent(Hash(1), torrent => torrent with { Progress = 0.4, Files = TakenFiles(0.4) });
        // Removed in qBittorrent just after the lookup listed it: its files answer 404.
        _qbittorrent.Intercept = request => request.RequestUri!.AbsolutePath == "/api/v2/torrents/files"
            ? VendorResponses.Text("Not Found", HttpStatusCode.NotFound)
            : null;

        //Act
        await PassAsync(cancellationToken);

        //Assert
        // The job took what the lookup said; the files stay as last read, and the files read set no reason —
        // why a job isn't moving on comes from the lookup alone.
        var row = await RowAsync(job.Id, cancellationToken);
        Assert.Equal((DownloadJobStatus.Downloading, (double?)0.4, (string?)null), (row.Status, row.Progress, row.BlockReason));
        Assert.Equal(TakenRows(0.2), await StoredFilesAsync(job.Id, cancellationToken));
    }

    [Fact]
    public async Task Poll_reads_the_files_before_the_job_turns_downloaded_and_writes_them_with_it()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var job = await PlantAsync(DownloadJobStatus.Downloading, Hash(1), "downloading", 0.6, storedProgress: 0.6, files: TakenFiles(0.6));
        await PassAsync(cancellationToken);
        var storedBefore = await StoredFilesAsync(job.Id, cancellationToken);
        // qBittorrent finishes it: the pass that reads so is the last one to read it at all.
        _qbittorrent.UpdateTorrent(Hash(1), torrent => torrent with { State = "stalledUP", Progress = 1, Files = TakenFiles(1) });
        // What anyone reading the job sees when qBittorrent is asked for the files, and while the pass writes them.
        DownloadJobStatus? whenFilesAsked = null;
        DownloadJobStatus? whileFilesWritten = null;
        _qbittorrent.Intercept = request =>
        {
            if (request.RequestUri!.AbsolutePath == FilesPath)
                whenFilesAsked = RowAsync(job.Id, cancellationToken).GetAwaiter().GetResult().Status;
            return null;
        };
        _factory.DbCommandHook.BeforeNextCommand(
            command => command.CommandText.StartsWith("UPDATE \"DownloadJobFiles\"", StringComparison.Ordinal),
            async token => whileFilesWritten = (await RowAsync(job.Id, token)).Status);

        //Act
        await PassAsync(cancellationToken);

        //Assert
        // Read before the job was written, and written with it, in one transaction: a reader sees the job still
        // Downloading until both are in, and then Downloaded with its final files — never a job ended on the files
        // of the pass before, which nothing would read again.
        Assert.Equal(TakenRows(0.6), storedBefore);
        Assert.Equal(DownloadJobStatus.Downloading, whenFilesAsked);
        Assert.Equal(DownloadJobStatus.Downloading, whileFilesWritten);
        var row = await RowAsync(job.Id, cancellationToken);
        Assert.Equal((DownloadJobStatus.Downloaded, (double?)1), (row.Status, row.Progress));
        Assert.Equal(TakenRows(1), await StoredFilesAsync(job.Id, cancellationToken));
    }

    [Fact]
    public async Task Poll_of_a_client_that_stops_answering_after_the_lookup_asks_it_nothing_more_that_pass()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // Two jobs that moved, so each would have its files read, and one qBittorrent no longer lists by its hash,
        // which would be looked for among the Movies torrents.
        var first = await PlantAsync(DownloadJobStatus.Submitted, Hash(1), "downloading", 0.2, files: TakenFiles(0.2));
        _factory.Time.Advance(TimeSpan.FromSeconds(1));
        var second = await PlantAsync(DownloadJobStatus.Submitted, Hash(2), "downloading", 0.4, files: TakenFiles(0.4));
        _factory.Time.Advance(TimeSpan.FromSeconds(1));
        var renamed = await PlantAsync(DownloadJobStatus.Downloading, Hash(3), "downloading", 0.5, storedProgress: 0.5);
        _qbittorrent.RemoveTorrent(Hash(3));
        // qBittorrent answers the lookup, then nothing more: its host stopped answering.
        _qbittorrent.Intercept = request => request.RequestUri!.AbsolutePath == FilesPath
            ? throw new HttpRequestException(HttpRequestError.ConnectionError, "No connection could be made because the target machine actively refused it.")
            : null;

        //Act
        await PassAsync(cancellationToken);

        //Assert
        // One files read, which failed; nothing else asked of qBittorrent that pass, each further call having waited
        // out its own timeout. Both observations written all the same, no files stored, and the job that needed its
        // destination listed shows the sentence of the call that failed — never missing.
        Assert.Single(_factory.QBittorrentHttp.Requests, request => request.Uri.AbsolutePath == FilesPath);
        Assert.DoesNotContain(QBittorrentCalls(), call => call.Contains("category=", StringComparison.Ordinal));
        var firstRow = await RowAsync(first.Id, cancellationToken);
        Assert.Equal((DownloadJobStatus.Downloading, (double?)0.2, (string?)null), (firstRow.Status, firstRow.Progress, firstRow.BlockReason));
        var secondRow = await RowAsync(second.Id, cancellationToken);
        Assert.Equal((DownloadJobStatus.Downloading, (double?)0.4, (string?)null), (secondRow.Status, secondRow.Progress, secondRow.BlockReason));
        Assert.Empty(await StoredFilesAsync(first.Id, cancellationToken));
        Assert.Empty(await StoredFilesAsync(second.Id, cancellationToken));
        var renamedRow = await RowAsync(renamed.Id, cancellationToken);
        Assert.Equal(
            (DownloadJobStatus.Downloading, UnreachableReason, (DownloadJobErrorOrigin?)DownloadJobErrorOrigin.DownloadClient),
            (renamedRow.Status, renamedRow.BlockReason, renamedRow.BlockReasonOrigin));
    }

    [Fact]
    public async Task Poll_of_a_torrent_qBittorrent_reports_an_error_for_keeps_its_status_with_why_until_the_error_clears()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var job = await PlantAsync(DownloadJobStatus.Downloading, Hash(1), "error", 0.4, storedProgress: 0.3);
        var plantedUtc = _factory.Time.GetUtcNow().UtcDateTime;
        _factory.Time.Advance(TimeSpan.FromMinutes(1));
        await PassAsync(cancellationToken);
        var errored = await RowAsync(job.Id, cancellationToken);
        var shown = await ReadAsync(job.Id, cancellationToken);
        // The files went missing on disk next: still an error, still followed.
        _qbittorrent.UpdateTorrent(Hash(1), torrent => torrent with { State = "missingFiles" });
        await PassAsync(cancellationToken);
        var stillErrored = await RowAsync(job.Id, cancellationToken);
        // Fixed in qBittorrent: downloading again.
        _qbittorrent.UpdateTorrent(Hash(1), torrent => torrent with { State = "downloading", Progress = 0.5 });

        //Act
        await PassAsync(cancellationToken);

        //Assert
        // MAM-14's decision 2: a reason, never an end — the status and its time stay, the progress is qBittorrent's,
        // and the reason is the app's sentence, the sender's to read. It goes by itself with the error.
        Assert.Equal(
            (DownloadJobStatus.Downloading, (double?)0.4, plantedUtc, ClientErrorReason, (DownloadJobErrorOrigin?)DownloadJobErrorOrigin.App),
            (errored.Status, errored.Progress, errored.StatusChangedUtc, errored.BlockReason, errored.BlockReasonOrigin));
        Assert.Equal((DownloadJobStatus.Downloading, ClientErrorReason), (shown.Status, shown.BlockReason));
        Assert.Equal((DownloadJobStatus.Downloading, ClientErrorReason), (stillErrored.Status, stillErrored.BlockReason));
        var row = await RowAsync(job.Id, cancellationToken);
        Assert.Equal(
            (DownloadJobStatus.Downloading, (double?)0.5, plantedUtc, (string?)null, (DownloadJobErrorOrigin?)null),
            (row.Status, row.Progress, row.StatusChangedUtc, row.BlockReason, row.BlockReasonOrigin));
    }

    [Fact]
    public async Task Poll_writes_nothing_for_a_job_nothing_happened_to()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var job = await PlantAsync(DownloadJobStatus.Downloading, Hash(1), "stalledDL", 0.3, storedProgress: 0.3, files: TakenFiles(0.3));
        await PassAsync(cancellationToken);
        var before = await RowAsync(job.Id, cancellationToken);
        // Any write to the jobs or their files throws instead of being sent — which the pass would log.
        _factory.DbCommandHook.BeforeNextCommand(IsWrite, _ => throw new InvalidOperationException("The idle pass wrote."));
        _factory.Logs.Clear();

        //Act
        await PassAsync(cancellationToken);

        //Assert
        Assert.DoesNotContain(_factory.Logs.Entries, entry => entry.Level >= LogLevel.Error);
        Assert.Equivalent(before, await RowAsync(job.Id, cancellationToken), strict: true);
        Assert.Equal(TakenRows(0.3), await StoredFilesAsync(job.Id, cancellationToken));
    }

    [Fact]
    public async Task Poll_looks_every_job_of_a_client_up_in_batches_of_fifty_ids()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var count = DownloadJobPoller.LookupBatchSize + 1;
        for (var n = 1; n <= count; n++)
        {
            await PlantAsync(DownloadJobStatus.Submitted, Hash(n), "downloading", 0.5);
            // A second apart, so the oldest-first order is the order they were sent in.
            _factory.Time.Advance(TimeSpan.FromSeconds(1));
        }

        //Act
        await PassAsync(cancellationToken);

        //Assert
        // One lookup per fifty ids, oldest job first, and every job followed from them.
        var lookups = QBittorrentCalls().Where(call => call.StartsWith("GET /api/v2/torrents/info", StringComparison.Ordinal)).ToList();
        Assert.Equal(
            [
                Lookup([.. Enumerable.Range(1, DownloadJobPoller.LookupBatchSize).Select(Hash)]),
                Lookup(Hash(count)),
            ],
            lookups);
        await _factory.WithDbAsync(async db =>
            Assert.Equal(count, await db.DownloadJobs.CountAsync(row => row.Status == DownloadJobStatus.Downloading && row.Progress == 0.5, cancellationToken)));
    }

    [Fact]
    public async Task Poll_logs_each_status_change_by_id_and_never_a_title_a_path_or_an_address()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var moving = await PlantAsync(DownloadJobStatus.Submitted, Hash(1), "downloading", 0.2, files: TakenFiles(0.2));
        var idle = await PlantAsync(DownloadJobStatus.Downloading, Hash(2), "error", 0.2, storedProgress: 0.2);
        _factory.Logs.Clear();

        //Act
        await PassAsync(cancellationToken);

        //Assert
        // Information: the change itself, by id, and nothing else — the job the client reports an error for changed
        // no status, and the pass's counts go to Debug, below what the app logs: a pass runs every 10 s.
        var poller = _factory.Logs.Entries.Where(entry => entry.Category == typeof(DownloadJobPoller).FullName).ToList();
        Assert.Equal(
            [(LogLevel.Information, $"Download job {moving.Id} went from Submitted to Downloading.")],
            poller.Select(entry => (entry.Level, entry.Message)));
        Assert.DoesNotContain(idle.Id.ToString(), string.Join('\n', poller.Select(entry => entry.Message)), StringComparison.Ordinal);
        Assert.All(_factory.Logs.Entries, entry =>
        {
            Assert.DoesNotContain(ReleaseTitle, entry.Message, StringComparison.Ordinal);
            Assert.DoesNotContain(TakenFolder, entry.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("qbittorrent.test", entry.Message, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public async Task Poll_whose_pass_throws_logs_it_and_the_next_pass_goes_on()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var job = await PlantAsync(DownloadJobStatus.Submitted, Hash(1), "downloading", 0.2);
        // The pass's first read of the followed jobs fails, as a database that can't be read would.
        _factory.DbCommandHook.BeforeNextCommand(
            command => command.CommandText.Contains("FROM \"DownloadJobs\"", StringComparison.Ordinal),
            _ => throw new InvalidOperationException("The database is locked."));
        _factory.Logs.Clear();
        await _factory.PollDownloadJobsAsync(cancellationToken);
        var failed = await RowAsync(job.Id, cancellationToken);
        var logged = _factory.Logs.Entries.Where(entry => entry.Category == typeof(DownloadJobTracker).FullName).ToList();
        _factory.Logs.Clear();

        //Act
        await PassAsync(cancellationToken);

        //Assert
        // Logged with its exception, and the tracker carried on: the next pass followed the job.
        var error = Assert.Single(logged);
        Assert.Equal(
            (LogLevel.Error, "A pass over the followed download jobs threw; the next pass runs as usual."),
            (error.Level, error.Message));
        Assert.IsType<InvalidOperationException>(error.Exception);
        Assert.Equal(DownloadJobStatus.Submitted, failed.Status);
        Assert.Equal(DownloadJobStatus.Downloading, (await RowAsync(job.Id, cancellationToken)).Status);
    }

    [Fact]
    public async Task Poll_stopped_by_a_shutdown_is_not_logged_and_leaves_the_rows_for_the_next_pass()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var job = await PlantAsync(DownloadJobStatus.Submitted, Hash(1), "downloading", 0.2);
        using var shutdown = new CancellationTokenSource();
        // The app stops while qBittorrent is being asked.
        _qbittorrent.Intercept = request =>
        {
            if (request.RequestUri!.AbsolutePath != "/api/v2/torrents/info")
                return null;
            shutdown.Cancel();
            throw new OperationCanceledException(shutdown.Token);
        };
        _factory.Logs.Clear();

        //Act
        var stopped = await Record.ExceptionAsync(() => _factory.PollDownloadJobsAsync(shutdown.Token));

        //Assert
        // A shutdown is no error: nothing logged, nothing written; the next pass goes on from the rows.
        Assert.IsAssignableFrom<OperationCanceledException>(stopped);
        Assert.DoesNotContain(_factory.Logs.Entries, entry => entry.Level >= LogLevel.Warning);
        Assert.Equal(DownloadJobStatus.Submitted, (await RowAsync(job.Id, cancellationToken)).Status);
        _qbittorrent.Intercept = null;
        await PassAsync(cancellationToken);
        Assert.Equal(DownloadJobStatus.Downloading, (await RowAsync(job.Id, cancellationToken)).Status);
    }

    [Fact]
    public async Task Tracker_runs_a_pass_as_soon_as_it_starts_and_stops_without_an_error()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var job = await PlantAsync(DownloadJobStatus.Submitted, Hash(1), "downloading", 0.2);
        var tracker = _factory.Resolve<DownloadJobTracker>();
        _factory.Logs.Clear();

        //Act
        // The host's own start and stop, which the test host leaves to the test.
        await tracker.StartAsync(cancellationToken);
        var moved = await WaitForStatusAsync(job.Id, DownloadJobStatus.Downloading, cancellationToken);
        await tracker.StopAsync(cancellationToken);

        //Assert
        // The first pass right after the start, without waiting PollInterval; the stop ends the wait for the next
        // one, which is no error. On .NET 10 a stop can also win the race before the loop runs at all.
        Assert.True(moved);
        Assert.True(tracker.ExecuteTask is { Status: TaskStatus.RanToCompletion or TaskStatus.Canceled }, $"{tracker.ExecuteTask?.Status}");
        Assert.DoesNotContain(_factory.Logs.Entries, entry => entry.Level >= LogLevel.Warning);
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>Whether the job reaches <paramref name="status"/> within five seconds, read every 50 ms: for the
    /// one test that lets the tracker's own loop run.</summary>
    private async Task<bool> WaitForStatusAsync(Guid jobId, DownloadJobStatus status, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            if ((await RowAsync(jobId, cancellationToken)).Status == status)
                return true;
            await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken);
        }

        return false;
    }

    /// <summary>One pass of the app's tracker, which must not have thrown: a pass that threw is logged and
    /// would otherwise read as one that changed nothing.</summary>
    private async Task PassAsync(CancellationToken cancellationToken)
    {
        await _factory.PollDownloadJobsAsync(cancellationToken);
        Assert.DoesNotContain(_factory.Logs.Entries, entry => entry.Level >= LogLevel.Error);
    }

    /// <summary>Sends a release as the member to qBittorrent's Movies, the way the Download dialog does.</summary>
    private async Task<DownloadJobDto> SendAsync(CancellationToken cancellationToken)
    {
        var release = await _releases.CreateFound(_member.User, _toloka, ReleaseTitle);
        var response = await _factory.PostHttpRequest(BaseUrl, _member.AccessToken, new SendDownloadRequest(Guid.NewGuid(), release.Id, _client.Id, _movies.Id));
        if (response.StatusCode != HttpStatusCode.Created)
            throw new InvalidOperationException($"The send answered {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync(cancellationToken)}");
        return (await response.Content.ReadFromJsonAsync<DownloadJobDto>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
    }

    /// <summary>
    /// A job of the member's a previous pass left <paramref name="status"/>, with qBittorrent's id
    /// <paramref name="hash"/> (its v1 info hash), and the torrent in qBittorrent, in <paramref name="state"/> at
    /// <paramref name="progress"/> with <paramref name="files"/>.
    /// </summary>
    private async Task<DownloadJobEntity> PlantAsync(
        DownloadJobStatus status,
        string hash,
        string state,
        double progress,
        double? storedProgress = null,
        IReadOnlyList<FakeQBittorrentFile>? files = null)
    {
        var release = await _releases.CreateFound(_member.User, _toloka, ReleaseTitle);
        _qbittorrent.AddTorrent(
            FakeQBittorrentTorrent.Of(new TorrentInfoHashes(hash, null), TakenFolder, "Movies", state, progress) with { Files = files ?? [] });
        return await Jobs().Create(
            release.Id,
            _client.Id,
            _client.Name,
            _movies.Id,
            status,
            _member.User.Id,
            infoHashV1: hash,
            clientJobId: hash,
            progress: storedProgress);
    }

    private IDownloadJobDataFactory Jobs() => new DownloadJobDataFactory(_factory.Scope.GetRequiredService<ApplicationDbContext>(), _factory.Time);

    private async Task<DownloadJobEntity> RowAsync(Guid jobId, CancellationToken cancellationToken)
    {
        DownloadJobEntity? row = null;
        await _factory.WithDbAsync(async db => row = await db.DownloadJobs.AsNoTracking().SingleAsync(job => job.Id == jobId, cancellationToken));
        return row!;
    }

    /// <summary>The job as the member reads it over HTTP.</summary>
    private async Task<DownloadJobDto> ReadAsync(Guid jobId, CancellationToken cancellationToken)
    {
        var response = await _factory.GetHttpRequest($"{BaseUrl}/{jobId}", _member.AccessToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<DownloadJobDto>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
    }

    private async Task<(int Position, string Path, long? SizeBytes, double? Progress, bool? IsWanted)[]> StoredFilesAsync(
        Guid jobId, CancellationToken cancellationToken)
    {
        (int, string, long?, double?, bool?)[] files = [];
        await _factory.WithDbAsync(async db => files = [.. (await db.DownloadJobFiles
                .AsNoTracking()
                .Where(file => file.DownloadJobId == jobId)
                .OrderBy(file => file.Position)
                .ToListAsync(cancellationToken))
            .Select(file => (file.Position, file.RelativePath, file.SizeBytes, file.Progress, file.IsWanted))]);
        return files;
    }

    /// <summary>The trilogy's three films at <paramref name="progress"/>, the third skipped in qBittorrent (priority
    /// 0), as torrent-files-multi.json lists them: the root folder first, sizes past 4 GiB.</summary>
    private static FakeQBittorrentFile[] TakenFiles(double progress) =>
    [
        new($"{TakenFolder}/Taken (2008) BDRip 1080p.mkv", 9_659_032_478, progress),
        new($"{TakenFolder}/Taken 2 (2012) BDRip 1080p.mkv", 8_589_934_592, progress, Priority: 6),
        new($"{TakenFolder}/Taken 3 (2014) BDRip 1080p.mkv", 10_737_418_240, progress, Priority: 0),
    ];

    /// <summary>What <see cref="TakenFiles"/> are stored as.</summary>
    private static (int Position, string Path, long? SizeBytes, double? Progress, bool? IsWanted)[] TakenRows(double progress) =>
    [
        (0, $"{TakenFolder}/Taken (2008) BDRip 1080p.mkv", 9_659_032_478, progress, true),
        (1, $"{TakenFolder}/Taken 2 (2012) BDRip 1080p.mkv", 8_589_934_592, progress, true),
        (2, $"{TakenFolder}/Taken 3 (2014) BDRip 1080p.mkv", 10_737_418_240, progress, false),
    ];

    /// <summary>A v1 info hash of its own for each <paramref name="n"/>.</summary>
    private static string Hash(int n) => n.ToString("x40", System.Globalization.CultureInfo.InvariantCulture);

    private static bool IsWrite(System.Data.Common.DbCommand command) =>
        command.CommandText.StartsWith("UPDATE", StringComparison.Ordinal)
        || command.CommandText.StartsWith("INSERT", StringComparison.Ordinal)
        || command.CommandText.StartsWith("DELETE", StringComparison.Ordinal);

    /// <summary>Every request qBittorrent received, as method and path with its query, in order.</summary>
    private List<string> QBittorrentCalls() => [.. _factory.QBittorrentHttp.Requests.Select(request => $"{request.Method} {request.Uri.PathAndQuery}")];

    /// <summary>The job lookup for <paramref name="torrentIds"/>, joined by <c>|</c> and escaped as the adapter
    /// sends them.</summary>
    private static string Lookup(params string[] torrentIds) =>
        $"GET /api/v2/torrents/info?hashes={Uri.EscapeDataString(string.Join('|', torrentIds))}";

    private static string Files(string torrentId) => $"GET {FilesPath}?hash={torrentId}";
}
