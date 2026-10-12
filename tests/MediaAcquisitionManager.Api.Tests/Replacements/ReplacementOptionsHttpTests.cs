using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Abstractions.Files;
using MediaAcquisitionManager.Core.Abstractions.Metadata;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Paths;
using MediaAcquisitionManager.Infrastructure.Persistence;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Data;
using MediaAcquisitionManager.TestInfrastructure.Files;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MediaAcquisitionManager.Api.Tests.Replacements;

// MAM-24, ADR 0031–0033 — the Download dialog's question, GET /api/replacements/options, what of a release the owner may
// replace through a destination: the release-level reasons in their order (switched off and a magnet asking no client at
// all; a destination its client can't place, outside every library or outside the Media folders, which asks it for its
// categories and nothing more; no matched work), then a film's every file of every version in the Library's order with its
// edition words and measured dynamic range, the work-level reasons (title alone, conflicting ids) and the title-and-year
// confirmation, and each copy's reason in its order — no path in the app, its folder not ticked (nested folders: every one
// that holds it), held by a replacement not yet ended, not inspected, then the disk's answer now: missing, unreadable,
// changed. One file two libraries report is one copy under both works, the strictest reason of either; two works of one
// title and year in one library confirm in words of their own. Copies are read
// against files planted in the fake inventory and inspected by a real inspection run; the release by the owner's own
// search. The torrents' held marks are ReplacementOptionsHoldingsHttpTests', a TV release's episodes
// ReplacementOptionsEpisodesHttpTests'; every member's 403 the owner-only sweep's.
public sealed class ReplacementOptionsHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/replacements/options";
    private const string ApiKey = "0123456789abcdef0123456789abcdef";

    private const string AlienImdbId = "tt0078748";
    private const string AlienRelease = "Alien (1979) Director's Cut BDRemux 2160p HDR";
    private const string FilmsFolder = "/media/movies";
    private const string TheatricalPath = "/media/movies/Alien (1979)/Alien (1979) Theatrical Cut.mkv";
    private const string DirectorsCutPath = "/media/movies/Alien (1979)/Alien (1979) Director's Cut.mkv";

    // Sentences the dialog shows, spelled out rather than read from product code.
    private const string ReleaseMissingError = "This release isn't available any more: a search keeps what it found for a day. Search again.";
    private const string DestinationGoneError = "The destination you picked no longer exists. Reload the page and pick one again.";
    private const string ReleaseIdMissingError = "Pick the release to send.";
    private const string DestinationIdMissingError = "Pick the destination to send it to.";
    private const string HoldingsInvalidError = "Say true or false for whether the download clients' torrents are read.";
    private const string OwnerOnlyBody = """{"error":"Only the installation owner can do this."}""";
    private const string FolderUncheckedProblem =
        "The destination's folder lies outside the Media folders or has no path in the app, so the app can't check it for files already there, and a replacement is sent only after that check.";

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
    private FakeQBittorrentServer _qbittorrent = null!;
    private DestinationMappingEntity _movies = null!;
    private LibraryLocationEntity _films = null!;

    public ReplacementOptionsHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        var users = new UserSeeder(_factory.Scope);
        _owner = await users.CreateAuthorizedOwner("owner");
        _member = await users.CreateAuthorized("member");
        var integrations = new IntegrationConnectionSeeder(_factory.Scope);
        // The library with no path mapping: the app finds each file where Jellyfin says it is.
        _jellyfin = await integrations.Create("jellyfin", "Living room", apiKey: ApiKey);
        _toloka = await integrations.Create("torznab", "Toloka", apiKey: ApiKey);
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

    // ── release-level reasons ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Options_with_replacement_switched_off_is_SwitchedOff_and_reads_no_client_no_work_and_no_disk()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await PlantAlienAsync(cancellationToken);
        var release = await FindAlienAsync();
        var described = _factory.Files.DescribedPaths.Count;

        //Act
        var response = await OptionsAsync(release, _movies, holdings: true);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // The wire shape the dialog reads, names and all.
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        Assert.Equal(
            ["unavailable", "destinationProblem", "releaseEditions", "works", "clients"],
            document.RootElement.EnumerateObject().Select(property => property.Name));
        Assert.Equal("SwitchedOff", document.RootElement.GetProperty("unavailable").GetString());
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("destinationProblem").ValueKind);
        Assert.Equal(["DirectorsCut"], document.RootElement.GetProperty("releaseEditions").EnumerateArray().Select(edition => edition.GetString()));
        Assert.Equal(0, document.RootElement.GetProperty("works").GetArrayLength());
        Assert.Equal(0, document.RootElement.GetProperty("clients").GetArrayLength());
        // ADR 0033 — off, nothing is read: no client asked, and no file looked at.
        Assert.Empty(_factory.QBittorrentHttp.Requests);
        Assert.Equal(described, _factory.Files.DescribedPaths.Count);
    }

    [Fact]
    public async Task Options_for_a_magnet_link_is_Magnet_and_asks_no_client()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await PlantAlienAsync(cancellationToken);
        await _replacement.SwitchOn(FilmsFolder);
        var release = await _releases.CreateFoundBySearch(
            _owner.User, _toloka, "Alien", 1979, AlienRelease, imdbId: AlienImdbId, payloadKind: ReleasePayloadKind.Magnet);

        //Act
        var response = await OptionsAsync(release, _movies, holdings: true);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var options = await ReadAsync(response, cancellationToken);
        Assert.Equal((ReplacementUnavailableReason?)ReplacementUnavailableReason.Magnet, options.Unavailable);
        Assert.Null(options.DestinationProblem);
        Assert.Empty(options.Works);
        Assert.Empty(_factory.QBittorrentHttp.Requests);
    }

    [Fact]
    public async Task Options_for_a_destination_outside_every_library_is_DestinationNotUsable_and_reads_only_the_categories()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await PlantAlienAsync(cancellationToken);
        await _replacement.SwitchOn(FilmsFolder);
        var release = await FindAlienAsync();
        // Cartoons saves into /media/cartoons, which no library holds.
        var cartoons = await new DestinationMappingSeeder(_factory.Scope).Create(_client, "Cartoons", "Cartoons");
        var described = _factory.Files.DescribedPaths.Count;

        //Act
        var response = await OptionsAsync(release, cartoons, holdings: true);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var options = await ReadAsync(response, cancellationToken);
        Assert.Equal((ReplacementUnavailableReason?)ReplacementUnavailableReason.DestinationNotUsable, options.Unavailable);
        Assert.Equal(
            "“/media/cartoons” is outside every media library folder, so automatic replacement stays off for this destination.",
            options.DestinationProblem);
        Assert.Empty(options.Works);
        Assert.Empty(options.Clients);
        // The category's folder was asked for, and nothing else: no torrent listing, no file looked at.
        Assert.Contains("GET /api/v2/torrents/categories", QBittorrentCalls());
        Assert.DoesNotContain(QBittorrentCalls(), call => call.StartsWith("GET /api/v2/torrents/info", StringComparison.Ordinal));
        AssertOnlyReads();
        Assert.Equal(described, _factory.Files.DescribedPaths.Count);
    }

    [Fact]
    public async Task Options_for_a_destination_in_a_library_outside_the_media_folders_is_DestinationNotUsable()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await PlantAlienAsync(cancellationToken);
        await _replacement.SwitchOn(FilmsFolder);
        var release = await FindAlienAsync();
        // A library at /srv/archive and a category saving there: inside a library, but outside the Media folders, where
        // the app can't look for files already there (ADR 0034).
        await _libraries.Create(_jellyfin, "Архів", "/srv/archive");
        var client = await new IntegrationConnectionSeeder(_factory.Scope).Create(
            "qbittorrent",
            "qBittorrent archive",
            _qbittorrent.BaseUrl,
            userName: FakeQBittorrentServer.DefaultUserName,
            password: FakeQBittorrentServer.DefaultPassword,
            pathMappings: [new PathMappingPair("/downloads", "/srv/archive")]);
        var archive = await new DestinationMappingSeeder(_factory.Scope).Create(client, "Archive", "Movies");

        //Act
        var response = await OptionsAsync(release, archive, holdings: true);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var options = await ReadAsync(response, cancellationToken);
        Assert.Equal(
            ((ReplacementUnavailableReason?)ReplacementUnavailableReason.DestinationNotUsable, FolderUncheckedProblem),
            (options.Unavailable, options.DestinationProblem));
        Assert.Empty(options.Works);
        Assert.DoesNotContain(QBittorrentCalls(), call => call.StartsWith("GET /api/v2/torrents/info", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Options_when_the_client_can_not_list_its_categories_is_DestinationNotUsable_naming_the_client()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await PlantAlienAsync(cancellationToken);
        await _replacement.SwitchOn(FilmsFolder);
        var release = await FindAlienAsync();
        _qbittorrent.Intercept = request => request.RequestUri!.AbsolutePath == "/api/v2/torrents/categories"
            ? VendorResponses.Text("boom", HttpStatusCode.InternalServerError)
            : null;

        //Act
        var response = await OptionsAsync(release, _movies);

        //Assert
        // A question the client can't answer is an answer to the dialog, never a 5xx.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var options = await ReadAsync(response, cancellationToken);
        Assert.Equal((ReplacementUnavailableReason?)ReplacementUnavailableReason.DestinationNotUsable, options.Unavailable);
        Assert.StartsWith("The app couldn't ask “qBittorrent” where this destination saves: ", options.DestinationProblem, StringComparison.Ordinal);
        Assert.Empty(options.Works);
    }

    [Fact]
    public async Task Options_for_a_category_the_client_no_longer_has_is_DestinationNotUsable()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await PlantAlienAsync(cancellationToken);
        await _replacement.SwitchOn(FilmsFolder);
        var release = await FindAlienAsync();
        var gone = await new DestinationMappingSeeder(_factory.Scope).Create(_client, "Old films", "Old films");

        //Act
        var response = await OptionsAsync(release, gone);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var options = await ReadAsync(response, cancellationToken);
        Assert.Equal(
            ((ReplacementUnavailableReason?)ReplacementUnavailableReason.DestinationNotUsable,
                "The app couldn't ask “qBittorrent” where this destination saves: it has no category “Old films” any more."),
            (options.Unavailable, options.DestinationProblem));
    }

    [Fact]
    public async Task Options_for_a_release_that_matches_no_work_is_NoMatchedWork()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await PlantAlienAsync(cancellationToken);
        await _replacement.SwitchOn(FilmsFolder);
        var release = await _releases.CreateFoundBySearch(_owner.User, _toloka, "Interstellar", 2014, "Interstellar (2014) BDRip 1080p");

        //Act
        var response = await OptionsAsync(release, _movies, holdings: true);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var options = await ReadAsync(response, cancellationToken);
        Assert.Equal((ReplacementUnavailableReason?)ReplacementUnavailableReason.NoMatchedWork, options.Unavailable);
        Assert.Null(options.DestinationProblem);
        Assert.Empty(options.Works);
        Assert.DoesNotContain(QBittorrentCalls(), call => call.StartsWith("GET /api/v2/torrents/info", StringComparison.Ordinal));
    }

    // ── works and copies ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Options_for_a_film_matched_by_its_id_lists_every_file_of_every_version_in_the_library_order()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var alien = await PlantAlienAsync(cancellationToken);
        await _replacement.SwitchOn(FilmsFolder);
        var release = await FindAlienAsync();
        var described = _factory.Files.DescribedPaths.Count;

        //Act
        var response = await OptionsAsync(release, _movies);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var options = await ReadAsync(response, cancellationToken);
        Assert.Null(options.Unavailable);
        Assert.Equal([MediaEdition.DirectorsCut], options.ReleaseEditions);
        Assert.Empty(options.Clients);
        var work = Assert.Single(options.Works);
        Assert.Equal(
            (alien.Id, MediaWorkKind.Movie, "Alien", (int?)1979, MatchEvidence.Identifier, false, "This release is Alien (1979)", (ReplacementWorkUnavailableReason?)null),
            (work.WorkId, work.Kind, work.Title, work.Year, work.Evidence, work.NeedsConfirmation, work.ConfirmationText, work.Unavailable));
        Assert.Empty(work.ConflictingSchemes);
        Assert.Equal(["Фільми"], work.LibraryNames);
        Assert.Empty(work.Episodes);
        // The Library's version order — by edition label here — and the version's own label and words; ffprobe's SDR.
        Assert.Equal(
            [
                (DirectorsCutPath, "Director's Cut", "DirectorsCut", "Sdr"),
                (TheatricalPath, "Theatrical Cut", "Theatrical", "Sdr"),
            ],
            work.Copies.Select(copy => (
                copy.File.AppPath,
                copy.EditionLabel,
                string.Join(",", copy.Editions),
                string.Join(",", copy.DynamicRanges))));
        Assert.All(work.Copies, copy => Assert.Equal(
            ((ReplacementCopyUnavailableReason?)null, false, 0, PathMapStatus.Identity, "Фільми"),
            (copy.Unavailable, copy.OtherNames, copy.Holds.Count, copy.File.PathStatus, copy.File.LocationName)));
        // Each copy looked at on the disk once, as it is now; no torrent read without the dialog asking.
        Assert.Equal(2, _factory.Files.DescribedPaths.Count - described);
        Assert.DoesNotContain(QBittorrentCalls(), call => call.StartsWith("GET /api/v2/torrents/info", StringComparison.Ordinal));
        AssertOnlyReads();
        await _factory.WithDbAsync(async db =>
        {
            Assert.Equal(0, await db.ReplacementJobs.CountAsync(cancellationToken));
            Assert.Equal(0, await db.DownloadJobs.CountAsync(cancellationToken));
            Assert.Equal(0, await db.AuditEvents.CountAsync(cancellationToken));
        });
    }

    [Fact]
    public async Task Options_for_a_film_matched_by_title_and_year_needs_the_owners_confirmation()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await PlantAlienAsync(cancellationToken);
        await _replacement.SwitchOn(FilmsFolder);
        // Toloka lists no ids: its releases match by title and year at best.
        var release = await _releases.CreateFoundBySearch(_owner.User, _toloka, "Alien", 1979, "Alien (1979) BDRip 1080p");

        //Act
        var response = await OptionsAsync(release, _movies);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var work = Assert.Single((await ReadAsync(response, cancellationToken)).Works);
        Assert.Equal(
            (MatchEvidence.TitleAndYear, true, "This release is Alien (1979)", (ReplacementWorkUnavailableReason?)null),
            (work.Evidence, work.NeedsConfirmation, work.ConfirmationText, work.Unavailable));
        Assert.All(work.Copies, copy => Assert.Null(copy.Unavailable));
    }

    [Fact]
    public async Task Options_for_a_film_matched_by_its_title_alone_can_not_be_replaced_and_reads_none_of_its_copies()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // The library gives the film no year: the release's title and year match its title alone.
        var alien = await _works.CreateMovie(_jellyfin, "Alien", null);
        await _works.AddVersion(_jellyfin, alien, NewItemId(), _films, null, TheatricalPath);
        _factory.Files.AddFile(TheatricalPath, TestMedia.File(inode: 1), TestMedia.Taken());
        await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);
        await _replacement.SwitchOn(FilmsFolder);
        var release = await _releases.CreateFoundBySearch(_owner.User, _toloka, "Alien", 1979, "Alien (1979) BDRip 1080p");
        var described = _factory.Files.DescribedPaths.Count;

        //Act
        var response = await OptionsAsync(release, _movies, holdings: true);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var options = await ReadAsync(response, cancellationToken);
        Assert.Null(options.Unavailable);
        var work = Assert.Single(options.Works);
        Assert.Equal(
            (MatchEvidence.TitleOnly, false, (ReplacementWorkUnavailableReason?)ReplacementWorkUnavailableReason.TitleOnly, "This release is Alien"),
            (work.Evidence, work.NeedsConfirmation, work.Unavailable, work.ConfirmationText));
        // Listed, so the owner sees why; never looked at on the disk, and no torrent read for a release that can replace
        // nothing.
        Assert.Equal([TheatricalPath], work.Copies.Select(copy => copy.File.AppPath));
        Assert.Equal(described, _factory.Files.DescribedPaths.Count);
        Assert.Empty(options.Clients);
        Assert.DoesNotContain(QBittorrentCalls(), call => call.StartsWith("GET /api/v2/torrents/info", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Options_for_a_film_whose_ids_conflict_with_the_release_can_not_be_replaced()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var alien = await _works.CreateMovie(_jellyfin, "Alien", 1979, imdbId: "tt9999999");
        await _works.AddVersion(_jellyfin, alien, NewItemId(), _films, null, TheatricalPath);
        await _replacement.SwitchOn(FilmsFolder);
        var release = await FindAlienAsync();

        //Act
        var response = await OptionsAsync(release, _movies);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var work = Assert.Single((await ReadAsync(response, cancellationToken)).Works);
        Assert.Equal(
            (MatchEvidence.TitleAndYear, false, (ReplacementWorkUnavailableReason?)ReplacementWorkUnavailableReason.ConflictingIds),
            (work.Evidence, work.NeedsConfirmation, work.Unavailable));
        Assert.Equal([MediaIdentifierScheme.Imdb], work.ConflictingSchemes);
    }

    [Fact]
    public async Task Options_gives_each_copy_the_first_reason_that_keeps_it_from_being_ticked()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        const string tickable = "/media/movies/Alien (1979)/a tickable.mkv";
        const string outside = "/srv/films/Alien (1979)/b outside the media folders.mkv";
        const string unticked = "/media/cartoons/Alien (1979)/c unticked.mkv";
        const string gone = "/media/movies/Alien (1979)/d gone.mkv";
        const string locked = "/media/movies/Alien (1979)/e locked.mkv";
        const string edited = "/media/movies/Alien (1979)/f edited.mkv";
        const string uninspected = "/media/movies/Alien (1979)/g not inspected.mkv";
        const string restored = "/media/movies/Alien (1979)/h restored.mkv";
        const string moved = "/media/movies/Alien (1979)/i moved.mkv";
        var cartoons = await _libraries.Create(_jellyfin, "Мультфільми", "/media/cartoons");
        var alien = await _works.CreateMovie(_jellyfin, "Alien", 1979, imdbId: AlienImdbId);
        // One version holding every file, so the Library's order is the paths'.
        await _works.AddVersion(_jellyfin, alien, NewItemId(), _films, null, tickable, outside, unticked, gone, locked, edited, restored, moved);
        var inode = 0;
        foreach (var path in new[] { tickable, outside, unticked, gone, locked, edited, restored, moved })
            _factory.Files.AddFile(path, TestMedia.File(++inode), TestMedia.Taken());
        await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);
        // Since the inspection: one file gone, one the disk can't read now, one written over, and one more name the
        // library reported that no inspection has read yet. And two whose size and modification time are the inspection's
        // but which are other files (decision 10): one restored with cp -p, a new inode, and one brought back from another
        // disk, a new device.
        _factory.Files.Remove(gone);
        _factory.Files.AddProblem(locked, FileInventoryStatus.Unreadable, "Permission denied.");
        _factory.Files.AddFile(edited, TestMedia.File(inode: 6, modifiedUtc: TestMedia.ModifiedUtc.AddMinutes(5)), TestMedia.Taken());
        _factory.Files.AddFile(restored, TestMedia.File(inode: 70), TestMedia.Taken());
        _factory.Files.AddFile(moved, TestMedia.File(inode: 8, deviceId: TestMedia.DeviceId + 1), TestMedia.Taken());
        await _works.AddVersion(_jellyfin, alien, NewItemId(), _films, "Unread", uninspected);
        _factory.Files.AddFile(uninspected, TestMedia.File(inode: 7), TestMedia.Taken());
        await _replacement.SwitchOn(FilmsFolder);
        var release = await FindAlienAsync();
        var described = _factory.Files.DescribedPaths.Count;

        //Act
        var response = await OptionsAsync(release, _movies);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var work = Assert.Single((await ReadAsync(response, cancellationToken)).Works);
        Assert.Null(work.Unavailable);
        Assert.Equal(
            new Dictionary<string, ReplacementCopyUnavailableReason?>
            {
                [tickable] = null,
                [outside] = ReplacementCopyUnavailableReason.NoAppPath,
                [unticked] = ReplacementCopyUnavailableReason.FolderNotTicked,
                [gone] = ReplacementCopyUnavailableReason.Missing,
                [locked] = ReplacementCopyUnavailableReason.Unreadable,
                [edited] = ReplacementCopyUnavailableReason.ChangedSinceInspection,
                [uninspected] = ReplacementCopyUnavailableReason.NotInspected,
                [restored] = ReplacementCopyUnavailableReason.ChangedSinceInspection,
                [moved] = ReplacementCopyUnavailableReason.ChangedSinceInspection,
            },
            work.Copies.ToDictionary(copy => copy.File.AppPath!, copy => copy.Unavailable));
        // The disk only for the six that passed every stored check.
        Assert.Equal(
            [tickable, gone, locked, edited, restored, moved],
            _factory.Files.DescribedPaths.Skip(described).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Options_keeps_a_copy_whose_media_reading_failed_tickable_with_no_dynamic_range()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await PlantAlienAsync(cancellationToken);
        await _replacement.SwitchOn(FilmsFolder);
        var release = await FindAlienAsync();
        // Inspected, its identity read, but the reading it points at is a cached failure of the media inspector.
        await _factory.WithDbAsync(async db =>
        {
            var file = await db.PhysicalFiles.AsNoTracking().SingleAsync(row => row.ServicePath == TheatricalPath, cancellationToken);
            var reading = await new MediaInspectionDataFactory(db, _factory.Time)
                .Create(TestMedia.DeviceId, 99, 1, TestMedia.ModifiedUtc, error: "ffprobe couldn't read it.");
            await new PhysicalFileDataFactory(db, _factory.Time).Link(file.Id, reading.Id);
        });

        //Act
        var response = await OptionsAsync(release, _movies);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var copy = Assert.Single(Assert.Single((await ReadAsync(response, cancellationToken)).Works).Copies, copy => copy.File.AppPath == TheatricalPath);
        Assert.Null(copy.Unavailable);
        Assert.Empty(copy.DynamicRanges);
    }

    [Theory]
    [InlineData(ReplacementJobState.Scheduled, true)]
    [InlineData(ReplacementJobState.Submitted, true)]
    [InlineData(ReplacementJobState.Downloading, true)]
    [InlineData(ReplacementJobState.Paused, true)]
    // A finished download's replacement waits here for MAM-25's check, and still holds its copies.
    [InlineData(ReplacementJobState.Downloaded, true)]
    // MAM-25: being checked, and verified, it still holds them: they move only in MAM-26.
    [InlineData(ReplacementJobState.Inspecting, true)]
    [InlineData(ReplacementJobState.Verified, true)]
    [InlineData(ReplacementJobState.NeedsReview, true)]
    [InlineData(ReplacementJobState.Failed, false)]
    [InlineData(ReplacementJobState.Cancelled, false)]
    public async Task Options_marks_a_copy_a_replacement_not_yet_ended_holds_and_not_one_an_ended_replacement_held(ReplacementJobState state, bool holds)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await PlantAlienAsync(cancellationToken);
        await _replacement.SwitchOn(FilmsFolder);
        var release = await FindAlienAsync();
        // Another download scheduled to replace the theatrical cut, now in that state.
        await PlantReplacementAsync(release, state, TheatricalPath, TestMedia.File(inode: 1));

        //Act
        var response = await OptionsAsync(release, _movies);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var work = Assert.Single((await ReadAsync(response, cancellationToken)).Works);
        Assert.Equal(
            [
                (DirectorsCutPath, (ReplacementCopyUnavailableReason?)null),
                (TheatricalPath, holds ? ReplacementCopyUnavailableReason.AlreadyScheduled : null),
            ],
            work.Copies.Select(copy => (copy.File.AppPath!, copy.Unavailable)));
    }

    [Theory]
    [InlineData(CopyRemovalState.Moving, true)]
    [InlineData(CopyRemovalState.Quarantined, true)]
    [InlineData(CopyRemovalState.NeedsReview, true)]
    [InlineData(CopyRemovalState.Restored, false)]
    [InlineData(CopyRemovalState.NotMoved, false)]
    public async Task Options_marks_a_copy_an_open_removal_holds_and_not_one_an_ended_removal_held(CopyRemovalState state, bool holds)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await PlantAlienAsync(cancellationToken);
        await _replacement.SwitchOn(FilmsFolder);
        var release = await FindAlienAsync();
        // MAM-40 — the owner removed the theatrical cut from its duplicate group, the library not synced since: a removal still
        // open holds it — the send would find it in the quarantine —, under its own reason, as no download replaces it.
        await _factory.WithDbAsync(async db =>
        {
            var removals = new CopyRemovalDataFactory(db, _factory.Time);
            var removal = await removals.Create(_owner.User.Id, state, TheatricalPath, TheatricalPath);
            await removals.AddName(
                removal.Id, 0, ReplacementNameKind.Video, TheatricalPath, $"/media/.mam-quarantine/{removal.Id}/movies/Alien (1979)/Alien (1979) Theatrical Cut.mkv", "/media",
                TestMedia.File(inode: 1), ReplacementNameState.Quarantined);
        });

        //Act
        var response = await OptionsAsync(release, _movies);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var work = Assert.Single((await ReadAsync(response, cancellationToken)).Works);
        Assert.Equal(
            [
                (DirectorsCutPath, (ReplacementCopyUnavailableReason?)null),
                (TheatricalPath, holds ? ReplacementCopyUnavailableReason.HeldByRemoval : null),
            ],
            work.Copies.Select(copy => (copy.File.AppPath!, copy.Unavailable)));
    }

    [Fact]
    public async Task Options_marks_a_copy_a_replacement_holds_under_another_path_by_its_identity()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await PlantAlienAsync(cancellationToken);
        await _replacement.SwitchOn(FilmsFolder);
        var release = await FindAlienAsync();
        // The theatrical cut was scheduled under its old name; the file was renamed since, and Jellyfin reports it at its
        // new path: the same device and inode.
        await PlantReplacementAsync(release, ReplacementJobState.Downloading, "/media/movies/Alien (1979)/Alien old name.mkv", TestMedia.File(inode: 1));

        //Act
        var response = await OptionsAsync(release, _movies);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var work = Assert.Single((await ReadAsync(response, cancellationToken)).Works);
        Assert.Equal(
            [
                (DirectorsCutPath, (ReplacementCopyUnavailableReason?)null),
                (TheatricalPath, ReplacementCopyUnavailableReason.AlreadyScheduled),
            ],
            work.Copies.Select(copy => (copy.File.AppPath!, copy.Unavailable)));
    }

    [Fact]
    public async Task Options_keeps_a_copy_in_an_unticked_library_nested_in_a_ticked_one_out_of_reach()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        const string kidsPath = "/media/movies/kids/Alien (1979)/Alien.mkv";
        await _libraries.Create(_jellyfin, "Дитячі", "/media/movies/kids");
        var alien = await _works.CreateMovie(_jellyfin, "Alien", 1979, imdbId: AlienImdbId);
        await _works.AddVersion(_jellyfin, alien, NewItemId(), _films, null, kidsPath, TheatricalPath);
        _factory.Files.AddFile(kidsPath, TestMedia.File(inode: 1), TestMedia.Taken());
        _factory.Files.AddFile(TheatricalPath, TestMedia.File(inode: 2), TestMedia.Taken());
        await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);
        // /media/movies ticked, the library inside it not: its own folder decides (R3).
        await _replacement.SwitchOn(FilmsFolder);
        var release = await FindAlienAsync();

        //Act
        var response = await OptionsAsync(release, _movies);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var work = Assert.Single((await ReadAsync(response, cancellationToken)).Works);
        Assert.Equal(
            [
                (TheatricalPath, (ReplacementCopyUnavailableReason?)null),
                (kidsPath, ReplacementCopyUnavailableReason.FolderNotTicked),
            ],
            work.Copies.Select(copy => (copy.File.AppPath!, copy.Unavailable)).OrderBy(copy => copy.Item2 is not null));
    }

    [Fact]
    public async Task Options_lets_a_copy_in_a_nested_library_be_ticked_once_both_folders_are_ticked()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        const string kidsPath = "/media/movies/kids/Alien (1979)/Alien.mkv";
        await _libraries.Create(_jellyfin, "Дитячі", "/media/movies/kids");
        var alien = await _works.CreateMovie(_jellyfin, "Alien", 1979, imdbId: AlienImdbId);
        await _works.AddVersion(_jellyfin, alien, NewItemId(), _films, null, kidsPath);
        _factory.Files.AddFile(kidsPath, TestMedia.File(inode: 1), TestMedia.Taken());
        await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);
        // Every library folder that holds the copy ticked: /media/movies and the library inside it.
        await _replacement.SwitchOn(FilmsFolder, "/media/movies/kids");
        var release = await FindAlienAsync();

        //Act
        var response = await OptionsAsync(release, _movies);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var copy = Assert.Single(Assert.Single((await ReadAsync(response, cancellationToken)).Works).Copies);
        Assert.Equal((kidsPath, (ReplacementCopyUnavailableReason?)null), (copy.File.AppPath, copy.Unavailable));
    }

    [Fact]
    public async Task Options_keeps_every_copy_out_of_reach_when_only_the_nested_library_is_ticked()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        const string kidsPath = "/media/movies/kids/Alien (1979)/Alien.mkv";
        await _libraries.Create(_jellyfin, "Дитячі", "/media/movies/kids");
        var alien = await _works.CreateMovie(_jellyfin, "Alien", 1979, imdbId: AlienImdbId);
        await _works.AddVersion(_jellyfin, alien, NewItemId(), _films, null, kidsPath, TheatricalPath);
        _factory.Files.AddFile(kidsPath, TestMedia.File(inode: 1), TestMedia.Taken());
        _factory.Files.AddFile(TheatricalPath, TestMedia.File(inode: 2), TestMedia.Taken());
        await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);
        // Only the library inside Фільми ticked: the kids copy is in Фільми too, which the owner left unticked.
        await _replacement.SwitchOn("/media/movies/kids");
        var release = await FindAlienAsync();

        //Act
        var response = await OptionsAsync(release, _movies);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var work = Assert.Single((await ReadAsync(response, cancellationToken)).Works);
        Assert.Equal(
            new Dictionary<string, ReplacementCopyUnavailableReason?>
            {
                [kidsPath] = ReplacementCopyUnavailableReason.FolderNotTicked,
                [TheatricalPath] = ReplacementCopyUnavailableReason.FolderNotTicked,
            },
            work.Copies.ToDictionary(copy => copy.File.AppPath!, copy => copy.Unavailable));
    }

    [Fact]
    public async Task Options_keeps_a_copy_no_library_folder_holds_out_of_reach_whatever_tick_is_stored_for_its_folder()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // Jellyfin still reports the file at its old folder, which no library lists any more; the owner's tick of that
        // folder is stale, and counts nowhere.
        const string oldPath = "/media/old-films/Alien (1979)/Alien.mkv";
        var alien = await _works.CreateMovie(_jellyfin, "Alien", 1979, imdbId: AlienImdbId);
        await _works.AddVersion(_jellyfin, alien, NewItemId(), _films, null, oldPath);
        _factory.Files.AddFile(oldPath, TestMedia.File(inode: 1), TestMedia.Taken());
        await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);
        await _replacement.SwitchOn(FilmsFolder, "/media/old-films");
        var release = await FindAlienAsync();

        //Act
        var response = await OptionsAsync(release, _movies);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var copy = Assert.Single(Assert.Single((await ReadAsync(response, cancellationToken)).Works).Copies);
        Assert.Equal((oldPath, (ReplacementCopyUnavailableReason?)ReplacementCopyUnavailableReason.FolderNotTicked), (copy.File.AppPath, copy.Unavailable));
    }

    [Fact]
    public async Task Options_lists_one_file_two_libraries_report_as_one_copy_of_both_works_told_apart_by_their_libraries()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // A second Jellyfin over the same folder: the same film is a second work, its file a second row of one path.
        var bedroom = await new IntegrationConnectionSeeder(_factory.Scope).Create("jellyfin", "Bedroom", "http://127.0.0.2:9", apiKey: ApiKey);
        var cinema = await _libraries.Create(bedroom, "Кіно", FilmsFolder);
        var living = await _works.CreateMovie(_jellyfin, "Alien", 1979);
        await _works.AddVersion(_jellyfin, living, NewItemId(), _films, null, TheatricalPath);
        var other = await _works.CreateMovie(bedroom, "Alien", 1979);
        await _works.AddVersion(bedroom, other, NewItemId(), cinema, null, TheatricalPath);
        _factory.Files.AddFile(TheatricalPath, TestMedia.File(inode: 1), TestMedia.Taken());
        await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);
        await _replacement.SwitchOn(FilmsFolder);
        var release = await _releases.CreateFoundBySearch(_owner.User, _toloka, "Alien", 1979, "Alien (1979) BDRip 1080p");
        var described = _factory.Files.DescribedPaths.Count;

        //Act
        var response = await OptionsAsync(release, _movies);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var works = (await ReadAsync(response, cancellationToken)).Works;
        Assert.Equal(2, works.Count);
        // Two confirmations that would read alike name their libraries.
        Assert.Equal(
            ["This release is Alien (1979) in “Кіно”", "This release is Alien (1979) in “Фільми”"],
            works.Select(work => work.ConfirmationText).Order(StringComparer.Ordinal));
        // One copy under both: one id, so one tick is the file's under each work, and each work's confirmation counts.
        var copies = works.Select(work => Assert.Single(work.Copies)).ToList();
        Assert.Single(copies.Select(copy => copy.File.Id).Distinct());
        Assert.All(copies, copy => Assert.Equal((TheatricalPath, (ReplacementCopyUnavailableReason?)null), (copy.File.AppPath, copy.Unavailable)));
        // Looked at once.
        Assert.Equal(1, _factory.Files.DescribedPaths.Count - described);
    }

    [Fact]
    public async Task Options_tells_two_works_of_one_title_and_year_in_one_library_apart_by_their_copies()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var (identified, unidentified) = await PlantTwoHalloweensAsync(
            "/media/movies/Halloween (2018)/Halloween.2018.1080p.mkv", "/media/movies/Halloween/Halloween.2018.2160p.mkv", cancellationToken);
        var release = await _releases.CreateFoundBySearch(_owner.User, _toloka, "Halloween", 2018, "Halloween (2018) BDRip 1080p");

        //Act
        var response = await OptionsAsync(release, _movies);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var works = (await ReadAsync(response, cancellationToken)).Works;
        // Their library alone reads the same for both: each confirmation names the work's own copy, the words the 400 and
        // the audit row keep too.
        Assert.Equal(
            new Dictionary<Guid, string>
            {
                [identified.Id] = "This release is Halloween (2018) in “Фільми” (the copy “Halloween.2018.1080p.mkv”)",
                [unidentified.Id] = "This release is Halloween (2018) in “Фільми” (the copy “Halloween.2018.2160p.mkv”)",
            },
            works.ToDictionary(work => work.WorkId, work => work.ConfirmationText));
        Assert.All(works, work => Assert.True(work.NeedsConfirmation));
    }

    [Theory]
    [InlineData("one name")]
    [InlineData("names longer than the audit row keeps")]
    public async Task Options_tells_two_works_their_copies_names_can_not_tell_apart_by_their_place_in_the_dialog(string names)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // The same name for both; or two names either of which would push the words past the audit row's 200 characters,
        // which would cut what tells the two apart.
        var (first, second) = names == "one name"
            ? ("Halloween (2018).mkv", "Halloween (2018).mkv")
            : ($"Halloween.2018.{new string('a', 140)}.1080p.mkv", $"Halloween.2018.{new string('a', 140)}.2160p.mkv");
        await PlantTwoHalloweensAsync($"/media/movies/Halloween (2018)/{first}", $"/media/movies/Halloween/{second}", cancellationToken);
        var release = await _releases.CreateFoundBySearch(_owner.User, _toloka, "Halloween", 2018, "Halloween (2018) BDRip 1080p");

        //Act
        var response = await OptionsAsync(release, _movies);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // Their place among the two, in the order the dialog lists them.
        Assert.Equal(
            ["This release is Halloween (2018) in “Фільми” (1 of 2)", "This release is Halloween (2018) in “Фільми” (2 of 2)"],
            (await ReadAsync(response, cancellationToken)).Works.Select(work => work.ConfirmationText));
    }

    [Fact]
    public async Task Options_refuses_a_file_two_works_share_when_one_matched_by_its_title_alone()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var bedroom = await new IntegrationConnectionSeeder(_factory.Scope).Create("jellyfin", "Bedroom", "http://127.0.0.2:9", apiKey: ApiKey);
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

        //Act
        var response = await OptionsAsync(release, _movies, holdings: true);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var options = await ReadAsync(response, cancellationToken);
        Assert.Equal(
            [(MatchEvidence.Identifier, (ReplacementWorkUnavailableReason?)null), (MatchEvidence.TitleOnly, ReplacementWorkUnavailableReason.TitleOnly)],
            options.Works.Select(work => (work.Evidence, work.Unavailable)));
        Assert.Equal(
            ReplacementCopyUnavailableReason.SharedWithUnavailableWork,
            Assert.Single(options.Works[0].Copies).Unavailable);
        // Nothing can be ticked, so no torrent is read.
        Assert.Empty(options.Clients);
    }

    // ── the request ─────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("", "dest", null, ReleaseIdMissingError)]
    [InlineData("not-a-guid", "dest", null, ReleaseIdMissingError)]
    [InlineData("release", "", null, DestinationIdMissingError)]
    [InlineData("release", "dest", "maybe", HoldingsInvalidError)]
    public async Task Options_with_a_query_missing_or_malformed_is_400_with_the_sentence(string release, string destination, string? holdings, string error)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var found = await FindAlienAsync();
        var releaseId = release == "release" ? found.Id.ToString() : release;
        var destinationId = destination == "dest" ? _movies.Id.ToString() : destination;
        var query = $"{BaseUrl}?releaseId={releaseId}&destinationId={destinationId}" + (holdings is null ? "" : $"&holdings={holdings}");

        //Act
        var response = await _factory.GetHttpRequest(query, _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(error, await response.ReadErrorAsync(cancellationToken));
        Assert.Empty(_factory.QBittorrentHttp.Requests);
    }

    [Fact]
    public async Task Options_for_a_release_of_another_users_search_is_404_with_the_release_sentence()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _replacement.SwitchOn(FilmsFolder);
        var membersRelease = await _releases.CreateFoundBySearch(_member.User, _toloka, "Alien", 1979, AlienRelease, imdbId: AlienImdbId);

        //Act
        var response = await OptionsAsync(membersRelease, _movies);

        //Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(ReleaseMissingError, await response.ReadErrorAsync(cancellationToken));
        Assert.Empty(_factory.QBittorrentHttp.Requests);
    }

    [Fact]
    public async Task Options_for_a_destination_that_is_gone_is_404_with_the_destination_sentence()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var release = await FindAlienAsync();

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}?releaseId={release.Id}&destinationId={Guid.NewGuid()}", _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(DestinationGoneError, await response.ReadErrorAsync(cancellationToken));
    }

    [Fact]
    public async Task Options_as_a_member_is_403_and_reads_nothing_even_for_their_own_release()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await PlantAlienAsync(cancellationToken);
        await _replacement.SwitchOn(FilmsFolder);
        var membersRelease = await _releases.CreateFoundBySearch(_member.User, _toloka, "Alien", 1979, AlienRelease, imdbId: AlienImdbId);
        var described = _factory.Files.DescribedPaths.Count;

        //Act
        var response = await OptionsAsync(membersRelease, _movies, holdings: true, token: _member.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(OwnerOnlyBody, await response.Content.ReadAsStringAsync(cancellationToken));
        Assert.Empty(_factory.QBittorrentHttp.Requests);
        Assert.Equal(described, _factory.Files.DescribedPaths.Count);
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>Alien (1979), IMDb tt0078748, in Фільми as two versions — the theatrical cut and the director's cut —,
    /// each one file on the disk, inspected (SDR).</summary>
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

    /// <summary>Two Halloween (2018) in Фільми, each one file at its path, inspected — one with an IMDb id, one left
    /// without (the duplicate MAM-20 finds) —, which a release with no id matches by title and year.</summary>
    private async Task<(MediaWorkEntity Identified, MediaWorkEntity Unidentified)> PlantTwoHalloweensAsync(
        string identifiedPath, string unidentifiedPath, CancellationToken cancellationToken)
    {
        var identified = await _works.CreateMovie(_jellyfin, "Halloween", 2018, imdbId: "tt1502407");
        await _works.AddVersion(_jellyfin, identified, NewItemId(), _films, null, identifiedPath);
        var unidentified = await _works.CreateMovie(_jellyfin, "Halloween", 2018);
        await _works.AddVersion(_jellyfin, unidentified, NewItemId(), _films, null, unidentifiedPath);
        _factory.Files.AddFile(identifiedPath, TestMedia.File(inode: 1), TestMedia.Taken());
        _factory.Files.AddFile(unidentifiedPath, TestMedia.File(inode: 2), TestMedia.Taken());
        await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);
        await _replacement.SwitchOn(FilmsFolder);
        return (identified, unidentified);
    }

    /// <summary>Another download of <paramref name="release"/> as a previous schedule left it: its replacement in
    /// <paramref name="state"/>, holding the copy at <paramref name="appPath"/> with the identity <paramref name="facts"/>
    /// read then, and the download in the state the replacement mirrors.</summary>
    private Task PlantReplacementAsync(ReleaseEntity release, ReplacementJobState state, string appPath, FileFacts facts) =>
        _factory.WithDbAsync(async db =>
        {
            var replacements = new ReplacementJobDataFactory(db, _factory.Time);
            var download = await new DownloadJobDataFactory(db, _factory.Time).Create(
                release.Id,
                _client.Id,
                _client.Name,
                status: state switch
                {
                    ReplacementJobState.Downloading => DownloadJobStatus.Downloading,
                    ReplacementJobState.Paused => DownloadJobStatus.Paused,
                    ReplacementJobState.Downloaded => DownloadJobStatus.Downloaded,
                    ReplacementJobState.NeedsReview => DownloadJobStatus.NeedsLinking,
                    ReplacementJobState.Failed => DownloadJobStatus.Failed,
                    ReplacementJobState.Cancelled => DownloadJobStatus.Cancelled,
                    ReplacementJobState.Scheduled => DownloadJobStatus.Created,
                    _ => DownloadJobStatus.Submitted,
                },
                requestedByUserId: _owner.User.Id);
            var replacement = await replacements.Create(download.Id, _owner.User.Id, state);
            await replacements.AddFile(replacement.Id, appPath, facts);
        });

    /// <summary>The owner's search for Alien (1979) found a release of it with its IMDb id.</summary>
    private Task<ReleaseEntity> FindAlienAsync() =>
        _releases.CreateFoundBySearch(_owner.User, _toloka, "Alien", 1979, AlienRelease, imdbId: AlienImdbId);

    private Task<HttpResponseMessage> OptionsAsync(ReleaseEntity release, DestinationMappingEntity destination, bool? holdings = null, string? token = null) =>
        _factory.GetHttpRequest(
            $"{BaseUrl}?releaseId={release.Id}&destinationId={destination.Id}" + (holdings is { } read ? $"&holdings={(read ? "true" : "false")}" : ""),
            token ?? _owner.AccessToken);

    private static async Task<ReplacementOptionsResponse> ReadAsync(HttpResponseMessage response, CancellationToken cancellationToken) =>
        (await response.Content.ReadFromJsonAsync<ReplacementOptionsResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;

    private string[] QBittorrentCalls() =>
        [.. _factory.QBittorrentHttp.Requests.Select(request => $"{request.Method} {request.Uri.PathAndQuery}")];

    /// <summary>The read writes nothing to the client: every request is a GET or the sign-in.</summary>
    private void AssertOnlyReads() =>
        Assert.All(QBittorrentCalls(), call => Assert.True(
            call.StartsWith("GET ", StringComparison.Ordinal) || call == "POST /api/v2/auth/login", call));

    private static string NewItemId() => Guid.NewGuid().ToString("N");
}
