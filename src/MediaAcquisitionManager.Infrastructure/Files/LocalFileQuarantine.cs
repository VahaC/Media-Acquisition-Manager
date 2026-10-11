using MediaAcquisitionManager.Core.Abstractions.Files;
using MediaAcquisitionManager.Core.Files;
using MediaAcquisitionManager.Core.Paths;

namespace MediaAcquisitionManager.Infrastructure.Files;

/// <summary>
/// MAM-26 — the app's one way of writing to the media folders: the <see cref="IFileQuarantine"/> Adapter over the
/// local file system, which renames a replaced copy's names into the quarantine and back and deletes what sits there
/// (ADR 0030, ADR 0033).
/// </summary>
/// <remarks>
/// <para>
/// Every path goes through the checks of a look first (<see cref="LocalFileInventory.Locate"/>, ADR 0009): inside an
/// allowed root by its text, every link on the way followed, and still inside one — for a move, still inside the root
/// whose quarantine takes the file, so what a move put there a restore can reach back for. What is checked is the folder
/// a name is in, which must not lie in a quarantine wherever its links lead; the name itself is never followed, so a link
/// at it is a link, which never moves. The quarantine side is stricter still: it is <c>.mam-quarantine</c> at the top of
/// the allowed root that holds the copy, reached from that root one folder at a time with no link followed at all.
/// </para>
/// <para>
/// On Linux a write is one <c>renameat2(RENAME_NOREPLACE)</c> or one <c>unlinkat</c> through the <c>O_PATH</c>
/// folder descriptors the checks opened (<see cref="LinuxCalls"/>), after the name's size, modification time, device
/// and inode were read again through them and matched what the caller expected, and the two folders were found on one
/// mount (<c>STATX_MNT_ID</c>) — a link swapped in after the checks leads nowhere, a name already there is never
/// replaced, and nothing is ever copied. Right before the call the paths are walked again and must still lead to the
/// folders held, so the path the caller records is where the name really was. After a rename the name must show the
/// same device and inode, and both folders are flushed to disk.
/// </para>
/// <para>
/// Anywhere else every write refuses (<see cref="FileQuarantineCapabilities.CanRename"/> is false): .NET has no rename
/// that never replaces and never copies, and <c>File.Move</c>, <c>FileInfo.MoveTo</c> and <c>Directory.Move</c> are
/// never used here (ADR 0030). The listing of a folder works everywhere, so a development box shows what would move.
/// </para>
/// <para>
/// Every call reads the file system afresh and keeps nothing, so one instance serves every job at once. Nothing here
/// creates a folder but the quarantine's own, removes a folder, or changes an owner or a mode.
/// </para>
/// </remarks>
internal sealed partial class LocalFileQuarantine : IFileQuarantine
{
    /// <summary>The platform's reader for the listing anywhere but Linux, where the listing reads through the folder's
    /// descriptor instead: Strategy (<see cref="IFileStatusReader"/>).</summary>
    private static readonly IFileStatusReader Reader =
        OperatingSystem.IsWindows() ? new WindowsFileStatusReader() : new PortableFileStatusReader();

    /// <inheritdoc/>
    public FileQuarantineCapabilities Capabilities { get; } = new(OperatingSystem.IsLinux());

    /// <summary>
    /// Called with the path of the name about to be renamed or deleted, after every check and right before the paths are
    /// walked again for the call: where a test swaps a folder or the file. <c>null</c> in the app.
    /// </summary>
    internal Action<string>? BeforeWrite { get; init; }

    /// <summary>
    /// Called with the path of the name about to be renamed after the last look of all, right before the rename itself:
    /// where a test swaps the file in the one moment no look can see, to find what the rename then did. <c>null</c> in
    /// the app.
    /// </summary>
    internal Action<string>? BeforeRenameCall { get; init; }

    /// <summary>
    /// The mount table <see cref="DescribeRenamePlacesAsync"/> reads the mounts' options from: the process's own; a test
    /// points it at a file of its own, or at one that isn't there.
    /// </summary>
    internal string MountInfoPath { get; init; } = "/proc/self/mountinfo";

    /// <inheritdoc/>
    public Task<FileQuarantineResult<IReadOnlyList<FolderFile>>> ListFolderFilesAsync(
        string fileAppPath, IReadOnlyList<string> allowedRoots, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var parsed = Parsed(fileAppPath);
        if (parsed.Value is not { } file)
            return Task.FromResult(Relay<NormalizedPath, IReadOnlyList<FolderFile>>(parsed));
        if (file.Segments.Count == 0)
            return Task.FromResult(Refused<IReadOnlyList<FolderFile>>(FileQuarantineStatus.Changed, FileQuarantineMessages.NotARegularFile(fileAppPath)));

        var folder = FolderOf(file);
        var located = Located(folder, allowedRoots, FileQuarantineMessages.NotFound(fileAppPath));
        if (located.Value is not { } resolvedFolder)
            return Task.FromResult(Relay<NormalizedPath, IReadOnlyList<FolderFile>>(located));
        if (LiesInQuarantine(resolvedFolder, allowedRoots))
            return Task.FromResult(Refused<IReadOnlyList<FolderFile>>(FileQuarantineStatus.OutsideAllowedRoots, FileQuarantineMessages.AlreadyInQuarantine(fileAppPath)));

        return Task.FromResult(OperatingSystem.IsLinux()
            ? ListOnLinux(folder, resolvedFolder)
            : ListByPath(folder, resolvedFolder));
    }

    /// <inheritdoc/>
    public Task<FileQuarantineResult<IReadOnlyList<string>>> ListFolderNamesAsync(
        string fileAppPath, IReadOnlyList<string> allowedRoots, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var parsed = Parsed(fileAppPath);
        if (parsed.Value is not { } file)
            return Task.FromResult(Relay<NormalizedPath, IReadOnlyList<string>>(parsed));
        if (file.Segments.Count == 0)
            return Task.FromResult(Refused<IReadOnlyList<string>>(FileQuarantineStatus.Changed, FileQuarantineMessages.NotARegularFile(fileAppPath)));

        var folder = FolderOf(file);
        var located = Located(folder, allowedRoots, FileQuarantineMessages.FolderGone(folder.ToString()));
        if (located.Value is not { } resolvedFolder)
            return Task.FromResult(Relay<NormalizedPath, IReadOnlyList<string>>(located));
        if (LiesInQuarantine(resolvedFolder, allowedRoots))
            return Task.FromResult(Refused<IReadOnlyList<string>>(FileQuarantineStatus.OutsideAllowedRoots, FileQuarantineMessages.AlreadyInQuarantine(fileAppPath)));

        return Task.FromResult(OperatingSystem.IsLinux()
            ? NamesOnLinux(folder, resolvedFolder)
            : NamesIn(folder.ToString(), SystemPath.For(resolvedFolder)));
    }

    /// <inheritdoc/>
    public Task<FileQuarantineResult<FileFacts>> MoveIntoQuarantineAsync(
        string appPath, FileFacts expected, string quarantinePath, IReadOnlyList<string> allowedRoots,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(expected);
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsLinux())
            return Task.FromResult(Refused<FileFacts>(FileQuarantineStatus.NotSupported, FileQuarantineMessages.NotLinux));

        var ends = Ends(appPath, quarantinePath, allowedRoots);
        if (ends.Value is not { } pair)
            return Task.FromResult(Relay<QuarantineEnds, FileFacts>(ends));

        // Within the quarantine's own root alone (ADR 0030: "at the top of the allowed folder that holds it"): a library
        // folder that is a link into another allowed root holds a file that root's quarantine is for, and a Restore, which
        // reaches back through its own root only, could never put it back.
        string[] ownRoot = [pair.Root.ToString()];
        var source = Located(FolderOf(pair.AppPath), ownRoot, FileQuarantineMessages.NotFound(appPath));
        if (source.Value is not { } sourceFolder)
            return Task.FromResult(Relay<NormalizedPath, FileFacts>(source));
        if (LiesInQuarantine(sourceFolder, ownRoot))
            return Task.FromResult(Refused<FileFacts>(FileQuarantineStatus.OutsideAllowedRoots, FileQuarantineMessages.AlreadyInQuarantine(appPath)));

        return Task.FromResult(MoveOnLinux(pair, expected, sourceFolder, cancellationToken));
    }

    /// <inheritdoc/>
    public Task<FileQuarantineResult<FileFacts>> RestoreFromQuarantineAsync(
        string quarantinePath, FileFacts expected, string appPath, IReadOnlyList<string> allowedRoots,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(expected);
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsLinux())
            return Task.FromResult(Refused<FileFacts>(FileQuarantineStatus.NotSupported, FileQuarantineMessages.NotLinux));

        var ends = Ends(appPath, quarantinePath, allowedRoots);
        if (ends.Value is not { } pair)
            return Task.FromResult(Relay<QuarantineEnds, FileFacts>(ends));

        // The folder it goes back into must still be there: no folder is ever created on this side (ADR 0030).
        var folder = FolderOf(pair.AppPath);
        var destination = Located(folder, allowedRoots, FileQuarantineMessages.FolderGone(folder.ToString()));
        if (destination.Status == FileQuarantineStatus.NotFound)
            return Task.FromResult(Refused<FileFacts>(FileQuarantineStatus.FolderGone, destination.Error!));
        if (destination.Value is not { } destinationFolder)
            return Task.FromResult(Relay<NormalizedPath, FileFacts>(destination));
        if (LiesInQuarantine(destinationFolder, allowedRoots))
            return Task.FromResult(Refused<FileFacts>(FileQuarantineStatus.OutsideAllowedRoots, FileQuarantineMessages.AlreadyInQuarantine(appPath)));

        return Task.FromResult(RestoreOnLinux(pair, expected, destinationFolder, cancellationToken));
    }

    /// <inheritdoc/>
    public Task<FileQuarantineResult<FileFacts>> DeleteFromQuarantineAsync(
        string quarantinePath, FileFacts expected, IReadOnlyList<string> allowedRoots,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(expected);
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsLinux())
            return Task.FromResult(Refused<FileFacts>(FileQuarantineStatus.NotSupported, FileQuarantineMessages.NotLinux));

        var roots = LocalFileInventory.UsableRoots(allowedRoots);
        if (roots.Count == 0)
            return Task.FromResult(Refused<FileFacts>(FileQuarantineStatus.OutsideAllowedRoots, FileInventoryMessages.NoAllowedRoots));
        var parsed = Parsed(quarantinePath);
        if (parsed.Value is not { } quarantined)
            return Task.FromResult(Relay<NormalizedPath, FileFacts>(parsed));
        if (QuarantineRootOf(quarantined, roots) is not { } root)
            return Task.FromResult(Refused<FileFacts>(FileQuarantineStatus.OutsideAllowedRoots, FileQuarantineMessages.NotInQuarantine(quarantinePath)));

        var resolvedRoot = ResolvedRoot(root);
        if (resolvedRoot.Value is not { } rootPath)
            return Task.FromResult(Relay<NormalizedPath, FileFacts>(resolvedRoot));

        return Task.FromResult(DeleteOnLinux(quarantined, root, rootPath, expected, cancellationToken));
    }

    // ── the checks before the disk ─────────────────────────────────────────────────────────────

    /// <summary>
    /// The two ends of a move or a restore, checked by their text: the quarantine path lies in the quarantine of an
    /// allowed root, and the library path is a file inside that same root and in no quarantine.
    /// </summary>
    private static FileQuarantineResult<QuarantineEnds> Ends(string appPath, string quarantinePath, IReadOnlyList<string> allowedRoots)
    {
        var roots = LocalFileInventory.UsableRoots(allowedRoots);
        if (roots.Count == 0)
            return Refused<QuarantineEnds>(FileQuarantineStatus.OutsideAllowedRoots, FileInventoryMessages.NoAllowedRoots);
        var parsedPath = Parsed(appPath);
        if (parsedPath.Value is not { } library)
            return Relay<NormalizedPath, QuarantineEnds>(parsedPath);
        var parsedQuarantinePath = Parsed(quarantinePath);
        if (parsedQuarantinePath.Value is not { } quarantined)
            return Relay<NormalizedPath, QuarantineEnds>(parsedQuarantinePath);

        if (QuarantineRootOf(quarantined, roots) is not { } root)
            return Refused<QuarantineEnds>(FileQuarantineStatus.OutsideAllowedRoots, FileQuarantineMessages.NotInQuarantine(quarantinePath));
        if (QuarantineFolder.Holds(library))
            return Refused<QuarantineEnds>(FileQuarantineStatus.OutsideAllowedRoots, FileQuarantineMessages.AlreadyInQuarantine(appPath));
        if (!library.IsUnder(root) || library.Segments.Count == root.Segments.Count)
        {
            return Refused<QuarantineEnds>(
                FileQuarantineStatus.OutsideAllowedRoots, FileQuarantineMessages.NotBesideQuarantine(appPath, QuarantineFolder.Of(root).ToString()));
        }

        var resolvedRoot = ResolvedRoot(root);
        if (resolvedRoot.Value is not { } rootPath)
            return Relay<NormalizedPath, QuarantineEnds>(resolvedRoot);

        return Done(new QuarantineEnds(library, quarantined, root, rootPath));
    }

    /// <summary>
    /// The allowed root whose quarantine holds <paramref name="path"/>: it lies in
    /// <c>&lt;root&gt;/.mam-quarantine/&lt;job&gt;/</c>, with a name below the job's folder and no second quarantine
    /// on the way — what <see cref="QuarantineFolder.PathOf"/> makes, and nothing else.
    /// </summary>
    private static NormalizedPath? QuarantineRootOf(NormalizedPath path, IReadOnlyList<NormalizedPath> roots) =>
        roots.FirstOrDefault(root =>
            path.IsUnder(root)
            && path.SegmentsAfter(root) is { Count: >= 3 } relative
            && QuarantineFolder.IsQuarantineName(relative[0], path.Style)
            && !relative.Skip(1).Any(segment => QuarantineFolder.IsQuarantineName(segment, path.Style)));

    /// <summary>The path parsed, or the sentence of a look at it that couldn't be.</summary>
    private static FileQuarantineResult<NormalizedPath> Parsed(string appPath) =>
        NormalizedPath.TryParse(appPath, out var path, out var error)
            ? Done(path)
            : Refused<NormalizedPath>(
                FileQuarantineStatus.OutsideAllowedRoots,
                error == PathParseError.DotSegment ? FileInventoryMessages.DotSegment(appPath) : FileInventoryMessages.NotAFullPath(appPath));

    /// <summary>
    /// Where the folder <paramref name="folder"/> really is, through the checks of a look; a folder that isn't there
    /// answers with <paramref name="notFound"/>, the sentence about what the caller wanted of it.
    /// </summary>
    private static FileQuarantineResult<NormalizedPath> Located(NormalizedPath folder, IReadOnlyList<string> allowedRoots, string notFound)
    {
        var located = LocalFileInventory.Locate(folder.ToString(), allowedRoots);
        return located.Status switch
        {
            FileInventoryStatus.Found => Done(located.Value!),
            FileInventoryStatus.NotFound => Refused<NormalizedPath>(FileQuarantineStatus.NotFound, notFound),
            _ => FromInventory<NormalizedPath>(located.Status, located.Error),
        };
    }

    /// <summary>
    /// Whether <paramref name="resolvedFolder"/>, where a library folder really is, lies in a quarantine below the allowed
    /// root that holds it: a link planted into a quarantine, which the scan never follows either (ADR 0030). Only the names
    /// below the root count, so an odd name above it can't refuse every folder of the root.
    /// </summary>
    private static bool LiesInQuarantine(NormalizedPath resolvedFolder, IReadOnlyList<string> allowedRoots) =>
        LocalFileInventory.UsableRoots(allowedRoots)
            .Select(root => LinkResolver.Resolve(root, root.ToString()).Value)
            .OfType<NormalizedPath>()
            .Where(resolvedFolder.IsUnder)
            .Any(root => resolvedFolder.SegmentsAfter(root).Any(segment => QuarantineFolder.IsQuarantineName(segment, resolvedFolder.Style)));

    /// <summary>Where the allowed root <paramref name="root"/> really is: a root that is a link counts as where it
    /// leads, as for a look.</summary>
    private static FileQuarantineResult<NormalizedPath> ResolvedRoot(NormalizedPath root)
    {
        var resolved = LinkResolver.Resolve(root, root.ToString());
        return resolved.Value is { } path ? Done(path) : FromInventory<NormalizedPath>(resolved.Status, resolved.Error);
    }

    /// <summary>A look's refusal as the quarantine's: the same sentence, the status that says the same.</summary>
    private static FileQuarantineResult<T> FromInventory<T>(FileInventoryStatus status, string? error) where T : class =>
        Refused<T>(
            status switch
            {
                FileInventoryStatus.OutsideAllowedRoots => FileQuarantineStatus.OutsideAllowedRoots,
                FileInventoryStatus.NotFound => FileQuarantineStatus.NotFound,
                FileInventoryStatus.NotARegularFile => FileQuarantineStatus.Changed,
                _ => FileQuarantineStatus.Failed,
            },
            error ?? "");

    private static NormalizedPath FolderOf(NormalizedPath path) => path with { Segments = [.. path.Segments.SkipLast(1)] };

    // ── the listing anywhere but Linux ─────────────────────────────────────────────────────────

    /// <summary>
    /// The regular files in <paramref name="resolvedFolder"/>, listed by its path and each read by the platform's
    /// reader, which gives NTFS's identity on Windows: a development box shows what would move. A link or a folder is
    /// left out; a file that went since the listing too.
    /// </summary>
    private static FileQuarantineResult<IReadOnlyList<FolderFile>> ListByPath(NormalizedPath folder, NormalizedPath resolvedFolder)
    {
        var listPath = SystemPath.For(resolvedFolder);
        var entries = new List<FileSystemInfo>();
        try
        {
            foreach (var entry in new DirectoryInfo(listPath).EnumerateFileSystemInfos("*", FolderWalker.ListingOptions))
                entries.Add(entry);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return ListingFailed<IReadOnlyList<FolderFile>>(exception, folder.ToString(), listPath);
        }

        var files = new List<FolderFile>();
        foreach (var entry in entries.OrderBy(entry => entry.Name, StringComparer.Ordinal))
        {
            if (entry.LinkTarget is not null || entry is DirectoryInfo || !FolderWalker.IsUsableName(entry.Name))
                continue;

            var appPath = folder.Append([entry.Name]).ToString();
            var opened = Reader.Open(SystemPath.For(resolvedFolder.Append([entry.Name])), appPath);
            if (opened.Status is FileInventoryStatus.NotFound or FileInventoryStatus.NotARegularFile)
                continue;
            if (opened.Value is not { } file)
                return FromInventory<IReadOnlyList<FolderFile>>(opened.Status, opened.Error);

            using (file)
                files.Add(new FolderFile(entry.Name, appPath, file.Facts));
        }

        return Done<IReadOnlyList<FolderFile>>(files);
    }

    /// <summary>
    /// Every name the folder at <paramref name="listPath"/> lists — its own path, or the descriptor's that holds it —,
    /// whatever each names, never followed: the names a rename back must not land on.
    /// </summary>
    private static FileQuarantineResult<IReadOnlyList<string>> NamesIn(string folderAppPath, string listPath)
    {
        try
        {
            return Done<IReadOnlyList<string>>(
                [.. new DirectoryInfo(listPath).EnumerateFileSystemInfos("*", FolderWalker.ListingOptions).Select(entry => entry.Name).Order(StringComparer.Ordinal)]);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return ListingFailed<IReadOnlyList<string>>(exception, folderAppPath, listPath);
        }
    }

    /// <summary>A folder whose listing failed: the app may not list it, or the system said why not.</summary>
    private static FileQuarantineResult<T> ListingFailed<T>(Exception exception, string folderAppPath, string listPath) where T : class
    {
        var failed = FileSystemFailure.For<T>(exception, folderAppPath, listPath);
        return exception is UnauthorizedAccessException
            ? Refused<T>(FileQuarantineStatus.NotAllowed, failed.Error!)
            : FromInventory<T>(failed.Status, failed.Error);
    }

    // ── results ────────────────────────────────────────────────────────────────────────────────

    private static FileQuarantineResult<T> Done<T>(T value) where T : class => new(FileQuarantineStatus.Done, value, null);

    private static FileQuarantineResult<T> Refused<T>(FileQuarantineStatus status, string error) where T : class => new(status, null, error);

    private static FileQuarantineResult<T> Relay<TFrom, T>(FileQuarantineResult<TFrom> refusal) where TFrom : class where T : class =>
        new(refusal.Status, null, refusal.Error);

    /// <summary>
    /// A move's or a restore's two ends, by their text: the name in the library, its name in the quarantine, the
    /// allowed root that holds both as the owner typed it, and where that root really is.
    /// </summary>
    private sealed record QuarantineEnds(NormalizedPath AppPath, NormalizedPath QuarantinePath, NormalizedPath Root, NormalizedPath ResolvedRoot);
}
