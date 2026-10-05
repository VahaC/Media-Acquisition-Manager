using System.Buffers;
using System.Security.Cryptography;
using MediaAcquisitionManager.Core.Abstractions.Files;
using MediaAcquisitionManager.Core.Files;
using MediaAcquisitionManager.Core.Paths;

namespace MediaAcquisitionManager.Infrastructure.Files;

/// <summary>
/// The app's read-only look at files on its own mounts (MAM-9): the <see cref="IFileInventory"/> Adapter over
/// the local file system and ffprobe.
/// </summary>
/// <remarks>
/// <para>
/// ADR 0009 — every look starts with the same checks (specification §4): the path must be a full path
/// inside an allowed root, folder by folder; every link on the way is followed (<see cref="LinkResolver"/>),
/// the allowed roots' own included, and the place the file really is must be inside one of them. Only then
/// is that link-free path opened by the platform's reader, which on Linux follows no link on the way, so
/// one swapped in since the checks leads nowhere; and ffprobe reads the file the reader opened, never the
/// path again. A link that leads out of the roots is never read through.
/// </para>
/// <para>
/// The roots are trusted only as far as they parse: one that doesn't, or that has another operating
/// system's style, is ignored, so a bad value can only narrow what is read, never widen it. Nothing here
/// writes, renames or deletes: the inventory is read-only by construction.
/// </para>
/// <para>
/// MAM-18, ADR 0021 — the walk of the allowed roots for media file names goes the same way, folder by folder
/// (<see cref="FolderWalker"/>).
/// </para>
/// <para>
/// MAM-19, ADR 0022 — the hash reads a file whole through the same checks and the same open file, its facts
/// asked again after the read as after ffprobe.
/// </para>
/// <para>
/// Every call reads the file system afresh and keeps nothing, so one instance serves every inspection at
/// once.
/// </para>
/// </remarks>
/// <param name="probe">Runs ffprobe.</param>
internal sealed class LocalFileInventory(IMediaProbeProcess probe) : IFileInventory
{
    /// <summary>The platform's reader, chosen once: Strategy (<see cref="IFileStatusReader"/>).</summary>
    private static readonly IFileStatusReader Reader =
        OperatingSystem.IsLinux() ? new LinuxFileStatusReader()
        : OperatingSystem.IsWindows() ? new WindowsFileStatusReader()
        : new PortableFileStatusReader();

    /// <inheritdoc/>
    public FileInventoryCapabilities Capabilities { get; } = new(Reader.ReportsFileIdentity);

    /// <summary>The folders one walk lists at most: <see cref="FolderWalker.MaximumFolders"/>; a test sets a few
    /// rather than make a hundred thousand.</summary>
    internal int MaximumListedFolders { get; init; } = FolderWalker.MaximumFolders;

    /// <summary>The entries one walk gives at most: <see cref="FolderWalker.MaximumEntries"/>; a test sets a
    /// few.</summary>
    internal int MaximumListedEntries { get; init; } = FolderWalker.MaximumEntries;

    /// <summary>
    /// MAM-19 — how much of a file one read of the hash takes: <see cref="HashChunkBytes"/>; a test sets a few
    /// bytes, so a small file is read in several.
    /// </summary>
    internal int HashChunkSize { get; init; } = HashChunkBytes;

    /// <summary>
    /// MAM-19 — called after each chunk the hash reads, with how many bytes it has read so far: where a test
    /// writes to the file in the middle of the read. <c>null</c> in the app.
    /// </summary>
    internal Action<long>? AfterHashChunk { get; init; }

    /// <summary>
    /// MAM-19 — 1 MiB per read: the hash reads a file from its start to its end, so a large read is one long
    /// sequential transfer from a spinning disk, eight times the Linux read-ahead window; more saves nothing
    /// measurable and holds more memory per file read.
    /// </summary>
    private const int HashChunkBytes = 1 << 20;

    /// <inheritdoc/>
    public Task<FileInventoryResult<FileFacts>> DescribeAsync(
        string appPath, IReadOnlyList<string> allowedRoots, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var located = Locate(appPath, allowedRoots);
        if (located.Value is not { } path)
            return Task.FromResult(FileSystemFailure.Relay<NormalizedPath, FileFacts>(located));

        // Opened, not only looked at: the open proves the app may read the file.
        var opened = Reader.Open(SystemPath.For(path), appPath);
        if (opened.Value is not { } file)
            return Task.FromResult(FileSystemFailure.Relay<IOpenedFile, FileFacts>(opened));

        using (file)
            return Task.FromResult(new FileInventoryResult<FileFacts>(FileInventoryStatus.Found, file.Facts, null));
    }

    /// <inheritdoc/>
    /// <remarks>ffprobe reads the file the checks approved and the reader opened, held open meanwhile.</remarks>
    public async Task<FileInventoryResult<MediaInspection>> InspectMediaAsync(
        string appPath, IReadOnlyList<string> allowedRoots, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var located = Locate(appPath, allowedRoots);
        if (located.Value is not { } path)
            return FileSystemFailure.Relay<NormalizedPath, MediaInspection>(located);

        var opened = Reader.Open(SystemPath.For(path), appPath);
        if (opened.Value is not { } file)
            return FileSystemFailure.Relay<IOpenedFile, MediaInspection>(opened);

        using var _ = file;
        var facts = file.Facts;
        var probed = await probe.RunAsync(file.ProbePath, cancellationToken);
        if (probed.Status == MediaProbeStatus.Unavailable)
        {
            return FileSystemFailure.Refused<MediaInspection>(
                FileInventoryStatus.InspectorUnavailable, FileInventoryMessages.InspectorUnavailable(probed.Reason ?? ""));
        }

        // Asked again after ffprobe, and before its answer is judged: a file still being written makes ffprobe
        // fail too, and then the change is the real story. What was read is kept only under the facts it was
        // read from — a version stored under the other version's key would be served for the wrong file.
        var after = file.ReadFactsAgain(appPath);
        if (after.Value is not { } factsAfter || !IsSameVersion(facts, factsAfter))
        {
            return FileSystemFailure.Refused<MediaInspection>(
                FileInventoryStatus.ChangedWhileReading, FileInventoryMessages.ChangedWhileReading(appPath));
        }

        if (probed.Status != MediaProbeStatus.Succeeded || probed.Output is null)
        {
            return FileSystemFailure.Refused<MediaInspection>(
                FileInventoryStatus.MediaUnreadable, FileInventoryMessages.MediaUnreadable(appPath, probed.Reason ?? "it gave no answer"));
        }

        if (!FfprobeOutputParser.TryParse(probed.Output, out var media))
        {
            return FileSystemFailure.Refused<MediaInspection>(
                FileInventoryStatus.MediaUnreadable, FileInventoryMessages.MediaUnreadable(appPath, "its answer wasn't the JSON ffprobe writes"));
        }

        return new FileInventoryResult<MediaInspection>(FileInventoryStatus.Found, new MediaInspection(facts, media), null);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// MAM-19, ADR 0022 — the bytes come from the file the checks approved and the reader opened, held open while
    /// they are read, never from the path again (where the platform holds one open). Nothing is cached here: the
    /// caller keeps the hash under the facts it comes with.
    /// </remarks>
    public async Task<FileInventoryResult<FileContentHash>> HashAsync(
        string appPath, IReadOnlyList<string> allowedRoots, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var located = Locate(appPath, allowedRoots);
        if (located.Value is not { } path)
            return FileSystemFailure.Relay<NormalizedPath, FileContentHash>(located);

        var systemPath = SystemPath.For(path);
        var opened = Reader.Open(systemPath, appPath);
        if (opened.Value is not { } file)
            return FileSystemFailure.Relay<IOpenedFile, FileContentHash>(opened);

        using var _ = file;
        var facts = file.Facts;
        FileInventoryResult<FileContentHash>? failed = null;
        (string Sha256, long Length)? read = null;
        try
        {
            read = await ReadWholeAsync(file, cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // On Windows .NET ends the message with the handle's \\?\ path, which isn't the path the owner knows.
            failed = FileSystemFailure.For<FileContentHash>(exception, appPath, systemPath);
        }

        // Asked again before the read is judged, as after ffprobe: a file still being written can fail the read
        // too, and then the change is the real story. The read must also have ended where the facts say the file
        // does, or bytes came and went between the two looks. A hash is kept only under the facts it was read
        // from — one stored under the other version's key would call two different files identical.
        var after = file.ReadFactsAgain(appPath);
        if (after.Value is not { } factsAfter
            || !IsSameVersion(facts, factsAfter)
            || (read is { } whole && whole.Length != facts.SizeBytes))
        {
            return FileSystemFailure.Refused<FileContentHash>(
                FileInventoryStatus.ChangedWhileReading, FileInventoryMessages.ChangedWhileHashing(appPath));
        }

        return failed ?? new FileInventoryResult<FileContentHash>(
            FileInventoryStatus.Found, new FileContentHash(facts, read!.Value.Sha256), null);
    }

    /// <inheritdoc/>
    /// <remarks>The roots are trusted as for a look at one file: only those that parse as this platform's paths
    /// are walked.</remarks>
    public IAsyncEnumerable<FileListingEntry> ListMediaFilesAsync(
        IReadOnlyList<string> allowedRoots, CancellationToken cancellationToken = default) =>
        FolderWalker.WalkAsync(UsableRoots(allowedRoots), Reader, MaximumListedFolders, MaximumListedEntries, cancellationToken);

    /// <summary>
    /// Where <paramref name="appPath"/> really is, once every check has passed: inside an allowed root by its
    /// text, every link on the way followed, and still inside an allowed root (a root that is itself a link
    /// counts as where it leads).
    /// </summary>
    private static FileInventoryResult<NormalizedPath> Locate(string appPath, IReadOnlyList<string> allowedRoots)
    {
        var roots = UsableRoots(allowedRoots);
        if (roots.Count == 0)
            return FileSystemFailure.Refused<NormalizedPath>(FileInventoryStatus.OutsideAllowedRoots, FileInventoryMessages.NoAllowedRoots);

        if (!NormalizedPath.TryParse(appPath, out var path, out var error))
        {
            return FileSystemFailure.Refused<NormalizedPath>(
                FileInventoryStatus.OutsideAllowedRoots,
                error == PathParseError.DotSegment ? FileInventoryMessages.DotSegment(appPath) : FileInventoryMessages.NotAFullPath(appPath));
        }

        // By text first: a path outside every root is refused without touching the disk at all.
        if (!roots.Any(path.IsUnder))
            return FileSystemFailure.Refused<NormalizedPath>(FileInventoryStatus.OutsideAllowedRoots, FileInventoryMessages.OutsideAllowedRoots(appPath));

        var resolved = LinkResolver.Resolve(path, appPath);
        if (resolved.Value is not { } resolvedPath)
            return resolved;

        // A root that can't be resolved (gone, unreadable) allows nothing.
        var resolvedRoots = roots
            .Select(root => LinkResolver.Resolve(root, root.ToString()).Value)
            .OfType<NormalizedPath>();
        return resolvedRoots.Any(resolvedPath.IsUnder)
            ? resolved
            : FileSystemFailure.Refused<NormalizedPath>(
                FileInventoryStatus.OutsideAllowedRoots, FileInventoryMessages.LinkLeadsOut(appPath, resolvedPath.ToString()));
    }

    /// <summary>
    /// The allowed roots that parse as paths of this platform's style, in their order; the others are dropped,
    /// so a bad value can only narrow what is read, never widen it.
    /// </summary>
    private static List<NormalizedPath> UsableRoots(IReadOnlyList<string> allowedRoots) =>
    [
        .. allowedRoots
            .Select(root => NormalizedPath.TryParse(root, out var parsed, out _) ? parsed : null)
            .OfType<NormalizedPath>()
            .Where(root => root.Style == AllowedRootRules.PlatformStyle),
    ];

    /// <summary>
    /// MAM-19 — every byte of <paramref name="file"/>, from the first, through SHA-256: the hash in lowercase hex
    /// and how many bytes there were.
    /// </summary>
    private async Task<(string Sha256, long Length)> ReadWholeAsync(IOpenedFile file, CancellationToken cancellationToken)
    {
        using var sha256 = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = ArrayPool<byte>.Shared.Rent(HashChunkSize);
        try
        {
            long length = 0;
            while (await file.ReadAsync(buffer.AsMemory(0, HashChunkSize), length, cancellationToken) is var count and > 0)
            {
                sha256.AppendData(buffer, 0, count);
                length += count;
                AfterHashChunk?.Invoke(length);
            }

            return (Convert.ToHexStringLower(sha256.GetHashAndReset()), length);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>
    /// The same version of the file: same identity, size and modification time — the cache key. The link count
    /// is left out; a second name made meanwhile changes nothing about the content.
    /// </summary>
    private static bool IsSameVersion(FileFacts before, FileFacts after) =>
        before.SizeBytes == after.SizeBytes
        && before.ModifiedUtc == after.ModifiedUtc
        && before.DeviceId == after.DeviceId
        && before.Inode == after.Inode;
}
