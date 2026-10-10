using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Api.Services.Downloads;
using MediaAcquisitionManager.Api.Services.Replacements;
using MediaAcquisitionManager.Core.Downloads;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Paths;
using MediaAcquisitionManager.Infrastructure.Persistence;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Data;
using MediaAcquisitionManager.TestInfrastructure.Files;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Logging;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MediaAcquisitionManager.Api.Tests.Replacements;

// MAM-24, ADR 0014, R6 — a scheduled replacement follows its download on every pass of the download jobs, and the Jobs page
// reads where it stands: Submitted, Downloading, Paused, then Downloaded, where the following leaves it — MAM-25's check
// takes it up from there (ReplacementVerificationHttpTests), and the following never touches a job being checked or
// verified —, nothing sent on any pass. It stops when its download is cancelled from the Jobs page — with a ReplacementCancelled row, a review
// included —, fails or can't be told apart in qBittorrent. A state past Scheduled is mirrored only for a download the strict
// check let through: a send the poller adopted after the app stopped before the check fails its replacement. Everything is
// rows, so a pass after a restart goes on from them. One replacement that breaks never stops the others, nor the
// following of the downloads. A job carries what replacement says now: switched off, and copies no longer in a ticked
// folder.
public sealed class ReplacementFollowingHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/replacements";
    private const string DownloadsUrl = "/api/downloads";
    private const string TolokaAddress = "http://prowlarr.test:9696/1/api";
    private const string TolokaApiKey = "planted-torznab-key-0024";
    private const string JellyfinApiKey = "0123456789abcdef0123456789abcdef";
    private const string AlienImdbId = "tt0078748";
    private const string AlienRelease = "Alien (1979) Director's Cut BDRemux 2160p HDR";
    private const string FilmsFolder = "/media/movies";
    private const string TheatricalPath = "/media/movies/Alien (1979)/Alien (1979) Theatrical Cut.mkv";
    private const string PlantedHash = "1111111111111111111111111111111111111111";
    private const string OtherHash = "2222222222222222222222222222222222222222";
    private const string PlantedExceptionMessage = "planted-exception-message-0024";

    // Sentences the Jobs page shows, spelled out rather than read from product code.
    private const string YouCancelledReason = "You cancelled the download, so nothing will be replaced.";
    private const string DownloadCancelledReason = "The download was cancelled, so nothing will be replaced.";
    private const string DownloadFailedReason = "The download failed, so nothing will be replaced.";
    private const string RefusedBeforeSentReason = "The download was refused before it was sent, so nothing will be replaced.";
    private const string InterruptedReason =
        "The app stopped while sending this download, before it checked where the new files would land, so nothing will be replaced.";
    private const string NeedsLinkingReason =
        "qBittorrent's downloads couldn't be told apart for this release, so the app can't follow it to verify the new file.";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private IReleaseSeeder _releases = null!;
    private AuthorizedUser _owner = null!;
    private IntegrationConnectionEntity _toloka = null!;
    private IntegrationConnectionEntity _client = null!;
    private FakeQBittorrentServer _qbittorrent = null!;
    private DestinationMappingEntity _movies = null!;
    private Guid _theatrical;

    public ReplacementFollowingHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        var cancellationToken = TestContext.Current.CancellationToken;
        _owner = await new UserSeeder(_factory.Scope).CreateAuthorizedOwner("owner");
        var integrations = new IntegrationConnectionSeeder(_factory.Scope);
        var jellyfin = await integrations.Create("jellyfin", "Living room", apiKey: JellyfinApiKey);
        new FakeTorznabServer(TolokaAddress, TolokaApiKey)
        {
            TorrentFile = TestTorrent.V1("Alien.1979.Directors.Cut.2160p.mkv", TestTorrent.Bytes((3 * TestTorrent.PieceLength) + 5, seed: 1)),
        }.Arm(_factory.TorznabHttp);
        _toloka = await integrations.Create("torznab", "Toloka", TolokaAddress, apiKey: TolokaApiKey);
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
        _releases = new ReleaseSeeder(_factory.Scope);
        var films = await new LibraryLocationSeeder(_factory.Scope).Create(jellyfin, "Фільми", FilmsFolder);
        await new AllowedRootSeeder(_factory.Scope).Allow("/media");

        // Alien's theatrical cut, inspected, in the ticked Фільми.
        var works = new MediaWorkSeeder(_factory.Scope);
        var alien = await works.CreateMovie(jellyfin, "Alien", 1979, imdbId: AlienImdbId);
        await works.AddVersion(jellyfin, alien, Guid.NewGuid().ToString("N"), films, "Theatrical Cut", TheatricalPath);
        _factory.Files.AddFile(TheatricalPath, TestMedia.File(inode: 1), TestMedia.Taken());
        await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);
        await new ReplacementSettingsSeeder(_factory.Scope).SwitchOn(FilmsFolder);
        await _factory.WithDbAsync(async db => _theatrical = await db.PhysicalFiles.Where(file => file.ServicePath == TheatricalPath).Select(file => file.Id).SingleAsync(cancellationToken));
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    // ── mirrored ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Pass_follows_the_download_to_downloaded_and_leaves_the_replacement_waiting_there_with_nothing_sent()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var job = await ScheduleAsync(cancellationToken);
        var sentBefore = _factory.QBittorrentHttp.Requests.Count;
        var seen = new List<(ReplacementJobState State, DateTime ChangedUtc)>();
        _factory.Logs.Clear();

        //Act
        foreach (var (state, progress) in new[] { ("downloading", 0.4), ("stoppedDL", 0.6), ("stalledUP", 1.0), ("stalledUP", 1.0) })
        {
            _factory.Time.Advance(TimeSpan.FromMinutes(1));
            _qbittorrent.UpdateTorrent(job.ClientJobId!, torrent => torrent with { State = state, Progress = progress });
            await PassAsync(cancellationToken);
            var replacement = await ReplacementAsync(cancellationToken);
            seen.Add((replacement.State, replacement.StateChangedUtc));
        }

        //Assert
        Assert.Equal(DownloadJobStatus.Submitted, job.Status);
        Assert.Equal(
            [ReplacementJobState.Downloading, ReplacementJobState.Paused, ReplacementJobState.Downloaded, ReplacementJobState.Downloaded],
            seen.Select(step => step.State));
        // A pass that finds it where it is writes nothing: Downloaded keeps the time it got there.
        Assert.Equal(seen[2].ChangedUtc, seen[3].ChangedUtc);
        Assert.True(seen[1].ChangedUtc > seen[0].ChangedUtc);
        Assert.Null((await ReplacementAsync(cancellationToken)).Reason);
        // ADR 0014 — following only reads: nothing sent to qBittorrent on any pass.
        Assert.All(_factory.QBittorrentHttp.Requests.Skip(sentBefore), request => Assert.Equal(HttpMethod.Get, request.Method));
        var replacementId = (await ReplacementAsync(cancellationToken)).Id;
        Assert.Contains(
            _factory.Logs.Entries,
            entry => entry.Category == typeof(ReplacementJobFollower).FullName
                && entry.Level == LogLevel.Information
                && entry.Message == $"Replacement job {replacementId} went from Downloading to Paused.");
        // And the Jobs page reads it.
        var listed = await ListAsync(cancellationToken);
        Assert.Equal(new DownloadJobReplacementDto(ReplacementJobState.Downloaded, 1, null, false, 0), Assert.Single(listed.Items).Replacement);
    }

    [Fact]
    public async Task Pass_after_a_restart_goes_on_from_the_rows_a_previous_run_left()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // The app stopped after the send was written Submitted, before its replacement followed.
        _qbittorrent.AddTorrent(TorrentOf(PlantedHash, "downloading", 0.3));
        var (download, replacement) = await PlantAsync(DownloadJobStatus.Submitted, DownloadOverwriteCheck.Checked, ReplacementJobState.Scheduled);

        //Act
        await PassAsync(cancellationToken);

        //Assert
        Assert.Equal(DownloadJobStatus.Downloading, (await DownloadAsync(download.Id, cancellationToken)).Status);
        Assert.Equal(ReplacementJobState.Downloading, (await ReplacementAsync(cancellationToken, replacement.Id)).State);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(DownloadOverwriteCheck.Checked)]
    public async Task Pass_leaves_a_replacement_scheduled_while_its_download_is_still_being_sent(DownloadOverwriteCheck? check)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // A send under way: younger than an interrupted one, checked or not yet.
        var (_, replacement) = await PlantAsync(DownloadJobStatus.Created, check, ReplacementJobState.Scheduled, clientJobId: null);

        //Act
        await PassAsync(cancellationToken);

        //Assert
        var row = await ReplacementAsync(cancellationToken, replacement.Id);
        Assert.Equal((ReplacementJobState.Scheduled, (string?)null, replacement.StateChangedUtc), (row.State, row.Reason, row.StateChangedUtc));
    }

    [Theory]
    [InlineData(ReplacementJobState.Inspecting)]
    [InlineData(ReplacementJobState.Verified)]
    public async Task Pass_never_touches_a_replacement_whose_new_files_are_being_checked_or_verified(ReplacementJobState state)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // MAM-25 — as a previous run left them: the download done, its replacement taken up by the check of its new files.
        // Were it followed, a done download would mirror it back to Downloaded on every pass.
        var (_, replacement) = await PlantAsync(DownloadJobStatus.Downloaded, DownloadOverwriteCheck.Checked, state, progress: 1);
        _factory.Time.Advance(TimeSpan.FromMinutes(1));

        //Act
        await PassAsync(cancellationToken);

        //Assert
        var row = await ReplacementAsync(cancellationToken, replacement.Id);
        Assert.Equal((state, (string?)null, replacement.StateChangedUtc), (row.State, row.Reason, row.StateChangedUtc));
        await _factory.WithDbAsync(async db => Assert.Equal(0, await db.AuditEvents.CountAsync(cancellationToken)));
    }

    // ── stopped ─────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Cancel_of_the_download_cancels_its_replacement_with_a_row_of_its_own()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var job = await ScheduleAsync(cancellationToken);
        var sentBefore = _factory.QBittorrentHttp.Requests.Count;

        //Act
        var response = await _factory.PostHttpRequest($"{DownloadsUrl}/{job.Id}/cancel", _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var cancelled = (await response.Content.ReadFromJsonAsync<DownloadJobDto>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
        Assert.Equal(DownloadJobStatus.Cancelled, cancelled.Status);
        Assert.Equal(new DownloadJobReplacementDto(ReplacementJobState.Cancelled, 1, YouCancelledReason, false, 0), cancelled.Replacement);
        var replacement = await ReplacementAsync(cancellationToken);
        await _factory.WithDbAsync(async db =>
        {
            var audit = await db.AuditEvents.AsNoTracking().SingleAsync(row => row.Action == AuditAction.ReplacementCancelled, cancellationToken);
            Assert.Equal(
                ((Guid?)_owner.User.Id, (AuditTargetKind?)AuditTargetKind.ReplacementJob, (Guid?)replacement.Id, (string?)AlienRelease),
                (audit.ActorUserId, audit.TargetKind, audit.TargetId, audit.TargetName));
            Assert.Equal(1, await db.AuditEvents.CountAsync(row => row.Action == AuditAction.DownloadCancelled, cancellationToken));
        });
        // ADR 0014 — a cancel only stops following: nothing sent to qBittorrent.
        Assert.Equal(sentBefore, _factory.QBittorrentHttp.Requests.Count);
    }

    [Fact]
    public async Task Pass_that_finds_two_downloads_for_the_hash_needs_review_and_a_cancel_then_ends_it()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var job = await ScheduleAsync(cancellationToken);
        // qBittorrent now lists two torrents for the job's hash: which is the new file can't be told.
        _qbittorrent.Intercept = request =>
            request.RequestUri!.AbsolutePath == "/api/v2/torrents/info" && request.RequestUri.Query.Contains("hashes=", StringComparison.Ordinal)
                ? Listing((job.ClientJobId!, job.ClientJobId!), (OtherHash, job.ClientJobId!))
                : null;
        await _factory.PollDownloadJobsAsync(cancellationToken);
        // Captured, asserted below: a pass that threw is logged and would otherwise read as one that changed nothing.
        var errorsAfterPass = _factory.Logs.Entries.Where(entry => entry.Level >= LogLevel.Error).ToList();
        var review = await ReplacementAsync(cancellationToken);

        //Act
        var response = await _factory.PostHttpRequest($"{DownloadsUrl}/{job.Id}/cancel", _owner.AccessToken);

        //Assert
        Assert.Empty(errorsAfterPass);
        Assert.Equal((ReplacementJobState.NeedsReview, (string?)NeedsLinkingReason), (review.State, review.Reason));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var cancelled = (await response.Content.ReadFromJsonAsync<DownloadJobDto>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
        Assert.Equal((ReplacementJobState.Cancelled, YouCancelledReason), (cancelled.Replacement!.State, cancelled.Replacement.Reason));
        await _factory.WithDbAsync(async db =>
            Assert.Equal(1, await db.AuditEvents.CountAsync(row => row.Action == AuditAction.ReplacementCancelled, cancellationToken)));
    }

    [Fact]
    public async Task Pass_ends_a_replacement_waiting_for_review_whose_download_was_cancelled_with_no_audit_row()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // As a previous run left them: the download cancelled, its replacement not followed since.
        var (_, replacement) = await PlantAsync(DownloadJobStatus.Cancelled, DownloadOverwriteCheck.Checked, ReplacementJobState.NeedsReview, reason: NeedsLinkingReason);

        //Act
        await PassAsync(cancellationToken);

        //Assert
        var row = await ReplacementAsync(cancellationToken, replacement.Id);
        Assert.Equal((ReplacementJobState.Cancelled, (string?)DownloadCancelledReason), (row.State, row.Reason));
        await _factory.WithDbAsync(async db => Assert.Equal(0, await db.AuditEvents.CountAsync(cancellationToken)));
    }

    [Fact]
    public async Task Pass_over_a_download_that_failed_after_the_check_fails_its_replacement_as_a_failed_download()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var (_, replacement) = await PlantAsync(DownloadJobStatus.Failed, DownloadOverwriteCheck.Checked, ReplacementJobState.Scheduled);

        //Act
        await PassAsync(cancellationToken);

        //Assert
        var row = await ReplacementAsync(cancellationToken, replacement.Id);
        Assert.Equal((ReplacementJobState.Failed, (string?)DownloadFailedReason), (row.State, row.Reason));
    }

    [Fact]
    public async Task Pass_that_adopts_an_interrupted_send_fails_its_replacement_as_never_checked()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // The app stopped after the hashes were recorded and before the check: qBittorrent has the torrent, finished — it
        // may be the very torrent that holds the old copy.
        _qbittorrent.AddTorrent(TorrentOf(PlantedHash, "stalledUP", 1));
        var (download, replacement) = await PlantAsync(DownloadJobStatus.Created, check: null, ReplacementJobState.Scheduled, clientJobId: null);
        _factory.Time.Advance(DownloadJobPoller.InterruptedSendAge + TimeSpan.FromMinutes(1));

        //Act
        await PassAsync(cancellationToken);

        //Assert
        // The poller adopts the download as it always has; the replacement won't follow a download nothing checked.
        Assert.Equal(DownloadJobStatus.Downloaded, (await DownloadAsync(download.Id, cancellationToken)).Status);
        var row = await ReplacementAsync(cancellationToken, replacement.Id);
        Assert.Equal((ReplacementJobState.Failed, (string?)InterruptedReason), (row.State, row.Reason));
    }

    [Fact]
    public async Task Pass_over_an_interrupted_send_qbittorrent_never_listed_fails_its_replacement()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var (download, replacement) = await PlantAsync(DownloadJobStatus.Created, check: null, ReplacementJobState.Scheduled, clientJobId: null);
        _factory.Time.Advance(DownloadJobPoller.InterruptedSendAge + TimeSpan.FromMinutes(1));

        //Act
        await PassAsync(cancellationToken);

        //Assert
        Assert.Equal(DownloadJobStatus.Failed, (await DownloadAsync(download.Id, cancellationToken)).Status);
        var row = await ReplacementAsync(cancellationToken, replacement.Id);
        Assert.Equal((ReplacementJobState.Failed, (string?)RefusedBeforeSentReason), (row.State, row.Reason));
    }

    // ── what breaks ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Pass_whose_following_of_one_replacement_throws_follows_the_others_and_logs_the_type_never_the_message()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        _qbittorrent.AddTorrent(TorrentOf(PlantedHash, "downloading", 0.3));
        _qbittorrent.AddTorrent(TorrentOf(OtherHash, "downloading", 0.3));
        var (_, first) = await PlantAsync(DownloadJobStatus.Downloading, DownloadOverwriteCheck.Checked, ReplacementJobState.Scheduled, progress: 0.3);
        // Oldest first: the second is the later.
        _factory.Time.Advance(TimeSpan.FromSeconds(1));
        var (_, second) = await PlantAsync(
            DownloadJobStatus.Downloading, DownloadOverwriteCheck.Checked, ReplacementJobState.Scheduled, hash: OtherHash, progress: 0.3);
        _factory.DbCommandHook.BeforeNextCommand(
            command => command.CommandText.StartsWith("UPDATE \"ReplacementJobs\"", StringComparison.Ordinal),
            _ => throw new InvalidOperationException(PlantedExceptionMessage));
        _factory.Logs.Clear();

        //Act
        await _factory.PollDownloadJobsAsync(cancellationToken);

        //Assert
        Assert.Equal(ReplacementJobState.Scheduled, (await ReplacementAsync(cancellationToken, first.Id)).State);
        Assert.Equal(ReplacementJobState.Downloading, (await ReplacementAsync(cancellationToken, second.Id)).State);
        // The app's own line — beside EF Core's of the failed command.
        var error = Assert.Single(_factory.Logs.Entries, IsTheAppsError);
        Assert.Equal(typeof(ReplacementJobFollower).FullName, error.Category);
        Assert.StartsWith($"Following replacement job {first.Id} threw System.InvalidOperationException", error.Message, StringComparison.Ordinal);
        Assert.Null(error.Exception);
        // EF Core's own lines quote the exception; the app's never do.
        Assert.DoesNotContain(
            _factory.Logs.Entries,
            entry => entry.Category.StartsWith("MediaAcquisitionManager.", StringComparison.Ordinal)
                && entry.Message.Contains(PlantedExceptionMessage, StringComparison.Ordinal));
        // The next pass follows it.
        _factory.Logs.Clear();
        await PassAsync(cancellationToken);
        Assert.Equal(ReplacementJobState.Downloading, (await ReplacementAsync(cancellationToken, first.Id)).State);
    }

    [Fact]
    public async Task Pass_whose_following_of_the_replacements_throws_still_followed_the_downloads()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        _qbittorrent.AddTorrent(TorrentOf(PlantedHash, "downloading", 0.3));
        var (download, replacement) = await PlantAsync(DownloadJobStatus.Submitted, DownloadOverwriteCheck.Checked, ReplacementJobState.Submitted);
        // The replacements' read fails, as a database that can't be read would.
        _factory.DbCommandHook.BeforeNextCommand(
            command => command.CommandText.Contains("FROM \"ReplacementJobs\"", StringComparison.Ordinal),
            _ => throw new InvalidOperationException(PlantedExceptionMessage));
        _factory.Logs.Clear();

        //Act
        await _factory.PollDownloadJobsAsync(cancellationToken);

        //Assert
        // The download was followed first, in a try of its own; the replacement waits for the next pass.
        Assert.Equal(DownloadJobStatus.Downloading, (await DownloadAsync(download.Id, cancellationToken)).Status);
        Assert.Equal(ReplacementJobState.Submitted, (await ReplacementAsync(cancellationToken, replacement.Id)).State);
        var error = Assert.Single(_factory.Logs.Entries, IsTheAppsError);
        Assert.Equal(typeof(DownloadJobTracker).FullName, error.Category);
        Assert.StartsWith("A pass over the scheduled replacements threw System.InvalidOperationException", error.Message, StringComparison.Ordinal);
        Assert.EndsWith("; the next pass runs as usual.", error.Message, StringComparison.Ordinal);
        // EF Core's own lines quote the exception; the app's never do.
        Assert.DoesNotContain(
            _factory.Logs.Entries,
            entry => entry.Category.StartsWith("MediaAcquisitionManager.", StringComparison.Ordinal)
                && entry.Message.Contains(PlantedExceptionMessage, StringComparison.Ordinal));
        _factory.Logs.Clear();
        await PassAsync(cancellationToken);
        Assert.Equal(ReplacementJobState.Downloading, (await ReplacementAsync(cancellationToken, replacement.Id)).State);
    }

    // ── the job's line ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Get_of_a_job_says_when_replacement_is_off_and_its_copies_are_no_longer_in_a_ticked_folder()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var job = await ScheduleAsync(cancellationToken);
        // The owner switched replacement off and unticked the folder since.
        await _factory.WithDbAsync(async db =>
        {
            await db.ReplacementSettings.ExecuteUpdateAsync(setters => setters.SetProperty(settings => settings.IsEnabled, false), cancellationToken);
            await db.ReplacementFolders.ExecuteDeleteAsync(cancellationToken);
        });

        //Act
        var response = await _factory.GetHttpRequest($"{DownloadsUrl}/{job.Id}", _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var read = (await response.Content.ReadFromJsonAsync<DownloadJobDto>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
        var expected = new DownloadJobReplacementDto(ReplacementJobState.Submitted, 1, null, SwitchedOff: true, CopiesOutsideTickedFolders: 1);
        Assert.Equal(expected, read.Replacement);
        Assert.Equal(expected, Assert.Single((await ListAsync(cancellationToken)).Items).Replacement);
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>The owner schedules the replacement of Alien's theatrical cut by a release of it: sent, Submitted.</summary>
    private async Task<DownloadJobDto> ScheduleAsync(CancellationToken cancellationToken)
    {
        var release = await _releases.CreateFoundBySearch(_owner.User, _toloka, "Alien", 1979, AlienRelease, imdbId: AlienImdbId);
        using var response = await _factory.PostHttpRequest(
            BaseUrl, _owner.AccessToken, new ScheduleReplacementRequest(Guid.NewGuid(), release.Id, _client.Id, _movies.Id, [_theatrical]));
        if (response.StatusCode != HttpStatusCode.Created)
            throw new InvalidOperationException($"The schedule answered {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync(cancellationToken)}");
        return (await response.Content.ReadFromJsonAsync<DownloadJobDto>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
    }

    /// <summary>A download job and its replacement as a previous run left them, of a release of Alien sent with the hash
    /// <paramref name="hash"/>, its one copy the theatrical cut.</summary>
    private async Task<(DownloadJobEntity Download, ReplacementJobEntity Replacement)> PlantAsync(
        DownloadJobStatus status,
        DownloadOverwriteCheck? check,
        ReplacementJobState state,
        string? clientJobId = PlantedHash,
        string? reason = null,
        string hash = PlantedHash,
        double? progress = null)
    {
        var release = await _releases.CreateFoundBySearch(_owner.User, _toloka, "Alien", 1979, AlienRelease, imdbId: AlienImdbId);
        var db = _factory.Scope.GetRequiredService<ApplicationDbContext>();
        var download = await new DownloadJobDataFactory(db, _factory.Time).Create(
            release.Id,
            _client.Id,
            _client.Name,
            _movies.Id,
            status,
            _owner.User.Id,
            infoHashV1: hash,
            clientJobId: clientJobId is null ? null : hash,
            progress: progress,
            overwriteCheck: check);
        var replacements = new ReplacementJobDataFactory(db, _factory.Time);
        var replacement = await replacements.Create(download.Id, _owner.User.Id, state, reason);
        await replacements.AddFile(replacement.Id, TheatricalPath, TestMedia.File(inode: 1), physicalFileId: _theatrical);
        return (download, replacement);
    }

    /// <summary>An error the app itself logged, not EF Core's line of the command a test made fail.</summary>
    private static bool IsTheAppsError(RecordedLogEntry entry) =>
        entry.Level >= LogLevel.Error && entry.Category.StartsWith("MediaAcquisitionManager.", StringComparison.Ordinal);

    private static FakeQBittorrentTorrent TorrentOf(string hash, string state, double progress) =>
        FakeQBittorrentTorrent.Of(new TorrentInfoHashes(hash, null), "Alien", "Movies", state, progress, savePath: "/downloads/movies");

    /// <summary>One pass of the app's tracker — the download jobs, then the replacements —, which must not have
    /// thrown: a pass that threw is logged and would otherwise read as one that changed nothing.</summary>
    private async Task PassAsync(CancellationToken cancellationToken)
    {
        await _factory.PollDownloadJobsAsync(cancellationToken);
        Assert.DoesNotContain(_factory.Logs.Entries, entry => entry.Level >= LogLevel.Error);
    }

    /// <summary>The replacement <paramref name="replacementId"/>, or the one there is.</summary>
    private async Task<ReplacementJobEntity> ReplacementAsync(CancellationToken cancellationToken, Guid? replacementId = null)
    {
        ReplacementJobEntity? row = null;
        await _factory.WithDbAsync(async db => row = replacementId is { } id
            ? await db.ReplacementJobs.AsNoTracking().SingleAsync(job => job.Id == id, cancellationToken)
            : await db.ReplacementJobs.AsNoTracking().SingleAsync(cancellationToken));
        return row!;
    }

    private async Task<DownloadJobEntity> DownloadAsync(Guid jobId, CancellationToken cancellationToken)
    {
        DownloadJobEntity? row = null;
        await _factory.WithDbAsync(async db => row = await db.DownloadJobs.AsNoTracking().SingleAsync(job => job.Id == jobId, cancellationToken));
        return row!;
    }

    private async Task<DownloadJobListResponse> ListAsync(CancellationToken cancellationToken)
    {
        using var response = await _factory.GetHttpRequest(DownloadsUrl, _owner.AccessToken);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<DownloadJobListResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
    }

    /// <summary>What <c>torrents/info</c> answers listing <paramref name="torrents"/>, each in Movies.</summary>
    private static HttpResponseMessage Listing(params (string Hash, string InfoHashV1)[] torrents) =>
        VendorResponses.Text(
            new JsonArray([
                .. torrents.Select(torrent => (JsonNode)new JsonObject
                {
                    ["category"] = "Movies",
                    ["content_path"] = "/downloads/movies/Alien",
                    ["hash"] = torrent.Hash,
                    ["infohash_v1"] = torrent.InfoHashV1,
                    ["infohash_v2"] = "",
                    ["name"] = "Alien",
                    ["progress"] = 0.2,
                    ["save_path"] = "/downloads/movies",
                    ["state"] = "downloading",
                }),
            ]).ToJsonString(),
            HttpStatusCode.OK,
            "application/json");
}
