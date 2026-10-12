namespace MediaAcquisitionManager.Core.Abstractions.Files;

/// <summary>
/// MAM-26, ADR 0030, ADR 0033 — the one contract through which the app writes to the media folders: it renames an
/// old copy's names into the quarantine and back, deletes what sits in the quarantine, and reads what those writes
/// need. <see cref="IFileInventory"/> stays read-only by construction; nothing else in the app moves, renames or
/// deletes a library file.
/// </summary>
/// <remarks>
/// <para>
/// One name per call, so the caller writes the intent and outcome audit rows and reads the replacement switch and the
/// ticks again around every write (ADR 0033). A rename is one <c>renameat2(RENAME_NOREPLACE)</c> through the folder
/// descriptors of the checks, on one mount, never a copy and never over a name already there; a write that can't be
/// done that way does nothing and says why. After a rename the name must show the same device and inode, and both
/// folders are flushed to disk.
/// </para>
/// <para>
/// Every member takes the allowed roots it may act inside, as <see cref="IFileInventory"/>'s do: a path, or a link on
/// the way to it, outside them is refused, and with no roots nothing is touched. The only folders it creates are
/// <c>.mam-quarantine</c> at the top of an allowed root and the folders beneath it
/// (<see cref="Core.Files.QuarantineFolder"/>); it never creates a folder anywhere else, never removes one, and never
/// changes an owner or a mode.
/// </para>
/// <para>
/// All methods return result types and never throw on expected failures; only a cancellation by the caller before a
/// write began propagates, as <see cref="OperationCanceledException"/> — once a rename or a deletion is under way it is
/// never cut off. Every message is one readable English sentence.
/// </para>
/// </remarks>
public interface IFileQuarantine
{
    /// <summary>What this platform can do. Renames work on Linux alone; elsewhere every write is
    /// <see cref="FileQuarantineStatus.NotSupported"/>.</summary>
    FileQuarantineCapabilities Capabilities { get; }

    /// <summary>
    /// The regular files in the folder that holds <paramref name="fileAppPath"/>, the file itself included, each with
    /// what the file system says about it: what the companion rule (<see cref="Core.Files.QuarantineCompanions"/>)
    /// chooses from. Links, folders and anything that isn't a regular file are left out, as they never move.
    /// </summary>
    /// <param name="fileAppPath">The video's absolute path inside the app.</param>
    /// <param name="allowedRoots">The folders it may read inside, in canonical form; none refuses every path.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    Task<FileQuarantineResult<IReadOnlyList<FolderFile>>> ListFolderFilesAsync(
        string fileAppPath, IReadOnlyList<string> allowedRoots, CancellationToken cancellationToken = default);

    /// <summary>
    /// Every name in the folder that holds <paramref name="fileAppPath"/>, whatever it names — a file, a folder, a link
    /// (one that leads nowhere too), a pipe —, as the folder lists it and never followed: what a rename back must not land
    /// on. A look (<see cref="IFileInventory.DescribeAsync"/>) can't tell, as it follows a link at the name to where it
    /// leads, and finds nothing where a link leads nowhere. A folder that is gone is
    /// <see cref="FileQuarantineStatus.NotFound"/>.
    /// </summary>
    /// <param name="fileAppPath">A name's absolute path inside the app; its folder is listed.</param>
    /// <param name="allowedRoots">The folders it may read inside, in canonical form; none refuses every path.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    Task<FileQuarantineResult<IReadOnlyList<string>>> ListFolderNamesAsync(
        string fileAppPath, IReadOnlyList<string> allowedRoots, CancellationToken cancellationToken = default);

    /// <summary>
    /// Renames the file at <paramref name="appPath"/> to <paramref name="quarantinePath"/>, creating the quarantine's
    /// folders beneath its allowed root as needed, when it is still the file <paramref name="expected"/> describes
    /// (size, modification time, device and inode) and both ends lie on one mount.
    /// </summary>
    /// <param name="appPath">The name to move, inside an allowed root.</param>
    /// <param name="expected">What the caller's checks read of it; anything else is
    /// <see cref="FileQuarantineStatus.Changed"/>.</param>
    /// <param name="quarantinePath">Where it goes: <see cref="Core.Files.QuarantineFolder.PathOf"/>.</param>
    /// <param name="allowedRoots">The folders it may act inside, in canonical form.</param>
    /// <param name="cancellationToken">Cancels the checks before the rename; never the rename itself.</param>
    /// <returns>On <see cref="FileQuarantineStatus.Done"/>, what the file system says about the file at its new name.</returns>
    Task<FileQuarantineResult<FileFacts>> MoveIntoQuarantineAsync(
        string appPath, FileFacts expected, string quarantinePath, IReadOnlyList<string> allowedRoots,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Renames the file at <paramref name="quarantinePath"/> back to <paramref name="appPath"/>, never over a name
    /// there now and never creating a folder for it: a folder that is gone is
    /// <see cref="FileQuarantineStatus.FolderGone"/>.
    /// </summary>
    /// <param name="quarantinePath">The name in the quarantine.</param>
    /// <param name="expected">What the move recorded of it; anything else is <see cref="FileQuarantineStatus.Changed"/>.</param>
    /// <param name="appPath">Where it came from.</param>
    /// <param name="allowedRoots">The folders it may act inside, in canonical form.</param>
    /// <param name="cancellationToken">Cancels the checks before the rename; never the rename itself.</param>
    /// <returns>On <see cref="FileQuarantineStatus.Done"/>, what the file system says about the file at its old name.</returns>
    Task<FileQuarantineResult<FileFacts>> RestoreFromQuarantineAsync(
        string quarantinePath, FileFacts expected, string appPath, IReadOnlyList<string> allowedRoots,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes the file at <paramref name="quarantinePath"/> for good, when it lies inside a
    /// <c>.mam-quarantine</c> folder at the top of an allowed root and is still the file <paramref name="expected"/>
    /// describes. A path anywhere else is refused as <see cref="FileQuarantineStatus.OutsideAllowedRoots"/>.
    /// </summary>
    /// <param name="quarantinePath">The name in the quarantine.</param>
    /// <param name="expected">What the move recorded of it; anything else is <see cref="FileQuarantineStatus.Changed"/>.</param>
    /// <param name="allowedRoots">The folders it may act inside, in canonical form.</param>
    /// <param name="cancellationToken">Cancels the checks before the deletion; never the deletion itself.</param>
    /// <returns>On <see cref="FileQuarantineStatus.Done"/>, what the file system said about the file just before it
    /// went.</returns>
    Task<FileQuarantineResult<FileFacts>> DeleteFromQuarantineAsync(
        string quarantinePath, FileFacts expected, IReadOnlyList<string> allowedRoots,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// For each of <paramref name="folderAppPaths"/>, whether a rename into the quarantine of the allowed root that
    /// holds it can work there: the same mount as that root (<c>STATX_MNT_ID</c>), mounted read-write in the mount
    /// table, and writable and searchable by the app's user, the folder and the place the quarantine's folders are made
    /// alike — <c>.mam-quarantine</c> once it is there, the root while it isn't. The Media folders page's hint (ADR 0033);
    /// the rename itself decides. One answer per folder asked, in the order asked; never throws. MAM-39 — where the app's
    /// user may not write, also which folder that is, whose it is and whom the app runs as
    /// (<see cref="RenamePlace.WriteAccess"/>), so the page can say which PUID and PGID to set.
    /// </summary>
    /// <param name="folderAppPaths">The ticked library folders, each inside an allowed root.</param>
    /// <param name="allowedRoots">The allowed roots, in canonical form.</param>
    /// <param name="cancellationToken">Cancels the look.</param>
    Task<IReadOnlyList<RenamePlace>> DescribeRenamePlacesAsync(
        IReadOnlyList<string> folderAppPaths, IReadOnlyList<string> allowedRoots,
        CancellationToken cancellationToken = default);
}
