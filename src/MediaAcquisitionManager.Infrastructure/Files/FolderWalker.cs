using System.Runtime.CompilerServices;
using MediaAcquisitionManager.Core.Abstractions.Files;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Files;
using MediaAcquisitionManager.Core.Paths;
using MediaAcquisitionManager.Core.Texts;

namespace MediaAcquisitionManager.Infrastructure.Files;

/// <summary>
/// Walks the allowed roots for media file names (MAM-18), behind <see cref="LocalFileInventory.ListMediaFilesAsync"/>:
/// every folder in them, links inside the roots followed, each media file name given with where the file really
/// is, and each place the walk didn't go into or through given with why.
/// </summary>
/// <remarks>
/// <para>
/// ADR 0021 (ADR 0009 extended) — a folder is reached the way a file is for a look at it: by a path with no link
/// left in it (<see cref="LinkResolver"/>), opened by the platform's reader, which on Linux follows no link on the
/// way (<see cref="IFileStatusReader.OpenFolder"/>), and listed through what was opened. Every folder has two paths:
/// the app path under the root as the owner typed it, which its names are given by, and the resolved path, which is
/// opened. A child's paths are its folder's paths and its name, never the full name the listing reports — on Linux
/// that is a <c>/proc</c> path, and anywhere it could lead through a link the checks never saw.
/// </para>
/// <para>
/// A link inside a root — a symbolic link or a junction — is told by its target before its type, since a link to a
/// folder lists as a folder. It is resolved and followed when it ends inside a resolved root, its names given under
/// the link's own path; one leading out is refused, never listed through. A link that leads nowhere now is a place
/// the walk couldn't list, so what was found under it before is kept, and so is a root that lists nothing, as an
/// unmounted disk does. A folder that is the one it is in or one above it on the way down — by its device and inode
/// where the reader gives them, else by its resolved path — isn't walked again: a link loop, a bind mount of a folder
/// inside itself, a letter-case alias.
/// </para>
/// <para>
/// Each folder is opened, read whole, judged entry by entry and closed before any of its entries is given: no folder
/// stays open across a <c>yield</c>, so a caller that stops early leaves nothing open, and a deep tree holds one
/// descriptor at a time. Nothing is opened for reading and nothing is written.
/// </para>
/// <para>
/// MAM-26, ADR 0030 — a folder named <see cref="QuarantineFolder.Name"/> is passed over in silence, at any depth: no
/// entry, no problem, nothing below it. It holds the replaced copies, which the replacement jobs know; given here, a
/// quarantined video would come back as a file no library reported, a possible match and part of a Media folders
/// count. A link that leads into a quarantine is not followed, and a root that lies in one gives nothing.
/// </para>
/// </remarks>
internal static class FolderWalker
{
    /// <summary>
    /// Folders listed per walk. The reference library's 1,394 video files (MAM-17) sit in far fewer folders than
    /// that; links can make a small disk look endless (each diamond of links doubles what lies below it), and the
    /// walk must end rather than list the same folders for hours.
    /// </summary>
    public const int MaximumFolders = 100_000;

    /// <summary>
    /// Entries given per walk: over a hundred times the reference library's files, and a bound on what the scan
    /// holds in memory before it stores anything.
    /// </summary>
    public const int MaximumEntries = 200_000;

    /// <summary>
    /// One folder, all of it: hidden and system entries too (the defaults skip them — a <c>.hidden</c> folder full
    /// of films is still on the disk), and a folder the app may not list fails rather than reading as empty, which
    /// would cost the names found in it before. MAM-26 — the quarantine lists a video's folder the same way.
    /// </summary>
    internal static readonly EnumerationOptions ListingOptions = new()
    {
        RecurseSubdirectories = false,
        AttributesToSkip = 0,
        IgnoreInaccessible = false,
        ReturnSpecialDirectories = false,
    };

    /// <summary>
    /// Walks <paramref name="roots"/> in their order, folders depth first and each folder's entries by name, ordinal
    /// (the contract is <see cref="IFileInventory.ListMediaFilesAsync"/>'s).
    /// </summary>
    /// <param name="roots">The usable allowed roots, as the owner typed them; none gives nothing.</param>
    /// <param name="reader">The platform's reader, which opens each folder.</param>
    /// <param name="maximumFolders">The folders listed at most: <see cref="MaximumFolders"/>, a few in a test.</param>
    /// <param name="maximumEntries">The entries given at most: <see cref="MaximumEntries"/>, a few in a test.</param>
    /// <param name="cancellationToken">Cancels the walk; asked before each folder.</param>
    public static async IAsyncEnumerable<FileListingEntry> WalkAsync(
        IReadOnlyList<NormalizedPath> roots,
        IFileStatusReader reader,
        int maximumFolders,
        int maximumEntries,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Every root resolved before the first is walked: a link in one may lead into another, which is inside.
        var resolutions = roots.Select(root => LinkResolver.Resolve(root, root.ToString())).ToList();
        var resolvedRoots = resolutions.Select(resolution => resolution.Value).OfType<NormalizedPath>().ToList();

        // The way down to the folder whose entries come next; the outermost frame holds the roots themselves.
        var descent = new List<Frame> { new(null, null, null, [.. roots.Select((root, index) => RootStep(root, resolutions[index])).OfType<Step>()]) };
        var foldersListed = 0;
        var entriesGiven = 0;
        while (descent.Count > 0)
        {
            var frame = descent[^1];
            if (frame.Next == frame.Steps.Count)
            {
                descent.RemoveAt(descent.Count - 1);
                continue;
            }

            var step = frame.Steps[frame.Next++];
            var entry = step.Entry;
            if (step.Folder is { } folder)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (foldersListed == maximumFolders)
                {
                    entry = LimitReached(folder.AppPath.ToString(), maximumFolders, maximumEntries);
                }
                else
                {
                    // Only a folder listed counts against the folders; one the walk can't list or doesn't go into gives
                    // an entry, which counts against the entries.
                    var (entered, problem) = Enter(folder, descent, reader, resolvedRoots);
                    if (entered is not null)
                    {
                        foldersListed++;
                        descent.Add(entered);
                        continue;
                    }

                    entry = problem;
                }
            }

            if (entry is null)
                continue;

            if (entry.Status != FileListingStatus.LimitReached && entriesGiven == maximumEntries)
                entry = LimitReached(entry.AppPath, maximumFolders, maximumEntries);
            if (entry.Status == FileListingStatus.LimitReached)
            {
                yield return entry;
                yield break;
            }

            entriesGiven++;
            yield return entry;
        }
    }

    /// <summary>
    /// A root in the outermost frame: the folder to walk, or why it can't be, in the resolver's words — or nothing for a
    /// root that is, or leads into, a quarantine (MAM-26).
    /// </summary>
    private static Step? RootStep(NormalizedPath root, FileInventoryResult<NormalizedPath> resolution)
    {
        if (QuarantineFolder.Holds(root) || resolution.Value is { } inQuarantine && QuarantineFolder.Holds(inQuarantine))
            return null;

        return resolution.Value is { } resolved
            ? new Step(null, new Folder(root, resolved, IsRoot: true))
            : Give(FileListingStatus.Unlisted, root.ToString(), resolution.Error);
    }

    /// <summary>
    /// Opens and lists <paramref name="folder"/>: its frame, every entry judged in name order, or the one entry
    /// saying why the walk doesn't go into it.
    /// </summary>
    /// <param name="folder">The folder to walk.</param>
    /// <param name="descent">The folders on the way down to it, the one it is in last.</param>
    /// <param name="reader">The platform's reader.</param>
    /// <param name="resolvedRoots">Where the roots really are.</param>
    private static (Frame? Entered, FileListingEntry? Problem) Enter(
        Folder folder, IReadOnlyList<Frame> descent, IFileStatusReader reader, IReadOnlyList<NormalizedPath> resolvedRoots)
    {
        var appPath = folder.AppPath.ToString();
        // By path before anything is opened: no system call, and the only way where the reader gives no identity.
        if (descent.Any(above => above.Folder?.ResolvedPath.IsSameAs(folder.ResolvedPath) == true))
            return (null, new FileListingEntry(FileListingStatus.Skipped, appPath, null, FileInventoryMessages.LeadsBackUp(appPath)));

        var opened = reader.OpenFolder(SystemPath.For(folder.ResolvedPath), appPath);
        if (opened.Value is not { } handle)
            return (null, new FileListingEntry(FileListingStatus.Unlisted, appPath, null, opened.Error));

        using (handle)
        {
            if (handle.Inode is { } inode && descent.Any(above => above.Inode == inode && above.DeviceId == handle.DeviceId))
                return (null, new FileListingEntry(FileListingStatus.Skipped, appPath, null, FileInventoryMessages.LeadsBackUp(appPath)));

            var listed = List(handle, appPath);
            if (listed.Value is not { } entries)
                return (null, new FileListingEntry(FileListingStatus.Unlisted, appPath, null, listed.Error));

            // Only a root: a mount point below one that is away is just an empty folder of the disk above it. A root
            // is told by where the folder really is, not by how the walk got there: one root reached through a
            // link in another is that root, and its disk being away costs the names under the link no more.
            if (entries.Count == 0 && (folder.IsRoot || resolvedRoots.Any(root => root.IsSameAs(folder.ResolvedPath))))
                return (null, new FileListingEntry(FileListingStatus.Unlisted, appPath, null, FileInventoryMessages.EmptyRoot(appPath)));

            // Judged while the folder is still open: on Linux a link's target is read through its descriptor.
            var steps = entries.Select(entry => Judge(entry, folder, resolvedRoots)).OfType<Step>().ToList();
            return (new Frame(folder, handle.DeviceId, handle.Inode, steps), null);
        }
    }

    /// <summary>The entries of <paramref name="folder"/> in name order, ordinal, or why it couldn't be listed.</summary>
    private static FileInventoryResult<List<FileSystemInfo>> List(IOpenedFolder folder, string appPath)
    {
        var entries = new List<FileSystemInfo>();
        try
        {
            // The enumerate call and every step of it in the one try, and the folder read whole before any entry is
            // judged: a listing that fails half-way (a disk error, a mount going away) gives the folder's one entry
            // and none of those read before it, which would look like all there is.
            foreach (var entry in new DirectoryInfo(folder.ListPath).EnumerateFileSystemInfos("*", ListingOptions))
                entries.Add(entry);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return FileSystemFailure.For<List<FileSystemInfo>>(exception, appPath, folder.ListPath);
        }

        // Ordinal: the same order on every platform and in every culture, so two walks of one disk agree.
        entries.Sort((left, right) => string.CompareOrdinal(left.Name, right.Name));
        return new FileInventoryResult<List<FileSystemInfo>>(FileInventoryStatus.Found, entries, null);
    }

    /// <summary>
    /// What one entry of a listed folder leads to: a folder to walk, an entry to give, or nothing (a file that isn't
    /// a media file). A failure while reading it is its own <see cref="FileListingStatus.Unlisted"/> entry, never its
    /// folder's.
    /// </summary>
    private static Step? Judge(FileSystemInfo entry, Folder parent, IReadOnlyList<NormalizedPath> resolvedRoots)
    {
        var name = entry.Name;
        // MAM-26 — before every other test, so a quarantine's deep paths never come back as a problem of their own.
        if (QuarantineFolder.IsQuarantineName(name, parent.AppPath.Style))
            return null;

        if (!IsUsableName(name))
        {
            // A name that can be neither shown nor stored as it is: its folder stands for it.
            var folderAppPath = parent.AppPath.ToString();
            return Give(FileListingStatus.Skipped, folderAppPath, FileInventoryMessages.UnusableName(folderAppPath));
        }

        var appPath = parent.AppPath.Append([name]);
        var appText = appPath.ToString();
        // A row holds a path of at most 4096 characters (PhysicalFileEntity.ServicePath): a longer name couldn't be
        // stored, nor any name below it. Links make one, and so does a tree deeper than any path a system call
        // takes, which the walk still reaches, as it opens one name at a time.
        if (appText.Length > PhysicalFileEntity.ServicePathMaximumLength)
            return Give(FileListingStatus.Skipped, appText, FileInventoryMessages.PathTooLong(appText));

        var resolvedPath = parent.ResolvedPath.Append([name]);
        try
        {
            if (entry.LinkTarget is not null)
                return FollowLink(entry, appPath, resolvedPath, resolvedRoots);

            if (entry is DirectoryInfo)
                return new Step(null, new Folder(appPath, resolvedPath, IsRoot: false));

            // By the type the listing gave and the name, never by attributes: a device or a pipe named like media
            // is given too, and the inspection refuses it later without opening it (LinuxFileStatusReader).
            return MediaFileNames.IsMediaFile(name) ? MediaFile(appText, resolvedPath) : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Give(FileListingStatus.Unlisted, appText, FileSystemFailure.For<NormalizedPath>(exception, appText, entry.FullName).Error);
        }
    }

    /// <summary>
    /// Where the link <paramref name="link"/> leads, judged as a look at one file judges it: every link on the way
    /// resolved, and the end inside a resolved root.
    /// </summary>
    private static Step? FollowLink(FileSystemInfo link, NormalizedPath appPath, NormalizedPath resolvedPath, IReadOnlyList<NormalizedPath> resolvedRoots)
    {
        var appText = appPath.ToString();
        var resolved = LinkResolver.Resolve(resolvedPath, appText, out var refused);
        if (resolved.Value is not { } target)
        {
            // Too many links, or a target that can't be followed safely, is refused. Anything else — leading nowhere
            // now, a folder on the way the app may not look into, an I/O error from a mount that died — is a place the
            // walk couldn't list, as the same failure on a folder reached without a link is, so the names found
            // under it before are kept.
            return Give(refused ? FileListingStatus.Skipped : FileListingStatus.Unlisted, appText, resolved.Error);
        }

        if (!resolvedRoots.Any(target.IsUnder))
            return Give(FileListingStatus.Skipped, appText, FileInventoryMessages.LinkLeadsOut(appText, target.ToString()));

        // MAM-26 — a link with another name is no way into a quarantine either.
        if (QuarantineFolder.Holds(target))
            return null;

        if (link is DirectoryInfo)
            return new Step(null, new Folder(appPath, target, IsRoot: false));

        // The link's own name decides: it is the name the file is given by.
        return MediaFileNames.IsMediaFile(link.Name) ? MediaFile(appText, target) : null;
    }

    /// <summary>
    /// Whether a name can be given as part of a path: a name that isn't UTF-8 reads with U+FFFD in place of what
    /// isn't, and would be stored as another name than the disk's; a control character would garble any sentence
    /// or log line it is quoted in. MAM-26 — the quarantine's listing of a folder leaves such a name out too.
    /// </summary>
    internal static bool IsUsableName(string name) => !name.Contains('�', StringComparison.Ordinal) && !name.Any(char.IsControl);

    private static Step MediaFile(string appPath, NormalizedPath resolvedPath) =>
        new(new FileListingEntry(FileListingStatus.MediaFile, appPath, resolvedPath.ToString(), null), null);

    private static Step Give(FileListingStatus status, string appPath, Message? error) =>
        new(new FileListingEntry(status, appPath, null, error), null);

    private static FileListingEntry LimitReached(string appPath, int maximumFolders, int maximumEntries) =>
        new(FileListingStatus.LimitReached, appPath, null, FileInventoryMessages.LimitReached(maximumEntries, maximumFolders));

    /// <summary>A folder to walk: its path under the root as the owner typed it, and where it really is.</summary>
    private sealed record Folder(NormalizedPath AppPath, NormalizedPath ResolvedPath, bool IsRoot);

    /// <summary>One entry of a listed folder, judged: the entry to give, or the folder to walk.</summary>
    private sealed record Step(FileListingEntry? Entry, Folder? Folder);

    /// <summary>A folder on the way down: its identity, its steps and how far they have been taken.</summary>
    private sealed class Frame(Folder? folder, long? deviceId, long? inode, IReadOnlyList<Step> steps)
    {
        /// <summary>The folder; <c>null</c> for the outermost frame, which holds the roots.</summary>
        public Folder? Folder => folder;

        public long? DeviceId => deviceId;

        public long? Inode => inode;

        public IReadOnlyList<Step> Steps => steps;

        /// <summary>The step to take next.</summary>
        public int Next { get; set; }
    }
}
