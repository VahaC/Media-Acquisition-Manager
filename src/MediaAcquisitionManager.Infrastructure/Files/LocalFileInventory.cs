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

    /// <summary>
    /// Where <paramref name="appPath"/> really is, once every check has passed: inside an allowed root by its
    /// text, every link on the way followed, and still inside an allowed root (a root that is itself a link
    /// counts as where it leads).
    /// </summary>
    private static FileInventoryResult<NormalizedPath> Locate(string appPath, IReadOnlyList<string> allowedRoots)
    {
        var roots = allowedRoots
            .Select(root => NormalizedPath.TryParse(root, out var parsed, out _) ? parsed : null)
            .OfType<NormalizedPath>()
            .Where(root => root.Style == AllowedRootRules.PlatformStyle)
            .ToList();
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
    /// The same version of the file: same identity, size and modification time — the cache key. The link count
    /// is left out; a second name made meanwhile changes nothing about the content.
    /// </summary>
    private static bool IsSameVersion(FileFacts before, FileFacts after) =>
        before.SizeBytes == after.SizeBytes
        && before.ModifiedUtc == after.ModifiedUtc
        && before.DeviceId == after.DeviceId
        && before.Inode == after.Inode;
}
