using System.Net;
using System.Net.Http.Json;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Api.Services.Replacements;
using MediaAcquisitionManager.Core.Abstractions.Files;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Paths;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Files;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MediaAcquisitionManager.TestInfrastructure.Replacements;

/// <summary>
/// MAM-25 — what every HTTP test of the check of a replacement's new files stands on (ADR 0035): the installation owner;
/// Jellyfin "Living room", whose <c>/data</c> is the app's <c>/media</c>, with its libraries Фільми
/// (<see cref="FilmsFolder"/>) and Серіали (<see cref="ShowsFolder"/>), both ticked for replacement and switched on;
/// Toloka; and qBittorrent, whose <c>/downloads</c> is the app's <c>/media</c> too, with the destinations Movies and TV
/// Shows saving into those two folders. Each behind its fake, armed.
/// </summary>
/// <remarks>
/// <para>
/// A test plants its library (<see cref="PlantFilmAsync"/>, <see cref="PlantSeriesAsync"/>, <see cref="PlantEpisodeAsync"/>,
/// then <see cref="InspectAsync"/>), and reaches a Downloaded replacement the way the owner does — the real schedule route,
/// then qBittorrent finishing the download and a pass of the download jobs (<see cref="DownloadedAsync"/>) —; then it puts
/// the new files on the disk, makes Jellyfin's items of them when asked to look, and runs the verifier's passes
/// (<see cref="VerifyAsync"/>).
/// </para>
/// <para>
/// ADR 0003 — Jellyfin names every file by another path than the app: a test gives the app's (<c>/media/…</c>), which the
/// disk and the sentences use, and the harness plants Jellyfin's side at <see cref="InJellyfin"/> of it. So a check that
/// asked or read Jellyfin by the app's path — or the app by Jellyfin's — finds nothing, and fails its test.
/// </para>
/// <para>
/// MAM-26 — the move into the quarantine stands on the same world: a test reaches a Verified replacement in one step
/// (<see cref="VerifiedAsync"/>), runs the mover's passes (<c>QuarantineReplacementsAsync</c>), and reads back where
/// each name went (<see cref="QuarantinePathOf"/>, written out rather than asked of the app) and the quarantine's audit
/// rows in the order they were saved (<see cref="QuarantineAuditsAsync"/>). <see cref="Mark"/> takes the marks
/// <see cref="Writes"/> counts from again.
/// </para>
/// <para>
/// Nothing here asserts: the test projects own their assertions. What a test reads back — the rows, the audit rows of the
/// check, the Jobs page's line, every request that wasn't a read, the old copies on the disk now — comes back as data.
/// Every test class makes its own: the tests are split by what they prove, and what they stand on is one place.
/// </para>
/// </remarks>
public sealed class ReplacementVerificationHarness
{
    /// <summary>The name of the Jellyfin integration, which the reasons quote.</summary>
    public const string JellyfinName = "Living room";

    /// <summary>The API key Jellyfin takes.</summary>
    public const string JellyfinApiKey = "0123456789abcdef0123456789abcdef";

    /// <summary>The name of the qBittorrent integration, which the reasons quote.</summary>
    public const string ClientName = "qBittorrent";

    /// <summary>The one folder the app may read: qBittorrent's <c>/downloads</c> and Jellyfin's <c>/data</c> inside the app.</summary>
    public const string MediaRoot = "/media";

    /// <summary>Jellyfin's own name of <see cref="MediaRoot"/>: the folder its mapping brings to the app's.</summary>
    public const string JellyfinRoot = "/data";

    /// <summary>Фільми's folder as the app reaches it, and ticks it: the Movies category saves into it.</summary>
    public const string FilmsFolder = "/media/movies";

    /// <summary>Серіали's folder as the app reaches it: the TV Shows category saves into it.</summary>
    public const string ShowsFolder = "/media/tv-shows";

    /// <summary>Фільми's folder as Jellyfin reports it.</summary>
    public const string FilmsLibraryFolder = "/data/movies";

    /// <summary>Серіали's folder as Jellyfin reports it.</summary>
    public const string ShowsLibraryFolder = "/data/tv-shows";

    /// <summary>MAM-26 — the quarantine of <see cref="MediaRoot"/>, where the old copies wait (ADR 0030).</summary>
    public const string QuarantineRoot = "/media/.mam-quarantine";

    private const string DownloadsUrl = "/api/downloads";
    private const string ReplacementsUrl = "/api/replacements";
    private const string TolokaAddress = "http://prowlarr.test:9696/1/api";
    private const string TolokaApiKey = "planted-torznab-key-0025";
    private const string ClientSignInPath = "/api/v2/auth/login";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private readonly Dictionary<Guid, string> _seriesItemIds = [];
    private int _qbittorrentMark;
    private int _jellyfinMark;

    private ReplacementVerificationHarness(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    /// <summary>The installation owner, signed in when the harness was made.</summary>
    public AuthorizedUser Owner { get; private set; } = null!;

    /// <summary>Jellyfin "Living room".</summary>
    public IntegrationConnectionEntity Jellyfin { get; private set; } = null!;

    /// <summary>The release search the owner's releases were found through.</summary>
    public IntegrationConnectionEntity Toloka { get; private set; } = null!;

    /// <summary>qBittorrent, which every replacement is sent to.</summary>
    public IntegrationConnectionEntity Client { get; private set; } = null!;

    /// <summary>The fake behind <see cref="Jellyfin"/>: its items, and every request to look it took.</summary>
    public FakeJellyfinServer JellyfinServer { get; private set; } = null!;

    /// <summary>The fake behind <see cref="Client"/>.</summary>
    public FakeQBittorrentServer QBittorrent { get; private set; } = null!;

    /// <summary>The fake behind <see cref="Toloka"/>, serving the .torrent <see cref="DownloadedAsync"/> sets.</summary>
    public FakeTorznabServer Feed { get; private set; } = null!;

    /// <summary>The default destination: qBittorrent's Movies, saving to <c>/downloads/movies</c>.</summary>
    public DestinationMappingEntity Movies { get; private set; } = null!;

    /// <summary>qBittorrent's TV Shows, saving to <c>/downloads/tv-shows</c>.</summary>
    public DestinationMappingEntity TvShows { get; private set; } = null!;

    /// <summary>Jellyfin's film library.</summary>
    public LibraryLocationEntity Films { get; private set; } = null!;

    /// <summary>Jellyfin's series library.</summary>
    public LibraryLocationEntity Shows { get; private set; } = null!;

    /// <summary>Plants everything the check stands on in <paramref name="factory"/>'s database and fakes; call it right after
    /// the factory's reset.</summary>
    public static async Task<ReplacementVerificationHarness> CreateAsync(MediaAcquisitionManagerApiFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        var harness = new ReplacementVerificationHarness(factory);
        var scope = factory.Scope;
        harness.Owner = await new UserSeeder(scope).CreateAuthorizedOwner("owner");

        var integrations = new IntegrationConnectionSeeder(scope);
        harness.Jellyfin = await integrations.Create(
            "jellyfin", JellyfinName, apiKey: JellyfinApiKey, pathMappings: [new PathMappingPair(JellyfinRoot, MediaRoot)]);
        harness.JellyfinServer = new FakeJellyfinServer { ApiKey = JellyfinApiKey };
        harness.JellyfinServer.Arm(factory.JellyfinHttp);
        harness.Feed = new FakeTorznabServer(TolokaAddress, TolokaApiKey);
        harness.Feed.Arm(factory.TorznabHttp);
        harness.Toloka = await integrations.Create("torznab", "Toloka", TolokaAddress, apiKey: TolokaApiKey);
        harness.QBittorrent = new FakeQBittorrentServer();
        harness.QBittorrent.Arm(factory.QBittorrentHttp);
        harness.Client = await integrations.Create(
            "qbittorrent",
            ClientName,
            harness.QBittorrent.BaseUrl,
            userName: FakeQBittorrentServer.DefaultUserName,
            password: FakeQBittorrentServer.DefaultPassword,
            pathMappings: [new PathMappingPair("/downloads", MediaRoot)]);

        var destinations = new DestinationMappingSeeder(scope);
        harness.Movies = await destinations.Create(harness.Client, "Movies", "Movies", isDefault: true);
        harness.TvShows = await destinations.Create(harness.Client, "TV Shows", "TV Shows");
        var libraries = new LibraryLocationSeeder(scope);
        harness.Films = await libraries.Create(harness.Jellyfin, "Фільми", FilmsLibraryFolder);
        harness.Shows = await libraries.Create(harness.Jellyfin, "Серіали", ShowsLibraryFolder);
        await new AllowedRootSeeder(scope).Allow(MediaRoot);
        await new ReplacementSettingsSeeder(scope).SwitchOn(FilmsFolder, ShowsFolder);
        return harness;
    }

    // ── the library ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// A film of Фільми as a sync stores it — <paramref name="title"/> of <paramref name="year"/> with the ids given, and
    /// <paramref name="originalTitle"/> beside it when given, one version labelled <paramref name="editionLabel"/> holding
    /// the file Jellyfin knows as <see cref="InJellyfin"/> of <paramref name="path"/>, whose item is the one Jellyfin
    /// computes for that path — and its file on the disk at <paramref name="path"/>, the app's, with the identity
    /// <paramref name="inode"/>, holding <paramref name="media"/> (Taken's streams when <c>null</c>). Inspect it
    /// (<see cref="InspectAsync"/>) before it can be ticked.
    /// </summary>
    public async Task<MediaWorkEntity> PlantFilmAsync(
        string title,
        int year,
        string path,
        long inode,
        string? imdbId = null,
        string? tmdbId = null,
        string? editionLabel = null,
        MediaFacts? media = null,
        string? originalTitle = null)
    {
        var libraryPath = InJellyfin(path);
        var works = new MediaWorkSeeder(_factory.Scope);
        var film = await works.CreateMovie(Jellyfin, title, year, tmdbId: tmdbId, imdbId: imdbId, originalTitle: originalTitle);
        await works.AddVersion(Jellyfin, film, JellyfinItemIds.Of(JellyfinItemIds.MovieType, libraryPath), Films, editionLabel, libraryPath);
        _factory.Files.AddFile(path, TestMedia.File(inode), media ?? TestMedia.Taken());
        return film;
    }

    /// <summary>
    /// A series of Серіали — <paramref name="title"/> of <paramref name="year"/> with the ids given, no episode yet — and
    /// its series item in Jellyfin with the same ids, its TMDb id a show's (<see cref="SeriesItemIdOf"/>).
    /// </summary>
    public async Task<MediaWorkEntity> PlantSeriesAsync(string title, int year, string? imdbId = null, string? tmdbTvId = null)
    {
        var series = await new MediaWorkSeeder(_factory.Scope).CreateSeries(Jellyfin, title, year, tmdbTvId: tmdbTvId, imdbId: imdbId, seasons: []);
        var itemId = Guid.NewGuid().ToString("N");
        JellyfinServer.Series.Add(JellyfinSamples.SeriesWith(itemId, title, year, $"{ShowsLibraryFolder}/{title}", imdbId, tmdbTvId));
        _seriesItemIds[series.Id] = itemId;
        return series;
    }

    /// <summary>Jellyfin's item of <paramref name="series"/>, which its episodes' items name as their series.</summary>
    public string SeriesItemIdOf(MediaWorkEntity series)
    {
        ArgumentNullException.ThrowIfNull(series);
        return _seriesItemIds[series.Id];
    }

    /// <summary>
    /// An episode of <paramref name="series"/> at <paramref name="season"/> and <paramref name="number"/> as a sync stores
    /// it, one version holding the file Jellyfin knows as <see cref="InJellyfin"/> of <paramref name="path"/>, on the disk
    /// at <paramref name="path"/>, the app's, with the identity <paramref name="inode"/> (The Continental's streams); and
    /// the episode item Jellyfin made of that file, read and numbered — to <paramref name="numberEnd"/> too, for a file of
    /// several episodes —, which the check reads for the old copy's kind, series and numbers.
    /// </summary>
    public async Task<MediaWorkEntity> PlantEpisodeAsync(
        MediaWorkEntity series, int season, int number, string title, string path, long inode, int? numberEnd = null)
    {
        var libraryPath = InJellyfin(path);
        var itemId = JellyfinItemIds.Of(JellyfinItemIds.EpisodeType, libraryPath);
        var works = new MediaWorkSeeder(_factory.Scope);
        var episode = await works.AddEpisodeAt(Jellyfin, series, title, season, number, itemId);
        await works.AddVersion(Jellyfin, episode, itemId, Shows, null, libraryPath);
        _factory.Files.AddFile(path, TestMedia.File(inode), TestMedia.Continental());
        JellyfinServer.PlantEpisode(libraryPath, title, SeriesItemIdOf(series), season, number, numberEnd);
        return episode;
    }

    /// <summary>The owner's inspection of the library's files, which a copy needs before it can be ticked.</summary>
    public Task InspectAsync(CancellationToken cancellationToken = default) => _factory.InspectFilesAsync(Owner.AccessToken, cancellationToken);

    /// <summary>The library file at <paramref name="path"/>, the app's, as the dialog ticks it.</summary>
    public async Task<Guid> FileIdAsync(string path, CancellationToken cancellationToken = default)
    {
        var libraryPath = InJellyfin(path);
        var id = Guid.Empty;
        await _factory.WithDbAsync(async db => id = await db.PhysicalFiles
            .Where(file => file.ServicePath == libraryPath)
            .Select(file => file.Id)
            .SingleAsync(cancellationToken));
        return id;
    }

    /// <summary>
    /// MAM-26 — where the copy at <paramref name="appPath"/>, the app's, waits in the quarantine while the replacement
    /// <paramref name="replacementJobId"/> holds it: <see cref="QuarantineRoot"/>, the job's id, then the copy's path
    /// inside <see cref="MediaRoot"/> (ADR 0030). Written out here, not asked of the app, for <see cref="InJellyfin"/>'s
    /// reason. Throws for a path outside <see cref="MediaRoot"/>, which has no place there.
    /// </summary>
    public static string QuarantinePathOf(Guid replacementJobId, string appPath)
    {
        ArgumentNullException.ThrowIfNull(appPath);
        if (!appPath.StartsWith(MediaRoot + "/", StringComparison.Ordinal))
            throw new ArgumentException($"Only a copy inside {MediaRoot} waits in its quarantine.", nameof(appPath));
        return $"{QuarantineRoot}/{replacementJobId}/{appPath[(MediaRoot.Length + 1)..]}";
    }

    /// <summary>
    /// The path Jellyfin knows the file at <paramref name="appPath"/> by: <see cref="MediaRoot"/> spelled
    /// <see cref="JellyfinRoot"/>. Written out here, not asked of the app, which must build it from the folder Jellyfin
    /// reported (<c>ReplacementLibraryPlaces</c>) — a copy of its rule would agree with it however wrong. Throws for a path
    /// outside <see cref="MediaRoot"/>, which Jellyfin can't name.
    /// </summary>
    public static string InJellyfin(string appPath)
    {
        ArgumentNullException.ThrowIfNull(appPath);
        if (!appPath.StartsWith(MediaRoot + "/", StringComparison.Ordinal))
            throw new ArgumentException($"Jellyfin names nothing outside {MediaRoot}.", nameof(appPath));
        return JellyfinRoot + appPath[MediaRoot.Length..];
    }

    // ── the download ────────────────────────────────────────────────────────────────────────────

    /// <summary>A release titled <paramref name="title"/> the owner's search for <paramref name="searchText"/> of
    /// <paramref name="year"/> found through Toloka, with the IMDb id given.</summary>
    public Task<ReleaseEntity> FindAsync(string searchText, int year, string title, ReleaseKind kind = ReleaseKind.Movie, string? imdbId = null) =>
        new ReleaseSeeder(_factory.Scope).CreateFoundBySearch(Owner.User, Toloka, searchText, year, title, kind, imdbId: imdbId);

    /// <summary>
    /// The owner schedules the replacement of the copies at <paramref name="tickedPaths"/> by <paramref name="release"/>
    /// to <paramref name="destination"/> through the real route — its .torrent holding <paramref name="files"/> —, and
    /// qBittorrent finishes it, listing <paramref name="files"/> as they are given: a pass of the download jobs makes the
    /// download and its replacement Downloaded. Nothing is on the disk at the new files' places yet. What was sent so far
    /// is behind the mark <see cref="Writes"/> counts from.
    /// </summary>
    /// <param name="files">As qBittorrent names them: a single file by its name, the files of a folder under it
    /// (<c>Pack/one.mkv</c>), each with its size and progress.</param>
    public async Task<DownloadJobDto> DownloadedAsync(
        ReleaseEntity release,
        DestinationMappingEntity destination,
        IReadOnlyList<string> tickedPaths,
        IReadOnlyList<FakeQBittorrentFile> files,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(release);
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(tickedPaths);
        ArgumentNullException.ThrowIfNull(files);
        Feed.TorrentFile = TorrentOf(files);
        var fileIds = new List<Guid>();
        foreach (var path in tickedPaths)
            fileIds.Add(await FileIdAsync(path, cancellationToken));

        using var scheduled = await _factory.PostHttpRequest(
            ReplacementsUrl, Owner.AccessToken, new ScheduleReplacementRequest(Guid.NewGuid(), release.Id, Client.Id, destination.Id, fileIds));
        if (scheduled.StatusCode != HttpStatusCode.Created)
            throw new InvalidOperationException($"The schedule answered {(int)scheduled.StatusCode}: {await scheduled.Content.ReadAsStringAsync(cancellationToken)}");
        var job = (await scheduled.Content.ReadFromJsonAsync<DownloadJobDto>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;

        QBittorrent.UpdateTorrent(job.ClientJobId!, torrent => torrent with { State = "stalledUP", Progress = 1.0, Files = files });
        _factory.Time.Advance(TimeSpan.FromMinutes(1));
        await _factory.PollDownloadJobsAsync(cancellationToken);
        if ((await ReplacementAsync(cancellationToken)).State != ReplacementJobState.Downloaded)
            throw new InvalidOperationException("The replacement didn't follow its download to Downloaded.");

        _factory.Time.Advance(TimeSpan.FromSeconds(5));
        Mark();
        return job;
    }

    /// <summary>
    /// MAM-26 — the owner's replacement of the copies at <paramref name="tickedPaths"/> by <paramref name="release"/>,
    /// verified as MAM-25 leaves it: <see cref="DownloadedAsync"/> with <paramref name="files"/>, every one of
    /// <paramref name="newFiles"/> on the disk, Jellyfin answering each request to look with <paramref name="identify"/>
    /// (making the items of the paths it is given), and the verifier's two passes a
    /// <see cref="ReplacementVerificationTracker.PassInterval"/> apart. Throws unless the replacement is Verified. Then
    /// Jellyfin answers a request to look with nothing again — the move's own request names the old copy's path, where
    /// <paramref name="identify"/> would plant an item —, and the marks <see cref="Writes"/> counts from are taken again,
    /// so it counts what follows only.
    /// </summary>
    /// <param name="newFiles">Each new file at its app path, with the identity and the streams the disk and the media
    /// inspector give.</param>
    public async Task<DownloadJobDto> VerifiedAsync(
        ReleaseEntity release,
        DestinationMappingEntity destination,
        IReadOnlyList<string> tickedPaths,
        IReadOnlyList<FakeQBittorrentFile> files,
        IReadOnlyList<(string AppPath, FileFacts Facts, MediaFacts Media)> newFiles,
        Action<IReadOnlyList<string>> identify,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(newFiles);
        ArgumentNullException.ThrowIfNull(identify);
        var job = await DownloadedAsync(release, destination, tickedPaths, files, cancellationToken);
        foreach (var (appPath, facts, media) in newFiles)
            _factory.Files.AddFile(appPath, facts, media);
        JellyfinServer.OnScanRequested = identify;
        await VerifyAsync(cancellationToken);
        _factory.Time.Advance(ReplacementVerificationTracker.PassInterval);
        await VerifyAsync(cancellationToken);
        JellyfinServer.OnScanRequested = null;

        var replacement = await ReplacementAsync(cancellationToken);
        if (replacement.State != ReplacementJobState.Verified)
            throw new InvalidOperationException($"The replacement is {replacement.State}, not Verified: {replacement.Reason}");
        Mark();
        return job;
    }

    /// <summary>MAM-26 — takes the marks <see cref="Writes"/> counts from again: every request to qBittorrent and Jellyfin
    /// so far is behind them.</summary>
    public void Mark()
    {
        _qbittorrentMark = _factory.QBittorrentHttp.Requests.Count;
        _jellyfinMark = _factory.JellyfinHttp.Requests.Count;
    }

    // ── the check ───────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// One pass of the app's verifier, which must not have logged an error: a pass that threw is logged and would otherwise
    /// read as one that changed nothing. A test of a pass that throws runs <c>VerifyReplacementsAsync</c> itself.
    /// </summary>
    public async Task VerifyAsync(CancellationToken cancellationToken = default)
    {
        await _factory.VerifyReplacementsAsync(cancellationToken);
        if (_factory.Logs.Entries.FirstOrDefault(entry => entry.Level >= LogLevel.Error) is { } error)
            throw new InvalidOperationException($"A pass logged an error: {error.Category}: {error.Message}");
    }

    /// <summary>The one replacement there is, as stored now.</summary>
    public async Task<ReplacementJobEntity> ReplacementAsync(CancellationToken cancellationToken = default)
    {
        ReplacementJobEntity? row = null;
        await _factory.WithDbAsync(async db => row = await db.ReplacementJobs.AsNoTracking().SingleAsync(cancellationToken));
        return row!;
    }

    /// <summary>The evidence of the replacement's new files, in the client's order.</summary>
    public async Task<IReadOnlyList<ReplacementJobNewFileEntity>> NewFilesAsync(CancellationToken cancellationToken = default)
    {
        List<ReplacementJobNewFileEntity> rows = [];
        await _factory.WithDbAsync(async db => rows = await db.ReplacementJobNewFiles.AsNoTracking().OrderBy(file => file.Position).ToListAsync(cancellationToken));
        return rows;
    }

    /// <summary>The registry ids the library gave each new file at its last read: by the file's place in the client's
    /// order, the item's own before its series', then by scheme and value.</summary>
    public async Task<IReadOnlyList<(int Position, bool OfSeries, MediaIdentifierScheme Scheme, string Value)>> NewFileIdentifiersAsync(
        CancellationToken cancellationToken = default)
    {
        List<(int Position, bool OfSeries, MediaIdentifierScheme Scheme, string Value)> rows = [];
        await _factory.WithDbAsync(async db => rows = (await db.ReplacementJobNewFileIdentifiers
                .AsNoTracking()
                .Join(db.ReplacementJobNewFiles.AsNoTracking(), identifier => identifier.ReplacementJobNewFileId, file => file.Id, (identifier, file) => new
                {
                    file.Position,
                    identifier.OfSeries,
                    identifier.Scheme,
                    identifier.Value,
                })
                .ToListAsync(cancellationToken))
            .OrderBy(row => row.Position)
            .ThenBy(row => row.OfSeries)
            .ThenBy(row => row.Scheme)
            .ThenBy(row => row.Value, StringComparer.Ordinal)
            .Select(row => (row.Position, row.OfSeries, row.Scheme, row.Value))
            .ToList());
        return rows;
    }

    /// <summary>The replacement's ticked old copies, in their order.</summary>
    public async Task<IReadOnlyList<ReplacementJobFileEntity>> CopiesAsync(CancellationToken cancellationToken = default)
    {
        List<ReplacementJobFileEntity> rows = [];
        await _factory.WithDbAsync(async db => rows = await db.ReplacementJobFiles.AsNoTracking().OrderBy(copy => copy.Position).ToListAsync(cancellationToken));
        return rows;
    }

    /// <summary>The audit rows of the check — started, verified, needs review, failed —, oldest first, each with its actor
    /// and target.</summary>
    public async Task<IReadOnlyList<(AuditAction Action, Guid? ActorUserId, AuditTargetKind? TargetKind, Guid? TargetId, string? TargetName)>> CheckAuditsAsync(
        CancellationToken cancellationToken = default)
    {
        AuditAction[] actions =
            [AuditAction.ReplacementCheckStarted, AuditAction.ReplacementVerified, AuditAction.ReplacementNeedsReview, AuditAction.ReplacementFailed];
        List<(AuditAction, Guid?, AuditTargetKind?, Guid?, string?)> rows = [];
        await _factory.WithDbAsync(async db => rows = (await db.AuditEvents
                .AsNoTracking()
                .Where(audit => actions.Contains(audit.Action))
                .OrderBy(audit => audit.CreatedUtc)
                .ThenBy(audit => audit.Action)
                .ToListAsync(cancellationToken))
            .Select(audit => (audit.Action, audit.ActorUserId, audit.TargetKind, audit.TargetId, audit.TargetName))
            .ToList());
        return rows;
    }

    /// <summary>
    /// MAM-26 — the audit rows of the quarantine — every action MAM-26 added (the mover's, Retry's, Restore's and Delete
    /// for good's, the job's and each name's) and <see cref="AuditAction.ReplacementNeedsReview"/>, which their stops
    /// write — in the order they were saved, each with its actor and target. Read by SQLite's own row order: the clock
    /// stands still in a test, so an intent and its outcome share a time, and the ids are random.
    /// </summary>
    /// <remarks>
    /// The order is the saves' order, not the calls': EF writes the rows of one save in the order of their (random) ids,
    /// so the rows one save holds come in any order among themselves — compare those as a set.
    /// </remarks>
    public async Task<IReadOnlyList<(AuditAction Action, Guid? ActorUserId, AuditTargetKind? TargetKind, Guid? TargetId, string? TargetName)>> QuarantineAuditsAsync(
        CancellationToken cancellationToken = default)
    {
        AuditAction[] actions =
        [
            AuditAction.ReplacementNeedsReview,
            AuditAction.ReplacementQuarantineStarted,
            AuditAction.ReplacementNameMoving,
            AuditAction.ReplacementNameMoved,
            AuditAction.ReplacementNameNotMoved,
            AuditAction.ReplacementQuarantined,
            AuditAction.ReplacementRetried,
            AuditAction.ReplacementRestoreStarted,
            AuditAction.ReplacementNameRestoring,
            AuditAction.ReplacementNameRestored,
            AuditAction.ReplacementNameNotRestored,
            AuditAction.ReplacementRestored,
            AuditAction.ReplacementDeleteStarted,
            AuditAction.ReplacementNameDeleting,
            AuditAction.ReplacementNameDeleted,
            AuditAction.ReplacementNameNotDeleted,
            AuditAction.ReplacementCompleted,
        ];
        List<(AuditAction, Guid?, AuditTargetKind?, Guid?, string?)> rows = [];
        // Nothing composed onto the raw query: EF would wrap it in a subquery, whose order SQLite needn't keep.
        await _factory.WithDbAsync(async db => rows = (await db.AuditEvents
                .FromSqlRaw("SELECT * FROM \"AuditEvents\" ORDER BY rowid")
                .AsNoTracking()
                .ToListAsync(cancellationToken))
            .Where(audit => actions.Contains(audit.Action))
            .Select(audit => (audit.Action, audit.ActorUserId, audit.TargetKind, audit.TargetId, audit.TargetName))
            .ToList());
        return rows;
    }

    /// <summary>
    /// The replacement line of the download <paramref name="downloadJobId"/> as the Jobs page reads it, through
    /// <c>GET /api/downloads/{id}</c> as the owner signed in afresh: a test that ran the clock past an hour outlived the
    /// session it started with. Throws unless the answer is 200.
    /// </summary>
    public async Task<DownloadJobReplacementDto?> LineAsync(Guid downloadJobId, CancellationToken cancellationToken = default)
    {
        var session = await new UserSeeder(_factory.Scope).CreateSession(Owner.User);
        using var response = await _factory.GetHttpRequest($"{DownloadsUrl}/{downloadJobId}", session.AccessToken);
        if (response.StatusCode != HttpStatusCode.OK)
            throw new InvalidOperationException($"The job's read answered {(int)response.StatusCode}.");
        return (await response.Content.ReadFromJsonAsync<DownloadJobDto>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!.Replacement;
    }

    /// <summary>
    /// Every request to qBittorrent and Jellyfin since the download was done that wasn't a read, as <c>POST /path</c>,
    /// qBittorrent's first: what the check changed anywhere. qBittorrent's sign-in changes nothing and isn't counted.
    /// </summary>
    public IReadOnlyList<string> Writes() =>
    [
        .. _factory.QBittorrentHttp.Requests.Skip(_qbittorrentMark)
            .Concat(_factory.JellyfinHttp.Requests.Skip(_jellyfinMark))
            .Where(request => request.Method != HttpMethod.Get && request.Uri.AbsolutePath != ClientSignInPath)
            .Select(request => $"{request.Method} {request.Uri.AbsolutePath}"),
    ];

    /// <summary>
    /// The ticked old copies the disk no longer describes as they were scheduled — gone, or of another identity, size or
    /// modification time —, by their app paths: none, while nothing touched them.
    /// </summary>
    public async Task<IReadOnlyList<string>> ChangedOldCopiesAsync(CancellationToken cancellationToken = default)
    {
        var changed = new List<string>();
        foreach (var copy in await CopiesAsync(cancellationToken))
        {
            var described = await _factory.Files.DescribeAsync(copy.AppPath, [MediaRoot], cancellationToken);
            if (described is not { Status: FileInventoryStatus.Found, Value: { } facts }
                || facts.DeviceId != copy.DeviceId
                || facts.Inode != copy.Inode
                || facts.SizeBytes != copy.SizeBytes
                || facts.ModifiedUtc != copy.ModifiedUtc)
                changed.Add(copy.AppPath);
        }

        return changed;
    }

    /// <summary>
    /// The .torrent of <paramref name="files"/>: a single file's own, or a folder's — its name the first part of the files'
    /// names. Each file's bytes are made up and at most four pieces long: the check reads what qBittorrent lists, never the
    /// .torrent, which only has to send.
    /// </summary>
    private static byte[] TorrentOf(IReadOnlyList<FakeQBittorrentFile> files)
    {
        static byte[] ContentOf(FakeQBittorrentFile file, int index) =>
            TestTorrent.Bytes((int)Math.Clamp(file.Size, 1, 4 * TestTorrent.PieceLength), seed: (byte)(index + 1));

        if (files is [{ Name: var name } single] && !name.Contains('/', StringComparison.Ordinal))
            return TestTorrent.V1(name, ContentOf(single, 0));

        var folder = files[0].Name.Split('/')[0];
        return TestTorrent.V1(folder, [.. files.Select((file, index) => (file.Name.Split('/')[1..], ContentOf(file, index)))]);
    }
}
