using System.Collections.Concurrent;
using MediaAcquisitionManager.Core.Abstractions.Files;
using MediaAcquisitionManager.Core.Files;
using MediaAcquisitionManager.Core.Paths;
using MediaAcquisitionManager.Core.Texts;
using MediaAcquisitionManager.Infrastructure.Files;

namespace MediaAcquisitionManager.TestInfrastructure.Files;

/// <summary>
/// MAM-26 — the app's one write to the media folders as the API tests see it (ADR 0030, ADR 0033):
/// <see cref="IFileQuarantine"/> is the Core seam to the host's folders, so it is faked here over
/// <see cref="FakeFileInventory"/>'s own files and folders, and the local implementation is tested against real folders
/// in Infrastructure.Tests.
/// </summary>
/// <remarks>
/// <para>
/// One store with the file inventory: a rename moves the planted file to its new name keeping its identity
/// (<see cref="FakeFileInventory.Move"/>), so the inventory's look finds it there and nothing at the old name; a deletion
/// takes it away. Every rule the contract promises is kept the way a test can see it: no roots, or a path outside them,
/// refuses; a move goes only into the quarantine of the allowed root that holds the file, and a restore or a deletion
/// only out of one; a file that isn't what the caller expected (size, modification time, device, inode) is
/// <see cref="FileQuarantineStatus.Changed"/>; a name already at the other end is
/// <see cref="FileQuarantineStatus.NameTaken"/>, never replaced; a restore into a folder that is gone is
/// <see cref="FileQuarantineStatus.FolderGone"/>, and no folder is made for it. The only folders it makes are the
/// quarantine's, each recorded (<see cref="CreatedFolders"/>); it throws should it ever be asked to make another, which
/// would be a bug of the caller's.
/// </para>
/// <para>
/// A test plants the host's other answers: a folder on another mount (<see cref="SetMount"/>; every folder is on
/// <see cref="DefaultMount"/> unless set), a read-only mount (<see cref="SetReadOnly"/>), a folder the app's user may not
/// write in (<see cref="SetNotWritable(string)"/>), any status for one path (<see cref="Refuse"/>), another file taking a name's
/// place between the last look and the rename (<see cref="Swap"/>), and a platform that can't rename at all
/// (<see cref="Capabilities"/>). The same plants answer the Media folders page's hint (<see cref="DescribeRenamePlacesAsync"/>);
/// MAM-39 — a folder made unwritable with its owner's ids and the app's names them in the hint, as the host's would.
/// </para>
/// <para>
/// Every rename and deletion done is recorded in order (<see cref="Renames"/>, <see cref="Deletions"/>); one refused is
/// not. The hooks run outside the fake's lock: <see cref="BeforeRename"/> and <see cref="BeforeDelete"/> before the
/// checks — the world changing right before the write, or the app stopping before it began — and
/// <see cref="AfterRename"/> once the rename is done, before its answer — the app stopping between a rename and its
/// outcome.
/// </para>
/// <para>
/// MAM-47 (ADR 0042) — where it answers in the very words of the real quarantine (a read-only mount, a file that isn't a
/// regular one, a swapped name), it uses its sentences (<see cref="FileQuarantineMessages"/>), so a test reads the keys a
/// reader of the real one sees. Its other words, and a refusal a test plants (<see cref="Refuse"/>), are no sentence of
/// the app's and have no key in the locale files: they travel as <see cref="Message.Legacy"/>, which every language shows
/// exactly as they are.
/// </para>
/// </remarks>
public sealed class FakeFileQuarantine(FakeFileInventory files) : IFileQuarantine
{
    /// <summary>The mount every folder is on unless a test sets another (<see cref="SetMount"/>).</summary>
    public const long DefaultMount = 1;

    private const string NotSupportedError = "This platform can't rename a file without risking a copy, so the app renames nothing here.";

    private readonly Lock _gate = new();
    private readonly ConcurrentDictionary<NormalizedPath, long> _mounts = new();
    private readonly ConcurrentDictionary<NormalizedPath, byte> _readOnly = new();
    private readonly ConcurrentDictionary<NormalizedPath, PlantedOwner?> _notWritable = new();
    private readonly ConcurrentDictionary<string, (FileQuarantineStatus Status, string Error)> _refusals = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, FileFacts> _swaps = new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<(string From, string To)> _renames = new();
    private readonly ConcurrentQueue<string> _deletions = new();
    private readonly ConcurrentQueue<string> _createdFolders = new();

    /// <inheritdoc/>
    /// <remarks>Renames work unless a test says otherwise; with <c>CanRename: false</c> every write is
    /// <see cref="FileQuarantineStatus.NotSupported"/> and every folder of the hint <see cref="RenamePlaceProblem.CouldNotCheck"/>.</remarks>
    public FileQuarantineCapabilities Capabilities { get; set; } = new(CanRename: true);

    /// <summary>
    /// Runs before each rename's checks, into the quarantine and out of it, with the name and where it goes: a test changes
    /// the world right before the write (the switch, a file, a folder), or throws as an app stopping before it began.
    /// </summary>
    public Func<string, string, CancellationToken, Task>? BeforeRename { get; set; }

    /// <summary>
    /// Runs once each rename is done, before its answer, with where the name was and where it is now: a test throws as an
    /// app stopping between a rename and its outcome, or changes the world before the next name.
    /// </summary>
    public Func<string, string, CancellationToken, Task>? AfterRename { get; set; }

    /// <summary>Runs before each deletion's checks, with the quarantined name twice (one hook shape for every write): a
    /// test changes the world right before the deletion, or throws as an app stopping before it began.</summary>
    public Func<string, string, CancellationToken, Task>? BeforeDelete { get; set; }

    /// <summary>Every rename done, into the quarantine and out of it, as (where it was, where it went), in order.</summary>
    public IReadOnlyList<(string From, string To)> Renames => [.. _renames];

    /// <summary>Every name deleted from a quarantine, in order.</summary>
    public IReadOnlyList<string> Deletions => [.. _deletions];

    /// <summary>Every folder the fake made, in order: each lies in a quarantine.</summary>
    public IReadOnlyList<string> CreatedFolders => [.. _createdFolders];

    /// <summary>The folder <paramref name="folderAppPath"/> and everything below it lie on the mount
    /// <paramref name="mountId"/> — the nearest folder set wins —, instead of <see cref="DefaultMount"/>.</summary>
    public void SetMount(string folderAppPath, long mountId) => _mounts[ParseOrThrow(folderAppPath)] = mountId;

    /// <summary>The folder <paramref name="folderAppPath"/> and everything below it lie on a read-only mount
    /// (<c>EROFS</c>).</summary>
    public void SetReadOnly(string folderAppPath) => _readOnly[ParseOrThrow(folderAppPath)] = 0;

    /// <summary>The app's user may not write in the folder <paramref name="folderAppPath"/> nor anywhere below it
    /// (<c>EACCES</c>); the hint names no owner, as where the host's ids can't be read.</summary>
    public void SetNotWritable(string folderAppPath) => _notWritable[ParseOrThrow(folderAppPath)] = null;

    /// <summary>
    /// MAM-39 — as <see cref="SetNotWritable(string)"/>, and the hint of a folder at or below
    /// <paramref name="folderAppPath"/> says whose folder it is (<paramref name="ownerUserId"/>,
    /// <paramref name="ownerGroupId"/>) and whom the app runs as (<paramref name="appUserId"/>,
    /// <paramref name="appGroupId"/>), naming <paramref name="reportedPath"/> — the quarantine's side, say — or, without
    /// one, the folder as asked, as the host does when the folder itself is refused.
    /// </summary>
    public void SetNotWritable(
        string folderAppPath, uint ownerUserId, uint ownerGroupId, uint appUserId, uint appGroupId, string? reportedPath = null) =>
        _notWritable[ParseOrThrow(folderAppPath)] = new PlantedOwner(ownerUserId, ownerGroupId, appUserId, appGroupId, reportedPath);

    /// <summary>
    /// Every write that touches <paramref name="appPath"/> — as the name or where it goes — answers
    /// <paramref name="status"/> and <paramref name="error"/>, and so does a listing of the folder at that path, whatever
    /// else holds.
    /// </summary>
    public void Refuse(string appPath, FileQuarantineStatus status, string error)
    {
        if (status == FileQuarantineStatus.Done)
            throw new ArgumentException("A refusal is any status but Done.", nameof(status));
        _refusals[appPath] = (status, error);
    }

    /// <summary>
    /// The next rename from <paramref name="appPath"/> — into the quarantine or out of it — passes every check and then
    /// moves another file, described by <paramref name="other"/>, which took the name's place after the last look: that
    /// file is at the new name, the planted one is gone wherever whoever swapped it put it, the rename is recorded, and the
    /// answer is <see cref="FileQuarantineStatus.Swapped"/>, as the local contract's after its look at the new name.
    /// </summary>
    public void Swap(string appPath, FileFacts other) => _swaps[appPath] = other;

    /// <summary>Renames work, every folder on <see cref="DefaultMount"/>, writable and read-write, nothing refused or
    /// swapped, no hooks, and nothing recorded. The files and folders are <see cref="FakeFileInventory"/>'s, reset with
    /// it.</summary>
    public void Reset()
    {
        Capabilities = new FileQuarantineCapabilities(CanRename: true);
        BeforeRename = null;
        AfterRename = null;
        BeforeDelete = null;
        _mounts.Clear();
        _readOnly.Clear();
        _notWritable.Clear();
        _refusals.Clear();
        _swaps.Clear();
        _renames.Clear();
        _deletions.Clear();
        _createdFolders.Clear();
    }

    public Task<FileQuarantineResult<IReadOnlyList<FolderFile>>> ListFolderFilesAsync(
        string fileAppPath, IReadOnlyList<string> allowedRoots, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Parse(fileAppPath) is not { Segments.Count: > 0 } file)
            return Task.FromResult(Refused<IReadOnlyList<FolderFile>>(FileQuarantineStatus.OutsideAllowedRoots, OutsideError(fileAppPath)));

        var folder = file with { Segments = [.. file.Segments.SkipLast(1)] };
        if (_refusals.TryGetValue(folder.ToString(), out var refusal))
            return Task.FromResult(Refused<IReadOnlyList<FolderFile>>(refusal.Status, refusal.Error));
        if (RootOf(file, allowedRoots) is null)
            return Task.FromResult(Refused<IReadOnlyList<FolderFile>>(FileQuarantineStatus.OutsideAllowedRoots, OutsideError(fileAppPath)));
        if (!files.IsFolder(folder))
            return Task.FromResult(Refused<IReadOnlyList<FolderFile>>(FileQuarantineStatus.NotFound, $"Nothing is at “{folder}”."));

        IReadOnlyList<FolderFile> listed = [.. files.FilesIn(folder).Select(entry => new FolderFile(NameOf(entry.AppPath), entry.AppPath, entry.Facts))];
        return Task.FromResult(new FileQuarantineResult<IReadOnlyList<FolderFile>>(FileQuarantineStatus.Done, listed, null));
    }

    /// <summary>
    /// Every name the fake's store holds right inside the folder of <paramref name="fileAppPath"/> — files, planted problems,
    /// linked names (one leading nowhere too) and folders —, or why it can't list it, as
    /// <see cref="ListFolderFilesAsync"/> refuses.
    /// </summary>
    public Task<FileQuarantineResult<IReadOnlyList<string>>> ListFolderNamesAsync(
        string fileAppPath, IReadOnlyList<string> allowedRoots, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Parse(fileAppPath) is not { Segments.Count: > 0 } file)
            return Task.FromResult(Refused<IReadOnlyList<string>>(FileQuarantineStatus.OutsideAllowedRoots, OutsideError(fileAppPath)));

        var folder = file with { Segments = [.. file.Segments.SkipLast(1)] };
        if (_refusals.TryGetValue(folder.ToString(), out var refusal))
            return Task.FromResult(Refused<IReadOnlyList<string>>(refusal.Status, refusal.Error));
        if (RootOf(file, allowedRoots) is null)
            return Task.FromResult(Refused<IReadOnlyList<string>>(FileQuarantineStatus.OutsideAllowedRoots, OutsideError(fileAppPath)));
        if (!files.IsFolder(folder))
            return Task.FromResult(Refused<IReadOnlyList<string>>(FileQuarantineStatus.NotFound, $"The folder “{folder}” is gone."));

        return Task.FromResult(new FileQuarantineResult<IReadOnlyList<string>>(FileQuarantineStatus.Done, files.NamesIn(folder), null));
    }

    public async Task<FileQuarantineResult<FileFacts>> MoveIntoQuarantineAsync(
        string appPath, FileFacts expected, string quarantinePath, IReadOnlyList<string> allowedRoots, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (BeforeRename is { } beforeRename)
            await beforeRename(appPath, quarantinePath, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        FileQuarantineResult<FileFacts> result;
        lock (_gate)
            result = Move(appPath, expected, quarantinePath, allowedRoots);

        if (result.Status == FileQuarantineStatus.Done && AfterRename is { } afterRename)
            await afterRename(appPath, quarantinePath, cancellationToken);
        return result;
    }

    public async Task<FileQuarantineResult<FileFacts>> RestoreFromQuarantineAsync(
        string quarantinePath, FileFacts expected, string appPath, IReadOnlyList<string> allowedRoots, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (BeforeRename is { } beforeRename)
            await beforeRename(quarantinePath, appPath, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        FileQuarantineResult<FileFacts> result;
        lock (_gate)
            result = Restore(quarantinePath, expected, appPath, allowedRoots);

        if (result.Status == FileQuarantineStatus.Done && AfterRename is { } afterRename)
            await afterRename(quarantinePath, appPath, cancellationToken);
        return result;
    }

    public async Task<FileQuarantineResult<FileFacts>> DeleteFromQuarantineAsync(
        string quarantinePath, FileFacts expected, IReadOnlyList<string> allowedRoots, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (BeforeDelete is { } beforeDelete)
            await beforeDelete(quarantinePath, quarantinePath, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
            return Delete(quarantinePath, expected, allowedRoots);
    }

    public Task<IReadOnlyList<RenamePlace>> DescribeRenamePlacesAsync(
        IReadOnlyList<string> folderAppPaths, IReadOnlyList<string> allowedRoots, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<RenamePlace> places = [.. folderAppPaths.Select(folder => PlaceOf(folder, allowedRoots))];
        return Task.FromResult(places);
    }

    // ── the writes, under the lock ──────────────────────────────────────────────────────────────

    private FileQuarantineResult<FileFacts> Move(string appPath, FileFacts expected, string quarantinePath, IReadOnlyList<string> allowedRoots)
    {
        if (!Capabilities.CanRename)
            return Refused<FileFacts>(FileQuarantineStatus.NotSupported, NotSupportedError);
        if (RefusalOf(appPath, quarantinePath) is { } refusal)
            return refusal;
        if (Parse(appPath) is not { } source || QuarantineFolder.Holds(source) || RootOf(source, allowedRoots) is not { } root)
            return Refused<FileFacts>(FileQuarantineStatus.OutsideAllowedRoots, OutsideError(appPath));
        if (Parse(quarantinePath) is not { } target || !InQuarantineOf(target, root))
            return Refused<FileFacts>(FileQuarantineStatus.OutsideAllowedRoots, NotInQuarantineError(quarantinePath));
        if (Check(appPath, expected) is { } changed)
            return changed;
        if (IsTaken(quarantinePath, target))
            return Refused<FileFacts>(FileQuarantineStatus.NameTaken, NameTakenError(quarantinePath));
        if (Blocked(source, target) is { } blocked)
            return blocked;

        // The quarantine's own folders, from .mam-quarantine down, where they aren't yet.
        var quarantine = QuarantineFolder.Of(root);
        for (var count = quarantine.Segments.Count; count < target.Segments.Count; count++)
        {
            var folder = target with { Segments = [.. target.Segments.Take(count)] };
            if (files.PlantedAt(folder.ToString()) is not null)
                return Refused<FileFacts>(FileQuarantineStatus.Failed, $"“{folder}” is a file, so the quarantine's folder can't be made there.");
        }

        for (var count = quarantine.Segments.Count; count < target.Segments.Count; count++)
        {
            var folder = target with { Segments = [.. target.Segments.Take(count)] };
            if (files.IsFolder(folder))
                continue;
            if (!QuarantineFolder.Holds(folder))
                throw new InvalidOperationException($"The fake was asked to make “{folder}”, outside every quarantine.");
            files.AddFolder(folder.ToString());
            _createdFolders.Enqueue(folder.ToString());
        }

        if (Swapped(appPath, quarantinePath) is { } swapped)
            return swapped;

        files.Move(appPath, quarantinePath);
        _renames.Enqueue((appPath, quarantinePath));
        return new FileQuarantineResult<FileFacts>(FileQuarantineStatus.Done, files.PlantedAt(quarantinePath)!.Value, null);
    }

    private FileQuarantineResult<FileFacts> Restore(string quarantinePath, FileFacts expected, string appPath, IReadOnlyList<string> allowedRoots)
    {
        if (!Capabilities.CanRename)
            return Refused<FileFacts>(FileQuarantineStatus.NotSupported, NotSupportedError);
        if (RefusalOf(quarantinePath, appPath) is { } refusal)
            return refusal;
        if (Parse(appPath) is not { Segments.Count: > 0 } target || QuarantineFolder.Holds(target) || RootOf(target, allowedRoots) is not { } root)
            return Refused<FileFacts>(FileQuarantineStatus.OutsideAllowedRoots, OutsideError(appPath));
        if (Parse(quarantinePath) is not { } source || !InQuarantineOf(source, root))
            return Refused<FileFacts>(FileQuarantineStatus.OutsideAllowedRoots, NotInQuarantineError(quarantinePath));
        if (Check(quarantinePath, expected) is { } changed)
            return changed;

        var folder = target with { Segments = [.. target.Segments.SkipLast(1)] };
        if (!files.IsFolder(folder))
            return Refused<FileFacts>(FileQuarantineStatus.FolderGone, $"The folder “{folder}” is gone, and the app creates no folder for it.");
        if (IsTaken(appPath, target))
            return Refused<FileFacts>(FileQuarantineStatus.NameTaken, NameTakenError(appPath));
        if (Blocked(source, target) is { } blocked)
            return blocked;

        if (Swapped(quarantinePath, appPath) is { } swapped)
            return swapped;

        files.Move(quarantinePath, appPath);
        _renames.Enqueue((quarantinePath, appPath));
        return new FileQuarantineResult<FileFacts>(FileQuarantineStatus.Done, files.PlantedAt(appPath)!.Value, null);
    }

    private FileQuarantineResult<FileFacts> Delete(string quarantinePath, FileFacts expected, IReadOnlyList<string> allowedRoots)
    {
        if (!Capabilities.CanRename)
            return Refused<FileFacts>(FileQuarantineStatus.NotSupported, NotSupportedError);
        if (_refusals.TryGetValue(quarantinePath, out var refusal))
            return Refused<FileFacts>(refusal.Status, refusal.Error);
        if (Parse(quarantinePath) is not { } path || RootOf(path, allowedRoots) is not { } root || !InQuarantineOf(path, root))
            return Refused<FileFacts>(FileQuarantineStatus.OutsideAllowedRoots, NotInQuarantineError(quarantinePath));
        if (Check(quarantinePath, expected) is { } changed)
            return changed;
        if (IsUnderAny(path, _readOnly))
            return Refused<FileFacts>(FileQuarantineStatus.ReadOnly, FileQuarantineMessages.ReadOnly(quarantinePath));
        if (IsUnderAny(path with { Segments = [.. path.Segments.SkipLast(1)] }, _notWritable))
            return Refused<FileFacts>(FileQuarantineStatus.NotAllowed, NotAllowedError(quarantinePath));

        var facts = files.PlantedAt(quarantinePath)!.Value!;
        files.Remove(quarantinePath);
        _deletions.Enqueue(quarantinePath);
        return new FileQuarantineResult<FileFacts>(FileQuarantineStatus.Done, facts, null);
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    private static string OutsideError(string path) => $"“{path}” is outside the media folders the app may write in.";

    private static string NotInQuarantineError(string path) => $"“{path}” isn't inside the quarantine of a media folder the app may write in.";

    private static string NameTakenError(string path) => $"Something is already at “{path}”, and the app never renames over it.";

    private static string NotAllowedError(string path) => $"The app may not write in the folder of “{path}”.";

    // A planted swap of the name: the other file renamed in its place, the planted one gone.
    private FileQuarantineResult<FileFacts>? Swapped(string from, string to)
    {
        if (!_swaps.TryRemove(from, out var other))
            return null;

        files.Remove(from);
        files.AddFile(to, other);
        _renames.Enqueue((from, to));
        return Refused<FileFacts>(FileQuarantineStatus.Swapped, FileQuarantineMessages.Swapped(from, to));
    }

    // A planted refusal of either end, the name's first.
    private FileQuarantineResult<FileFacts>? RefusalOf(string name, string other) =>
        _refusals.TryGetValue(name, out var refusal) || _refusals.TryGetValue(other, out refusal)
            ? Refused<FileFacts>(refusal.Status, refusal.Error)
            : null;

    // Whether the file at the path is still the one expected: a regular file, of the same size, time and identity.
    private FileQuarantineResult<FileFacts>? Check(string appPath, FileFacts expected)
    {
        if (files.IsLinkedName(appPath))
            return Refused<FileFacts>(FileQuarantineStatus.Changed, FileQuarantineMessages.NotARegularFile(appPath));

        switch (files.PlantedAt(appPath))
        {
            case null:
                return Refused<FileFacts>(FileQuarantineStatus.NotFound, $"Nothing is at “{appPath}”.");
            case { Status: FileInventoryStatus.Found, Value: { } facts }:
                return facts.SizeBytes == expected.SizeBytes
                    && facts.ModifiedUtc == expected.ModifiedUtc
                    && facts.DeviceId == expected.DeviceId
                    && facts.Inode == expected.Inode
                        ? null
                        : Refused<FileFacts>(FileQuarantineStatus.Changed, $"“{appPath}” changed since the app checked it.");
            case { Status: FileInventoryStatus.NotFound } planted:
                return Refused<FileFacts>(FileQuarantineStatus.NotFound, planted.Error);
            case { Status: FileInventoryStatus.OutsideAllowedRoots } planted:
                return Refused<FileFacts>(FileQuarantineStatus.OutsideAllowedRoots, planted.Error);
            case var planted:
                return Refused<FileFacts>(FileQuarantineStatus.Failed, planted.Error);
        }
    }

    // A file, a linked name or a folder is at the path.
    private bool IsTaken(string appPath, NormalizedPath path) =>
        files.PlantedAt(appPath) is not null || files.IsLinkedName(appPath) || files.IsFolder(path);

    // Another mount, a read-only one, or a folder the app may not write in, at either end.
    private FileQuarantineResult<FileFacts>? Blocked(NormalizedPath source, NormalizedPath target)
    {
        if (MountOf(source) != MountOf(target))
            return Refused<FileFacts>(FileQuarantineStatus.OtherMount, $"“{source}” and “{target}” lie on different mounts, so renaming it would copy it.");
        if (IsUnderAny(source, _readOnly))
            return Refused<FileFacts>(FileQuarantineStatus.ReadOnly, FileQuarantineMessages.ReadOnly(source.ToString()));
        if (IsUnderAny(target, _readOnly))
            return Refused<FileFacts>(FileQuarantineStatus.ReadOnly, FileQuarantineMessages.ReadOnly(target.ToString()));
        if (IsUnderAny(source with { Segments = [.. source.Segments.SkipLast(1)] }, _notWritable))
            return Refused<FileFacts>(FileQuarantineStatus.NotAllowed, NotAllowedError(source.ToString()));
        if (IsUnderAny(target with { Segments = [.. target.Segments.SkipLast(1)] }, _notWritable))
            return Refused<FileFacts>(FileQuarantineStatus.NotAllowed, NotAllowedError(target.ToString()));
        return null;
    }

    private RenamePlace PlaceOf(string folderAppPath, IReadOnlyList<string> allowedRoots)
    {
        if (!Capabilities.CanRename
            || Parse(folderAppPath) is not { } folder
            || RootOf(folder, allowedRoots) is not { } root
            || !files.IsFolder(folder))
            return new RenamePlace(folderAppPath, [RenamePlaceProblem.CouldNotCheck]);

        var problems = new List<RenamePlaceProblem>();
        if (MountOf(folder) != MountOf(QuarantineFolder.Of(root)))
            problems.Add(RenamePlaceProblem.OtherMount);
        if (IsUnderAny(folder, _readOnly))
            problems.Add(RenamePlaceProblem.ReadOnly);

        // The nearest folder made unwritable, at or above the folder: the root, above it, is found this way too.
        RenamePlaceWriteAccess? writeAccess = null;
        var notWritable = _notWritable.Where(entry => folder.IsUnder(entry.Key)).OrderByDescending(entry => entry.Key.Segments.Count).ToList();
        if (notWritable.Count > 0)
        {
            problems.Add(RenamePlaceProblem.NotWritable);
            if (notWritable[0].Value is { } owner)
            {
                writeAccess = new RenamePlaceWriteAccess(
                    owner.ReportedPath ?? folderAppPath, owner.OwnerUserId, owner.OwnerGroupId, owner.AppUserId, owner.AppGroupId);
            }
        }

        return new RenamePlace(folderAppPath, problems, writeAccess);
    }

    // The nearest folder a test set a mount for, at or above the path.
    private long MountOf(NormalizedPath path) =>
        _mounts.Where(mount => path.IsUnder(mount.Key)).OrderByDescending(mount => mount.Key.Segments.Count).Select(mount => (long?)mount.Value).FirstOrDefault()
        ?? DefaultMount;

    private static bool IsUnderAny<T>(NormalizedPath path, ConcurrentDictionary<NormalizedPath, T> folders) => folders.Keys.Any(path.IsUnder);

    // The allowed root that holds the path; null when none does.
    private static NormalizedPath? RootOf(NormalizedPath path, IReadOnlyList<string> allowedRoots) =>
        allowedRoots.Select(Parse).OfType<NormalizedPath>().FirstOrDefault(path.IsUnder);

    // Inside the root's quarantine, below its folder.
    private static bool InQuarantineOf(NormalizedPath path, NormalizedPath root)
    {
        var quarantine = QuarantineFolder.Of(root);
        return path.IsUnder(quarantine) && path.Segments.Count > quarantine.Segments.Count;
    }

    // The fake's own words, or the test's (see the remarks): shown as they are, in every language.
    private static FileQuarantineResult<T> Refused<T>(FileQuarantineStatus status, string error) where T : class =>
        new(status, null, Message.Legacy(error));

    private static FileQuarantineResult<T> Refused<T>(FileQuarantineStatus status, Message? error) where T : class => new(status, null, error);

    private static NormalizedPath? Parse(string text) => NormalizedPath.TryParse(text, out var path, out _) ? path : null;

    private static NormalizedPath ParseOrThrow(string text) =>
        Parse(text) ?? throw new ArgumentException($"“{text}” is no absolute path.", nameof(text));

    private static string NameOf(string appPath) => appPath[(appPath.LastIndexOfAny(['/', '\\']) + 1)..];

    /// <summary>MAM-39 — whose an unwritable folder is and whom the app runs as, and the folder the hint names when the
    /// test gave one.</summary>
    private sealed record PlantedOwner(uint OwnerUserId, uint OwnerGroupId, uint AppUserId, uint AppGroupId, string? ReportedPath);
}
