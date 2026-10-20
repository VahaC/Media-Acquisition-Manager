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
using MediaAcquisitionManager.TestInfrastructure.Texts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MediaAcquisitionManager.Api.Tests.Replacements;

// MAM-25, ADR 0035, ADR 0032 — once its download is done, a replacement of a film checks the new file before anything is
// touched, on the verifier's own passes: complete by qBittorrent's word, read by the media inspector, Jellyfin asked once to
// look at it, then identified by Jellyfin's ids as the old copy's own work — the owner's own TMDb link counting as the
// work's — and paired with it: Verified, with the evidence of each check, a ReplacementCheckStarted and a
// ReplacementVerified row with no actor, whether replacement is switched on or not (ADR 0033); a collection pack's other
// film, another work by Jellyfin's ids, is simply added once the ticked film found its new file (Vasyl, 2026-10-10).
// Another work's id while it found none, conflicting ids, two new files of the film, a name claiming another edition than
// the release and a file running under three quarters of the old copy each stop it for review at once; ids of a scheme
// the work isn't known by here compare with nothing, so they wait for the hour and then stop it; a longer cut, an old
// copy whose running time was never measured, and a name holding any title the work goes by — or, once a sync dropped
// them, the one the dialog showed — pass. Jellyfin hears one POST, the request to look, and is asked and read by its own
// path of the file (/data), never the app's (/media; ADR 0003); every other call to Jellyfin and qBittorrent is a read,
// and the old copy stays where it was. A check that throws costs its job that pass, and the loop goes on. The files'
// checks are ReplacementVerificationFilesHttpTests', a season pack's ReplacementVerificationEpisodesHttpTests', the waits
// ReplacementVerificationLibraryHttpTests'.
public sealed class ReplacementVerificationHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string AlienImdbId = "tt0078748";
    private const string AlienTmdbId = "348";
    private const string AliensImdbId = "tt0090605";
    private const string AliensTmdbId = "679";
    private const string AlienRelease = "Alien (1979) Director's Cut BDRemux 2160p HDR";
    private const string TheatricalPath = "/media/movies/Alien (1979)/Alien (1979) Theatrical Cut.mkv";
    private const string TheatricalLibraryPath = "/data/movies/Alien (1979)/Alien (1979) Theatrical Cut.mkv";
    private const string NewName = "Alien.1979.Directors.Cut.2160p.mkv";
    // The Movies category saves to /downloads/movies, which qBittorrent's mapping brings to /media/movies: Фільми's folder,
    // which Jellyfin reports as /data/movies — so it is asked and read by the second path, never the first.
    private const string NewPath = "/media/movies/Alien.1979.Directors.Cut.2160p.mkv";
    private const string NewLibraryPath = "/data/movies/Alien.1979.Directors.Cut.2160p.mkv";
    private const string PackFolder = "Alien.1979.Directors.Cut";
    private const string PackUhdPath = "/media/movies/Alien.1979.Directors.Cut/Alien.1979.Directors.Cut.2160p.mkv";
    private const string PackHdPath = "/media/movies/Alien.1979.Directors.Cut/Alien.1979.Directors.Cut.1080p.mkv";
    private const string PackUhdLibraryPath = "/data/movies/Alien.1979.Directors.Cut/Alien.1979.Directors.Cut.2160p.mkv";
    private const string PackHdLibraryPath = "/data/movies/Alien.1979.Directors.Cut/Alien.1979.Directors.Cut.1080p.mkv";
    private const string Alien3ImdbId = "tt0103644";
    private const string CollectionRelease = "Alien + Aliens (1979-1986) BDRemux 2160p";
    private const string CollectionFolder = "Alien.Aliens.1979-1986.2160p.BDRemux";
    private const string CollectionAlienPath = "/media/movies/Alien.Aliens.1979-1986.2160p.BDRemux/Alien.1979.2160p.BDRemux.mkv";
    private const string CollectionAliensPath = "/media/movies/Alien.Aliens.1979-1986.2160p.BDRemux/Aliens.1986.2160p.BDRemux.mkv";
    private const string CollectionAlienLibraryPath = "/data/movies/Alien.Aliens.1979-1986.2160p.BDRemux/Alien.1979.2160p.BDRemux.mkv";
    private const string CollectionAliensLibraryPath = "/data/movies/Alien.Aliens.1979-1986.2160p.BDRemux/Aliens.1986.2160p.BDRemux.mkv";
    private const string ExtendedName = "Alien.1979.Extended.Cut.2160p.mkv";
    private const string ExtendedPath = "/media/movies/Alien.1979.Extended.Cut.2160p.mkv";
    private const int NewSize = (3 * TestTorrent.PieceLength) + 5;
    private const long TakenMilliseconds = 5_604_672;
    private const string PlantedExceptionMessage = "planted-exception-message-0025";

    // Sentences the Jobs page shows, spelled out rather than read from product code.
    private const string WrongIdReason =
        "“Living room” identified “/media/movies/Alien.1979.Directors.Cut.2160p.mkv” as another work (IMDb tt0090605), not Alien (1979).";
    private const string CollectionWrongIdReason =
        "“Living room” identified “/media/movies/Alien.Aliens.1979-1986.2160p.BDRemux/Alien.1979.2160p.BDRemux.mkv” as another work (IMDb tt0103644), "
        + "not Alien (1979).";
    private const string CollectionOtherFilmReason =
        "“Living room” identified “/media/movies/Alien.Aliens.1979-1986.2160p.BDRemux/Aliens.1986.2160p.BDRemux.mkv” as another work "
        + "(IMDb tt0090605, TMDb 679), not Alien (1979).";
    private const string ConflictingIdsReason =
        "“Living room” gave “/media/movies/Alien.1979.Directors.Cut.2160p.mkv” conflicting ids: IMDb tt0078748 is Alien (1979)'s, TMDb 679 is not.";
    private const string TwoNewFilesReason =
        "Two new files are Alien (1979): “/media/movies/Alien.1979.Directors.Cut/Alien.1979.Directors.Cut.2160p.mkv” and "
        + "“/media/movies/Alien.1979.Directors.Cut/Alien.1979.Directors.Cut.1080p.mkv”; the app can't tell which one replaces the old copy.";
    private const string OtherEditionReason =
        "“/media/movies/Alien.1979.Extended.Cut.2160p.mkv” claims Extended cut, while the release claimed Director's cut.";
    private const string TooShortReason =
        "“/media/movies/Alien.1979.Directors.Cut.2160p.mkv” runs 1:06:40, less than three quarters of the old copy's 1:33:24.";
    private const string OtherSchemeReason =
        "“Living room” gave “/media/movies/Alien.1979.Directors.Cut.2160p.mkv” TMDb 348 within an hour, but Alien (1979) is known here only by IMDb tt0078748.";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private ReplacementVerificationHarness _harness = null!;
    private MediaWorkEntity _alien = null!;

    public ReplacementVerificationHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        var cancellationToken = TestContext.Current.CancellationToken;
        _harness = await ReplacementVerificationHarness.CreateAsync(_factory);
        // Alien's theatrical cut, inspected, in the ticked Фільми.
        _alien = await _harness.PlantFilmAsync("Alien", 1979, TheatricalPath, inode: 1, imdbId: AlienImdbId, editionLabel: "Theatrical Cut");
        await _harness.InspectAsync(cancellationToken);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    // ── verified ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Verify_of_a_film_whose_new_item_carries_the_old_works_imdb_id_verifies_it_and_keeps_the_evidence()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var job = await DownloadedAsync(cancellationToken);
        _factory.Files.AddFile(NewPath, TestMedia.File(inode: 7, sizeBytes: NewSize), TestMedia.Taken());
        // Jellyfin makes the film item of the path it is asked to look at, refreshed with Alien's IMDb id.
        _harness.JellyfinServer.OnScanRequested = paths => _harness.JellyfinServer.PlantFilm(paths[0], "Alien", 1979, imdbId: AlienImdbId);
        var startedUtc = _factory.Time.GetUtcNow().UtcDateTime;

        //Act
        await _harness.VerifyAsync(cancellationToken);
        var asked = await _harness.ReplacementAsync(cancellationToken);
        var jellyfinBefore = _factory.JellyfinHttp.Requests.Count;
        _factory.Time.Advance(ReplacementVerificationTracker.PassInterval);
        var readUtc = _factory.Time.GetUtcNow().UtcDateTime;
        await _harness.VerifyAsync(cancellationToken);
        var line = await _harness.LineAsync(job.Id, cancellationToken);

        //Assert
        // The first pass checks the files and asks Jellyfin to look; the second reads it and pairs.
        Assert.Equal((ReplacementJobState.Inspecting, (DateTime?)startedUtc, (DateTime?)startedUtc), (asked.State, asked.InspectionStartedUtc, asked.LibraryAskedUtc));
        Assert.Equal(new DownloadJobReplacementDto(ReplacementJobState.Verified, 1, null, false, 0) { Id = (await _harness.ReplacementAsync(cancellationToken)).Id, CanCancel = true }, line);
        var replacement = await _harness.ReplacementAsync(cancellationToken);
        Assert.Equal(
            (ReplacementJobState.Verified, (string?)null, readUtc, (DateTime?)startedUtc, (DateTime?)startedUtc),
            (replacement.State, TestTexts.En(replacement.Reason), replacement.StateChangedUtc, replacement.InspectionStartedUtc, replacement.LibraryAskedUtc));

        // Each check's evidence: qBittorrent's word, the place in the app and in Jellyfin, the media inspector's reading,
        // Jellyfin's item and the work its ids made it.
        var newFile = Assert.Single(await _harness.NewFilesAsync(cancellationToken));
        Assert.Equal(
            (replacement.Id, 0, NewName, (long?)NewSize, (double?)1.0),
            (newFile.ReplacementJobId, newFile.Position, newFile.RelativePath, newFile.ClientSizeBytes, newFile.ClientProgress));
        // ADR 0003 — in the app by qBittorrent's mapping, in Jellyfin by the folder it reported, never one for the other.
        Assert.Equal(
            ((string?)NewPath, (Guid?)_harness.Jellyfin.Id, (string?)NewLibraryPath),
            (newFile.AppPath, newFile.LibraryIntegrationId, newFile.LibraryPath));
        Assert.Equal(
            ((long?)TestMedia.DeviceId, (long?)7, (long?)NewSize, (DateTime?)TestMedia.ModifiedUtc, (DateTime?)startedUtc),
            (newFile.DeviceId, newFile.Inode, newFile.SizeBytes, newFile.ModifiedUtc, newFile.CheckedUtc));
        Assert.Equal(
            ((long?)TakenMilliseconds, "h264", (int?)1920, (int?)1080),
            (newFile.DurationMilliseconds, newFile.VideoCodec, newFile.Width, newFile.Height));
        Assert.Equal(
            (JellyfinItemIds.Of(JellyfinItemIds.MovieType, NewLibraryPath), (LibraryFileKind?)LibraryFileKind.Movie, true, (DateTime?)readUtc),
            (newFile.LibraryItemId, newFile.Kind, newFile.Refreshed, newFile.ReadUtc));
        Assert.Equal(
            ((string?)null, (int?)null, (int?)null, (int?)null),
            (newFile.SeriesItemId, newFile.SeasonNumber, newFile.EpisodeNumber, newFile.EpisodeNumberEnd));
        Assert.Equal(((Guid?)_alien.Id, (string?)null), (newFile.MediaWorkId, TestTexts.En(newFile.Problem)));
        Assert.Equal([(0, false, MediaIdentifierScheme.Imdb, AlienImdbId)], await _harness.NewFileIdentifiersAsync(cancellationToken));

        // The theatrical cut is paired with it, its running time read from the inspection the schedule found.
        var copy = Assert.Single(await _harness.CopiesAsync(cancellationToken));
        Assert.Equal(((Guid?)newFile.Id, (long?)TakenMilliseconds, (string?)null), (copy.PairedNewFileId, copy.DurationMilliseconds, TestTexts.En(copy.Problem)));

        // Two rows of the app's own, no actor, each naming the replacement by its release.
        Assert.Equal(
            [Audited(AuditAction.ReplacementCheckStarted, replacement.Id), Audited(AuditAction.ReplacementVerified, replacement.Id)],
            await _harness.CheckAuditsAsync(cancellationToken));

        // ADR 0030, ADR 0033 — Jellyfin heard one POST, the request to look at the new file by its own path; the rest were
        // reads of the item that path computes, and the old copy is where it was.
        Assert.Equal(["POST /Library/Media/Updated"], _harness.Writes());
        Assert.Equal([NewLibraryPath], Assert.Single(_harness.JellyfinServer.ScanRequests));
        Assert.Contains(
            $"/Items/{JellyfinItemIds.Of(JellyfinItemIds.MovieType, NewLibraryPath)}",
            _factory.JellyfinHttp.Requests.Skip(jellyfinBefore).Select(request => request.Uri.AbsolutePath));
        Assert.Empty(await _harness.ChangedOldCopiesAsync(cancellationToken));
        // The media inspector read the new file; the file inventory has no member that writes.
        Assert.Contains(NewPath, _factory.Files.ReadPaths);
    }

    [Fact]
    public async Task Verify_of_a_film_whose_new_item_carries_only_the_tmdb_id_the_owner_linked_verifies_it()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // ADR 0026 — the owner linked the theatrical cut's item to TMDb by hand: his word is the work's id.
        await LinkToTmdbAsync();
        await DownloadedAsync(cancellationToken);
        _factory.Files.AddFile(NewPath, TestMedia.File(inode: 7, sizeBytes: NewSize), TestMedia.Taken());
        // Jellyfin found Alien by its TMDb id alone.
        _harness.JellyfinServer.OnScanRequested = paths => _harness.JellyfinServer.PlantFilm(paths[0], "Alien", 1979, tmdbId: AlienTmdbId);
        await _harness.VerifyAsync(cancellationToken);

        //Act
        _factory.Time.Advance(ReplacementVerificationTracker.PassInterval);
        await _harness.VerifyAsync(cancellationToken);

        //Assert
        var replacement = await _harness.ReplacementAsync(cancellationToken);
        Assert.Equal((ReplacementJobState.Verified, (string?)null), (replacement.State, TestTexts.En(replacement.Reason)));
        var newFile = Assert.Single(await _harness.NewFilesAsync(cancellationToken));
        Assert.Equal(((Guid?)_alien.Id, (string?)null), (newFile.MediaWorkId, TestTexts.En(newFile.Problem)));
        Assert.Equal([(0, false, MediaIdentifierScheme.Tmdb, AlienTmdbId)], await _harness.NewFileIdentifiersAsync(cancellationToken));
        Assert.Equal((Guid?)newFile.Id, Assert.Single(await _harness.CopiesAsync(cancellationToken)).PairedNewFileId);
        Assert.Equal(["POST /Library/Media/Updated"], _harness.Writes());
    }

    [Theory]
    // Exactly three quarters of the old copy's 1:33:24 is not under them.
    [InlineData(4_203_504)]
    // A longer cut, as a Redux or an Extended one is: MAM-37's measure stops one way only.
    [InlineData(9_000_000)]
    public async Task Verify_of_a_new_file_running_at_least_three_quarters_of_the_old_copy_verifies_it(long newMilliseconds)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await DownloadedAsync(cancellationToken);
        _factory.Files.AddFile(NewPath, TestMedia.File(inode: 7, sizeBytes: NewSize), TestMedia.Taken() with { Duration = TimeSpan.FromMilliseconds(newMilliseconds) });
        _harness.JellyfinServer.OnScanRequested = paths => _harness.JellyfinServer.PlantFilm(paths[0], "Alien", 1979, imdbId: AlienImdbId);
        await _harness.VerifyAsync(cancellationToken);

        //Act
        _factory.Time.Advance(ReplacementVerificationTracker.PassInterval);
        await _harness.VerifyAsync(cancellationToken);

        //Assert
        Assert.Equal(ReplacementJobState.Verified, (await _harness.ReplacementAsync(cancellationToken)).State);
        Assert.Equal((long?)newMilliseconds, Assert.Single(await _harness.NewFilesAsync(cancellationToken)).DurationMilliseconds);
        Assert.Equal((long?)TakenMilliseconds, Assert.Single(await _harness.CopiesAsync(cancellationToken)).DurationMilliseconds);
    }

    [Fact]
    public async Task Verify_of_a_short_new_file_whose_old_copy_has_no_measured_running_time_verifies_it()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // The theatrical cut was re-saved since, and ffprobe found no running time in it: the inspection keeps none.
        _factory.Files.AddFile(
            TheatricalPath,
            TestMedia.File(inode: 1, modifiedUtc: TestMedia.ModifiedUtc.AddDays(1)),
            TestMedia.Taken() with { Duration = null });
        await _harness.InspectAsync(cancellationToken);
        await DownloadedAsync(cancellationToken);
        // A tenth of what the old copy would run, were it known: nothing to compare with.
        _factory.Files.AddFile(NewPath, TestMedia.File(inode: 7, sizeBytes: NewSize), TestMedia.Taken() with { Duration = TimeSpan.FromMinutes(9) });
        _harness.JellyfinServer.OnScanRequested = paths => _harness.JellyfinServer.PlantFilm(paths[0], "Alien", 1979, imdbId: AlienImdbId);
        await _harness.VerifyAsync(cancellationToken);

        //Act
        _factory.Time.Advance(ReplacementVerificationTracker.PassInterval);
        await _harness.VerifyAsync(cancellationToken);

        //Assert
        Assert.Equal(ReplacementJobState.Verified, (await _harness.ReplacementAsync(cancellationToken)).State);
        var copy = Assert.Single(await _harness.CopiesAsync(cancellationToken));
        Assert.Equal(((long?)null, (string?)null), (copy.DurationMilliseconds, TestTexts.En(copy.Problem)));
    }

    [Fact]
    public async Task Verify_reads_no_edition_from_any_title_the_work_goes_by_in_the_new_files_name()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // Shown as "Необроблені алмази", the film goes by "Uncut Gems" too, which its new file is named by: the title is no
        // Uncut cut (MAM-37's rule), though the dialog showed the Ukrainian one.
        const string oldPath = "/media/movies/Uncut Gems (2019)/Uncut.Gems.2019.1080p.mkv";
        const string newName = "Uncut.Gems.2019.2160p.mkv";
        const string gemsImdbId = "tt5727208";
        var gems = await _harness.PlantFilmAsync("Необроблені алмази", 2019, oldPath, inode: 3, imdbId: gemsImdbId, originalTitle: "Uncut Gems");
        await _harness.InspectAsync(cancellationToken);
        var release = await _harness.FindAsync("Необроблені алмази", 2019, "Необроблені алмази / Uncut Gems (2019) BDRemux 2160p", imdbId: gemsImdbId);
        await _harness.DownloadedAsync(release, _harness.Movies, [oldPath], [new FakeQBittorrentFile(newName, NewSize, 1.0)], cancellationToken);
        _factory.Files.AddFile($"/media/movies/{newName}", TestMedia.File(inode: 7, sizeBytes: NewSize), TestMedia.Taken());
        _harness.JellyfinServer.OnScanRequested = paths => _harness.JellyfinServer.PlantFilm(paths[0], "Uncut Gems", 2019, imdbId: gemsImdbId);
        await _harness.VerifyAsync(cancellationToken);

        //Act
        _factory.Time.Advance(ReplacementVerificationTracker.PassInterval);
        await _harness.VerifyAsync(cancellationToken);

        //Assert
        var replacement = await _harness.ReplacementAsync(cancellationToken);
        Assert.Equal((ReplacementJobState.Verified, (string?)null, ""), (replacement.State, TestTexts.En(replacement.Reason), replacement.ReleaseEditions));
        var newFile = Assert.Single(await _harness.NewFilesAsync(cancellationToken));
        Assert.Equal(((Guid?)gems.Id, (string?)null), (newFile.MediaWorkId, TestTexts.En(newFile.Problem)));
        Assert.Equal(((Guid?)newFile.Id, (string?)null), CopyOutcome(Assert.Single(await _harness.CopiesAsync(cancellationToken))));
    }

    [Fact]
    public async Task Verify_of_a_work_whose_titles_a_sync_dropped_reads_no_edition_from_the_title_the_dialog_showed()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        const string oldPath = "/media/movies/Uncut Gems (2019)/Uncut.Gems.2019.1080p.mkv";
        const string newName = "Uncut.Gems.2019.2160p.mkv";
        const string gemsImdbId = "tt5727208";
        var gems = await _harness.PlantFilmAsync("Uncut Gems", 2019, oldPath, inode: 3, imdbId: gemsImdbId);
        await _harness.InspectAsync(cancellationToken);
        var release = await _harness.FindAsync("Uncut Gems", 2019, "Uncut Gems (2019) BDRemux 2160p", imdbId: gemsImdbId);
        await _harness.DownloadedAsync(release, _harness.Movies, [oldPath], [new FakeQBittorrentFile(newName, NewSize, 1.0)], cancellationToken);
        // A sync reported the work anew since: its title rows are gone, and the job's own snapshot is what is left.
        await _factory.WithDbAsync(db => db.MediaWorkTitles.Where(title => title.MediaWorkId == gems.Id).ExecuteDeleteAsync(cancellationToken));
        _factory.Files.AddFile($"/media/movies/{newName}", TestMedia.File(inode: 7, sizeBytes: NewSize), TestMedia.Taken());
        _harness.JellyfinServer.OnScanRequested = paths => _harness.JellyfinServer.PlantFilm(paths[0], "Uncut Gems", 2019, imdbId: gemsImdbId);
        await _harness.VerifyAsync(cancellationToken);

        //Act
        _factory.Time.Advance(ReplacementVerificationTracker.PassInterval);
        await _harness.VerifyAsync(cancellationToken);

        //Assert
        var replacement = await _harness.ReplacementAsync(cancellationToken);
        Assert.Equal((ReplacementJobState.Verified, (string?)null), (replacement.State, TestTexts.En(replacement.Reason)));
    }

    [Fact]
    public async Task Verify_while_replacement_is_switched_off_still_verifies_and_moves_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var job = await DownloadedAsync(cancellationToken);
        _factory.Files.AddFile(NewPath, TestMedia.File(inode: 7, sizeBytes: NewSize), TestMedia.Taken());
        _harness.JellyfinServer.OnScanRequested = paths => _harness.JellyfinServer.PlantFilm(paths[0], "Alien", 1979, imdbId: AlienImdbId);
        // ADR 0033 — the owner switched replacement off after the download: a scheduled job keeps its checks.
        await _factory.WithDbAsync(async db =>
            await db.ReplacementSettings.ExecuteUpdateAsync(setters => setters.SetProperty(settings => settings.IsEnabled, false), cancellationToken));

        //Act
        await _harness.VerifyAsync(cancellationToken);
        _factory.Time.Advance(ReplacementVerificationTracker.PassInterval);
        await _harness.VerifyAsync(cancellationToken);

        //Assert
        Assert.Equal(new DownloadJobReplacementDto(ReplacementJobState.Verified, 1, null, SwitchedOff: true, 0) { Id = (await _harness.ReplacementAsync(cancellationToken)).Id, CanCancel = true }, await _harness.LineAsync(job.Id, cancellationToken));
        Assert.Equal(["POST /Library/Media/Updated"], _harness.Writes());
        Assert.Empty(await _harness.ChangedOldCopiesAsync(cancellationToken));
    }

    [Fact]
    public async Task Verify_of_a_collection_pack_whose_ticked_film_paired_simply_adds_the_film_jellyfin_identifies_as_another_work()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // Vasyl, 2026-10-10 — a pack of Alien and Aliens, Alien's theatrical cut alone ticked: Aliens is no matched work, so
        // Jellyfin's ids make it another; once Alien found its new file, it is the collection's other film, simply added.
        var job = await CollectionDownloadedAsync(cancellationToken);
        _harness.JellyfinServer.OnScanRequested = paths =>
        {
            _harness.JellyfinServer.PlantFilm(paths[0], "Alien", 1979, imdbId: AlienImdbId);
            _harness.JellyfinServer.PlantFilm(paths[1], "Aliens", 1986, imdbId: AliensImdbId, tmdbId: AliensTmdbId);
        };
        await _harness.VerifyAsync(cancellationToken);

        //Act
        _factory.Time.Advance(ReplacementVerificationTracker.PassInterval);
        await _harness.VerifyAsync(cancellationToken);

        //Assert
        Assert.Equal(new DownloadJobReplacementDto(ReplacementJobState.Verified, 1, null, false, 0) { Id = (await _harness.ReplacementAsync(cancellationToken)).Id, CanCancel = true }, await _harness.LineAsync(job.Id, cancellationToken));
        var replacement = await _harness.ReplacementAsync(cancellationToken);
        Assert.Equal((ReplacementJobState.Verified, (string?)null), (replacement.State, TestTexts.En(replacement.Reason)));
        // Alien's new file is Alien's; Aliens' is no work of the job's, and says nothing wrong of itself.
        var newFiles = await _harness.NewFilesAsync(cancellationToken);
        Assert.Equal(
            [
                (0, (string?)CollectionAlienPath, (string?)CollectionAlienLibraryPath, (LibraryFileKind?)LibraryFileKind.Movie, true, (Guid?)_alien.Id, (string?)null),
                (1, CollectionAliensPath, CollectionAliensLibraryPath, LibraryFileKind.Movie, true, null, null),
            ],
            newFiles.Select(file => (file.Position, file.AppPath, file.LibraryPath, file.Kind, file.Refreshed, file.MediaWorkId, TestTexts.En(file.Problem))));
        Assert.Equal(
            [(0, false, MediaIdentifierScheme.Imdb, AlienImdbId), (1, false, MediaIdentifierScheme.Imdb, AliensImdbId), (1, false, MediaIdentifierScheme.Tmdb, AliensTmdbId)],
            await _harness.NewFileIdentifiersAsync(cancellationToken));
        Assert.Equal(((Guid?)newFiles[0].Id, (string?)null), CopyOutcome(Assert.Single(await _harness.CopiesAsync(cancellationToken))));
        Assert.Equal(
            [Audited(AuditAction.ReplacementCheckStarted, replacement.Id, CollectionRelease), Audited(AuditAction.ReplacementVerified, replacement.Id, CollectionRelease)],
            await _harness.CheckAuditsAsync(cancellationToken));
        // One request to look at both new files by Jellyfin's paths; nothing else written, the old copy where it was.
        Assert.Equal(["POST /Library/Media/Updated"], _harness.Writes());
        Assert.Equal([CollectionAlienLibraryPath, CollectionAliensLibraryPath], Assert.Single(_harness.JellyfinServer.ScanRequests));
        Assert.Empty(await _harness.ChangedOldCopiesAsync(cancellationToken));
    }

    // ── stopped ─────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Verify_of_a_new_file_jellyfin_identifies_as_another_work_stops_for_review_at_once()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var job = await DownloadedAsync(cancellationToken);
        _factory.Files.AddFile(NewPath, TestMedia.File(inode: 7, sizeBytes: NewSize), TestMedia.Taken());
        // Jellyfin took the file for Aliens (1986).
        _harness.JellyfinServer.OnScanRequested = paths => _harness.JellyfinServer.PlantFilm(paths[0], "Aliens", 1986, imdbId: AliensImdbId);
        await _harness.VerifyAsync(cancellationToken);

        //Act
        _factory.Time.Advance(ReplacementVerificationTracker.PassInterval);
        await _harness.VerifyAsync(cancellationToken);

        //Assert
        Assert.Equal(
            new DownloadJobReplacementDto(ReplacementJobState.NeedsReview, 1, WrongIdReason, false, 0)
            {
                Id = (await _harness.ReplacementAsync(cancellationToken)).Id,
                CanRetry = true,
                CanCancel = true,
                Stop = ReplacementStop.Check,
            },
            await _harness.LineAsync(job.Id, cancellationToken));
        var replacement = await _harness.ReplacementAsync(cancellationToken);
        // The new file says why; the copy waits on it, unpaired, with no problem of its own.
        var newFile = Assert.Single(await _harness.NewFilesAsync(cancellationToken));
        Assert.Equal(((Guid?)null, (string?)WrongIdReason), (newFile.MediaWorkId, TestTexts.En(newFile.Problem)));
        Assert.Equal([(0, false, MediaIdentifierScheme.Imdb, AliensImdbId)], await _harness.NewFileIdentifiersAsync(cancellationToken));
        var copy = Assert.Single(await _harness.CopiesAsync(cancellationToken));
        Assert.Equal(((Guid?)null, (string?)null), (copy.PairedNewFileId, TestTexts.En(copy.Problem)));
        Assert.Equal(
            [Audited(AuditAction.ReplacementCheckStarted, replacement.Id), Audited(AuditAction.ReplacementNeedsReview, replacement.Id)],
            await _harness.CheckAuditsAsync(cancellationToken));
        Assert.Equal(["POST /Library/Media/Updated"], _harness.Writes());
        Assert.Empty(await _harness.ChangedOldCopiesAsync(cancellationToken));
    }

    [Fact]
    public async Task Verify_of_a_collection_pack_whose_alien_file_jellyfin_identifies_as_another_film_stops_for_review_at_once()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // Jellyfin took the pack's Alien for Alien³: no ticked copy found its new file, so the other films may be it, and
        // neither is simply added.
        var job = await CollectionDownloadedAsync(cancellationToken);
        _harness.JellyfinServer.OnScanRequested = paths =>
        {
            _harness.JellyfinServer.PlantFilm(paths[0], "Alien³", 1992, imdbId: Alien3ImdbId);
            _harness.JellyfinServer.PlantFilm(paths[1], "Aliens", 1986, imdbId: AliensImdbId, tmdbId: AliensTmdbId);
        };
        await _harness.VerifyAsync(cancellationToken);

        //Act
        _factory.Time.Advance(ReplacementVerificationTracker.PassInterval);
        await _harness.VerifyAsync(cancellationToken);

        //Assert
        Assert.Equal(
            new DownloadJobReplacementDto(ReplacementJobState.NeedsReview, 1, CollectionWrongIdReason, false, 0)
            {
                Id = (await _harness.ReplacementAsync(cancellationToken)).Id,
                CanRetry = true,
                CanCancel = true,
                Stop = ReplacementStop.Check,
            },
            await _harness.LineAsync(job.Id, cancellationToken));
        var replacement = await _harness.ReplacementAsync(cancellationToken);
        Assert.Equal((ReplacementJobState.NeedsReview, (string?)CollectionWrongIdReason), (replacement.State, TestTexts.En(replacement.Reason)));
        // Each new file says why; the copy waits on them, unpaired, with no problem of its own.
        Assert.Equal(
            [((Guid?)null, (string?)CollectionWrongIdReason), (null, CollectionOtherFilmReason)],
            (await _harness.NewFilesAsync(cancellationToken)).Select(NewFileOutcome));
        Assert.Equal(((Guid?)null, (string?)null), CopyOutcome(Assert.Single(await _harness.CopiesAsync(cancellationToken))));
        Assert.Equal(
            [Audited(AuditAction.ReplacementCheckStarted, replacement.Id, CollectionRelease), Audited(AuditAction.ReplacementNeedsReview, replacement.Id, CollectionRelease)],
            await _harness.CheckAuditsAsync(cancellationToken));
        Assert.Equal(["POST /Library/Media/Updated"], _harness.Writes());
        Assert.Empty(await _harness.ChangedOldCopiesAsync(cancellationToken));
    }

    [Fact]
    public async Task Verify_of_a_new_file_whose_ids_conflict_with_the_films_stops_for_review_at_once()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await LinkToTmdbAsync();
        var job = await DownloadedAsync(cancellationToken);
        _factory.Files.AddFile(NewPath, TestMedia.File(inode: 7, sizeBytes: NewSize), TestMedia.Taken());
        // Alien's IMDb id beside Aliens' TMDb id: no one work.
        _harness.JellyfinServer.OnScanRequested = paths => _harness.JellyfinServer.PlantFilm(paths[0], "Alien", 1979, imdbId: AlienImdbId, tmdbId: AliensTmdbId);
        await _harness.VerifyAsync(cancellationToken);

        //Act
        _factory.Time.Advance(ReplacementVerificationTracker.PassInterval);
        await _harness.VerifyAsync(cancellationToken);

        //Assert
        Assert.Equal(
            new DownloadJobReplacementDto(ReplacementJobState.NeedsReview, 1, ConflictingIdsReason, false, 0)
            {
                Id = (await _harness.ReplacementAsync(cancellationToken)).Id,
                CanRetry = true,
                CanCancel = true,
                Stop = ReplacementStop.Check,
            },
            await _harness.LineAsync(job.Id, cancellationToken));
        var replacement = await _harness.ReplacementAsync(cancellationToken);
        var newFile = Assert.Single(await _harness.NewFilesAsync(cancellationToken));
        Assert.Equal(((Guid?)null, (string?)ConflictingIdsReason), (newFile.MediaWorkId, TestTexts.En(newFile.Problem)));
        Assert.Equal(
            [(0, false, MediaIdentifierScheme.Imdb, AlienImdbId), (0, false, MediaIdentifierScheme.Tmdb, AliensTmdbId)],
            await _harness.NewFileIdentifiersAsync(cancellationToken));
        Assert.Equal(((Guid?)null, (string?)null), CopyOutcome(Assert.Single(await _harness.CopiesAsync(cancellationToken))));
        Assert.Equal(
            [Audited(AuditAction.ReplacementCheckStarted, replacement.Id), Audited(AuditAction.ReplacementNeedsReview, replacement.Id)],
            await _harness.CheckAuditsAsync(cancellationToken));
        Assert.Equal(["POST /Library/Media/Updated"], _harness.Writes());
        Assert.Empty(await _harness.ChangedOldCopiesAsync(cancellationToken));
    }

    [Fact]
    public async Task Verify_of_a_new_file_whose_ids_share_no_scheme_with_the_films_keeps_checking_until_the_hour_runs_out_then_stops_for_review()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var job = await DownloadedAsync(cancellationToken);
        _factory.Files.AddFile(NewPath, TestMedia.File(inode: 7, sizeBytes: NewSize), TestMedia.Taken());
        // Alien is known here by its IMDb id alone, and Jellyfin found the new file by its TMDb id alone: no id to compare,
        // which is no other work — the owner may still give the item its IMDb id in Jellyfin within the hour.
        _harness.JellyfinServer.OnScanRequested = paths => _harness.JellyfinServer.PlantFilm(paths[0], "Alien", 1979, tmdbId: AlienTmdbId);
        await _harness.VerifyAsync(cancellationToken);

        //Act
        _factory.Time.Advance(ReplacementVerificationRules.LibraryWait - TimeSpan.FromSeconds(1));
        await _harness.VerifyAsync(cancellationToken);
        var beforeTheHour = await _harness.ReplacementAsync(cancellationToken);
        var waitingFiles = await _harness.NewFilesAsync(cancellationToken);
        _factory.Time.Advance(TimeSpan.FromSeconds(1));
        await _harness.VerifyAsync(cancellationToken);

        //Assert
        // Waiting, the file holds no problem yet: it may still be given an id to compare.
        Assert.Equal((ReplacementJobState.Inspecting, (string?)null), (beforeTheHour.State, TestTexts.En(beforeTheHour.Reason)));
        Assert.Equal(((Guid?)null, (string?)null), NewFileOutcome(Assert.Single(waitingFiles)));
        Assert.Equal(
            new DownloadJobReplacementDto(ReplacementJobState.NeedsReview, 1, OtherSchemeReason, false, 0)
            {
                Id = (await _harness.ReplacementAsync(cancellationToken)).Id,
                CanRetry = true,
                CanCancel = true,
                Stop = ReplacementStop.Check,
            },
            await _harness.LineAsync(job.Id, cancellationToken));
        var replacement = await _harness.ReplacementAsync(cancellationToken);
        Assert.Equal((ReplacementJobState.NeedsReview, (string?)OtherSchemeReason), (replacement.State, TestTexts.En(replacement.Reason)));
        Assert.Equal(((Guid?)null, (string?)OtherSchemeReason), NewFileOutcome(Assert.Single(await _harness.NewFilesAsync(cancellationToken))));
        Assert.Equal([(0, false, MediaIdentifierScheme.Tmdb, AlienTmdbId)], await _harness.NewFileIdentifiersAsync(cancellationToken));
        Assert.Equal(((Guid?)null, (string?)null), CopyOutcome(Assert.Single(await _harness.CopiesAsync(cancellationToken))));
        Assert.Equal(
            [Audited(AuditAction.ReplacementCheckStarted, replacement.Id), Audited(AuditAction.ReplacementNeedsReview, replacement.Id)],
            await _harness.CheckAuditsAsync(cancellationToken));
        Assert.Equal(["POST /Library/Media/Updated"], _harness.Writes());
        Assert.Empty(await _harness.ChangedOldCopiesAsync(cancellationToken));
    }

    [Fact]
    public async Task Verify_of_a_download_holding_two_new_files_of_the_film_stops_for_review_as_an_unclear_pair()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // A multi-file torrent: 100 % of it doesn't say which file replaces the copy.
        var job = await DownloadedAsync(
            cancellationToken,
            new FakeQBittorrentFile($"{PackFolder}/Alien.1979.Directors.Cut.2160p.mkv", NewSize, 1.0),
            new FakeQBittorrentFile($"{PackFolder}/Alien.1979.Directors.Cut.1080p.mkv", NewSize, 1.0));
        _factory.Files.AddFile(PackUhdPath, TestMedia.File(inode: 7, sizeBytes: NewSize), TestMedia.Taken());
        _factory.Files.AddFile(PackHdPath, TestMedia.File(inode: 8, sizeBytes: NewSize), TestMedia.Taken());
        // Jellyfin makes both Alien.
        _harness.JellyfinServer.OnScanRequested = paths =>
        {
            foreach (var path in paths)
                _harness.JellyfinServer.PlantFilm(path, "Alien", 1979, imdbId: AlienImdbId);
        };
        await _harness.VerifyAsync(cancellationToken);

        //Act
        _factory.Time.Advance(ReplacementVerificationTracker.PassInterval);
        await _harness.VerifyAsync(cancellationToken);

        //Assert
        Assert.Equal(
            new DownloadJobReplacementDto(ReplacementJobState.NeedsReview, 1, TwoNewFilesReason, false, 0)
            {
                Id = (await _harness.ReplacementAsync(cancellationToken)).Id,
                CanRetry = true,
                CanCancel = true,
                Stop = ReplacementStop.Check,
            },
            await _harness.LineAsync(job.Id, cancellationToken));
        var replacement = await _harness.ReplacementAsync(cancellationToken);
        // Both are Alien and say nothing wrong of themselves; the copy says why it can't be paired.
        Assert.Equal(
            [((string?)PackUhdPath, (Guid?)_alien.Id, (string?)null), (PackHdPath, _alien.Id, null)],
            (await _harness.NewFilesAsync(cancellationToken)).Select(file => (file.AppPath, file.MediaWorkId, TestTexts.En(file.Problem))));
        Assert.Equal(((Guid?)null, (string?)TwoNewFilesReason), CopyOutcome(Assert.Single(await _harness.CopiesAsync(cancellationToken))));
        Assert.Equal(
            [Audited(AuditAction.ReplacementCheckStarted, replacement.Id), Audited(AuditAction.ReplacementNeedsReview, replacement.Id)],
            await _harness.CheckAuditsAsync(cancellationToken));
        Assert.Equal(["POST /Library/Media/Updated"], _harness.Writes());
        Assert.Equal([PackUhdLibraryPath, PackHdLibraryPath], Assert.Single(_harness.JellyfinServer.ScanRequests));
        Assert.Empty(await _harness.ChangedOldCopiesAsync(cancellationToken));
    }

    [Fact]
    public async Task Verify_of_a_new_file_whose_name_claims_another_edition_than_the_release_stops_for_review()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // The release claimed the Director's Cut; its file's name says Extended. A name may stop a replacement, never let one go.
        var job = await DownloadedAsync(cancellationToken, new FakeQBittorrentFile(ExtendedName, NewSize, 1.0));
        _factory.Files.AddFile(ExtendedPath, TestMedia.File(inode: 7, sizeBytes: NewSize), TestMedia.Taken());
        _harness.JellyfinServer.OnScanRequested = paths => _harness.JellyfinServer.PlantFilm(paths[0], "Alien", 1979, imdbId: AlienImdbId);
        await _harness.VerifyAsync(cancellationToken);

        //Act
        _factory.Time.Advance(ReplacementVerificationTracker.PassInterval);
        await _harness.VerifyAsync(cancellationToken);

        //Assert
        Assert.Equal(
            new DownloadJobReplacementDto(ReplacementJobState.NeedsReview, 1, OtherEditionReason, false, 0)
            {
                Id = (await _harness.ReplacementAsync(cancellationToken)).Id,
                // MAM-27 — only dropping the copy or keeping both settles it: the release's word on its cut can't change, so Retry isn't offered.
                CanRetry = false,
                DropOnlyCopyCount = 1,
                CanCancel = true,
                Stop = ReplacementStop.Check,
            },
            await _harness.LineAsync(job.Id, cancellationToken));
        var replacement = await _harness.ReplacementAsync(cancellationToken);
        Assert.Equal("DirectorsCut", replacement.ReleaseEditions);
        // Jellyfin's ids made it Alien, and it paired: the stop is the pair's.
        var newFile = Assert.Single(await _harness.NewFilesAsync(cancellationToken));
        Assert.Equal(((Guid?)_alien.Id, (string?)null), (newFile.MediaWorkId, TestTexts.En(newFile.Problem)));
        Assert.Equal(((Guid?)newFile.Id, (string?)OtherEditionReason), CopyOutcome(Assert.Single(await _harness.CopiesAsync(cancellationToken))));
        Assert.Equal(
            [Audited(AuditAction.ReplacementCheckStarted, replacement.Id), Audited(AuditAction.ReplacementNeedsReview, replacement.Id)],
            await _harness.CheckAuditsAsync(cancellationToken));
        Assert.Equal(["POST /Library/Media/Updated"], _harness.Writes());
        Assert.Empty(await _harness.ChangedOldCopiesAsync(cancellationToken));
    }

    [Fact]
    public async Task Verify_of_a_new_file_running_under_three_quarters_of_the_old_copy_stops_for_review()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var job = await DownloadedAsync(cancellationToken);
        // 1:06:40 against the theatrical cut's 1:33:24: a cut-down copy, or a sample.
        _factory.Files.AddFile(NewPath, TestMedia.File(inode: 7, sizeBytes: NewSize), TestMedia.Taken() with { Duration = TimeSpan.FromMilliseconds(4_000_000) });
        _harness.JellyfinServer.OnScanRequested = paths => _harness.JellyfinServer.PlantFilm(paths[0], "Alien", 1979, imdbId: AlienImdbId);
        await _harness.VerifyAsync(cancellationToken);

        //Act
        _factory.Time.Advance(ReplacementVerificationTracker.PassInterval);
        await _harness.VerifyAsync(cancellationToken);

        //Assert
        Assert.Equal(
            new DownloadJobReplacementDto(ReplacementJobState.NeedsReview, 1, TooShortReason, false, 0)
            {
                Id = (await _harness.ReplacementAsync(cancellationToken)).Id,
                // MAM-27 — only dropping the copy or keeping both settles it: no fix in Jellyfin makes the new file longer, so Retry isn't offered.
                CanRetry = false,
                DropOnlyCopyCount = 1,
                CanCancel = true,
                Stop = ReplacementStop.Check,
            },
            await _harness.LineAsync(job.Id, cancellationToken));
        var replacement = await _harness.ReplacementAsync(cancellationToken);
        var newFile = Assert.Single(await _harness.NewFilesAsync(cancellationToken));
        Assert.Equal(((long?)4_000_000, (Guid?)_alien.Id, (string?)null), (newFile.DurationMilliseconds, newFile.MediaWorkId, TestTexts.En(newFile.Problem)));
        var copy = Assert.Single(await _harness.CopiesAsync(cancellationToken));
        Assert.Equal(((Guid?)newFile.Id, (long?)TakenMilliseconds, (string?)TooShortReason), (copy.PairedNewFileId, copy.DurationMilliseconds, TestTexts.En(copy.Problem)));
        Assert.Equal(
            [Audited(AuditAction.ReplacementCheckStarted, replacement.Id), Audited(AuditAction.ReplacementNeedsReview, replacement.Id)],
            await _harness.CheckAuditsAsync(cancellationToken));
        Assert.Equal(["POST /Library/Media/Updated"], _harness.Writes());
        Assert.Empty(await _harness.ChangedOldCopiesAsync(cancellationToken));
    }

    // ── the loop ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Verify_whose_check_of_a_job_throws_logs_the_type_never_the_message_and_the_next_pass_goes_on()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await DownloadedAsync(cancellationToken);
        _factory.Files.AddFile(NewPath, TestMedia.File(inode: 7, sizeBytes: NewSize), TestMedia.Taken());
        // The claim's write fails, as a database that can't be written would.
        _factory.DbCommandHook.BeforeNextCommand(
            command => command.CommandText.StartsWith("UPDATE \"ReplacementJobs\"", StringComparison.Ordinal),
            _ => throw new InvalidOperationException(PlantedExceptionMessage));
        _factory.Logs.Clear();
        await _factory.VerifyReplacementsAsync(cancellationToken);
        var failed = await _harness.ReplacementAsync(cancellationToken);
        var errors = _factory.Logs.Entries
            .Where(entry => entry.Level >= LogLevel.Error && entry.Category.StartsWith("MediaAcquisitionManager.", StringComparison.Ordinal))
            .ToList();
        var logged = _factory.Logs.Entries.ToList();
        _factory.Logs.Clear();

        //Act
        await _harness.VerifyAsync(cancellationToken);

        //Assert
        // Nothing written, nor its audit row; the app's own line names the type, never the message.
        Assert.Equal(ReplacementJobState.Downloaded, failed.State);
        var error = Assert.Single(errors);
        Assert.Equal(typeof(ReplacementVerifier).FullName, error.Category);
        Assert.StartsWith($"Checking replacement job {failed.Id} threw System.InvalidOperationException", error.Message, StringComparison.Ordinal);
        Assert.Null(error.Exception);
        Assert.DoesNotContain(
            logged,
            entry => entry.Category.StartsWith("MediaAcquisitionManager.", StringComparison.Ordinal)
                && entry.Message.Contains(PlantedExceptionMessage, StringComparison.Ordinal));
        // The next pass claims it.
        Assert.Equal(ReplacementJobState.Inspecting, (await _harness.ReplacementAsync(cancellationToken)).State);
        Assert.Equal([Audited(AuditAction.ReplacementCheckStarted, failed.Id)], await _harness.CheckAuditsAsync(cancellationToken));
    }

    [Fact]
    public async Task Tracker_runs_a_pass_as_soon_as_it_starts_and_stops_without_an_error()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await DownloadedAsync(cancellationToken);
        _factory.Files.AddFile(NewPath, TestMedia.File(inode: 7, sizeBytes: NewSize), TestMedia.Taken());
        var tracker = _factory.Resolve<ReplacementVerificationTracker>();
        _factory.Logs.Clear();

        //Act
        // The host's own start and stop, which the test host leaves to the test.
        await tracker.StartAsync(cancellationToken);
        var asked = await WaitForLibraryAskedAsync(cancellationToken);
        await tracker.StopAsync(cancellationToken);

        //Assert
        // The first pass right after the start, without waiting PassInterval; the stop ends the wait for the next one,
        // which is no error. On .NET 10 a stop can also win the race before the loop runs at all.
        Assert.True(asked);
        Assert.True(tracker.ExecuteTask is { Status: TaskStatus.RanToCompletion or TaskStatus.Canceled }, $"{tracker.ExecuteTask?.Status}");
        Assert.DoesNotContain(_factory.Logs.Entries, entry => entry.Level >= LogLevel.Warning);
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The owner schedules the replacement of Alien's theatrical cut by the Director's Cut release, through the real route,
    /// and qBittorrent finishes it holding <paramref name="files"/> — the director's cut alone, in the Movies category's
    /// folder, unless given —: the replacement is Downloaded.
    /// </summary>
    private async Task<DownloadJobDto> DownloadedAsync(CancellationToken cancellationToken, params FakeQBittorrentFile[] files)
    {
        var release = await _harness.FindAsync("Alien", 1979, AlienRelease, imdbId: AlienImdbId);
        return await _harness.DownloadedAsync(
            release, _harness.Movies, [TheatricalPath], files.Length > 0 ? files : [new FakeQBittorrentFile(NewName, NewSize, 1.0)], cancellationToken);
    }

    /// <summary>
    /// The owner schedules the replacement of Alien's theatrical cut alone by a collection of Alien and Aliens, through the
    /// real route, and qBittorrent finishes it, the pack's two films in a folder of the Movies category's: the replacement is
    /// Downloaded, and both films are on the disk.
    /// </summary>
    private async Task<DownloadJobDto> CollectionDownloadedAsync(CancellationToken cancellationToken)
    {
        var release = await _harness.FindAsync("Alien", 1979, CollectionRelease, imdbId: AlienImdbId);
        var job = await _harness.DownloadedAsync(
            release,
            _harness.Movies,
            [TheatricalPath],
            [
                new FakeQBittorrentFile($"{CollectionFolder}/Alien.1979.2160p.BDRemux.mkv", NewSize, 1.0),
                new FakeQBittorrentFile($"{CollectionFolder}/Aliens.1986.2160p.BDRemux.mkv", NewSize, 1.0),
            ],
            cancellationToken);
        _factory.Files.AddFile(CollectionAlienPath, TestMedia.File(inode: 7, sizeBytes: NewSize), TestMedia.Taken());
        _factory.Files.AddFile(CollectionAliensPath, TestMedia.File(inode: 8, sizeBytes: NewSize), TestMedia.Taken());
        return job;
    }

    /// <summary>MAM-34 — the owner's TMDb link of the theatrical cut's item to Alien's TMDb id, set before the schedule.</summary>
    private Task LinkToTmdbAsync() =>
        new LibraryItemLinkSeeder(_factory.Scope).Create(
            _harness.Jellyfin, JellyfinItemIds.Of(JellyfinItemIds.MovieType, TheatricalLibraryPath), _alien.Id, MediaIdentifierScheme.Tmdb, AlienTmdbId);

    /// <summary>Whether the replacement's library is asked to look within five seconds, read every 50 ms: for the one
    /// test that lets the tracker's own loop run.</summary>
    private async Task<bool> WaitForLibraryAskedAsync(CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            if ((await _harness.ReplacementAsync(cancellationToken)).LibraryAskedUtc is not null)
                return true;
            await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken);
        }

        return false;
    }

    /// <summary>An audit row of the check as it should be: the app's own (no actor), naming the replacement by its release,
    /// the Director's Cut unless <paramref name="release"/> says otherwise.</summary>
    private static (AuditAction, Guid?, AuditTargetKind?, Guid?, string?) Audited(AuditAction action, Guid replacementJobId, string release = AlienRelease) =>
        (action, null, AuditTargetKind.ReplacementJob, replacementJobId, release);

    private static (Guid? MediaWorkId, string? Problem) NewFileOutcome(ReplacementJobNewFileEntity file) => (file.MediaWorkId, TestTexts.En(file.Problem));

    private static (Guid? PairedNewFileId, string? Problem) CopyOutcome(ReplacementJobFileEntity copy) => (copy.PairedNewFileId, TestTexts.En(copy.Problem));
}
