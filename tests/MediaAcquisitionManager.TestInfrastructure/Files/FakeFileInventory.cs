using System.Collections.Concurrent;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using MediaAcquisitionManager.Core.Abstractions.Files;
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
/// Thread-safe: a run looks at and reads two files at a time. Every call is recorded, and
/// <see cref="MostReadsAtOnce"/> and <see cref="MostHashesAtOnce"/> keep how many reads and hashes overlapped.
/// </para>
/// </remarks>
public sealed class FakeFileInventory : IFileInventory
{
    /// <summary>What the inspector says about a file a test added without media facts.</summary>
    public const string UnreadableMediaError = "ffprobe couldn't read “{0}” as media: Invalid data found when processing input.";

    private readonly ConcurrentDictionary<string, FakeFile> _files = new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<string> _described = new();
    private readonly ConcurrentQueue<string> _read = new();
    private readonly ConcurrentQueue<IReadOnlyList<string>> _rootsGiven = new();
    private readonly ConcurrentDictionary<string, string> _linkedNames = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, FileListingEntry> _listingProblems = new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<IReadOnlyList<string>> _listedRoots = new();
    private readonly ConcurrentQueue<string> _hashed = new();
    private readonly ConcurrentQueue<IReadOnlyList<string>> _hashRootsGiven = new();
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

    /// <summary>Every path looked at, in call order.</summary>
    public IReadOnlyList<string> DescribedPaths => [.. _described];

    /// <summary>Every path read by the media inspector, in call order.</summary>
    public IReadOnlyList<string> ReadPaths => [.. _read];

    /// <summary>The allowed roots each look and read was given.</summary>
    public IReadOnlyList<IReadOnlyList<string>> RootsGiven => [.. _rootsGiven];

    /// <summary>MAM-18 — the allowed roots each walk was given, in call order: one entry per walk.</summary>
    public IReadOnlyList<IReadOnlyList<string>> ListedRoots => [.. _listedRoots];

    /// <summary>The most reads that were under way at the same moment.</summary>
    public int MostReadsAtOnce => Volatile.Read(ref _mostReadsAtOnce);

    /// <summary>MAM-19 — every path read whole for its hash, in call order.</summary>
    public IReadOnlyList<string> HashedPaths => [.. _hashed];

    /// <summary>MAM-19 — the allowed roots each hash was given, in call order.</summary>
    public IReadOnlyList<IReadOnlyList<string>> HashRootsGiven => [.. _hashRootsGiven];

    /// <summary>MAM-19 — the most hashes that were under way at the same moment.</summary>
    public int MostHashesAtOnce => Volatile.Read(ref _mostHashesAtOnce);

    /// <summary>
    /// A regular file at <paramref name="appPath"/> the file system describes with
    /// <paramref name="facts"/>; the media inspector reads <paramref name="media"/> from it, or fails
    /// with <see cref="UnreadableMediaError"/> when that is <c>null</c>. <paramref name="read"/> replaces
    /// the read's answer altogether (an inspector that can't start, a file that changed under the read).
    /// MAM-19 — read whole, it hashes to <see cref="DefaultSha256"/> of <paramref name="facts"/>, unless
    /// <paramref name="hash"/> replaces that answer (a copy's hash, <see cref="HashOf"/>, or a file that
    /// changed under the hash). Replaces whatever was at the path.
    /// </summary>
    public void AddFile(
        string appPath,
        FileFacts facts,
        MediaFacts? media = null,
        FileInventoryResult<MediaInspection>? read = null,
        FileInventoryResult<FileContentHash>? hash = null) =>
        _files[appPath] = new FakeFile(
            new FileInventoryResult<FileFacts>(FileInventoryStatus.Found, facts, null),
            read ?? (media is null
                ? new FileInventoryResult<MediaInspection>(FileInventoryStatus.MediaUnreadable, null, string.Format(UnreadableMediaError, appPath))
                : new FileInventoryResult<MediaInspection>(FileInventoryStatus.Found, new MediaInspection(facts, media), null)),
            hash ?? HashOf(facts, DefaultSha256(facts)));

    /// <summary>A path the file inventory answers <paramref name="status"/> and <paramref name="error"/>
    /// for, looked at, read or hashed alike (a locked file, a folder, a link leading out of the roots).</summary>
    public void AddProblem(string appPath, FileInventoryStatus status, string error) =>
        _files[appPath] = new FakeFile(
            new FileInventoryResult<FileFacts>(status, null, error),
            new FileInventoryResult<MediaInspection>(status, null, error),
            new FileInventoryResult<FileContentHash>(status, null, error));

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

    /// <summary>Takes the file, or the linked name, at <paramref name="appPath"/> away.</summary>
    public void Remove(string appPath)
    {
        _files.TryRemove(appPath, out _);
        _linkedNames.TryRemove(appPath, out _);
    }

    /// <summary>No files, linked names or listing problems, no recorded calls, no hooks, and identity
    /// reported.</summary>
    public void Reset()
    {
        _files.Clear();
        _linkedNames.Clear();
        _listingProblems.Clear();
        _described.Clear();
        _read.Clear();
        _rootsGiven.Clear();
        _listedRoots.Clear();
        _hashed.Clear();
        _hashRootsGiven.Clear();
        Volatile.Write(ref _mostReadsAtOnce, 0);
        Volatile.Write(ref _mostHashesAtOnce, 0);
        DuringRead = null;
        DuringListing = null;
        DuringHash = null;
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

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    // Everything the walk of one root gives, in ordinal path order: the listing problems under it, and the
    // media file names under it that no Unlisted or Skipped place covers.
    private List<FileListingEntry> EntriesUnder(NormalizedPath root)
    {
        var problems = _listingProblems.Values.Where(problem => IsAtOrUnder(problem.AppPath, root)).ToList();
        var hiding = problems
            .Where(problem => problem.Status is FileListingStatus.Unlisted or FileListingStatus.Skipped)
            .Select(problem => problem.AppPath)
            .ToList();
        var names = _files.Keys
            .Select(path => (Name: path, Target: path))
            .Concat(_linkedNames.Select(link => (Name: link.Key, Target: link.Value)))
            .Where(name => MediaFileNames.IsMediaFile(FileNameOf(name.Name))
                && IsAtOrUnder(name.Name, root)
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
        return _files.TryGetValue(target, out var file)
            ? pick(file)
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

    /// <summary>What the inventory answers for one path.</summary>
    private sealed record FakeFile(
        FileInventoryResult<FileFacts> Describe, FileInventoryResult<MediaInspection> Read, FileInventoryResult<FileContentHash> Hash);
}
