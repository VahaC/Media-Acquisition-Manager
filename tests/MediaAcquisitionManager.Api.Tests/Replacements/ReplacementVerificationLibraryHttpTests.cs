using System.Net;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Api.Services.Replacements;
using MediaAcquisitionManager.Core.Abstractions.Library;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Replacements;
using MediaAcquisitionManager.TestInfrastructure.Files;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Replacements;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Tests.Replacements;

// MAM-25, ADR 0035, ADR 0030 — check 4 of a replacement's new file and the waits (Vasyl, 2026-10-10): the library is asked
// to look at the new path — again on each pass while it won't take the request — and then read on every pass, from the
// rows, for an hour from the request it took, however late. A file Jellyfin doesn't list, hasn't finished reading or gave
// no id yet keeps the job Inspecting — the owner may fix the item in Jellyfin meanwhile, and the check passes by itself —,
// and stops it for review once the hour ran out with what is still missing; so does a Jellyfin unavailable, switched off or
// failing its reads of an item, which never lets a file pass, and a Jellyfin that won't take the request to look, an hour
// from the start of the check. A library of a type that can't be asked to look, a library removed, and a new file that
// changed or went after its check, stop the job at once. A verified job is never read again.
public sealed class ReplacementVerificationLibraryHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string AlienImdbId = "tt0078748";
    private const string AlienRelease = "Alien (1979) Director's Cut BDRemux 2160p HDR";
    private const string TheatricalPath = "/media/movies/Alien (1979)/Alien (1979) Theatrical Cut.mkv";
    private const string NewName = "Alien.1979.Directors.Cut.2160p.mkv";
    private const string NewPath = "/media/movies/Alien.1979.Directors.Cut.2160p.mkv";
    // The new file as Jellyfin knows it, in the folder it reported: what it is asked and read by.
    private const string NewLibraryPath = "/data/movies/Alien.1979.Directors.Cut.2160p.mkv";
    // A folder of a library whose type this build can't ask to look at a file.
    private const string HomeVideosFolder = "/media/home-videos";
    private const string HomeVideoPath = "/media/home-videos/Alien.1979.Directors.Cut.2160p.mkv";
    private const int NewSize = (3 * TestTorrent.PieceLength) + 5;

    // Sentences the Jobs page shows, spelled out rather than read from product code.
    private const string JellyfinUnavailableReason =
        "The app couldn't read “Living room” within an hour of asking it to look at the new files: Jellyfin answered 503 Service Unavailable. Its own log says why.";
    private const string NoIdReason =
        "“Living room” gave “/media/movies/Alien.1979.Directors.Cut.2160p.mkv” no TMDb or IMDb id within an hour, so it can't be told to be Alien (1979).";
    private const string NotListedReason =
        "“Living room” didn't list “/media/movies/Alien.1979.Directors.Cut.2160p.mkv” within an hour of being asked to look.";
    private const string NotRefreshedReason =
        "“Living room” hadn't finished reading “/media/movies/Alien.1979.Directors.Cut.2160p.mkv” within an hour.";
    private const string NoCapabilityReason =
        "This version of the app can't ask “fake” to look at a file, so the new files can't be confirmed.";
    private const string ChangedReason = "The new file “/media/movies/Alien.1979.Directors.Cut.2160p.mkv” changed after it was checked.";
    private const string NotAskedReason =
        "The app couldn't ask “Living room” to look at the new files within an hour: Jellyfin answered 500 Internal Server Error. Its own log says why.";
    private const string GoneReason = "Nothing is at “/media/movies/Alien.1979.Directors.Cut.2160p.mkv”.";
    private const string SwitchedOffReason = "“Living room” is switched off, so it can't confirm the new files.";
    private const string ItemsUnreadReason =
        "The app couldn't read “Living room” within an hour of asking it to look at the new files: Jellyfin answered 500 Internal Server Error. Its own log says why.";
    private const string RemovedReason = "The library that holds the new files was removed from Integrations, so they can't be confirmed.";
    private const string IntegrationsUrl = "/api/settings/integrations";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private ReplacementVerificationHarness _harness = null!;
    private MediaWorkEntity _alien = null!;

    public ReplacementVerificationLibraryHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _harness = await ReplacementVerificationHarness.CreateAsync(_factory);
        // Alien's theatrical cut, inspected, in the ticked Фільми.
        _alien = await _harness.PlantFilmAsync("Alien", 1979, TheatricalPath, inode: 1, imdbId: AlienImdbId, editionLabel: "Theatrical Cut");
        await _harness.InspectAsync(TestContext.Current.CancellationToken);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    // ── the hour ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Verify_while_jellyfin_is_unavailable_keeps_checking_until_the_hour_runs_out_then_stops_for_review_never_verified()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var job = await DownloadedAsync(cancellationToken);
        _factory.Files.AddFile(NewPath, TestMedia.File(inode: 7, sizeBytes: NewSize), TestMedia.Taken());
        // Jellyfin takes the request to look, then answers 503 to everything: its film item, with Alien's id, is never read.
        _harness.JellyfinServer.OnScanRequested = paths => _harness.JellyfinServer.PlantFilm(paths[0], "Alien", 1979, imdbId: AlienImdbId);
        await _harness.VerifyAsync(cancellationToken);
        _factory.JellyfinHttp.Handler = null;

        //Act
        _factory.Time.Advance(ReplacementVerificationRules.LibraryWait - TimeSpan.FromSeconds(1));
        await _harness.VerifyAsync(cancellationToken);
        var beforeTheHour = await _harness.ReplacementAsync(cancellationToken);
        _factory.Time.Advance(TimeSpan.FromSeconds(1));
        await _harness.VerifyAsync(cancellationToken);
        var atTheHour = await _harness.ReplacementAsync(cancellationToken);
        // Jellyfin answers again: a job stopped for review is never read again.
        _harness.JellyfinServer.Arm(_factory.JellyfinHttp);
        _factory.Time.Advance(ReplacementVerificationTracker.PassInterval);
        await _harness.VerifyAsync(cancellationToken);

        //Assert
        Assert.Equal((ReplacementJobState.Inspecting, (string?)null), (beforeTheHour.State, beforeTheHour.Reason));
        Assert.Equal((ReplacementJobState.NeedsReview, (string?)JellyfinUnavailableReason), (atTheHour.State, atTheHour.Reason));
        await AssertNeedsReviewAsync(job, JellyfinUnavailableReason, cancellationToken);
        // Jellyfin was never read, so nothing of it is kept; the files' evidence stays as checked.
        var newFile = Assert.Single(await _harness.NewFilesAsync(cancellationToken));
        Assert.Equal(((string?)null, (DateTime?)null, (long?)7, (string?)null), (newFile.LibraryItemId, newFile.ReadUtc, newFile.Inode, newFile.Problem));
        Assert.Single(_harness.JellyfinServer.ScanRequests);
    }

    [Theory]
    // The owner switched Jellyfin off under Integrations once it took the request to look: it may be switched on again.
    [InlineData(true, SwitchedOffReason)]
    // Jellyfin answers its version check, and fails every read of an item.
    [InlineData(false, ItemsUnreadReason)]
    public async Task Verify_while_jellyfin_cant_be_read_after_taking_the_request_keeps_checking_until_the_hour_runs_out_then_stops_for_review(
        bool switchedOff, string reason)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var job = await DownloadedAsync(cancellationToken);
        _factory.Files.AddFile(NewPath, TestMedia.File(inode: 7, sizeBytes: NewSize), TestMedia.Taken());
        // Its item, with Alien's id, would verify the file, were it read.
        _harness.JellyfinServer.OnScanRequested = paths => _harness.JellyfinServer.PlantFilm(paths[0], "Alien", 1979, imdbId: AlienImdbId);
        await _harness.VerifyAsync(cancellationToken);
        if (switchedOff)
        {
            await _factory.WithDbAsync(db => db.IntegrationConnections
                .Where(integration => integration.Id == _harness.Jellyfin.Id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(integration => integration.IsEnabled, false), cancellationToken));
        }
        else
        {
            _harness.JellyfinServer.Intercept = request =>
                request.RequestUri!.AbsolutePath.StartsWith("/Items/", StringComparison.Ordinal)
                    ? new HttpResponseMessage(HttpStatusCode.InternalServerError)
                    : null;
        }

        //Act
        _factory.Time.Advance(ReplacementVerificationRules.LibraryWait - TimeSpan.FromSeconds(1));
        await _harness.VerifyAsync(cancellationToken);
        var beforeTheHour = await _harness.ReplacementAsync(cancellationToken);
        _factory.Time.Advance(TimeSpan.FromSeconds(1));
        await _harness.VerifyAsync(cancellationToken);

        //Assert
        // An hour from the request to look, never a pass on an unread library.
        Assert.Equal((ReplacementJobState.Inspecting, (string?)null), (beforeTheHour.State, beforeTheHour.Reason));
        await AssertNeedsReviewAsync(job, reason, cancellationToken);
        var newFile = Assert.Single(await _harness.NewFilesAsync(cancellationToken));
        Assert.Equal(((string?)null, (DateTime?)null, (Guid?)null, (string?)null), (newFile.LibraryItemId, newFile.ReadUtc, newFile.MediaWorkId, newFile.Problem));
    }

    [Fact]
    public async Task Verify_of_a_new_file_jellyfin_gives_no_id_keeps_checking_until_the_hour_runs_out_then_stops_for_review()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var job = await DownloadedAsync(cancellationToken);
        _factory.Files.AddFile(NewPath, TestMedia.File(inode: 7, sizeBytes: NewSize), TestMedia.Taken());
        // Read and refreshed, but no provider found the film: no TMDb or IMDb id to tell it by.
        _harness.JellyfinServer.OnScanRequested = paths => _harness.JellyfinServer.PlantFilm(paths[0], "Alien.1979.Directors.Cut.2160p", 1979);
        await _harness.VerifyAsync(cancellationToken);

        //Act
        _factory.Time.Advance(ReplacementVerificationRules.LibraryWait - TimeSpan.FromSeconds(1));
        await _harness.VerifyAsync(cancellationToken);
        var beforeTheHour = await _harness.ReplacementAsync(cancellationToken);
        var pendingProblem = Assert.Single(await _harness.NewFilesAsync(cancellationToken)).Problem;
        _factory.Time.Advance(TimeSpan.FromSeconds(1));
        await _harness.VerifyAsync(cancellationToken);

        //Assert
        // Waiting, the file holds no problem yet: it may still be given an id.
        Assert.Equal((ReplacementJobState.Inspecting, (string?)null, (string?)null), (beforeTheHour.State, beforeTheHour.Reason, pendingProblem));
        await AssertNeedsReviewAsync(job, NoIdReason, cancellationToken);
        var newFile = Assert.Single(await _harness.NewFilesAsync(cancellationToken));
        Assert.Equal(
            ((LibraryFileKind?)LibraryFileKind.Movie, true, (Guid?)null, (string?)NoIdReason),
            (newFile.Kind, newFile.Refreshed, newFile.MediaWorkId, newFile.Problem));
        Assert.Empty(await _harness.NewFileIdentifiersAsync(cancellationToken));
        // The copy waits on the file: no "no new file" while it was still unidentified.
        Assert.Equal(((Guid?)null, (string?)null), CopyOutcome(Assert.Single(await _harness.CopiesAsync(cancellationToken))));
    }

    [Fact]
    public async Task Verify_of_a_new_file_the_owner_identifies_in_jellyfin_within_the_hour_verifies_it()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var job = await DownloadedAsync(cancellationToken);
        _factory.Files.AddFile(NewPath, TestMedia.File(inode: 7, sizeBytes: NewSize), TestMedia.Taken());
        _harness.JellyfinServer.OnScanRequested = paths => _harness.JellyfinServer.PlantFilm(paths[0], "Alien.1979.Directors.Cut.2160p", 1979);
        await _harness.VerifyAsync(cancellationToken);
        _factory.Time.Advance(TimeSpan.FromMinutes(30));
        await _harness.VerifyAsync(cancellationToken);
        var waiting = await _harness.ReplacementAsync(cancellationToken);

        //Act
        // Half an hour on, the owner identifies the item as Alien in Jellyfin himself.
        _harness.JellyfinServer.PlantFilm(NewLibraryPath, "Alien", 1979, imdbId: AlienImdbId);
        _factory.Time.Advance(ReplacementVerificationTracker.PassInterval);
        await _harness.VerifyAsync(cancellationToken);

        //Assert
        Assert.Equal(ReplacementJobState.Inspecting, waiting.State);
        Assert.Equal(new DownloadJobReplacementDto(ReplacementJobState.Verified, 1, null, false, 0) { Id = (await _harness.ReplacementAsync(cancellationToken)).Id, CanCancel = true }, await _harness.LineAsync(job.Id, cancellationToken));
        var newFile = Assert.Single(await _harness.NewFilesAsync(cancellationToken));
        Assert.Equal(((Guid?)_alien.Id, (string?)null), (newFile.MediaWorkId, newFile.Problem));
        Assert.Equal([(0, false, MediaIdentifierScheme.Imdb, AlienImdbId)], await _harness.NewFileIdentifiersAsync(cancellationToken));
        Assert.Equal((Guid?)newFile.Id, Assert.Single(await _harness.CopiesAsync(cancellationToken)).PairedNewFileId);
        // Asked once: every pass after read Jellyfin again.
        Assert.Equal(["POST /Library/Media/Updated"], _harness.Writes());
    }

    [Theory]
    // Jellyfin took the request, and made no item of the path.
    [InlineData(false, NotListedReason)]
    // Jellyfin made the item, and never finished reading the file: no running time, no ids yet.
    [InlineData(true, NotRefreshedReason)]
    public async Task Verify_of_a_new_file_jellyfin_hasnt_read_keeps_checking_until_the_hour_runs_out_then_stops_for_review(bool listed, string reason)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var job = await DownloadedAsync(cancellationToken);
        _factory.Files.AddFile(NewPath, TestMedia.File(inode: 7, sizeBytes: NewSize), TestMedia.Taken());
        if (listed)
            _harness.JellyfinServer.OnScanRequested = paths => _harness.JellyfinServer.PlantFilm(paths[0], "Alien.1979.Directors.Cut.2160p", 1979, refreshed: false);
        await _harness.VerifyAsync(cancellationToken);

        //Act
        _factory.Time.Advance(ReplacementVerificationRules.LibraryWait - TimeSpan.FromSeconds(1));
        await _harness.VerifyAsync(cancellationToken);
        var beforeTheHour = await _harness.ReplacementAsync(cancellationToken);
        _factory.Time.Advance(TimeSpan.FromSeconds(1));
        await _harness.VerifyAsync(cancellationToken);

        //Assert
        Assert.Equal((ReplacementJobState.Inspecting, (string?)null), (beforeTheHour.State, beforeTheHour.Reason));
        await AssertNeedsReviewAsync(job, reason, cancellationToken);
        var newFile = Assert.Single(await _harness.NewFilesAsync(cancellationToken));
        Assert.Equal(
            (listed ? JellyfinItemIds.Of(JellyfinItemIds.MovieType, NewLibraryPath) : null, listed ? LibraryFileKind.Movie : (LibraryFileKind?)null, false, (string?)reason),
            (newFile.LibraryItemId, newFile.Kind, newFile.Refreshed, newFile.Problem));
        Assert.Equal(((Guid?)null, (string?)null), CopyOutcome(Assert.Single(await _harness.CopiesAsync(cancellationToken))));
    }

    [Fact]
    public async Task Verify_where_jellyfin_wont_take_the_request_to_look_asks_again_each_pass_until_the_hour_runs_out_then_stops_for_review()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var job = await DownloadedAsync(cancellationToken);
        _factory.Files.AddFile(NewPath, TestMedia.File(inode: 7, sizeBytes: NewSize), TestMedia.Taken());
        _harness.JellyfinServer.ScanRefusal = HttpStatusCode.InternalServerError;
        await _harness.VerifyAsync(cancellationToken);

        //Act
        _factory.Time.Advance(ReplacementVerificationRules.FilesWait - TimeSpan.FromSeconds(1));
        await _harness.VerifyAsync(cancellationToken);
        var beforeTheHour = await _harness.ReplacementAsync(cancellationToken);
        _factory.Time.Advance(TimeSpan.FromSeconds(1));
        await _harness.VerifyAsync(cancellationToken);

        //Assert
        // Never asked as far as the app knows: each pass asks again, an hour from the start of the check.
        Assert.Equal((ReplacementJobState.Inspecting, (DateTime?)null), (beforeTheHour.State, beforeTheHour.LibraryAskedUtc));
        Assert.Equal(
            new DownloadJobReplacementDto(ReplacementJobState.NeedsReview, 1, NotAskedReason, false, 0)
            {
                Id = (await _harness.ReplacementAsync(cancellationToken)).Id,
                CanRetry = true,
                CanCancel = true,
                Stop = ReplacementStop.Check,
            },
            await _harness.LineAsync(job.Id, cancellationToken));
        var replacement = await _harness.ReplacementAsync(cancellationToken);
        Assert.Equal((ReplacementJobState.NeedsReview, (string?)NotAskedReason, (DateTime?)null), (replacement.State, replacement.Reason, replacement.LibraryAskedUtc));
        Assert.Equal(
            [Audited(AuditAction.ReplacementCheckStarted, replacement.Id), Audited(AuditAction.ReplacementNeedsReview, replacement.Id)],
            await _harness.CheckAuditsAsync(cancellationToken));
        Assert.Equal(Enumerable.Repeat("POST /Library/Media/Updated", 3), _harness.Writes());
        Assert.All(_harness.JellyfinServer.ScanRequests, paths => Assert.Equal([NewLibraryPath], paths));
        Assert.Empty(await _harness.ChangedOldCopiesAsync(cancellationToken));
    }

    [Fact]
    public async Task Verify_of_a_jellyfin_that_took_the_request_to_look_late_gives_it_its_hour_from_then()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var job = await DownloadedAsync(cancellationToken);
        _factory.Files.AddFile(NewPath, TestMedia.File(inode: 7, sizeBytes: NewSize), TestMedia.Taken());
        var startedUtc = _factory.Time.GetUtcNow().UtcDateTime;
        // Vasyl, 2026-10-10: the hour runs from when the app asked Jellyfin to look. Jellyfin refuses the request for 50
        // minutes, then takes it — and never makes an item of the file.
        _harness.JellyfinServer.ScanRefusal = HttpStatusCode.InternalServerError;
        await _harness.VerifyAsync(cancellationToken);
        _factory.Time.Advance(TimeSpan.FromMinutes(50));
        _harness.JellyfinServer.ScanRefusal = null;
        var askedUtc = _factory.Time.GetUtcNow().UtcDateTime;
        await _harness.VerifyAsync(cancellationToken);

        //Act
        // An hour and a second from the start of the check: not an hour from the request it took.
        _factory.Time.Advance(TimeSpan.FromMinutes(10) + TimeSpan.FromSeconds(1));
        await _harness.VerifyAsync(cancellationToken);
        var pastTheChecksHour = await _harness.ReplacementAsync(cancellationToken);
        // An hour from the request it took.
        _factory.Time.Advance(TimeSpan.FromMinutes(50) - TimeSpan.FromSeconds(1));
        await _harness.VerifyAsync(cancellationToken);

        //Assert
        Assert.Equal(
            (ReplacementJobState.Inspecting, (string?)null, (DateTime?)startedUtc, (DateTime?)askedUtc),
            (pastTheChecksHour.State, pastTheChecksHour.Reason, pastTheChecksHour.InspectionStartedUtc, pastTheChecksHour.LibraryAskedUtc));
        Assert.Equal(
            new DownloadJobReplacementDto(ReplacementJobState.NeedsReview, 1, NotListedReason, false, 0)
            {
                Id = (await _harness.ReplacementAsync(cancellationToken)).Id,
                CanRetry = true,
                CanCancel = true,
                Stop = ReplacementStop.Check,
            },
            await _harness.LineAsync(job.Id, cancellationToken));
        var replacement = await _harness.ReplacementAsync(cancellationToken);
        Assert.Equal(
            (ReplacementJobState.NeedsReview, (string?)NotListedReason, (DateTime?)startedUtc, (DateTime?)askedUtc),
            (replacement.State, replacement.Reason, replacement.InspectionStartedUtc, replacement.LibraryAskedUtc));
        var newFile = Assert.Single(await _harness.NewFilesAsync(cancellationToken));
        Assert.Equal(((string?)null, (string?)NotListedReason), (newFile.LibraryItemId, newFile.Problem));
        Assert.Equal(
            [Audited(AuditAction.ReplacementCheckStarted, replacement.Id), Audited(AuditAction.ReplacementNeedsReview, replacement.Id)],
            await _harness.CheckAuditsAsync(cancellationToken));
        // The refused request and the one it took; nothing else written.
        Assert.Equal(Enumerable.Repeat("POST /Library/Media/Updated", 2), _harness.Writes());
        Assert.Empty(await _harness.ChangedOldCopiesAsync(cancellationToken));
    }

    // ── stopped at once ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Verify_of_a_new_file_in_a_library_whose_type_cant_be_asked_to_look_stops_for_review_at_once()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // A media library of the test's own type, with no way to be asked to look at a file.
        var homeVideos = await new IntegrationConnectionSeeder(_factory.Scope).Create(FakeIntegrationAdapter.TypeId, "Home videos box");
        await new LibraryLocationSeeder(_factory.Scope).Create(homeVideos, "Home videos", HomeVideosFolder);
        var job = await DownloadedAsync(cancellationToken);
        // qBittorrent moved the download into that library's folder.
        _harness.QBittorrent.UpdateTorrent(
            job.ClientJobId!, torrent => torrent with { SavePath = "/downloads/home-videos", ContentPath = "/downloads/home-videos/Alien" });
        _factory.Files.AddFile(HomeVideoPath, TestMedia.File(inode: 7, sizeBytes: NewSize), TestMedia.Taken());

        //Act
        await _harness.VerifyAsync(cancellationToken);

        //Assert
        Assert.Equal(
            new DownloadJobReplacementDto(ReplacementJobState.NeedsReview, 1, NoCapabilityReason, false, 0)
            {
                Id = (await _harness.ReplacementAsync(cancellationToken)).Id,
                CanRetry = true,
                CanCancel = true,
                Stop = ReplacementStop.Check,
            },
            await _harness.LineAsync(job.Id, cancellationToken));
        var replacement = await _harness.ReplacementAsync(cancellationToken);
        Assert.Equal((ReplacementJobState.NeedsReview, (DateTime?)null), (replacement.State, replacement.LibraryAskedUtc));
        // The file passed its checks, in that library; the library is why it stopped.
        var newFile = Assert.Single(await _harness.NewFilesAsync(cancellationToken));
        Assert.Equal(
            ((string?)HomeVideoPath, (Guid?)homeVideos.Id, (string?)HomeVideoPath, (long?)7, (string?)null),
            (newFile.AppPath, newFile.LibraryIntegrationId, newFile.LibraryPath, newFile.Inode, newFile.Problem));
        Assert.Equal(
            [Audited(AuditAction.ReplacementCheckStarted, replacement.Id), Audited(AuditAction.ReplacementNeedsReview, replacement.Id)],
            await _harness.CheckAuditsAsync(cancellationToken));
        Assert.Empty(_harness.Writes());
        Assert.Empty(await _harness.ChangedOldCopiesAsync(cancellationToken));
    }

    [Theory]
    // Written again after the media inspector read it: whatever Jellyfin says now is of another file.
    [InlineData(false, ChangedReason)]
    // Gone since — moved away, or deleted by another program: there is nothing left to replace the copy with.
    [InlineData(true, GoneReason)]
    public async Task Verify_of_a_new_file_that_changed_or_went_after_it_was_checked_stops_for_review_without_reading_jellyfin(bool gone, string reason)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var job = await DownloadedAsync(cancellationToken);
        _factory.Files.AddFile(NewPath, TestMedia.File(inode: 7, sizeBytes: NewSize), TestMedia.Taken());
        _harness.JellyfinServer.OnScanRequested = paths => _harness.JellyfinServer.PlantFilm(paths[0], "Alien", 1979, imdbId: AlienImdbId);
        await _harness.VerifyAsync(cancellationToken);
        if (gone)
            _factory.Files.Remove(NewPath);
        else
            _factory.Files.AddFile(NewPath, TestMedia.File(inode: 7, sizeBytes: NewSize, modifiedUtc: TestMedia.ModifiedUtc.AddMinutes(5)), TestMedia.Taken());
        var jellyfinBefore = _factory.JellyfinHttp.Requests.Count;

        //Act
        _factory.Time.Advance(ReplacementVerificationTracker.PassInterval);
        await _harness.VerifyAsync(cancellationToken);

        //Assert
        await AssertNeedsReviewAsync(job, reason, cancellationToken);
        // The evidence of the check stays as it read the file; the reason is the file's.
        var newFile = Assert.Single(await _harness.NewFilesAsync(cancellationToken));
        Assert.Equal(
            ((DateTime?)TestMedia.ModifiedUtc, (string?)null, (string?)reason),
            (newFile.ModifiedUtc, newFile.LibraryItemId, newFile.Problem));
        Assert.Empty(_factory.JellyfinHttp.Requests.Skip(jellyfinBefore));
    }

    [Fact]
    public async Task Verify_of_a_new_file_whose_library_was_removed_after_it_was_asked_to_look_stops_for_review_at_once()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var job = await DownloadedAsync(cancellationToken);
        _factory.Files.AddFile(NewPath, TestMedia.File(inode: 7, sizeBytes: NewSize), TestMedia.Taken());
        await _harness.VerifyAsync(cancellationToken);
        // The owner removed Jellyfin under Integrations: its libraries went with it, and no one else can confirm the file.
        using var removed = await _factory.DeleteHttpRequest($"{IntegrationsUrl}/{_harness.Jellyfin.Id}", _harness.Owner.AccessToken);
        var jellyfinBefore = _factory.JellyfinHttp.Requests.Count;

        //Act
        _factory.Time.Advance(ReplacementVerificationTracker.PassInterval);
        await _harness.VerifyAsync(cancellationToken);

        //Assert
        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        // ADR 0033 — the ticked Фільми went with its library, so the copy now lies in no ticked folder.
        Assert.Equal(
            new DownloadJobReplacementDto(ReplacementJobState.NeedsReview, 1, RemovedReason, false, 1)
            {
                Id = (await _harness.ReplacementAsync(cancellationToken)).Id,
                CanRetry = true,
                CanCancel = true,
                Stop = ReplacementStop.Check,
            },
            await _harness.LineAsync(job.Id, cancellationToken));
        var replacement = await _harness.ReplacementAsync(cancellationToken);
        Assert.Equal((ReplacementJobState.NeedsReview, (string?)RemovedReason), (replacement.State, replacement.Reason));
        var newFile = Assert.Single(await _harness.NewFilesAsync(cancellationToken));
        Assert.Equal(((string?)null, (string?)null), (newFile.LibraryItemId, newFile.Problem));
        Assert.Equal(
            [Audited(AuditAction.ReplacementCheckStarted, replacement.Id), Audited(AuditAction.ReplacementNeedsReview, replacement.Id)],
            await _harness.CheckAuditsAsync(cancellationToken));
        // Nothing asked of a library that is gone; the one request to look was before it went.
        Assert.Empty(_factory.JellyfinHttp.Requests.Skip(jellyfinBefore));
        Assert.Equal(["POST /Library/Media/Updated"], _harness.Writes());
        Assert.Empty(await _harness.ChangedOldCopiesAsync(cancellationToken));
    }

    // ── from the rows ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Verify_on_later_passes_goes_on_from_the_rows_never_asking_the_library_or_reading_the_file_again()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await DownloadedAsync(cancellationToken);
        _factory.Files.AddFile(NewPath, TestMedia.File(inode: 7, sizeBytes: NewSize), TestMedia.Taken());
        var startedUtc = _factory.Time.GetUtcNow().UtcDateTime;
        // The send looked at the new file's place before (MAM-38); only the check's own looks count.
        var describedBefore = _factory.Files.DescribedPaths.Count;
        // Jellyfin takes a few passes to make the item: each pass is a scope of its own, as after a restart.
        await _harness.VerifyAsync(cancellationToken);
        _factory.Time.Advance(ReplacementVerificationTracker.PassInterval);
        await _harness.VerifyAsync(cancellationToken);
        _factory.Time.Advance(ReplacementVerificationTracker.PassInterval);
        await _harness.VerifyAsync(cancellationToken);
        var waiting = await _harness.ReplacementAsync(cancellationToken);
        _harness.JellyfinServer.PlantFilm(NewLibraryPath, "Alien", 1979, imdbId: AlienImdbId);

        //Act
        _factory.Time.Advance(ReplacementVerificationTracker.PassInterval);
        await _harness.VerifyAsync(cancellationToken);

        //Assert
        Assert.Equal((ReplacementJobState.Inspecting, (DateTime?)startedUtc, (DateTime?)startedUtc), (waiting.State, waiting.InspectionStartedUtc, waiting.LibraryAskedUtc));
        var replacement = await _harness.ReplacementAsync(cancellationToken);
        Assert.Equal((ReplacementJobState.Verified, (DateTime?)startedUtc), (replacement.State, replacement.LibraryAskedUtc));
        // Asked once, read by the media inspector once; every later pass only looked at the file's identity.
        Assert.Single(_harness.JellyfinServer.ScanRequests);
        Assert.Single(_factory.Files.ReadPaths, path => path == NewPath);
        Assert.Equal(3, _factory.Files.DescribedPaths.Skip(describedBefore).Count(path => path == NewPath));
        Assert.Equal(
            [Audited(AuditAction.ReplacementCheckStarted, replacement.Id), Audited(AuditAction.ReplacementVerified, replacement.Id)],
            await _harness.CheckAuditsAsync(cancellationToken));
    }

    [Fact]
    public async Task Verify_never_reads_a_verified_job_again()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await DownloadedAsync(cancellationToken);
        _factory.Files.AddFile(NewPath, TestMedia.File(inode: 7, sizeBytes: NewSize), TestMedia.Taken());
        _harness.JellyfinServer.OnScanRequested = paths => _harness.JellyfinServer.PlantFilm(paths[0], "Alien", 1979, imdbId: AlienImdbId);
        await _harness.VerifyAsync(cancellationToken);
        _factory.Time.Advance(ReplacementVerificationTracker.PassInterval);
        await _harness.VerifyAsync(cancellationToken);
        var verified = await _harness.ReplacementAsync(cancellationToken);
        // Since, Jellyfin re-identified the item as another film, and the file was removed.
        _harness.JellyfinServer.PlantFilm(NewLibraryPath, "Aliens", 1986, imdbId: "tt0090605");
        _factory.Files.Remove(NewPath);
        var (jellyfinBefore, qbittorrentBefore, describedBefore) =
            (_factory.JellyfinHttp.Requests.Count, _factory.QBittorrentHttp.Requests.Count, _factory.Files.DescribedPaths.Count);

        //Act
        _factory.Time.Advance(ReplacementVerificationRules.LibraryWait);
        await _harness.VerifyAsync(cancellationToken);
        await _harness.VerifyAsync(cancellationToken);

        //Assert
        var replacement = await _harness.ReplacementAsync(cancellationToken);
        Assert.Equal((ReplacementJobState.Verified, verified.StateChangedUtc), (replacement.State, replacement.StateChangedUtc));
        Assert.Equal((Guid?)_alien.Id, Assert.Single(await _harness.NewFilesAsync(cancellationToken)).MediaWorkId);
        Assert.Equal(
            (jellyfinBefore, qbittorrentBefore, describedBefore),
            (_factory.JellyfinHttp.Requests.Count, _factory.QBittorrentHttp.Requests.Count, _factory.Files.DescribedPaths.Count));
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The owner schedules the replacement of Alien's theatrical cut by the Director's Cut release, through the real route,
    /// and qBittorrent finishes it — the director's cut alone, in the Movies category's folder —: the replacement is
    /// Downloaded.
    /// </summary>
    private async Task<DownloadJobDto> DownloadedAsync(CancellationToken cancellationToken)
    {
        var release = await _harness.FindAsync("Alien", 1979, AlienRelease, imdbId: AlienImdbId);
        return await _harness.DownloadedAsync(release, _harness.Movies, [TheatricalPath], [new FakeQBittorrentFile(NewName, NewSize, 1.0)], cancellationToken);
    }

    /// <summary>
    /// The job stopped for review with <paramref name="reason"/> — on the Jobs page too, the check's stop offering the owner's
    /// Retry and Keep both (MAM-27) —, with its two audit rows of the app's own, and nothing touched: Jellyfin asked once to
    /// look, qBittorrent only read, the old copy where it was.
    /// </summary>
    private async Task AssertNeedsReviewAsync(DownloadJobDto job, string reason, CancellationToken cancellationToken)
    {
        Assert.Equal(
            new DownloadJobReplacementDto(ReplacementJobState.NeedsReview, 1, reason, false, 0)
            {
                Id = (await _harness.ReplacementAsync(cancellationToken)).Id,
                CanRetry = true,
                CanCancel = true,
                Stop = ReplacementStop.Check,
            },
            await _harness.LineAsync(job.Id, cancellationToken));
        var replacement = await _harness.ReplacementAsync(cancellationToken);
        Assert.Equal((ReplacementJobState.NeedsReview, (string?)reason), (replacement.State, replacement.Reason));
        Assert.Equal(
            [Audited(AuditAction.ReplacementCheckStarted, replacement.Id), Audited(AuditAction.ReplacementNeedsReview, replacement.Id)],
            await _harness.CheckAuditsAsync(cancellationToken));
        Assert.Equal(["POST /Library/Media/Updated"], _harness.Writes());
        Assert.Empty(await _harness.ChangedOldCopiesAsync(cancellationToken));
    }

    /// <summary>An audit row of the check as it should be: the app's own (no actor), naming the replacement by its release.</summary>
    private static (AuditAction, Guid?, AuditTargetKind?, Guid?, string?) Audited(AuditAction action, Guid replacementJobId) =>
        (action, null, AuditTargetKind.ReplacementJob, replacementJobId, AlienRelease);

    private static (Guid? PairedNewFileId, string? Problem) CopyOutcome(ReplacementJobFileEntity copy) => (copy.PairedNewFileId, copy.Problem);
}
