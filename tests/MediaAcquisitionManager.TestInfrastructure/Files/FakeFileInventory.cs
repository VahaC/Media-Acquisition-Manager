using System.Collections.Concurrent;
using MediaAcquisitionManager.Core.Abstractions.Files;
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
/// outside them, is <c>OutsideAllowedRoots</c>. Links are the local implementation's business and are
/// not modelled here.
/// </para>
/// <para>
/// Thread-safe: a run looks at and reads two files at a time. Every call is recorded, and
/// <see cref="MostReadsAtOnce"/> keeps how many reads overlapped.
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
    private int _readsUnderWay;
    private int _mostReadsAtOnce;

    /// <inheritdoc/>
    public FileInventoryCapabilities Capabilities { get; set; } = new(ReportsFileIdentity: true);

    /// <summary>
    /// Runs inside each read while it counts as under way, before it answers: a test holds reads open to
    /// watch the concurrency or the run's progress, or changes a file "while it is being read".
    /// </summary>
    public Func<string, CancellationToken, Task>? DuringRead { get; set; }

    /// <summary>Every path looked at, in call order.</summary>
    public IReadOnlyList<string> DescribedPaths => [.. _described];

    /// <summary>Every path read by the media inspector, in call order.</summary>
    public IReadOnlyList<string> ReadPaths => [.. _read];

    /// <summary>The allowed roots each call was given.</summary>
    public IReadOnlyList<IReadOnlyList<string>> RootsGiven => [.. _rootsGiven];

    /// <summary>The most reads that were under way at the same moment.</summary>
    public int MostReadsAtOnce => Volatile.Read(ref _mostReadsAtOnce);

    /// <summary>
    /// A regular file at <paramref name="appPath"/> the file system describes with
    /// <paramref name="facts"/>; the media inspector reads <paramref name="media"/> from it, or fails
    /// with <see cref="UnreadableMediaError"/> when that is <c>null</c>. <paramref name="read"/> replaces
    /// the read's answer altogether (an inspector that can't start, a file that changed under the read).
    /// Replaces whatever was at the path.
    /// </summary>
    public void AddFile(string appPath, FileFacts facts, MediaFacts? media = null, FileInventoryResult<MediaInspection>? read = null) =>
        _files[appPath] = new FakeFile(
            new FileInventoryResult<FileFacts>(FileInventoryStatus.Found, facts, null),
            read ?? (media is null
                ? new FileInventoryResult<MediaInspection>(FileInventoryStatus.MediaUnreadable, null, string.Format(UnreadableMediaError, appPath))
                : new FileInventoryResult<MediaInspection>(FileInventoryStatus.Found, new MediaInspection(facts, media), null)));

    /// <summary>A path the file inventory answers <paramref name="status"/> and <paramref name="error"/>
    /// for, looked at or read alike (a locked file, a folder, a link leading out of the roots).</summary>
    public void AddProblem(string appPath, FileInventoryStatus status, string error) =>
        _files[appPath] = new FakeFile(
            new FileInventoryResult<FileFacts>(status, null, error),
            new FileInventoryResult<MediaInspection>(status, null, error));

    /// <summary>Takes the file at <paramref name="appPath"/> away.</summary>
    public void Remove(string appPath) => _files.TryRemove(appPath, out _);

    /// <summary>No files, no recorded calls, no hook, and identity reported.</summary>
    public void Reset()
    {
        _files.Clear();
        _described.Clear();
        _read.Clear();
        _rootsGiven.Clear();
        Volatile.Write(ref _mostReadsAtOnce, 0);
        DuringRead = null;
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

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    private FileInventoryResult<T> Answer<T>(string appPath, IReadOnlyList<string> allowedRoots, Func<FakeFile, FileInventoryResult<T>> pick)
        where T : class
    {
        if (!IsInside(appPath, allowedRoots))
            return new FileInventoryResult<T>(FileInventoryStatus.OutsideAllowedRoots, null, $"“{appPath}” is outside the media folders the app may read.");
        return _files.TryGetValue(appPath, out var file)
            ? pick(file)
            : new FileInventoryResult<T>(FileInventoryStatus.NotFound, null, $"Nothing is at “{appPath}”.");
    }

    private static bool IsInside(string appPath, IReadOnlyList<string> allowedRoots) =>
        NormalizedPath.TryParse(appPath, out var path, out _)
        && allowedRoots.Any(root => NormalizedPath.TryParse(root, out var parsed, out _) && path.IsUnder(parsed));

    /// <summary>What the inventory answers for one path.</summary>
    private sealed record FakeFile(FileInventoryResult<FileFacts> Describe, FileInventoryResult<MediaInspection> Read);
}
