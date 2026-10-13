using System.Collections.Concurrent;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using MediaAcquisitionManager.Core.Abstractions.Files;
using MediaAcquisitionManager.Core.Downloads;
using MediaAcquisitionManager.Core.Files;
using MediaAcquisitionManager.Core.Paths;

namespace MediaAcquisitionManager.TestInfrastructure.Files;

/// <summary>
/// The app's mounts and its media inspector as the API tests see them (MAM-9): <see cref="IFileInventory"/>
/// is the Core seam to the host's files and the ffprobe it runs, so it is faked here the way the vendor
/// HTTP is, and the local implementation is tested against real folders in Infrastructure.Tests.
/// </summary>
/// <remarks>
/// <para>
/// A test puts files where it wants them (<see cref="AddFile"/>) or plants an answer for a path
/// (<see cref="AddProblem"/>). Unarmed, nothing is anywhere: every path is <c>NotFound</c>, so a test that
/// forgot a file can't look like one that read it.
/// </para>
/// <para>
/// The allowed roots are checked the way the contract promises, folder by folder: no roots, or a path
/// outside them, is <c>OutsideAllowedRoots</c>. Of links, only the outcome is modelled: a second name of a
/// planted file (<see cref="AddLinkedName"/>) answers as the file does. Which links the walk follows, and
/// why it refuses one, is the local implementation's business, planted here as a listing problem
/// (<see cref="AddListingProblem"/>).
/// </para>
/// <para>
/// MAM-18 — the walk (<see cref="ListMediaFilesAsync"/>) lists, root by root in the owner's order and in
/// ordinal path order under each, every planted path and linked name under the root whose file name is a
/// media file's (<see cref="MediaFileNames"/>), and every listing problem under it. Nothing at or under an
/// <c>Unlisted</c> or <c>Skipped</c> place is listed, as the real walk neither lists a folder it couldn't
/// read nor walks one it refuses; a <c>LimitReached</c> entry ends the walk. Nothing is looked at or read
/// while it walks.
/// </para>
/// <para>
/// MAM-19 — a planted file hashes to <see cref="DefaultSha256"/> of its facts unless the test plants a hash
/// (<see cref="HashOf"/>): two names of one identity hash alike, two identities differently, so a
/// byte-identical copy is always a test's deliberate choice.
/// </para>
/// <para>
/// MAM-20 — every device holds <see cref="DefaultFileSystem"/> unless a test plants another
/// (<see cref="SetFileSystem"/>), and every device asked is recorded (<see cref="DescribedDevices"/>).
/// </para>
/// <para>
/// MAM-26 — the folders are known too: every folder on the way to a planted file is one (<see cref="AddFile"/>), and a
/// test adds an empty one (<see cref="AddFolder"/>) or takes one away with what it holds (<see cref="RemoveFolder"/>). A
/// folder stays when its files go, as on a disk, and a look at one answers <c>NotARegularFile</c>, as the real look does.
/// A file moves to another name keeping its identity
/// (<see cref="Move"/>) — what <see cref="FakeFileQuarantine"/> renames with, sharing this store. The walk leaves out
/// every folder named <c>.mam-quarantine</c>, at any depth, and every linked name leading into one, as the real walk
/// does (ADR 0030).
/// </para>
/// <para>
/// Thread-safe: a run looks at and reads two files at a time. Every call is recorded, and
/// <see cref="MostReadsAtOnce"/> and <see cref="MostHashesAtOnce"/> keep how many reads and hashes overlapped.
/// </para>
/// </remarks>
public sealed class FakeFileInventory : IFileInventory
{
    /// <summary>What the inspector says about a file a test added without media facts.</summary>
    public const string UnreadableMediaError = "ffprobe couldn't read “{0}” as media: Invalid data found when processing input.";

    /// <summary>
    /// MAM-20 — the file system every device holds unless a test says otherwise (<see cref="SetFileSystem"/>): one whose
    /// files never share blocks, so a byte-identical copy's room is estimated by default.
    /// </summary>
    public const string DefaultFileSystem = "ext4";

    private readonly ConcurrentDictionary<string, FakeFile> _files = new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<string> _described = new();
    private readonly ConcurrentQueue<string> _read = new();
    private readonly ConcurrentQueue<IReadOnlyList<string>> _rootsGiven = new();
    private readonly ConcurrentDictionary<string, string> _linkedNames = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, FileListingEntry> _listingProblems = new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<IReadOnlyList<string>> _listedRoots = new();
    private readonly ConcurrentQueue<string> _listedFolders = new();
    private readonly ConcurrentQueue<string> _hashed = new();
    private readonly ConcurrentQueue<IReadOnlyList<string>> _hashRootsGiven = new();
    private readonly ConcurrentDictionary<long, string?> _fileSystems = new();
    private readonly ConcurrentQueue<long> _describedDevices = new();
    private readonly ConcurrentQueue<string> _piecesHashed = new();
    private readonly ConcurrentDictionary<NormalizedPath, byte> _folders = new();
    private int _readsUnderWay;
    private int _mostReadsAtOnce;
    private int _hashesUnderWay;
    private int _mostHashesAtOnce;

    /// <inheritdoc/>
    public FileInventoryCapabilities Capabilities { get; set; } = new(ReportsFileIdentity: true);

    /// <summary>
    /// Runs inside each read while it counts as under way, before it answers: a test holds reads open to
    /// watch the concurrency or the run's progress, or changes a file "while it is being read".
    /// </summary>
    public Func<string, CancellationToken, Task>? DuringRead { get; set; }

    /// <summary>
    /// MAM-18 — runs inside the walk before each entry is given to the caller: a test watches the scan's
    /// progress, changes the folders "while they are walked", stops the host, or breaks the walk.
    /// </summary>
    public Func<FileListingEntry, CancellationToken, Task>? DuringListing { get; set; }

    /// <summary>
    /// MAM-19 — runs inside each hash while it counts as under way, before it answers: a test holds hashes
    /// open to watch the concurrency or the run's progress, stops the host, or changes a file "while it is
    /// being hashed".
    /// </summary>
    public Func<string, CancellationToken, Task>? DuringHash { get; set; }

    /// <summary>
    /// MAM-38 — runs inside each hash of some pieces before it answers: a test throws there, as a bug nobody foresaw
    /// would, to see the send still finish its job.
    /// </summary>
    public Func<string, CancellationToken, Task>? DuringPiecesHash { get; set; }

    /// <summary>Every path looked at, in call order.</summary>
    public IReadOnlyList<string> DescribedPaths => [.. _described];

    /// <summary>Every path read by the media inspector, in call order.</summary>
    public IReadOnlyList<string> ReadPaths => [.. _read];

    /// <summary>The allowed roots each look and read was given.</summary>
    public IReadOnlyList<IReadOnlyList<string>> RootsGiven => [.. _rootsGiven];

    /// <summary>MAM-18 — the allowed roots each walk was given, in call order: one entry per walk.</summary>
    public IReadOnlyList<IReadOnlyList<string>> ListedRoots => [.. _listedRoots];

    /// <summary>MAM-43 — every folder walked on its own (<see cref="ListMediaFilesUnderAsync"/>), in call order.</summary>
    public IReadOnlyList<string> ListedFolders => [.. _listedFolders];

    /// <summary>The most reads that were under way at the same moment.</summary>
    public int MostReadsAtOnce => Volatile.Read(ref _mostReadsAtOnce);

    /// <summary>MAM-19 — every path read whole for its hash, in call order.</summary>
    public IReadOnlyList<string> HashedPaths => [.. _hashed];

    /// <summary>MAM-19 — the allowed roots each hash was given, in call order.</summary>
    public IReadOnlyList<IReadOnlyList<string>> HashRootsGiven => [.. _hashRootsGiven];

    /// <summary>MAM-19 — the most hashes that were under way at the same moment.</summary>
    public int MostHashesAtOnce => Volatile.Read(ref _mostHashesAtOnce);

    /// <summary>MAM-38 — every path some of whose pieces were hashed, in call order.</summary>
    public IReadOnlyList<string> PiecesHashedPaths => [.. _piecesHashed];

    /// <summary>MAM-20 — every device whose file system was asked for, in call order, as often as asked.</summary>
    public IReadOnlyList<long> DescribedDevices => [.. _describedDevices];

    /// <summary>
    /// MAM-20 — the device <paramref name="deviceId"/> holds a file system of <paramref name="type"/> (<c>btrfs</c>,
    /// <c>zfs</c>), or an unknown one when <c>null</c>, instead of <see cref="DefaultFileSystem"/>.
    /// </summary>
    public void SetFileSystem(long deviceId, string? type) => _fileSystems[deviceId] = type;

    /// <summary>
    /// A regular file at <paramref name="appPath"/> the file system describes with
    /// <paramref name="facts"/>; the media inspector reads <paramref name="media"/> from it, or fails
    /// with <see cref="UnreadableMediaError"/> when that is <c>null</c>. <paramref name="read"/> replaces
    /// the read's answer altogether (an inspector that can't start, a file that changed under the read).
    /// MAM-19 — read whole, it hashes to <see cref="DefaultSha256"/> of <paramref name="facts"/>, unless
    /// <paramref name="hash"/> replaces that answer (a copy's hash, <see cref="HashOf"/>, or a file that
    /// changed under the hash). Replaces whatever was at the path. MAM-26 — every folder on the way to it is a folder
    /// from then on.
    /// </summary>
    public void AddFile(
        string appPath,
        FileFacts facts,
        MediaFacts? media = null,
        FileInventoryResult<MediaInspection>? read = null,
        FileInventoryResult<FileContentHash>? hash = null)
    {
        _files[appPath] = new FakeFile(
            new FileInventoryResult<FileFacts>(FileInventoryStatus.Found, facts, null),
            read ?? (media is null
                ? new FileInventoryResult<MediaInspection>(FileInventoryStatus.MediaUnreadable, null, string.Format(UnreadableMediaError, appPath))
                : new FileInventoryResult<MediaInspection>(FileInventoryStatus.Found, new MediaInspection(facts, media), null)),
            hash ?? HashOf(facts, DefaultSha256(facts)),
            null);
        if (ParentOf(appPath) is { } folder)
            AddFolders(folder);
    }

    /// <summary>
    /// MAM-38 — a regular file at <paramref name="appPath"/> holding <paramref name="content"/>: described with
    /// <paramref name="facts"/>, or with facts of its length when <c>null</c>, and its pieces hashed from those
    /// bytes. Read by the inspector or hashed whole like a file <see cref="AddFile"/> planted without media facts.
    /// </summary>
    public void AddContent(string appPath, byte[] content, FileFacts? facts = null)
    {
        ArgumentNullException.ThrowIfNull(content);
        var described = facts ?? new FileFacts(content.LongLength, new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc), 2049, 9_000_000 + _files.Count, 1);
        AddFile(appPath, described);
        _files[appPath] = _files[appPath] with { Content = content };
    }

    /// <summary>A path the file inventory answers <paramref name="status"/> and <paramref name="error"/>
    /// for, looked at, read or hashed alike (a locked file, a folder, a link leading out of the roots).</summary>
    public void AddProblem(string appPath, FileInventoryStatus status, string error) =>
        _files[appPath] = new FakeFile(
            new FileInventoryResult<FileFacts>(status, null, error),
            new FileInventoryResult<MediaInspection>(status, null, error),
            new FileInventoryResult<FileContentHash>(status, null, error),
            null);

    /// <summary>MAM-19 — the answer of a whole read of a file with <paramref name="facts"/> whose bytes hash to
    /// <paramref name="sha256"/>: what <see cref="AddFile"/> takes to plant a byte-identical copy.</summary>
    public static FileInventoryResult<FileContentHash> HashOf(FileFacts facts, string sha256) =>
        new(FileInventoryStatus.Found, new FileContentHash(facts, sha256), null);

    /// <summary>
    /// MAM-19 — the hash a planted file's bytes have unless a test says otherwise: the SHA-256 of its identity,
    /// size and modification time, so the names of one file hash alike and two files never do.
    /// </summary>
    public static string DefaultSha256(FileFacts facts) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Create(
            CultureInfo.InvariantCulture, $"{facts.DeviceId}:{facts.Inode}:{facts.SizeBytes}:{facts.ModifiedUtc.Ticks}"))));

    /// <summary>
    /// MAM-18 — <paramref name="linkAppPath"/> is another name of the file at <paramref name="targetAppPath"/>,
    /// reached through a link inside the roots (a folder link, a symbolic link): the walk lists it with the
    /// target as where the file really is, and it is looked at and read as the target is, whatever is
    /// planted there when asked.
    /// </summary>
    public void AddLinkedName(string linkAppPath, string targetAppPath) => _linkedNames[linkAppPath] = targetAppPath;

    /// <summary>
    /// MAM-18 — a place the walk answers <paramref name="status"/> and <paramref name="error"/> for when it
    /// reaches <paramref name="appPath"/>: a folder it couldn't list now (<c>Unlisted</c>), one or a link it
    /// refuses (<c>Skipped</c>), or where it stopped at its limit (<c>LimitReached</c>).
    /// </summary>
    public void AddListingProblem(string appPath, FileListingStatus status, string error)
    {
        if (status == FileListingStatus.MediaFile)
            throw new ArgumentException("A media file is planted with AddFile, not as a listing problem.", nameof(status));
        _listingProblems[appPath] = new FileListingEntry(status, appPath, null, error);
    }

    /// <summary>Takes the file, or the linked name, at <paramref name="appPath"/> away. Its folder stays.</summary>
    public void Remove(string appPath)
    {
        _files.TryRemove(appPath, out _);
        _linkedNames.TryRemove(appPath, out _);
    }

    /// <summary>
    /// MAM-26 — renames the file at <paramref name="fromAppPath"/> to <paramref name="toAppPath"/>: everything planted for
    /// it — its identity above all — answers at the new name and nothing at the old one, and every folder on the way to
    /// the new name is a folder. Never over a name: throws when nothing is at the old name or something is at the new
    /// one, a test's own mistake. As a previous run's rename left a file, or what <see cref="FakeFileQuarantine"/> does.
    /// </summary>
    public void Move(string fromAppPath, string toAppPath)
    {
        if (_files.ContainsKey(toAppPath) || _linkedNames.ContainsKey(toAppPath))
            throw new InvalidOperationException($"Something is at “{toAppPath}” already; a rename never replaces it.");
        if (!_files.TryRemove(fromAppPath, out var file))
            throw new InvalidOperationException($"Nothing is planted at “{fromAppPath}” to move.");
        if (!_files.TryAdd(toAppPath, file))
        {
            _files[fromAppPath] = file;
            throw new InvalidOperationException($"Something is at “{toAppPath}” already; a rename never replaces it.");
        }

        if (ParentOf(toAppPath) is { } folder)
            AddFolders(folder);
    }

    /// <summary>MAM-26 — <paramref name="appPath"/> is a folder, empty or not, with every folder on the way to it.</summary>
    public void AddFolder(string appPath)
    {
        if (Parse(appPath) is { } folder)
            AddFolders(folder);
    }

    /// <summary>
    /// MAM-26 — the folder at <paramref name="appPath"/> is gone, with every folder, file, linked name and listing problem
    /// inside it: as the owner deleting it by hand.
    /// </summary>
    public void RemoveFolder(string appPath)
    {
        if (Parse(appPath) is not { } folder)
            return;

        foreach (var known in _folders.Keys.Where(known => known.IsUnder(folder)))
            _folders.TryRemove(known, out _);
        foreach (var path in _files.Keys.Where(path => IsAtOrUnder(path, folder)))
            _files.TryRemove(path, out _);
        foreach (var path in _linkedNames.Keys.Where(path => IsAtOrUnder(path, folder)))
            _linkedNames.TryRemove(path, out _);
        foreach (var path in _listingProblems.Keys.Where(path => IsAtOrUnder(path, folder)))
            _listingProblems.TryRemove(path, out _);
    }

    /// <summary>MAM-26 — whether <paramref name="appPath"/> is a folder now: one on the way to a file planted since the
    /// reset, or one a test added, and not removed.</summary>
    public bool IsFolder(string appPath) => Parse(appPath) is { } folder && IsFolder(folder);

    /// <inheritdoc cref="IsFolder(string)"/>
    internal bool IsFolder(NormalizedPath folder) => _folders.ContainsKey(folder);

    /// <summary>
    /// MAM-26 — what is planted at exactly <paramref name="appPath"/>, never through a linked name: the look's answer of
    /// a file or a planted problem, or <c>null</c> for nothing.
    /// </summary>
    internal FileInventoryResult<FileFacts>? PlantedAt(string appPath) => _files.TryGetValue(appPath, out var file) ? file.Describe : null;

    /// <summary>MAM-26 — whether <paramref name="appPath"/> is a linked name (<see cref="AddLinkedName"/>).</summary>
    internal bool IsLinkedName(string appPath) => _linkedNames.ContainsKey(appPath);

    /// <summary>
    /// MAM-26 — every planted regular file right inside <paramref name="folder"/> — a file the look finds, not a planted
    /// problem or a linked name — with its path and identity, by name (ordinal).
    /// </summary>
    internal IReadOnlyList<(string AppPath, FileFacts Facts)> FilesIn(NormalizedPath folder) =>
    [
        .. _files
            .Where(file => file.Value.Describe is { Status: FileInventoryStatus.Found, Value: not null }
                && ParentOf(file.Key) is { } parent
                && parent.IsSameAs(folder))
            .Select(file => (AppPath: file.Key, Facts: file.Value.Describe.Value!))
            .OrderBy(file => file.AppPath, StringComparer.Ordinal),
    ];

    /// <summary>
    /// MAM-26 — every name right inside <paramref name="folder"/>, whatever is planted there: a file, a planted problem, a
    /// linked name — one leading nowhere too —, or a folder; by name (ordinal).
    /// </summary>
    internal IReadOnlyList<string> NamesIn(NormalizedPath folder) =>
    [
        .. _files.Keys
            .Concat(_linkedNames.Keys)
            .Select(Parse)
            .OfType<NormalizedPath>()
            .Concat(_folders.Keys)
            .Where(path => path.Segments.Count > 0 && (path with { Segments = [.. path.Segments.SkipLast(1)] }).IsSameAs(folder))
            .Select(path => path.Segments[^1])
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal),
    ];

    /// <summary>No files, linked names, listing problems or (MAM-26) folders, no recorded calls, no hooks, identity
    /// reported, and (MAM-20) every device on <see cref="DefaultFileSystem"/>.</summary>
    public void Reset()
    {
        _files.Clear();
        _folders.Clear();
        _fileSystems.Clear();
        _describedDevices.Clear();
        _linkedNames.Clear();
        _listingProblems.Clear();
        _described.Clear();
        _read.Clear();
        _rootsGiven.Clear();
        _listedRoots.Clear();
        _listedFolders.Clear();
        _hashed.Clear();
        _hashRootsGiven.Clear();
        _piecesHashed.Clear();
        Volatile.Write(ref _mostReadsAtOnce, 0);
        Volatile.Write(ref _mostHashesAtOnce, 0);
        DuringRead = null;
        DuringListing = null;
        DuringHash = null;
        DuringPiecesHash = null;
        Capabilities = new FileInventoryCapabilities(ReportsFileIdentity: true);
    }

    public Task<FileInventoryResult<FileFacts>> DescribeAsync(
        string appPath, IReadOnlyList<string> allowedRoots, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _described.Enqueue(appPath);
        _rootsGiven.Enqueue(allowedRoots);
        return Task.FromResult(Answer(appPath, allowedRoots, file => file.Describe));
    }

    public async Task<FileInventoryResult<MediaInspection>> InspectMediaAsync(
        string appPath, IReadOnlyList<string> allowedRoots, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _read.Enqueue(appPath);
        _rootsGiven.Enqueue(allowedRoots);
        var underWay = Interlocked.Increment(ref _readsUnderWay);
        try
        {
            int most;
            do
            {
                most = Volatile.Read(ref _mostReadsAtOnce);
            }
            while (underWay > most && Interlocked.CompareExchange(ref _mostReadsAtOnce, underWay, most) != most);

            if (DuringRead is { } duringRead)
                await duringRead(appPath, cancellationToken);
            return Answer(appPath, allowedRoots, file => file.Read);
        }
        finally
        {
            Interlocked.Decrement(ref _readsUnderWay);
        }
    }

    public async Task<FileInventoryResult<FileContentHash>> HashAsync(
        string appPath, IReadOnlyList<string> allowedRoots, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _hashed.Enqueue(appPath);
        _hashRootsGiven.Enqueue(allowedRoots);
        var underWay = Interlocked.Increment(ref _hashesUnderWay);
        try
        {
            int most;
            do
            {
                most = Volatile.Read(ref _mostHashesAtOnce);
            }
            while (underWay > most && Interlocked.CompareExchange(ref _mostHashesAtOnce, underWay, most) != most);

            if (DuringHash is { } duringHash)
                await duringHash(appPath, cancellationToken);
            return Answer(appPath, allowedRoots, file => file.Hash);
        }
        finally
        {
            Interlocked.Decrement(ref _hashesUnderWay);
        }
    }

    /// <summary>
    /// MAM-38 — the pieces' hashes worked out from the bytes <see cref="AddContent"/> planted; a file planted without
    /// bytes is unreadable to it, and a piece past the planted bytes is a file that changed while it was read.
    /// </summary>
    public async Task<FileInventoryResult<FilePieceHashes>> HashPiecesAsync(
        string appPath, IReadOnlyList<string> allowedRoots, IReadOnlyList<FilePieceRange> pieces, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _piecesHashed.Enqueue(appPath);
        _rootsGiven.Enqueue(allowedRoots);
        if (DuringPiecesHash is { } duringPiecesHash)
            await duringPiecesHash(appPath, cancellationToken);

        var described = Answer(appPath, allowedRoots, file => file.Describe);
        if (described.Value is not { } facts)
            return new FileInventoryResult<FilePieceHashes>(described.Status, null, described.Error);

        var target = _linkedNames.GetValueOrDefault(appPath) ?? appPath;
        if (_files[target].Content is not { } content)
            return new FileInventoryResult<FilePieceHashes>(FileInventoryStatus.Unreadable, null, $"The test planted no bytes at “{appPath}”.");
        if (pieces.Any(piece => piece.Offset < 0 || piece.Offset + piece.Length > content.LongLength))
        {
            return new FileInventoryResult<FilePieceHashes>(
                FileInventoryStatus.ChangedWhileReading, null, $"“{appPath}” changed while the app was reading some of its pieces.");
        }

        var hashes = pieces
            .Select(piece => TorrentPieceHash.Of(content.AsSpan((int)piece.Offset, piece.Length), piece.Hashing, piece.TreeLeafCount))
            .ToList();
        return new FileInventoryResult<FilePieceHashes>(FileInventoryStatus.Found, new FilePieceHashes(facts, hashes), null);
    }

    /// <summary>
    /// MAM-43 — the walk of one folder: what <see cref="ListMediaFilesAsync"/> gives under it, after the text check of a
    /// look (a folder outside the roots gives one <c>Skipped</c> entry). Recorded in <see cref="ListedFolders"/>.
    /// </summary>
    public async IAsyncEnumerable<FileListingEntry> ListMediaFilesUnderAsync(
        string appFolder, IReadOnlyList<string> allowedRoots, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        _listedFolders.Enqueue(appFolder);
        if (!IsInside(appFolder, allowedRoots) || !NormalizedPath.TryParse(appFolder, out var folder, out _))
        {
            yield return new FileListingEntry(FileListingStatus.Skipped, appFolder, null, $"“{appFolder}” is outside the media folders the app may read.");
            yield break;
        }

        foreach (var entry in EntriesUnder(folder))
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return entry;
            if (entry.Status == FileListingStatus.LimitReached)
                yield break;
        }

        await Task.CompletedTask;
    }

    public async IAsyncEnumerable<FileListingEntry> ListMediaFilesAsync(
        IReadOnlyList<string> allowedRoots, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        _listedRoots.Enqueue(allowedRoots);
        // A path under two roots (one inside the other) is listed once, under the first.
        var listed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var root in allowedRoots)
        {
            if (!NormalizedPath.TryParse(root, out var rootPath, out _))
                continue;

            foreach (var entry in EntriesUnder(rootPath).Where(entry => listed.Add(entry.AppPath)))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (DuringListing is { } duringListing)
                    await duringListing(entry, cancellationToken);
                yield return entry;
                if (entry.Status == FileListingStatus.LimitReached)
                    yield break;
            }
        }
    }

    /// <summary>MAM-20 — each device's file system: <see cref="DefaultFileSystem"/> unless
    /// <see cref="SetFileSystem"/> said otherwise; every device asked is recorded.</summary>
    public Task<IReadOnlyList<DeviceFileSystem>> DescribeFileSystemsAsync(
        IReadOnlyCollection<long> deviceIds, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        foreach (var deviceId in deviceIds)
            _describedDevices.Enqueue(deviceId);
        return Task.FromResult<IReadOnlyList<DeviceFileSystem>>(
            [.. deviceIds.Select(deviceId => new DeviceFileSystem(deviceId, _fileSystems.TryGetValue(deviceId, out var type) ? type : DefaultFileSystem))]);
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    // Everything the walk of one root gives, in ordinal path order: the listing problems under it, and the
    // media file names under it that no Unlisted or Skipped place covers. MAM-26 — nothing in a quarantine, at any
    // depth, nor a linked name leading into one (ADR 0030): the real walk never enters such a folder, says nothing
    // of it, and gives nothing for a root inside one.
    private List<FileListingEntry> EntriesUnder(NormalizedPath root)
    {
        var problems = _listingProblems.Values.Where(problem => IsAtOrUnder(problem.AppPath, root) && !InQuarantine(problem.AppPath)).ToList();
        var hiding = problems
            .Where(problem => problem.Status is FileListingStatus.Unlisted or FileListingStatus.Skipped)
            .Select(problem => problem.AppPath)
            .ToList();
        var names = _files.Keys
            .Select(path => (Name: path, Target: path))
            .Concat(_linkedNames.Select(link => (Name: link.Key, Target: link.Value)))
            .Where(name => MediaFileNames.IsMediaFile(FileNameOf(name.Name))
                && IsAtOrUnder(name.Name, root)
                && !InQuarantine(name.Name)
                && !InQuarantine(name.Target)
                && !hiding.Any(place => IsAtOrUnder(name.Name, place)))
            .Select(name => new FileListingEntry(FileListingStatus.MediaFile, name.Name, name.Target, null));
        return [.. problems.Concat(names).OrderBy(entry => entry.AppPath, StringComparer.Ordinal)];
    }

    private FileInventoryResult<T> Answer<T>(string appPath, IReadOnlyList<string> allowedRoots, Func<FakeFile, FileInventoryResult<T>> pick)
        where T : class
    {
        // A linked name answers as the file it leads to, and never leads out of the roots.
        var target = _linkedNames.GetValueOrDefault(appPath) ?? appPath;
        if (!IsInside(appPath, allowedRoots) || !IsInside(target, allowedRoots))
            return new FileInventoryResult<T>(FileInventoryStatus.OutsideAllowedRoots, null, $"“{appPath}” is outside the media folders the app may read.");
        if (_files.TryGetValue(target, out var file))
            return pick(file);
        // MAM-26 — a folder is something, as on a disk: the look finds no regular file there, and says so.
        return Parse(target) is { } folder && IsFolder(folder)
            ? new FileInventoryResult<T>(FileInventoryStatus.NotARegularFile, null, $"“{appPath}” is a folder, not a file.")
            : new FileInventoryResult<T>(FileInventoryStatus.NotFound, null, $"Nothing is at “{appPath}”.");
    }

    private static bool IsInside(string appPath, IReadOnlyList<string> allowedRoots) =>
        allowedRoots.Any(root => NormalizedPath.TryParse(root, out var parsed, out _) && IsAtOrUnder(appPath, parsed));

    private static bool IsAtOrUnder(string appPath, NormalizedPath place) =>
        NormalizedPath.TryParse(appPath, out var path, out _) && path.IsUnder(place);

    private static bool IsAtOrUnder(string appPath, string place) =>
        NormalizedPath.TryParse(place, out var parsed, out _) && IsAtOrUnder(appPath, parsed);

    // The last name of a path in either style.
    private static string FileNameOf(string appPath) => appPath[(appPath.LastIndexOfAny(['/', '\\']) + 1)..];

    // MAM-26 — whether the path lies in a quarantine folder at any depth.
    private static bool InQuarantine(string appPath) => Parse(appPath) is { } path && QuarantineFolder.Holds(path);

    private static NormalizedPath? Parse(string appPath) => NormalizedPath.TryParse(appPath, out var path, out _) ? path : null;

    // The folder holding the path; null for a root or a path that doesn't parse.
    private static NormalizedPath? ParentOf(string appPath) =>
        Parse(appPath) is { Segments.Count: > 0 } path ? path with { Segments = [.. path.Segments.SkipLast(1)] } : null;

    // The folder and every folder on the way to it, its root included.
    private void AddFolders(NormalizedPath folder)
    {
        for (var count = 0; count <= folder.Segments.Count; count++)
            _folders.TryAdd(folder with { Segments = [.. folder.Segments.Take(count)] }, 0);
    }

    /// <summary>What the inventory answers for one path.</summary>
    private sealed record FakeFile(
        FileInventoryResult<FileFacts> Describe,
        FileInventoryResult<MediaInspection> Read,
        FileInventoryResult<FileContentHash> Hash,
        byte[]? Content);
}
