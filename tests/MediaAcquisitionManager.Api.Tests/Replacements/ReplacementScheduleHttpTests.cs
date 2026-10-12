using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Abstractions.Files;
using MediaAcquisitionManager.Core.Abstractions.Metadata;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Paths;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Data;
using MediaAcquisitionManager.TestInfrastructure.Files;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MediaAcquisitionManager.Api.Tests.Replacements;

// MAM-24, ADR 0013, ADR 0032 — POST /api/replacements, the owner's schedule of the old copies he ticked: a send with more to
// it. The job, the replacement with each ticked copy's identity read fresh from the disk (a st_dev past long.MaxValue, a
// modification time to the tick), its works with their registry ids and the owner's confirmation, and the audit rows are
// written in one save before anything is fetched — not one request to qBittorrent before the .torrent is fetched (R1) —;
// then the release is sent, and the answer is the job with its replacement where the send left it, 201 with the job's own
// Location. A schedule whose ticks don't fit the release's works is 400 — a file two works list needs each one's
// confirmation, and two works whose words would read alike are named apart —, one whose copy can't be replaced now — each
// of the copy's and the work's reasons, a replacement still holding it among them — 409 naming the file, and either writes
// and sends nothing. A request id answers its job only with the same ticks, as sets; a Keep both send's id is something
// else to the schedule and the other way round; two copies of one schedule racing make one job and one replacement,
// whichever of them reads the other's first (R4). Only the owner schedules. What the strict send refuses is
// ReplacementScheduleSendHttpTests', how the replacement follows its download ReplacementFollowingHttpTests'.
public sealed class ReplacementScheduleHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/replacements";
    private const string DownloadsUrl = "/api/downloads";
    private const string TolokaAddress = "http://prowlarr.test:9696/1/api";
    private const string TolokaApiKey = "planted-torznab-key-0024";
    private const string JellyfinApiKey = "0123456789abcdef0123456789abcdef";

    private const string AlienImdbId = "tt0078748";
    private const string AlienTmdbId = "348";
    private const string AlienRelease = "Alien (1979) Director's Cut BDRemux 2160p HDR";
    private const string FilmsFolder = "/media/movies";
    private const string TheatricalPath = "/media/movies/Alien (1979)/Alien (1979) Theatrical Cut.mkv";
    private const string DirectorsCutPath = "/media/movies/Alien (1979)/Alien (1979) Director's Cut.mkv";
    private const string TorrentName = "Alien.1979.Directors.Cut.2160p.mkv";

    private const string YellowstoneImdbId = "tt4236770";
    private const string ShowsFolder = "/media/tv-shows";

    private const string HalloweenPath = "/media/movies/Halloween (2018)/Halloween.2018.1080p.mkv";
    private const string OtherHalloweenPath = "/media/movies/Halloween/Halloween.2018.2160p.mkv";

    // Sentences the dialog shows, spelled out rather than read from product code.
    private const string RequestIdMissingError =
        "The download has no request id, which keeps a retry from sending it twice. Reload the page and try again.";
    private const string RequestIdReusedError = "This request id was already used to send a different download. Reload the page and try again.";
    private const string NoFilesError = "Tick at least one copy to replace.";
    private const string FileTickedTwiceError = "One copy is ticked twice. Tick each copy once.";
    private const string TooManyFilesError = "One download can replace at most 500 copies. Tick fewer, or send the rest with another release.";
    private const string NotACopyError =
        "One of the ticked copies isn't a copy of a work this release matched. The list was read again: check the ticks and send again.";
    private const string NotConfirmableError =
        "One of the ticked confirmations isn't one this release needs. The list was read again: check the ticks and send again.";
    private const string ConfirmationMissingError =
        "Tick “This release is Alien (1979)” to replace a copy of it. The list was read again: check the ticks and send again.";
    private const string SwitchedOffError =
        "Replacement is switched off, so no copy can be replaced. Switch it on, and tick the folders it may touch, on the Media folders page, or pick Keep both.";
    private const string MagnetError =
        "This release is a magnet link, whose files the app can't check before sending, so it can't replace a copy. Pick Keep both to download it.";
    private const string NoMatchedWorkError =
        "This release matches no film or held episode of yours any more, so it can't replace a copy. Pick Keep both to download it.";
    private const string ChangedError =
        "“Alien (1979) Theatrical Cut.mkv” changed since the app inspected it, so it can't be replaced. Inspect the library again, or pick Keep both.";
    private const string MissingError =
        "“Alien (1979) Theatrical Cut.mkv” isn't there any more, so it can't be replaced. Pick Keep both to download the release.";
    private const string FolderNotTickedError =
        "“Alien (1979) Theatrical Cut.mkv” isn't in a folder ticked for replacement on the Media folders page, so it can't be replaced. Tick its folder there, or pick Keep both.";
    private const string AlreadyScheduledError =
        "“Alien (1979) Theatrical Cut.mkv” is already scheduled for replacement by another download, so it can't be replaced again. Pick Keep both to download the release.";
    private const string HeldByRemovalError =
        "A removal under Removed copies on the Duplicates page holds “Alien (1979) Theatrical Cut.mkv”, so it can't be replaced. Pick Keep both to download the release.";
    private const string TitleOnlyError =
        "This release matched Alien by its title alone, which never allows a replacement, so “Alien (1979) Theatrical Cut.mkv” can't be replaced. Pick Keep both to download it.";
    private const string ConflictingIdsError =
        "The ids of Alien (1979) and of this release conflict, so “Alien (1979) Theatrical Cut.mkv” can't be replaced. Pick Keep both to download it.";
    private const string NoAppPathError =
        "The app can't reach “Alien.mkv” inside the Media folders, so it can't be replaced. Pick Keep both to download the release.";
    private const string NotInspectedError =
        "The app hasn't inspected “Alien (1979) Unread.mkv” yet, so it can't be replaced. Inspect the library first, or pick Keep both.";
    private const string UnreadableError =
        "The app can't read “Alien (1979) Theatrical Cut.mkv” now, so it can't be replaced. Pick Keep both to download the release.";
    private const string OutsideTheClaimError =
        "Its library places “Yellowstone S01E05.mkv” outside the episodes this release claims, so it can't be replaced. Pick Keep both to download the release.";
    private const string SharedWithUnavailableWorkError =
        "“Alien (1979) Theatrical Cut.mkv” is also a copy of another work this release matched, which can't be replaced, so it can't be replaced either. Pick Keep both to download the release.";
    private const string OwnerOnlyBody = """{"error":"Only the installation owner can do this."}""";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private IMediaWorkSeeder _works = null!;
    private IReleaseSeeder _releases = null!;
    private ILibraryLocationSeeder _libraries = null!;
    private IReplacementSettingsSeeder _replacement = null!;
    private AuthorizedUser _owner = null!;
    private AuthorizedUser _member = null!;
    private IntegrationConnectionEntity _jellyfin = null!;
    private IntegrationConnectionEntity _toloka = null!;
    private IntegrationConnectionEntity _client = null!;
    private FakeTorznabServer _feed = null!;
    private FakeQBittorrentServer _qbittorrent = null!;
    private DestinationMappingEntity _movies = null!;
    private LibraryLocationEntity _films = null!;

    public ReplacementScheduleHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        var users = new UserSeeder(_factory.Scope);
        _owner = await users.CreateAuthorizedOwner("owner");
        _member = await users.CreateAuthorized("member");
        var integrations = new IntegrationConnectionSeeder(_factory.Scope);
        // The library with no path mapping: the app finds each file where Jellyfin says it is.
        _jellyfin = await integrations.Create("jellyfin", "Living room", apiKey: JellyfinApiKey);
        _feed = new FakeTorznabServer(TolokaAddress, TolokaApiKey)
        {
            TorrentFile = TestTorrent.V1(TorrentName, TestTorrent.Bytes((3 * TestTorrent.PieceLength) + 5, seed: 1)),
        };
        _feed.Arm(_factory.TorznabHttp);
        _toloka = await integrations.Create("torznab", "Toloka", TolokaAddress, apiKey: TolokaApiKey);
        _qbittorrent = new FakeQBittorrentServer();
        _qbittorrent.Arm(_factory.QBittorrentHttp);
        // qBittorrent's /downloads is the app's /media: the category Movies saves into the library folder /media/movies.
        _client = await integrations.Create(
            "qbittorrent",
            "qBittorrent",
            _qbittorrent.BaseUrl,
            userName: FakeQBittorrentServer.DefaultUserName,
            password: FakeQBittorrentServer.DefaultPassword,
            pathMappings: [new PathMappingPair("/downloads", "/media")]);
        _movies = await new DestinationMappingSeeder(_factory.Scope).Create(_client, "Movies", "Movies", isDefault: true);
        _works = new MediaWorkSeeder(_factory.Scope);
        _releases = new ReleaseSeeder(_factory.Scope);
        _libraries = new LibraryLocationSeeder(_factory.Scope);
        _replacement = new ReplacementSettingsSeeder(_factory.Scope);
        _films = await _libraries.Create(_jellyfin, "Фільми", FilmsFolder);
        await new AllowedRootSeeder(_factory.Scope).Allow("/media");
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    // ── scheduled ───────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Schedule_writes_the_job_and_the_replacement_with_fresh_identities_before_anything_is_fetched_and_answers_201()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var alien = await PlantAlienAsync(cancellationToken);
        await _replacement.SwitchOn(FilmsFolder);
        var release = await FindAlienAsync();
        var theatrical = await FileIdAsync(TheatricalPath);
        var versionId = await VersionIdAsync(TheatricalPath);
        // A second name made since the inspection: the identity is the same, and the job keeps the link count of now.
        _factory.Files.AddFile(TheatricalPath, TestMedia.File(inode: 1, linkCount: 2), TestMedia.Taken());
        var described = _factory.Files.DescribedPaths.Count;
        (int QBittorrentRequests, int Jobs, int Replacements, int Files, int Works, int Identifiers, int AuditRows)? atFetch = null;
        _feed.Intercept = request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/download", StringComparison.Ordinal))
                atFetch = CountsAsync(cancellationToken).GetAwaiter().GetResult();
            return null;
        };

        //Act
        var response = await ScheduleAsync(release, [theatrical]);

        //Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var job = await ReadJobAsync(response, cancellationToken);
        // The job is a download job, read where every job is.
        Assert.Equal($"{DownloadsUrl}/{job.Id}", response.Headers.Location!.AbsolutePath);
        Assert.Equal(
            (DownloadJobStatus.Submitted, (DownloadOverwriteCheck?)DownloadOverwriteCheck.Checked, AlienRelease),
            (job.Status, job.OverwriteCheck, job.ReleaseTitle));
        // The answer already says how the replacement stands: the download went to qBittorrent after the strict check.
        Assert.Equal(
            new DownloadJobReplacementDto(ReplacementJobState.Submitted, 1, null, false, 0) { Id = await ReplacementIdAsync(cancellationToken), CanCancel = true },
            job.Replacement);
        // ADR 0013, R1 — everything written before the .torrent was fetched, the work's IMDb id included, and nothing asked
        // of qBittorrent before it.
        Assert.Equal((0, 1, 1, 1, 1, 1, 2), atFetch);
        // Decision 10 — the copy's identity read from the disk during the schedule.
        Assert.Contains(TheatricalPath, _factory.Files.DescribedPaths.Skip(described));
        var now = _factory.Time.GetUtcNow().UtcDateTime;
        await _factory.WithDbAsync(async db =>
        {
            var download = await db.DownloadJobs.AsNoTracking().SingleAsync(cancellationToken);
            Assert.Equal((job.Id, (ReleasePayloadKind?)ReleasePayloadKind.TorrentFile), (download.Id, download.PayloadKind));
            var replacement = await db.ReplacementJobs.AsNoTracking().SingleAsync(cancellationToken);
            Assert.Equal(
                (job.Id, (Guid?)_owner.User.Id, "owner", ReplacementJobState.Submitted, (string?)null, "DirectorsCut", now),
                (replacement.DownloadJobId, replacement.RequestedByUserId, replacement.RequestedByUserName, replacement.State, replacement.Reason,
                    replacement.ReleaseEditions, replacement.CreatedUtc));
            var file = await db.ReplacementJobFiles.AsNoTracking().SingleAsync(cancellationToken);
            Assert.Equal(
                (replacement.Id, 0, theatrical, versionId, alien.Id, alien.Id, (int?)null, (int?)null, TheatricalPath, TheatricalPath),
                (file.ReplacementJobId, file.Position, file.PhysicalFileId, file.MediaVersionId, file.MediaWorkId, file.MatchedWorkId,
                    file.SeasonNumber, file.EpisodeNumber, file.ServicePath, file.AppPath));
            // Bit for bit: a st_dev past long.MaxValue as unsigned, and a modification time to the tick.
            Assert.Equal(
                (TestMedia.DeviceId, 1L, 3_891_719_630L, TestMedia.ModifiedUtc, (int?)2, (string?)"Theatrical Cut"),
                (file.DeviceId, file.Inode, file.SizeBytes, file.ModifiedUtc, file.LinkCount, file.EditionLabel));
            Assert.Equal(TestMedia.ModifiedUtc.Ticks, file.ModifiedUtc.Ticks);
            var work = await db.ReplacementJobWorks.AsNoTracking().SingleAsync(cancellationToken);
            Assert.Equal(
                (replacement.Id, alien.Id, MediaWorkKind.Movie, (string?)"Alien", (int?)1979, MatchEvidence.Identifier, false, "This release is Alien (1979)"),
                (work.ReplacementJobId, work.MediaWorkId, work.Kind, work.Title, work.Year, work.Evidence, work.ConfirmedByOwner, work.ConfirmationText));
            Assert.Equal(
                [(MediaIdentifierScheme.Imdb, AlienImdbId, false)],
                await db.ReplacementJobWorkIdentifiers.AsNoTracking()
                    .Where(identifier => identifier.ReplacementJobWorkId == work.Id)
                    .Select(identifier => ValueTuple.Create(identifier.Scheme, identifier.Value, identifier.SetByOwner))
                    .ToListAsync(cancellationToken));
            var audits = await db.AuditEvents.AsNoTracking().OrderBy(audit => audit.Action).ToListAsync(cancellationToken);
            Assert.Equal(
                [
                    (AuditAction.DownloadRequested, (Guid?)_owner.User.Id, (AuditTargetKind?)AuditTargetKind.DownloadJob, (Guid?)job.Id, (string?)AlienRelease, now),
                    (AuditAction.ReplacementScheduled, _owner.User.Id, AuditTargetKind.ReplacementJob, replacement.Id, AlienRelease, now),
                ],
                audits.Select(audit => (audit.Action, audit.ActorUserId, audit.TargetKind, audit.TargetId, audit.TargetName, audit.CreatedUtc)));
        });
        // Only reads, the sign-in and the one add: nothing removed, stopped or paused, and nothing written on the disk (the
        // file inventory has no write).
        AssertOnlyReadsAnd(adds: 1);
        Assert.Single(_factory.TorznabHttp.Requests);
    }

    [Fact]
    public async Task Schedule_of_a_title_and_year_match_keeps_the_owners_confirmation_with_its_audit_row()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var alien = await PlantAlienAsync(cancellationToken);
        await _replacement.SwitchOn(FilmsFolder);
        // Toloka lists no id: the release matches Alien by its title and year.
        var release = await _releases.CreateFoundBySearch(_owner.User, _toloka, "Alien", 1979, "Alien (1979) BDRip 1080p");
        var theatrical = await FileIdAsync(TheatricalPath);
        (int QBittorrentRequests, int Jobs, int Replacements, int Files, int Works, int Identifiers, int AuditRows)? atFetch = null;
        var confirmedAtFetch = false;
        _feed.Intercept = request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/download", StringComparison.Ordinal))
            {
                atFetch = CountsAsync(cancellationToken).GetAwaiter().GetResult();
                _factory.WithDbAsync(async db => confirmedAtFetch = await db.AuditEvents.AnyAsync(
                        audit => audit.Action == AuditAction.ReplacementWorkConfirmed, cancellationToken))
                    .GetAwaiter()
                    .GetResult();
            }

            return null;
        };

        //Act
        var response = await ScheduleAsync(release, [theatrical], confirmedWorkIds: [alien.Id]);

        //Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(ReplacementJobState.Submitted, (await ReadJobAsync(response, cancellationToken)).Replacement!.State);
        // ADR 0013 — the owner's consent, its audit row and the work's IMDb id in the job's one save, before anything is
        // fetched or sent: a send that fails or a crash after it can't leave a job without them.
        Assert.Equal((0, 1, 1, 1, 1, 1, 3), atFetch);
        Assert.True(confirmedAtFetch);
        await _factory.WithDbAsync(async db =>
        {
            var replacement = await db.ReplacementJobs.AsNoTracking().SingleAsync(cancellationToken);
            var work = await db.ReplacementJobWorks.AsNoTracking().SingleAsync(cancellationToken);
            Assert.Equal(
                (alien.Id, MatchEvidence.TitleAndYear, true, "This release is Alien (1979)"),
                (work.MediaWorkId, work.Evidence, work.ConfirmedByOwner, work.ConfirmationText));
            // ADR 0032's consent: its own row, named by the words the owner ticked.
            var confirmed = await db.AuditEvents.AsNoTracking().SingleAsync(audit => audit.Action == AuditAction.ReplacementWorkConfirmed, cancellationToken);
            Assert.Equal(
                ((Guid?)_owner.User.Id, (AuditTargetKind?)AuditTargetKind.ReplacementJob, (Guid?)replacement.Id, (string?)"This release is Alien (1979)"),
                (confirmed.ActorUserId, confirmed.TargetKind, confirmed.TargetId, confirmed.TargetName));
            Assert.Equal(3, await db.AuditEvents.CountAsync(cancellationToken));
        });
    }

    [Fact]
    public async Task Schedule_of_an_episode_keeps_its_place_in_the_claim_and_its_series()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var shows = await _libraries.Create(_jellyfin, "Серіали", ShowsFolder);
        var yellowstone = await _works.CreateSeries(_jellyfin, "Yellowstone", 2018, imdbId: YellowstoneImdbId, seasons: []);
        await PlantEpisodeAsync(yellowstone, shows, 2, 1, "A Thousand Acres", inode: 11);
        var newBeginnings = await PlantEpisodeAsync(yellowstone, shows, 2, 2, "New Beginnings", inode: 12);
        await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);
        await _replacement.SwitchOn(ShowsFolder);
        var tvShows = await new DestinationMappingSeeder(_factory.Scope).Create(_client, "TV Shows", "TV Shows");
        _feed.TorrentFile = TestTorrent.V1("Yellowstone.S02E02.1080p.mkv", TestTorrent.Bytes(TestTorrent.PieceLength + 9, seed: 3));
        var release = await _releases.CreateFoundBySearch(
            _owner.User, _toloka, "Yellowstone", 2018, "Yellowstone S02E02 1080p WEB-DL", ReleaseKind.Tv, imdbId: YellowstoneImdbId);
        var episodeFile = await FileIdAsync(EpisodePathOf(2, 2));

        //Act
        var response = await ScheduleAsync(release, [episodeFile], destination: tvShows);

        //Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        await _factory.WithDbAsync(async db =>
        {
            var file = await db.ReplacementJobFiles.AsNoTracking().SingleAsync(cancellationToken);
            Assert.Equal(
                (newBeginnings.Id, yellowstone.Id, (int?)2, (int?)2, EpisodePathOf(2, 2)),
                (file.MediaWorkId, file.MatchedWorkId, file.SeasonNumber, file.EpisodeNumber, file.AppPath));
            var work = await db.ReplacementJobWorks.AsNoTracking().SingleAsync(cancellationToken);
            Assert.Equal(
                (yellowstone.Id, MediaWorkKind.Series, (string?)"Yellowstone", (int?)2018, MatchEvidence.Identifier, "This release is Yellowstone (2018), season 2, episode 2"),
                (work.MediaWorkId, work.Kind, work.Title, work.Year, work.Evidence, work.ConfirmationText));
            // A series' own ids: what MAM-25 compares the new episode's series with.
            Assert.Equal(
                [(MediaIdentifierScheme.Imdb, YellowstoneImdbId)],
                await db.ReplacementJobWorkIdentifiers.AsNoTracking()
                    .Select(identifier => ValueTuple.Create(identifier.Scheme, identifier.Value))
                    .ToListAsync(cancellationToken));
        });
    }

    [Fact]
    public async Task Schedule_keeps_the_works_ids_the_owners_link_included_after_a_library_sync_replaces_its_rows()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var alien = await PlantAlienAsync(cancellationToken);
        // The owner linked the theatrical cut's item to TMDb by hand (MAM-34): his word counts as the work's (ADR 0026).
        var itemId = await ItemIdAsync(TheatricalPath);
        await new LibraryItemLinkSeeder(_factory.Scope).Create(_jellyfin, itemId, alien.Id, MediaIdentifierScheme.Tmdb, AlienTmdbId);
        await _replacement.SwitchOn(FilmsFolder);
        var release = await FindAlienAsync();
        var scheduled = await ScheduleAsync(release, [await FileIdAsync(TheatricalPath)]);
        new FakeJellyfinServer { ApiKey = JellyfinApiKey }.Arm(_factory.JellyfinHttp);

        //Act
        // A sync of the library as Jellyfin answers it now, which no longer lists Alien: its works, versions and files go.
        await new LibrarySnapshotSeeder(_factory.Scope).Sync(_jellyfin);

        //Assert
        Assert.Equal(HttpStatusCode.Created, scheduled.StatusCode);
        await _factory.WithDbAsync(async db =>
        {
            Assert.False(await db.MediaWorks.AnyAsync(work => work.Id == alien.Id, cancellationToken));
            Assert.False(await db.PhysicalFiles.AnyAsync(file => file.ServicePath == TheatricalPath, cancellationToken));
            // The snapshot isn't keyed into those rows: what MAM-25 compares with is the work as it was when scheduled.
            var work = await db.ReplacementJobWorks.AsNoTracking().SingleAsync(cancellationToken);
            Assert.Equal((alien.Id, (string?)"Alien", (int?)1979), (work.MediaWorkId, work.Title, work.Year));
            Assert.Equal(
                [(MediaIdentifierScheme.Imdb, AlienImdbId, false), (MediaIdentifierScheme.Tmdb, AlienTmdbId, true)],
                (await db.ReplacementJobWorkIdentifiers.AsNoTracking()
                    .Select(identifier => ValueTuple.Create(identifier.Scheme, identifier.Value, identifier.SetByOwner))
                    .ToListAsync(cancellationToken))
                .Order());
            var file = await db.ReplacementJobFiles.AsNoTracking().SingleAsync(cancellationToken);
            Assert.Equal((TheatricalPath, TestMedia.DeviceId, 1L), (file.AppPath, file.DeviceId, file.Inode));
        });
    }

    // ── refused before anything is written ──────────────────────────────────────────────────────

    [Theory]
    [InlineData("no list", NoFilesError)]
    [InlineData("an empty list", NoFilesError)]
    [InlineData("one copy twice", FileTickedTwiceError)]
    [InlineData("501 copies", TooManyFilesError)]
    public async Task Schedule_whose_ticks_are_none_twice_or_too_many_is_400_and_writes_nothing(string ticks, string error)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await PlantAlienAsync(cancellationToken);
        await _replacement.SwitchOn(FilmsFolder);
        var release = await FindAlienAsync();
        var theatrical = await FileIdAsync(TheatricalPath);
        IReadOnlyList<Guid>? fileIds = ticks switch
        {
            "no list" => null,
            "an empty list" => [],
            "one copy twice" => [theatrical, theatrical],
            _ => [.. Enumerable.Range(0, 501).Select(_ => Guid.NewGuid())],
        };

        //Act
        var response = await _factory.PostHttpRequest(
            BaseUrl, _owner.AccessToken, new ScheduleReplacementRequest(Guid.NewGuid(), release.Id, _client.Id, _movies.Id, fileIds));

        //Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(error, await response.ReadErrorAsync(cancellationToken));
        await AssertNothingWrittenOrSentAsync(qBittorrentBefore: 0, cancellationToken);
    }

    [Fact]
    public async Task Schedule_with_an_empty_body_is_400_with_the_request_id_sentence()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(RequestIdMissingError, await response.ReadErrorAsync(cancellationToken));
        await AssertNothingWrittenOrSentAsync(qBittorrentBefore: 0, cancellationToken);
    }

    [Fact]
    public async Task Schedule_of_a_file_the_release_does_not_offer_is_400_and_writes_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await PlantAlienAsync(cancellationToken);
        await _replacement.SwitchOn(FilmsFolder);
        var release = await FindAlienAsync();
        var theatrical = await FileIdAsync(TheatricalPath);

        //Act
        var response = await ScheduleAsync(release, [theatrical, Guid.NewGuid()]);

        //Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(NotACopyError, await response.ReadErrorAsync(cancellationToken));
        await AssertNothingWrittenOrSentAsync(qBittorrentBefore: 0, cancellationToken);
    }

    [Fact]
    public async Task Schedule_without_the_confirmation_a_title_and_year_match_needs_is_400_and_writes_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await PlantAlienAsync(cancellationToken);
        await _replacement.SwitchOn(FilmsFolder);
        var release = await _releases.CreateFoundBySearch(_owner.User, _toloka, "Alien", 1979, "Alien (1979) BDRip 1080p");
        var theatrical = await FileIdAsync(TheatricalPath);

        //Act
        var response = await ScheduleAsync(release, [theatrical]);

        //Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ConfirmationMissingError, await response.ReadErrorAsync(cancellationToken));
        await AssertNothingWrittenOrSentAsync(qBittorrentBefore: 0, cancellationToken);
    }

    [Fact]
    public async Task Schedule_with_a_confirmation_the_release_does_not_need_is_400_and_writes_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var alien = await PlantAlienAsync(cancellationToken);
        await _replacement.SwitchOn(FilmsFolder);
        // Matched by its IMDb id: nothing to confirm.
        var release = await FindAlienAsync();

        //Act
        var response = await ScheduleAsync(release, [await FileIdAsync(TheatricalPath)], confirmedWorkIds: [alien.Id]);

        //Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(NotConfirmableError, await response.ReadErrorAsync(cancellationToken));
        await AssertNothingWrittenOrSentAsync(qBittorrentBefore: 0, cancellationToken);
    }

    [Fact]
    public async Task Schedule_confirming_a_work_none_of_whose_copies_is_ticked_is_400_and_writes_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var living = await PlantAlienAsync(cancellationToken);
        // A second Jellyfin over the same folder holds another Alien: two works matched by title and year, told apart by
        // their libraries.
        var bedroom = await new IntegrationConnectionSeeder(_factory.Scope).Create("jellyfin", "Bedroom", "http://127.0.0.2:9", apiKey: JellyfinApiKey);
        var cinema = await _libraries.Create(bedroom, "Кіно", FilmsFolder);
        var other = await _works.CreateMovie(bedroom, "Alien", 1979);
        await _works.AddVersion(bedroom, other, NewItemId(), cinema, null, "/media/movies/Alien.1979.mkv");
        _factory.Files.AddFile("/media/movies/Alien.1979.mkv", TestMedia.File(inode: 3), TestMedia.Taken());
        await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);
        await _replacement.SwitchOn(FilmsFolder);
        var release = await _releases.CreateFoundBySearch(_owner.User, _toloka, "Alien", 1979, "Alien (1979) BDRip 1080p");

        //Act
        var response = await ScheduleAsync(release, [await FileIdAsync(TheatricalPath)], confirmedWorkIds: [living.Id, other.Id]);

        //Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            "Tick a copy of Alien (1979) or untick “This release is Alien (1979) in “Кіно””. The list was read again: check the ticks and send again.",
            await response.ReadErrorAsync(cancellationToken));
        await AssertNothingWrittenOrSentAsync(qBittorrentBefore: 0, cancellationToken);
    }

    [Fact]
    public async Task Schedule_of_a_copy_that_changed_after_the_dialog_read_it_is_409_naming_the_file_and_writes_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await PlantAlienAsync(cancellationToken);
        await _replacement.SwitchOn(FilmsFolder);
        var release = await FindAlienAsync();
        // The dialog's read offered the copy.
        using var options = await _factory.GetHttpRequest($"{BaseUrl}/options?releaseId={release.Id}&destinationId={_movies.Id}", _owner.AccessToken);
        var offered = (await options.Content.ReadFromJsonAsync<ReplacementOptionsResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
        var theatrical = offered.Works.Single().Copies.Single(copy => copy.File.AppPath == TheatricalPath);
        // Then the file was rewritten before Send.
        _factory.Files.AddFile(TheatricalPath, TestMedia.File(inode: 1, sizeBytes: 3_000_000_000), TestMedia.Taken());
        var qBittorrentBefore = _factory.QBittorrentHttp.Requests.Count;

        //Act
        var response = await ScheduleAsync(release, [theatrical.File.Id]);

        //Assert
        Assert.Null(theatrical.Unavailable);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(ChangedError, await response.ReadErrorAsync(cancellationToken));
        await AssertNothingWrittenOrSentAsync(qBittorrentBefore, cancellationToken);
    }

    [Fact]
    public async Task Schedule_of_a_copy_no_longer_there_is_409_naming_the_file_and_writes_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await PlantAlienAsync(cancellationToken);
        await _replacement.SwitchOn(FilmsFolder);
        var release = await FindAlienAsync();
        var theatrical = await FileIdAsync(TheatricalPath);
        _factory.Files.Remove(TheatricalPath);

        //Act
        var response = await ScheduleAsync(release, [theatrical]);

        //Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(MissingError, await response.ReadErrorAsync(cancellationToken));
        await AssertNothingWrittenOrSentAsync(qBittorrentBefore: 0, cancellationToken);
    }

    [Fact]
    public async Task Schedule_of_a_copy_whose_folder_is_not_ticked_is_409_and_reads_no_disk()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await PlantAlienAsync(cancellationToken);
        // On, but no folder ticked.
        await _replacement.SwitchOn();
        var release = await FindAlienAsync();
        var theatrical = await FileIdAsync(TheatricalPath);
        var described = _factory.Files.DescribedPaths.Count;

        //Act
        var response = await ScheduleAsync(release, [theatrical]);

        //Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(FolderNotTickedError, await response.ReadErrorAsync(cancellationToken));
        Assert.Equal(described, _factory.Files.DescribedPaths.Count);
        await AssertNothingWrittenOrSentAsync(qBittorrentBefore: 0, cancellationToken);
    }

    [Fact]
    public async Task Schedule_with_replacement_switched_off_is_409_and_reads_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await PlantAlienAsync(cancellationToken);
        var release = await FindAlienAsync();
        var theatrical = await FileIdAsync(TheatricalPath);
        var described = _factory.Files.DescribedPaths.Count;

        //Act
        var response = await ScheduleAsync(release, [theatrical]);

        //Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(SwitchedOffError, await response.ReadErrorAsync(cancellationToken));
        Assert.Equal(described, _factory.Files.DescribedPaths.Count);
        await AssertNothingWrittenOrSentAsync(qBittorrentBefore: 0, cancellationToken);
    }

    [Fact]
    public async Task Schedule_of_a_magnet_link_is_409_and_writes_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await PlantAlienAsync(cancellationToken);
        await _replacement.SwitchOn(FilmsFolder);
        var release = await _releases.CreateFoundBySearch(
            _owner.User, _toloka, "Alien", 1979, AlienRelease, imdbId: AlienImdbId, payloadKind: ReleasePayloadKind.Magnet);

        //Act
        var response = await ScheduleAsync(release, [await FileIdAsync(TheatricalPath)]);

        //Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(MagnetError, await response.ReadErrorAsync(cancellationToken));
        await AssertNothingWrittenOrSentAsync(qBittorrentBefore: 0, cancellationToken);
    }

    [Fact]
    public async Task Schedule_of_a_release_that_matches_no_work_is_409_and_writes_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await PlantAlienAsync(cancellationToken);
        await _replacement.SwitchOn(FilmsFolder);
        var release = await _releases.CreateFoundBySearch(_owner.User, _toloka, "Heat", 1995, "Heat (1995) BDRip 1080p", imdbId: "tt0113277");

        //Act
        var response = await ScheduleAsync(release, [await FileIdAsync(TheatricalPath)]);

        //Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(NoMatchedWorkError, await response.ReadErrorAsync(cancellationToken));
        await AssertNothingWrittenOrSentAsync(qBittorrentBefore: 0, cancellationToken);
    }

    [Fact]
    public async Task Schedule_of_a_work_matched_by_its_title_alone_is_409_naming_the_file()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // The library gives Alien no year and no id: a title alone, which never replaces (specification §3.1).
        var alien = await _works.CreateMovie(_jellyfin, "Alien", null);
        await _works.AddVersion(_jellyfin, alien, NewItemId(), _films, null, TheatricalPath);
        _factory.Files.AddFile(TheatricalPath, TestMedia.File(inode: 1), TestMedia.Taken());
        await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);
        await _replacement.SwitchOn(FilmsFolder);
        var release = await _releases.CreateFoundBySearch(_owner.User, _toloka, "Alien", 1979, "Alien (1979) BDRip 1080p");

        //Act
        var response = await ScheduleAsync(release, [await FileIdAsync(TheatricalPath)]);

        //Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(TitleOnlyError, await response.ReadErrorAsync(cancellationToken));
        await AssertNothingWrittenOrSentAsync(qBittorrentBefore: 0, cancellationToken);
    }

    [Fact]
    public async Task Schedule_of_a_work_whose_ids_conflict_with_the_release_is_409_naming_the_file()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // The library's Alien has another IMDb id than the release's: ids that disagree never replace (ADR 0032).
        var alien = await _works.CreateMovie(_jellyfin, "Alien", 1979, imdbId: "tt9999999");
        await _works.AddVersion(_jellyfin, alien, NewItemId(), _films, null, TheatricalPath);
        _factory.Files.AddFile(TheatricalPath, TestMedia.File(inode: 1), TestMedia.Taken());
        await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);
        await _replacement.SwitchOn(FilmsFolder);
        var release = await FindAlienAsync();

        //Act
        var response = await ScheduleAsync(release, [await FileIdAsync(TheatricalPath)]);

        //Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(ConflictingIdsError, await response.ReadErrorAsync(cancellationToken));
        await AssertNothingWrittenOrSentAsync(qBittorrentBefore: 0, cancellationToken);
    }

    [Fact]
    public async Task Schedule_of_a_copy_the_app_can_not_reach_inside_the_media_folders_is_409_naming_the_file()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        const string outside = "/srv/films/Alien (1979)/Alien.mkv";
        var alien = await _works.CreateMovie(_jellyfin, "Alien", 1979, imdbId: AlienImdbId);
        await _works.AddVersion(_jellyfin, alien, NewItemId(), _films, null, outside);
        await _replacement.SwitchOn(FilmsFolder);
        var release = await FindAlienAsync();
        var described = _factory.Files.DescribedPaths.Count;

        //Act
        var response = await ScheduleAsync(release, [await FileIdAsync(outside)]);

        //Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(NoAppPathError, await response.ReadErrorAsync(cancellationToken));
        Assert.Equal(described, _factory.Files.DescribedPaths.Count);
        await AssertNothingWrittenOrSentAsync(qBittorrentBefore: 0, cancellationToken);
    }

    [Fact]
    public async Task Schedule_of_a_copy_the_app_has_not_inspected_is_409_naming_the_file()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        const string unread = "/media/movies/Alien (1979)/Alien (1979) Unread.mkv";
        var alien = await PlantAlienAsync(cancellationToken);
        // A version the library reported since the inspection: no identity the app read to keep.
        await _works.AddVersion(_jellyfin, alien, NewItemId(), _films, "Unread", unread);
        _factory.Files.AddFile(unread, TestMedia.File(inode: 3), TestMedia.Taken());
        await _replacement.SwitchOn(FilmsFolder);
        var release = await FindAlienAsync();

        //Act
        var response = await ScheduleAsync(release, [await FileIdAsync(unread)]);

        //Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(NotInspectedError, await response.ReadErrorAsync(cancellationToken));
        await AssertNothingWrittenOrSentAsync(qBittorrentBefore: 0, cancellationToken);
    }

    [Fact]
    public async Task Schedule_of_a_copy_the_disk_can_not_read_now_is_409_naming_the_file()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await PlantAlienAsync(cancellationToken);
        await _replacement.SwitchOn(FilmsFolder);
        var release = await FindAlienAsync();
        var theatrical = await FileIdAsync(TheatricalPath);
        // Since the inspection, the app may no longer read it.
        _factory.Files.AddProblem(TheatricalPath, FileInventoryStatus.Unreadable, "Permission denied.");

        //Act
        var response = await ScheduleAsync(release, [theatrical]);

        //Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(UnreadableError, await response.ReadErrorAsync(cancellationToken));
        await AssertNothingWrittenOrSentAsync(qBittorrentBefore: 0, cancellationToken);
    }

    [Fact]
    public async Task Schedule_of_an_episode_copy_its_own_item_places_outside_the_claim_is_409_naming_the_file()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var shows = await _libraries.Create(_jellyfin, "Серіали", ShowsFolder);
        var yellowstone = await _works.CreateSeries(_jellyfin, "Yellowstone", 2018, imdbId: YellowstoneImdbId, seasons: []);
        // One episode, two items: the first places it at S01E05, the second at S02E04. The release claims season 2, so
        // the episode is listed there — with the first item's copy, which that item places outside the claim (R3).
        const string firstItem = "item-s01e05";
        const string secondItem = "item-s02e04";
        var ghost = await _works.AddEpisodeAt(_jellyfin, yellowstone, "Ghost", 1, 5, firstItem);
        await _works.PlaceEpisode(_jellyfin, ghost, yellowstone, 2, 4, secondItem);
        await _works.AddVersion(_jellyfin, ghost, firstItem, shows, null, EpisodePathOf(1, 5));
        await _works.AddVersion(_jellyfin, ghost, secondItem, shows, null, EpisodePathOf(2, 4));
        _factory.Files.AddFile(EpisodePathOf(1, 5), TestMedia.File(inode: 105), TestMedia.Taken());
        _factory.Files.AddFile(EpisodePathOf(2, 4), TestMedia.File(inode: 204), TestMedia.Taken());
        await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);
        await _replacement.SwitchOn(ShowsFolder);
        var tvShows = await new DestinationMappingSeeder(_factory.Scope).Create(_client, "TV Shows", "TV Shows");
        var release = await _releases.CreateFoundBySearch(
            _owner.User, _toloka, "Yellowstone", 2018, "Yellowstone (S2) (2019) WEB-DL 1080p", ReleaseKind.Tv, imdbId: YellowstoneImdbId);

        //Act
        var response = await ScheduleAsync(release, [await FileIdAsync(EpisodePathOf(1, 5))], destination: tvShows);

        //Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(OutsideTheClaimError, await response.ReadErrorAsync(cancellationToken));
        await AssertNothingWrittenOrSentAsync(qBittorrentBefore: 0, cancellationToken);
    }

    [Theory]
    [InlineData("Yellowstone (2018) WEB-DL 1080p")]
    [InlineData("Yellowstone (S5) (2022-2024) WEB-DL 1080p")]
    public async Task Schedule_of_an_episode_a_release_claims_none_of_is_400_and_writes_nothing(string title)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var shows = await _libraries.Create(_jellyfin, "Серіали", ShowsFolder);
        var yellowstone = await _works.CreateSeries(_jellyfin, "Yellowstone", 2018, imdbId: YellowstoneImdbId, seasons: []);
        await PlantEpisodeAsync(yellowstone, shows, 2, 2, "New Beginnings", inode: 12);
        await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);
        await _replacement.SwitchOn(ShowsFolder);
        var tvShows = await new DestinationMappingSeeder(_factory.Scope).Create(_client, "TV Shows", "TV Shows");
        // A title naming no season, or a season the library holds none of: the series lists no copy at all.
        var release = await _releases.CreateFoundBySearch(_owner.User, _toloka, "Yellowstone", 2018, title, ReleaseKind.Tv, imdbId: YellowstoneImdbId);

        //Act
        var response = await ScheduleAsync(release, [await FileIdAsync(EpisodePathOf(2, 2))], destination: tvShows);

        //Assert
        // The held episode isn't a copy this release offers, whatever the series' reason.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(NotACopyError, await response.ReadErrorAsync(cancellationToken));
        await AssertNothingWrittenOrSentAsync(qBittorrentBefore: 0, cancellationToken);
    }

    [Fact]
    public async Task Schedule_of_a_file_a_work_matched_by_its_title_alone_also_lists_is_409_naming_the_file()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var bedroom = await new IntegrationConnectionSeeder(_factory.Scope).Create("jellyfin", "Bedroom", "http://127.0.0.2:9", apiKey: JellyfinApiKey);
        var cinema = await _libraries.Create(bedroom, "Кіно", FilmsFolder);
        var living = await _works.CreateMovie(_jellyfin, "Alien", 1979, imdbId: AlienImdbId);
        await _works.AddVersion(_jellyfin, living, NewItemId(), _films, null, TheatricalPath);
        // The other library gives the same file's film no year and no id: a title alone, which never replaces (ADR 0032).
        var other = await _works.CreateMovie(bedroom, "Alien", null);
        await _works.AddVersion(bedroom, other, NewItemId(), cinema, null, TheatricalPath);
        _factory.Files.AddFile(TheatricalPath, TestMedia.File(inode: 1), TestMedia.Taken());
        await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);
        await _replacement.SwitchOn(FilmsFolder);
        var release = await FindAlienAsync();
        // Ticked under the work matched by its id, as the dialog lists it.
        var shared = (await OfferedAsync(release, cancellationToken)).Works.Single(work => work.WorkId == living.Id).Copies.Single().File.Id;
        var qBittorrentBefore = _factory.QBittorrentHttp.Requests.Count;

        //Act
        var response = await ScheduleAsync(release, [shared]);

        //Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(SharedWithUnavailableWorkError, await response.ReadErrorAsync(cancellationToken));
        await AssertNothingWrittenOrSentAsync(qBittorrentBefore, cancellationToken);
    }

    [Theory]
    [InlineData("Фільми", "Кіно")]
    [InlineData("Кіно", "Фільми")]
    public async Task Schedule_of_a_file_two_works_list_with_only_one_of_their_confirmations_is_400_naming_the_other(string confirmed, string missing)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var (living, other) = await PlantAlienInTwoLibrariesAsync(cancellationToken);
        var release = await _releases.CreateFoundBySearch(_owner.User, _toloka, "Alien", 1979, "Alien (1979) BDRip 1080p");
        var shared = await SharedCopyIdAsync(release, cancellationToken);
        var qBittorrentBefore = _factory.QBittorrentHttp.Requests.Count;

        //Act
        var response = await ScheduleAsync(release, [shared], confirmedWorkIds: [confirmed == "Фільми" ? living.Id : other.Id]);

        //Assert
        // R3 — one file under two works matched by title and year: each work's confirmation is needed for it, whichever
        // work's listing comes first.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            $"Tick “This release is Alien (1979) in “{missing}”” to replace a copy of it. The list was read again: check the ticks and send again.",
            await response.ReadErrorAsync(cancellationToken));
        await AssertNothingWrittenOrSentAsync(qBittorrentBefore, cancellationToken);
    }

    [Fact]
    public async Task Schedule_of_a_file_two_works_list_with_both_confirmations_keeps_one_copy_and_both_works()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var (living, other) = await PlantAlienInTwoLibrariesAsync(cancellationToken);
        var release = await _releases.CreateFoundBySearch(_owner.User, _toloka, "Alien", 1979, "Alien (1979) BDRip 1080p");
        var shared = await SharedCopyIdAsync(release, cancellationToken);

        //Act
        var response = await ScheduleAsync(release, [shared], confirmedWorkIds: [living.Id, other.Id]);

        //Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        await _factory.WithDbAsync(async db =>
        {
            Assert.Equal(TheatricalPath, (await db.ReplacementJobFiles.AsNoTracking().SingleAsync(cancellationToken)).AppPath);
            Assert.Equal(
                [
                    (living.Id, true, "This release is Alien (1979) in “Фільми”"),
                    (other.Id, true, "This release is Alien (1979) in “Кіно”"),
                ],
                (await db.ReplacementJobWorks.AsNoTracking().ToListAsync(cancellationToken))
                    .Select(work => (work.MediaWorkId, work.ConfirmedByOwner, work.ConfirmationText))
                    .OrderBy(work => work.MediaWorkId == living.Id ? 0 : 1));
            Assert.Equal(
                ["This release is Alien (1979) in “Кіно”", "This release is Alien (1979) in “Фільми”"],
                (await db.AuditEvents.AsNoTracking()
                    .Where(audit => audit.Action == AuditAction.ReplacementWorkConfirmed)
                    .Select(audit => audit.TargetName!)
                    .ToListAsync(cancellationToken))
                .Order(StringComparer.Ordinal));
        });
    }

    [Fact]
    public async Task Schedule_confirming_one_of_two_works_of_one_title_and_year_in_one_library_names_the_other_by_its_copy()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var (identified, _) = await PlantTwoHalloweensAsync(cancellationToken);
        var release = await _releases.CreateFoundBySearch(_owner.User, _toloka, "Halloween", 2018, "Halloween (2018) BDRip 1080p");

        //Act
        var response = await ScheduleAsync(
            release, [await FileIdAsync(HalloweenPath), await FileIdAsync(OtherHalloweenPath)], confirmedWorkIds: [identified.Id]);

        //Assert
        // Their library alone reads the same for both: the sentence names the one still to tick by its copy.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            "Tick “This release is Halloween (2018) in “Фільми” (the copy “Halloween.2018.2160p.mkv”)” to replace a copy of it. The list was read again: check the ticks and send again.",
            await response.ReadErrorAsync(cancellationToken));
        await AssertNothingWrittenOrSentAsync(qBittorrentBefore: 0, cancellationToken);
    }

    [Fact]
    public async Task Schedule_confirming_two_works_of_one_title_and_year_in_one_library_keeps_each_ones_own_words()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var (identified, unidentified) = await PlantTwoHalloweensAsync(cancellationToken);
        var release = await _releases.CreateFoundBySearch(_owner.User, _toloka, "Halloween", 2018, "Halloween (2018) BDRip 1080p");

        //Act
        var response = await ScheduleAsync(
            release, [await FileIdAsync(HalloweenPath), await FileIdAsync(OtherHalloweenPath)], confirmedWorkIds: [identified.Id, unidentified.Id]);

        //Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        const string identifiedWords = "This release is Halloween (2018) in “Фільми” (the copy “Halloween.2018.1080p.mkv”)";
        const string unidentifiedWords = "This release is Halloween (2018) in “Фільми” (the copy “Halloween.2018.2160p.mkv”)";
        await _factory.WithDbAsync(async db =>
        {
            Assert.Equal(
                [(identified.Id, identifiedWords), (unidentified.Id, unidentifiedWords)],
                (await db.ReplacementJobWorks.AsNoTracking().ToListAsync(cancellationToken))
                    .Select(work => (work.MediaWorkId, work.ConfirmationText))
                    .OrderBy(work => work.ConfirmationText, StringComparer.Ordinal));
            // ADR 0032's consent: each work's own row says which one the owner confirmed.
            Assert.Equal(
                [identifiedWords, unidentifiedWords],
                (await db.AuditEvents.AsNoTracking()
                    .Where(audit => audit.Action == AuditAction.ReplacementWorkConfirmed)
                    .Select(audit => audit.TargetName!)
                    .ToListAsync(cancellationToken))
                .Order(StringComparer.Ordinal));
        });
    }

    [Fact]
    public async Task Schedule_of_a_copy_another_download_already_replaces_is_409_and_writes_nothing_more()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await PlantAlienAsync(cancellationToken);
        await _replacement.SwitchOn(FilmsFolder);
        var theatrical = await FileIdAsync(TheatricalPath);
        var first = await ScheduleAsync(await FindAlienAsync(), [theatrical]);
        // Another listing of Alien, from another search: another release.
        var other = await _releases.CreateFoundBySearch(_owner.User, _toloka, "Alien", 1979, "Alien (1979) Remastered 1080p", imdbId: AlienImdbId);
        var qBittorrentBefore = _factory.QBittorrentHttp.Requests.Count;

        //Act
        var response = await ScheduleAsync(other, [theatrical]);

        //Assert
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(AlreadyScheduledError, await response.ReadErrorAsync(cancellationToken));
        await _factory.WithDbAsync(async db =>
        {
            Assert.Equal(1, await db.DownloadJobs.CountAsync(cancellationToken));
            Assert.Equal(1, await db.ReplacementJobs.CountAsync(cancellationToken));
        });
        Assert.Equal(qBittorrentBefore, _factory.QBittorrentHttp.Requests.Count);
    }

    [Fact]
    public async Task Schedule_of_a_copy_an_open_removal_holds_is_409_naming_the_removal_and_writes_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await PlantAlienAsync(cancellationToken);
        await _replacement.SwitchOn(FilmsFolder);
        var release = await FindAlienAsync();
        var theatrical = await FileIdAsync(TheatricalPath);
        // MAM-40 — the owner removed the theatrical cut from its duplicate group after the dialog listed it: it is in the
        // quarantine, under Removed copies, and the library not synced since.
        await _factory.WithDbAsync(async db =>
        {
            var removals = new CopyRemovalDataFactory(db, _factory.Time);
            var removal = await removals.Create(_owner.User.Id, CopyRemovalState.Quarantined, TheatricalPath, TheatricalPath);
            await removals.AddName(
                removal.Id, 0, ReplacementNameKind.Video, TheatricalPath, $"/media/.mam-quarantine/{removal.Id}/movies/Alien (1979)/Alien (1979) Theatrical Cut.mkv", "/media",
                TestMedia.File(inode: 1), ReplacementNameState.Quarantined);
        });

        //Act
        var response = await ScheduleAsync(release, [theatrical]);

        //Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(HeldByRemovalError, await response.ReadErrorAsync(cancellationToken));
        await _factory.WithDbAsync(async db =>
        {
            Assert.Equal(0, await db.DownloadJobs.CountAsync(cancellationToken));
            Assert.Equal(0, await db.ReplacementJobs.CountAsync(cancellationToken));
            Assert.Equal(0, await db.AuditEvents.CountAsync(cancellationToken));
        });
        Assert.Empty(_factory.QBittorrentHttp.Requests);
    }

    [Fact]
    public async Task Schedule_of_a_copy_whose_earlier_replacement_waits_downloaded_is_409_and_writes_nothing_more()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await PlantAlienAsync(cancellationToken);
        await _replacement.SwitchOn(FilmsFolder);
        // An earlier download replacing the theatrical cut finished: in 0.3.0 its replacement rests at Downloaded, waiting
        // for MAM-25's check, and still holds the copy — a second replacement would find it gone.
        var earlier = await _releases.CreateFoundBySearch(_owner.User, _toloka, "Alien", 1979, "Alien (1979) Remastered 1080p", imdbId: AlienImdbId);
        var planted = Guid.Empty;
        await _factory.WithDbAsync(async db =>
        {
            var download = await new DownloadJobDataFactory(db, _factory.Time).Create(
                earlier.Id, _client.Id, _client.Name, status: DownloadJobStatus.Downloaded, requestedByUserId: _owner.User.Id);
            var replacements = new ReplacementJobDataFactory(db, _factory.Time);
            planted = (await replacements.Create(download.Id, _owner.User.Id, ReplacementJobState.Downloaded)).Id;
            await replacements.AddFile(planted, TheatricalPath, TestMedia.File(inode: 1));
        });
        var release = await FindAlienAsync();

        //Act
        var response = await ScheduleAsync(release, [await FileIdAsync(TheatricalPath)]);

        //Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(AlreadyScheduledError, await response.ReadErrorAsync(cancellationToken));
        await _factory.WithDbAsync(async db =>
        {
            Assert.Equal(planted, (await db.ReplacementJobs.AsNoTracking().SingleAsync(cancellationToken)).Id);
            Assert.Equal(1, await db.DownloadJobs.CountAsync(cancellationToken));
            Assert.Equal(0, await db.AuditEvents.CountAsync(cancellationToken));
        });
        Assert.Empty(_factory.TorznabHttp.Requests);
        Assert.Empty(_factory.QBittorrentHttp.Requests);
    }

    [Fact]
    public async Task Schedule_with_the_dialogs_body_answers_the_replacement_camel_case_with_its_state_by_name()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await PlantAlienAsync(cancellationToken);
        await _replacement.SwitchOn(FilmsFolder);
        var release = await FindAlienAsync();
        var theatrical = await FileIdAsync(TheatricalPath);
        // MAM-24 — the body as the Download dialog builds it (frontend ScheduleReplacementRequest), names and all.
        var body = new
        {
            requestId = Guid.NewGuid(),
            releaseId = release.Id,
            downloadClientId = _client.Id,
            destinationId = _movies.Id,
            fileIds = new[] { theatrical },
            confirmedWorkIds = Array.Empty<Guid>(),
        };

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _owner.AccessToken, body);

        //Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var replacement = document.RootElement.GetProperty("replacement");
        // Exactly the names the Jobs page and the dialog's sent view read (frontend DownloadJobReplacement), the state by
        // name, as every enum on the wire. MAM-26 — and the replacement's id, its quarantine's counts and Retry. MAM-27 — and
        // its drops, Keep both, the stop of a review and where a running check is.
        Assert.Equal(
            [
                "state", "copyCount", "reason", "switchedOff", "copiesOutsideTickedFolders", "id", "quarantinedCopyCount", "stayedFileCount", "canRetry",
                "droppedCopyCount", "dropOnlyCopyCount", "canCancel", "stop", "checkStage", "checkWaitsUntilUtc",
            ],
            replacement.EnumerateObject().Select(property => property.Name));
        Assert.Equal(
            ("Submitted", 1, JsonValueKind.Null, false, 0),
            (replacement.GetProperty("state").GetString(), replacement.GetProperty("copyCount").GetInt32(), replacement.GetProperty("reason").ValueKind,
                replacement.GetProperty("switchedOff").GetBoolean(), replacement.GetProperty("copiesOutsideTickedFolders").GetInt32()));
        Assert.Equal(
            (0, 0, false),
            (replacement.GetProperty("quarantinedCopyCount").GetInt32(), replacement.GetProperty("stayedFileCount").GetInt32(),
                replacement.GetProperty("canRetry").GetBoolean()));
        Assert.Equal(
            (0, 0, true, JsonValueKind.Null, JsonValueKind.Null, JsonValueKind.Null),
            (replacement.GetProperty("droppedCopyCount").GetInt32(), replacement.GetProperty("dropOnlyCopyCount").GetInt32(),
                replacement.GetProperty("canCancel").GetBoolean(), replacement.GetProperty("stop").ValueKind, replacement.GetProperty("checkStage").ValueKind,
                replacement.GetProperty("checkWaitsUntilUtc").ValueKind));
        var id = replacement.GetProperty("id").GetGuid();
        await _factory.WithDbAsync(async db =>
        {
            var row = await db.ReplacementJobs.AsNoTracking().SingleAsync(cancellationToken);
            Assert.Equal((row.Id, ReplacementJobState.Submitted), (id, row.State));
            Assert.Equal(theatrical, (await db.ReplacementJobFiles.AsNoTracking().SingleAsync(cancellationToken)).PhysicalFileId);
        });
    }

    // ── one request, one job ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Schedule_again_with_its_request_id_and_the_same_ticks_answers_its_job_and_sends_once()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await PlantAlienAsync(cancellationToken);
        await _replacement.SwitchOn(FilmsFolder);
        var release = await FindAlienAsync();
        var theatrical = await FileIdAsync(TheatricalPath);
        var directorsCut = await FileIdAsync(DirectorsCutPath);
        var requestId = Guid.NewGuid();
        // Ticked theatrical cut first; the job keeps them in the dialog's order, the director's cut's version first.
        var first = await ScheduleAsync(release, [theatrical, directorsCut], requestId: requestId);
        // ADR 0013 — the retry is answered before anything is read: a copy changed since doesn't stop it.
        _factory.Files.AddFile(TheatricalPath, TestMedia.File(inode: 1, sizeBytes: 5), TestMedia.Taken());

        //Act
        var response = await ScheduleAsync(release, [theatrical, directorsCut], requestId: requestId);

        //Assert
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var job = await ReadJobAsync(first, cancellationToken);
        Assert.Equivalent(job, await ReadJobAsync(response, cancellationToken), strict: true);
        Assert.Equal(
            new DownloadJobReplacementDto(ReplacementJobState.Submitted, 2, null, false, 0) { Id = await ReplacementIdAsync(cancellationToken), CanCancel = true },
            job.Replacement);
        await _factory.WithDbAsync(async db =>
        {
            Assert.Equal(1, await db.DownloadJobs.CountAsync(cancellationToken));
            Assert.Equal(1, await db.ReplacementJobs.CountAsync(cancellationToken));
            // In the dialog's order, whatever order the ticks came in: a retry is compared with them as sets.
            Assert.Equal(
                [directorsCut, theatrical],
                await db.ReplacementJobFiles.AsNoTracking().OrderBy(file => file.Position).Select(file => file.PhysicalFileId).ToListAsync(cancellationToken));
            Assert.Equal(2, await db.AuditEvents.CountAsync(cancellationToken));
        });
        Assert.Single(_qbittorrent.AddRequests);
        Assert.Single(_factory.TorznabHttp.Requests);
    }

    [Theory]
    [InlineData("another copy")]
    [InlineData("a confirmation more")]
    public async Task Schedule_again_with_its_request_id_and_other_ticks_is_409_and_writes_nothing_more(string change)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var alien = await PlantAlienAsync(cancellationToken);
        await _replacement.SwitchOn(FilmsFolder);
        var release = await FindAlienAsync();
        var theatrical = await FileIdAsync(TheatricalPath);
        var requestId = Guid.NewGuid();
        var first = await ScheduleAsync(release, [theatrical], requestId: requestId);
        var qBittorrentBefore = _factory.QBittorrentHttp.Requests.Count;

        //Act
        var response = change == "another copy"
            ? await ScheduleAsync(release, [theatrical, await FileIdAsync(DirectorsCutPath)], requestId: requestId)
            : await ScheduleAsync(release, [theatrical], confirmedWorkIds: [alien.Id], requestId: requestId);

        //Assert
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(RequestIdReusedError, await response.ReadErrorAsync(cancellationToken));
        await _factory.WithDbAsync(async db =>
        {
            Assert.Equal(1, await db.ReplacementJobs.CountAsync(cancellationToken));
            Assert.Equal(1, await db.ReplacementJobFiles.CountAsync(cancellationToken));
            Assert.Equal(2, await db.AuditEvents.CountAsync(cancellationToken));
        });
        Assert.Equal(qBittorrentBefore, _factory.QBittorrentHttp.Requests.Count);
    }

    [Fact]
    public async Task Schedule_with_the_request_id_of_a_keep_both_send_is_409_and_writes_nothing_more()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await PlantAlienAsync(cancellationToken);
        await _replacement.SwitchOn(FilmsFolder);
        var release = await FindAlienAsync();
        var requestId = Guid.NewGuid();
        var sent = await _factory.PostHttpRequest(DownloadsUrl, _owner.AccessToken, new SendDownloadRequest(requestId, release.Id, _client.Id, _movies.Id));

        //Act
        var response = await ScheduleAsync(release, [await FileIdAsync(TheatricalPath)], requestId: requestId);

        //Assert
        Assert.Equal(HttpStatusCode.Created, sent.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(RequestIdReusedError, await response.ReadErrorAsync(cancellationToken));
        await _factory.WithDbAsync(async db =>
        {
            Assert.Equal(1, await db.DownloadJobs.CountAsync(cancellationToken));
            Assert.Equal(0, await db.ReplacementJobs.CountAsync(cancellationToken));
        });
        Assert.Single(_qbittorrent.AddRequests);
    }

    [Fact]
    public async Task Send_with_the_request_id_of_a_schedule_is_409_and_writes_nothing_more()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await PlantAlienAsync(cancellationToken);
        await _replacement.SwitchOn(FilmsFolder);
        var release = await FindAlienAsync();
        var requestId = Guid.NewGuid();
        var scheduled = await ScheduleAsync(release, [await FileIdAsync(TheatricalPath)], requestId: requestId);

        //Act
        var response = await _factory.PostHttpRequest(DownloadsUrl, _owner.AccessToken, new SendDownloadRequest(requestId, release.Id, _client.Id, _movies.Id));

        //Assert
        Assert.Equal(HttpStatusCode.Created, scheduled.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(RequestIdReusedError, await response.ReadErrorAsync(cancellationToken));
        await _factory.WithDbAsync(async db =>
        {
            Assert.Equal(1, await db.DownloadJobs.CountAsync(cancellationToken));
            Assert.Equal(1, await db.ReplacementJobs.CountAsync(cancellationToken));
        });
        Assert.Single(_qbittorrent.AddRequests);
    }

    [Fact]
    public async Task Schedule_twice_at_once_with_one_request_id_makes_one_job_and_one_replacement_and_sends_once()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await PlantAlienAsync(cancellationToken);
        await _replacement.SwitchOn(FilmsFolder);
        var release = await FindAlienAsync();
        var request = new ScheduleReplacementRequest(Guid.NewGuid(), release.Id, _client.Id, _movies.Id, [await FileIdAsync(TheatricalPath)]);
        HttpResponseMessage? competing = null;
        // Deterministic overlap: this copy found no job for its request id, planned its replacement and is about to save,
        // when the other copy is sent — and saved, sent to qBittorrent and followed — whole.
        _factory.SaveChangesHook.BeforeNextSave(
            context => context.ChangeTracker.Entries<DownloadJobEntity>().Any(entry => entry.State == EntityState.Added),
            async _ => competing = await _factory.PostHttpRequest(BaseUrl, _owner.AccessToken, request));
        _factory.Logs.Clear();

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _owner.AccessToken, request);

        //Assert
        // The unique index lets one job commit, its replacement with it; the other copy answers that job, a 200 rather
        // than a 409 — its replacement rows and audit rows rolled back with its insert.
        Assert.Equal(HttpStatusCode.Created, competing?.StatusCode);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var job = await ReadJobAsync(competing, cancellationToken);
        Assert.Equal(ReplacementJobState.Submitted, job.Replacement!.State);
        Assert.Equivalent(job, await ReadJobAsync(response, cancellationToken), strict: true);
        await _factory.WithDbAsync(async db =>
        {
            Assert.Equal(1, await db.DownloadJobs.CountAsync(cancellationToken));
            Assert.Equal(job.Id, (await db.ReplacementJobs.AsNoTracking().SingleAsync(cancellationToken)).DownloadJobId);
            Assert.Equal(1, await db.ReplacementJobFiles.CountAsync(cancellationToken));
            Assert.Equal(1, await db.ReplacementJobWorks.CountAsync(cancellationToken));
            Assert.Equal(2, await db.AuditEvents.CountAsync(cancellationToken));
        });
        Assert.Single(_qbittorrent.AddRequests);
        Assert.Contains(
            _factory.Logs.Entries,
            entry => entry.Level == LogLevel.Information
                && entry.Message == $"A concurrent copy of request {request.RequestId} made download job {job.Id} first; this one answered it.");
    }

    [Fact]
    public async Task Schedule_whose_copy_wrote_its_job_while_it_planned_answers_that_job_rather_than_already_scheduled()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await PlantAlienAsync(cancellationToken);
        await _replacement.SwitchOn(FilmsFolder);
        var release = await FindAlienAsync();
        var request = new ScheduleReplacementRequest(Guid.NewGuid(), release.Id, _client.Id, _movies.Id, [await FileIdAsync(TheatricalPath)]);
        HttpResponseMessage? competing = null;
        // Deterministic overlap: this copy found no job for its request id and none that sent the release, and is planning
        // its replacement, when the other copy is sent — saved, sent to qBittorrent and followed — whole, just before this
        // one reads which copies the replacements not yet ended hold: the other copy's replacement now holds the ticked one.
        _factory.DbCommandHook.BeforeNextCommand(
            command => command.CommandText.StartsWith("SELECT", StringComparison.Ordinal)
                && command.CommandText.Contains("\"ReplacementJobFiles\"", StringComparison.Ordinal)
                && command.CommandText.Contains("\"ReplacementJobs\"", StringComparison.Ordinal),
            async _ => competing = await _factory.PostHttpRequest(BaseUrl, _owner.AccessToken, request));

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _owner.AccessToken, request);

        //Assert
        // Answered as the unique index answers a copy that gets as far as the save (ADR 0013, R4) — a 200 with the job its
        // request made, never a 409 saying another download already replaces the copy.
        Assert.Equal(HttpStatusCode.Created, competing?.StatusCode);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var job = await ReadJobAsync(competing, cancellationToken);
        Assert.Equivalent(job, await ReadJobAsync(response, cancellationToken), strict: true);
        await _factory.WithDbAsync(async db =>
        {
            Assert.Equal(1, await db.DownloadJobs.CountAsync(cancellationToken));
            Assert.Equal(job.Id, (await db.ReplacementJobs.AsNoTracking().SingleAsync(cancellationToken)).DownloadJobId);
            Assert.Equal(1, await db.ReplacementJobFiles.CountAsync(cancellationToken));
        });
        Assert.Single(_qbittorrent.AddRequests);
    }

    // ── who ─────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Schedule_as_a_member_is_403_and_writes_nothing_even_for_their_own_release()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await PlantAlienAsync(cancellationToken);
        await _replacement.SwitchOn(FilmsFolder);
        var membersRelease = await _releases.CreateFoundBySearch(_member.User, _toloka, "Alien", 1979, AlienRelease, imdbId: AlienImdbId);
        var theatrical = await FileIdAsync(TheatricalPath);
        var described = _factory.Files.DescribedPaths.Count;

        //Act
        var response = await _factory.PostHttpRequest(
            BaseUrl, _member.AccessToken, new ScheduleReplacementRequest(Guid.NewGuid(), membersRelease.Id, _client.Id, _movies.Id, [theatrical]));

        //Assert
        // ADR 0033 — a member keeps Keep both; the owner check comes before anything is read.
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(OwnerOnlyBody, await response.Content.ReadAsStringAsync(cancellationToken));
        Assert.Equal(described, _factory.Files.DescribedPaths.Count);
        await AssertNothingWrittenOrSentAsync(qBittorrentBefore: 0, cancellationToken);
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>Alien (1979), IMDb tt0078748, in Фільми as two versions — the theatrical cut and the director's cut —,
    /// each one file on the disk, inspected.</summary>
    private async Task<MediaWorkEntity> PlantAlienAsync(CancellationToken cancellationToken)
    {
        var alien = await _works.CreateMovie(_jellyfin, "Alien", 1979, imdbId: AlienImdbId);
        await _works.AddVersion(_jellyfin, alien, NewItemId(), _films, "Theatrical Cut", TheatricalPath);
        await _works.AddVersion(_jellyfin, alien, NewItemId(), _films, "Director's Cut", DirectorsCutPath);
        _factory.Files.AddFile(TheatricalPath, TestMedia.File(inode: 1), TestMedia.Taken());
        _factory.Files.AddFile(DirectorsCutPath, TestMedia.File(inode: 2, sizeBytes: 4_000_000_000), TestMedia.Taken());
        await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);
        return alien;
    }

    /// <summary>An episode of <paramref name="series"/> at its place, by an item of its own whose version holds one file on
    /// the disk; inspected by the caller.</summary>
    private async Task<MediaWorkEntity> PlantEpisodeAsync(
        MediaWorkEntity series, LibraryLocationEntity library, int season, int number, string title, long inode)
    {
        var itemId = NewItemId();
        var episode = await _works.AddEpisodeAt(_jellyfin, series, title, season, number, itemId);
        await _works.AddVersion(_jellyfin, episode, itemId, library, null, EpisodePathOf(season, number));
        _factory.Files.AddFile(EpisodePathOf(season, number), TestMedia.File(inode), TestMedia.Taken());
        return episode;
    }

    /// <summary>One Alien (1979) in each of two libraries over Фільми's folder — Living room's Фільми and a second
    /// Jellyfin's Кіно —, neither with an id, both listing the theatrical cut's one file, inspected.</summary>
    private async Task<(MediaWorkEntity Living, MediaWorkEntity Other)> PlantAlienInTwoLibrariesAsync(CancellationToken cancellationToken)
    {
        var bedroom = await new IntegrationConnectionSeeder(_factory.Scope).Create("jellyfin", "Bedroom", "http://127.0.0.2:9", apiKey: JellyfinApiKey);
        var cinema = await _libraries.Create(bedroom, "Кіно", FilmsFolder);
        var living = await _works.CreateMovie(_jellyfin, "Alien", 1979);
        await _works.AddVersion(_jellyfin, living, NewItemId(), _films, null, TheatricalPath);
        var other = await _works.CreateMovie(bedroom, "Alien", 1979);
        await _works.AddVersion(bedroom, other, NewItemId(), cinema, null, TheatricalPath);
        _factory.Files.AddFile(TheatricalPath, TestMedia.File(inode: 1), TestMedia.Taken());
        await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);
        await _replacement.SwitchOn(FilmsFolder);
        return (living, other);
    }

    /// <summary>Two Halloween (2018) in Фільми, each matched by title and year by a release with no id: one with an IMDb
    /// id, one left without (the duplicate MAM-20 finds), each one file of its own, inspected.</summary>
    private async Task<(MediaWorkEntity Identified, MediaWorkEntity Unidentified)> PlantTwoHalloweensAsync(CancellationToken cancellationToken)
    {
        var identified = await _works.CreateMovie(_jellyfin, "Halloween", 2018, imdbId: "tt1502407");
        await _works.AddVersion(_jellyfin, identified, NewItemId(), _films, null, HalloweenPath);
        var unidentified = await _works.CreateMovie(_jellyfin, "Halloween", 2018);
        await _works.AddVersion(_jellyfin, unidentified, NewItemId(), _films, null, OtherHalloweenPath);
        _factory.Files.AddFile(HalloweenPath, TestMedia.File(inode: 21), TestMedia.Taken());
        _factory.Files.AddFile(OtherHalloweenPath, TestMedia.File(inode: 22), TestMedia.Taken());
        await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);
        await _replacement.SwitchOn(FilmsFolder);
        return (identified, unidentified);
    }

    /// <summary>What the Download dialog's read offers of <paramref name="release"/> through Movies.</summary>
    private async Task<ReplacementOptionsResponse> OfferedAsync(ReleaseEntity release, CancellationToken cancellationToken)
    {
        using var response = await _factory.GetHttpRequest($"{BaseUrl}/options?releaseId={release.Id}&destinationId={_movies.Id}", _owner.AccessToken);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ReplacementOptionsResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
    }

    /// <summary>The one id the dialog lists a file two works list by, under both.</summary>
    private async Task<Guid> SharedCopyIdAsync(ReleaseEntity release, CancellationToken cancellationToken) =>
        (await OfferedAsync(release, cancellationToken)).Works.Select(work => Assert.Single(work.Copies).File.Id).Distinct().Single();

    private static string EpisodePathOf(int season, int number) =>
        $"{ShowsFolder}/Yellowstone/Season {season}/Yellowstone S{season:00}E{number:00}.mkv";

    /// <summary>The owner's search for Alien (1979) found a release of it with its IMDb id.</summary>
    private Task<ReleaseEntity> FindAlienAsync() =>
        _releases.CreateFoundBySearch(_owner.User, _toloka, "Alien", 1979, AlienRelease, imdbId: AlienImdbId);

    private Task<HttpResponseMessage> ScheduleAsync(
        ReleaseEntity release,
        IReadOnlyList<Guid> fileIds,
        IReadOnlyList<Guid>? confirmedWorkIds = null,
        Guid? requestId = null,
        DestinationMappingEntity? destination = null) =>
        _factory.PostHttpRequest(
            BaseUrl,
            _owner.AccessToken,
            new ScheduleReplacementRequest(requestId ?? Guid.NewGuid(), release.Id, _client.Id, (destination ?? _movies).Id, fileIds, confirmedWorkIds ?? []));

    private static async Task<DownloadJobDto> ReadJobAsync(HttpResponseMessage? response, CancellationToken cancellationToken) =>
        (await response!.Content.ReadFromJsonAsync<DownloadJobDto>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;

    /// <summary>The id the dialog lists a copy by: its file row's, one listing at its path.</summary>
    private async Task<Guid> FileIdAsync(string servicePath)
    {
        var id = Guid.Empty;
        await _factory.WithDbAsync(async db => id = await db.PhysicalFiles.Where(file => file.ServicePath == servicePath).Select(file => file.Id).SingleAsync());
        return id;
    }

    private async Task<Guid> VersionIdAsync(string servicePath)
    {
        var id = Guid.Empty;
        await _factory.WithDbAsync(async db => id = await db.PhysicalFiles.Where(file => file.ServicePath == servicePath).Select(file => file.MediaVersionId!.Value).SingleAsync());
        return id;
    }

    /// <summary>The library item a file's version came from.</summary>
    private async Task<string> ItemIdAsync(string servicePath)
    {
        var itemId = "";
        await _factory.WithDbAsync(async db => itemId = await db.PhysicalFiles
            .Where(file => file.ServicePath == servicePath)
            .Join(db.MediaVersions, file => file.MediaVersionId, version => version.Id, (_, version) => version.SourceItemId!)
            .SingleAsync());
        return itemId;
    }

    /// <summary>MAM-26 — the id of the one replacement written, which its line on the job carries.</summary>
    private async Task<Guid> ReplacementIdAsync(CancellationToken cancellationToken)
    {
        var id = Guid.Empty;
        await _factory.WithDbAsync(async db => id = await db.ReplacementJobs.Select(replacement => replacement.Id).SingleAsync(cancellationToken));
        return id;
    }

    /// <summary>What the database holds at a moment, read through a context of its own, and how many requests qBittorrent
    /// has had.</summary>
    private async Task<(int QBittorrentRequests, int Jobs, int Replacements, int Files, int Works, int Identifiers, int AuditRows)> CountsAsync(
        CancellationToken cancellationToken)
    {
        (int, int, int, int, int, int, int) counts = default;
        await _factory.WithDbAsync(async db => counts = (
            _factory.QBittorrentHttp.Requests.Count,
            await db.DownloadJobs.CountAsync(cancellationToken),
            await db.ReplacementJobs.CountAsync(cancellationToken),
            await db.ReplacementJobFiles.CountAsync(cancellationToken),
            await db.ReplacementJobWorks.CountAsync(cancellationToken),
            await db.ReplacementJobWorkIdentifiers.CountAsync(cancellationToken),
            await db.AuditEvents.CountAsync(cancellationToken)));
        return counts;
    }

    /// <summary>No job, replacement or audit row, nothing fetched, and no request to qBittorrent past
    /// <paramref name="qBittorrentBefore"/>.</summary>
    private async Task AssertNothingWrittenOrSentAsync(int qBittorrentBefore, CancellationToken cancellationToken)
    {
        await _factory.WithDbAsync(async db =>
        {
            Assert.Equal(0, await db.DownloadJobs.CountAsync(cancellationToken));
            Assert.Equal(0, await db.ReplacementJobs.CountAsync(cancellationToken));
            Assert.Equal(0, await db.ReplacementJobFiles.CountAsync(cancellationToken));
            Assert.Equal(0, await db.ReplacementJobWorks.CountAsync(cancellationToken));
            Assert.Equal(0, await db.AuditEvents.CountAsync(cancellationToken));
        });
        Assert.Empty(_factory.TorznabHttp.Requests);
        Assert.Equal(qBittorrentBefore, _factory.QBittorrentHttp.Requests.Count);
    }

    private string[] QBittorrentCalls() =>
        [.. _factory.QBittorrentHttp.Requests.Select(request => $"{request.Method} {request.Uri.PathAndQuery}")];

    /// <summary>Every request to qBittorrent is a GET, the sign-in, or one of <paramref name="adds"/> submissions — never
    /// a stop, a pause or a removal.</summary>
    private void AssertOnlyReadsAnd(int adds)
    {
        var writes = QBittorrentCalls().Where(call => !call.StartsWith("GET ", StringComparison.Ordinal) && call != "POST /api/v2/auth/login");
        Assert.Equal(Enumerable.Repeat("POST /api/v2/torrents/add", adds), writes);
    }

    private static string NewItemId() => Guid.NewGuid().ToString("N");
}
