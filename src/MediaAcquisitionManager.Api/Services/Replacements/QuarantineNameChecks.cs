using MediaAcquisitionManager.Core.Abstractions.Files;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Files;
using MediaAcquisitionManager.Core.Paths;
using MediaAcquisitionManager.Core.Replacements;

namespace MediaAcquisitionManager.Api.Services.Replacements;

/// <summary>
/// MAM-40 — what the owner's Restore and Delete for good check of each name in the quarantine before anything is written
/// (ADR 0030, ADR 0033), and the small facts every write over such a name reads — where it may be written, what the move
/// recorded of it, how many names its file has now —: a replacement's names (MAM-26) and a removal's alike.
/// </summary>
/// <remarks>Reads the disk only; writes nothing. Each sentence names the file or the folder it is about.</remarks>
internal static class QuarantineNameChecks
{
    /// <summary>The {error} of a write the file system refused without saying why; the contract always says, but a name's
    /// problem is never left blank.</summary>
    public const string NoReasonError = "The file system gave no reason.";

    /// <summary>
    /// The roots a write of <paramref name="row"/> may act inside: its own allowed root alone, the one whose quarantine
    /// holds it, while an allowed root of now holds that root — itself, or a parent the owner put in its place (ADR 0030:
    /// a Save that keeps the quarantine within reach) —; <c>null</c> when none does. Its own root rather than today's: the
    /// write contract finds a quarantine only at the top of a root it is given, and a root inside an allowed one widens
    /// nothing.
    /// </summary>
    public static IReadOnlyList<string>? RootsOf(QuarantinedNameEntity row, ReplacementGateState gate) =>
        NormalizedPath.TryParse(row.AllowedRoot, out var root, out _)
        && gate.AllowedRoots.Any(allowed => NormalizedPath.TryParse(allowed, out var parsed, out _) && root.IsUnder(parsed))
            ? [row.AllowedRoot]
            : null;

    /// <summary>The quarantine holding <paramref name="row"/>: <c>&lt;its allowed root&gt;/.mam-quarantine</c>.</summary>
    public static string QuarantineOf(QuarantinedNameEntity row) =>
        NormalizedPath.TryParse(row.AllowedRoot, out var root, out _) ? QuarantineFolder.Of(root).ToString() : row.AllowedRoot;

    /// <summary>Whether the file at <paramref name="row"/>'s quarantine path is no longer the one the move put there: gone,
    /// or another device, inode, size or modification time (ADR 0030).</summary>
    public static async Task<bool> ChangedInQuarantineAsync(
        IFileInventory files, QuarantinedNameEntity row, IReadOnlyList<string> roots, CancellationToken cancellationToken)
    {
        var described = await files.DescribeAsync(row.QuarantinePath, roots, cancellationToken);
        return described is not { Status: FileInventoryStatus.Found, Value: { } facts }
            || facts.DeviceId != row.DeviceId
            || facts.Inode != row.Inode
            || facts.SizeBytes != row.SizeBytes
            || facts.ModifiedUtc != row.ModifiedUtc;
    }

    /// <summary>What the move recorded of <paramref name="row"/>: what the write contract must find before it writes.</summary>
    public static FileFacts FactsOf(QuarantinedNameEntity row) => new(row.SizeBytes, row.ModifiedUtc, row.DeviceId, row.Inode, row.LinkCount);

    /// <summary>The file's own name, quoted: what a stop's sentence names.</summary>
    public static string NameOf(QuarantinedNameEntity row) => ReplacementText.Quote(ReplacementText.FileNameOf(row.AppPath));

    /// <summary>Whether <paramref name="row"/>'s old path lies in a ticked folder now (ADR 0033): the gate the move went
    /// through, which Restore goes through again.</summary>
    public static bool IsInTickedFolder(QuarantinedNameEntity row, ReplacementGateState gate) =>
        NormalizedPath.TryParse(row.AppPath, out var path, out _) && gate.IsInTickedFolder(path);

    /// <summary>Whether <paramref name="name"/> may still move into the quarantine, as <paramref name="gate"/> reads now: in
    /// a ticked folder, inside the very allowed root whose quarantine its plan named (ADR 0033).</summary>
    public static bool StillTicked(QuarantinedNameEntity name, ReplacementGateState gate) =>
        NormalizedPath.TryParse(name.AppPath, out var appPath, out _)
        && NormalizedPath.TryParse(name.AllowedRoot, out var root, out _)
        && gate.AllowedRoots.Any(allowed => NormalizedPath.TryParse(allowed, out var path, out _) && path.IsSameAs(root))
        && gate.IsInTickedFolder(appPath);

    /// <summary>
    /// Why <paramref name="names"/> can't be put back now, name by name, nothing written; <c>null</c> when they can: for
    /// every name its quarantine is within the Media folders, its old path in a ticked folder, the file in the quarantine
    /// still the one the move put there, nothing at its old path, and its old folder still there. The platform and the
    /// switch are the caller's to check first, in its own words.
    /// </summary>
    public static async Task<string?> RestoreRefusalAsync(
        IFileInventory files,
        IFileQuarantine quarantine,
        IEnumerable<QuarantinedNameEntity> names,
        ReplacementGateState gate,
        CancellationToken cancellationToken)
    {
        foreach (var row in names)
        {
            if (RootsOf(row, gate) is not { } roots)
                return $"{ReplacementText.Quote(QuarantineOf(row))} is no longer inside the Media folders, so nothing was put back.";
            if (!IsInTickedFolder(row, gate))
                return $"{ReplacementText.Quote(row.AppPath)} is no longer in a folder ticked for replacement, so nothing was put back.";
            if (await ChangedInQuarantineAsync(files, row, roots, cancellationToken))
                return $"{ReplacementText.Quote(row.QuarantinePath)} changed in the quarantine since it was moved there, so nothing was put back.";

            // The folder the name goes back into, listed through the write contract, which never makes it again; and
            // anything at the old path among its names — a file, a folder, a link, one leading nowhere too, which a look
            // following it would find nothing at — is something a rename back would have to go around.
            var folder = await quarantine.ListFolderNamesAsync(row.AppPath, roots, cancellationToken);
            if (folder.Status == FileQuarantineStatus.NotFound)
                return $"The folder {ReplacementText.Quote(FolderOf(row.AppPath))} is gone, so nothing was put back; the app creates no folder.";
            if (folder is not { Status: FileQuarantineStatus.Done, Value: { } namesThere })
                return $"The app couldn't look in the folder of {ReplacementText.Quote(row.AppPath)}: {folder.Error ?? NoReasonError} Nothing was put back.";
            if (namesThere.Any(name => IsNameOf(row.AppPath, name)))
                return $"{ReplacementText.Quote(row.AppPath)} already holds a file, so nothing was put back.";
        }

        return null;
    }

    /// <summary>
    /// Why <paramref name="names"/> can't be deleted now, name by name, nothing written; <c>null</c> when they can: every
    /// name's quarantine is within the Media folders with the file there still the one the move put there. No tick is read:
    /// the quarantine belongs to the allowed root, not to a library folder (ADR 0033). The platform and the switch are the
    /// caller's to check first, in its own words.
    /// </summary>
    public static async Task<string?> DeleteRefusalAsync(
        IFileInventory files, IEnumerable<QuarantinedNameEntity> names, ReplacementGateState gate, CancellationToken cancellationToken)
    {
        foreach (var row in names)
        {
            if (RootsOf(row, gate) is not { } roots)
                return $"{ReplacementText.Quote(QuarantineOf(row))} is no longer inside the Media folders, so nothing was deleted.";
            if (await ChangedInQuarantineAsync(files, row, roots, cancellationToken))
                return $"{ReplacementText.Quote(row.QuarantinePath)} changed in the quarantine since it was moved there, so nothing was deleted.";
        }

        return null;
    }

    /// <summary>
    /// ADR 0031 — how many names each of <paramref name="quarantined"/> has now, by its row's id, as the disk says it at its
    /// quarantine path when it is still the file the move put there; the count the move read is the fallback. A torrent
    /// removed with its files since the move leaves the quarantined name the last one, and then deleting it frees its room.
    /// </summary>
    public static async Task<Dictionary<Guid, int>> LinkCountsNowAsync(
        IFileInventory files, IEnumerable<QuarantinedNameEntity> quarantined, ReplacementGateState gate, CancellationToken cancellationToken)
    {
        var counts = new Dictionary<Guid, int>();
        foreach (var name in quarantined)
        {
            if (RootsOf(name, gate) is { } roots
                && await files.DescribeAsync(name.QuarantinePath, roots, cancellationToken) is { Status: FileInventoryStatus.Found, Value: { } facts }
                && facts.DeviceId == name.DeviceId
                && facts.Inode == name.Inode
                && facts.LinkCount is { } linkCount)
                counts[name.Id] = linkCount;
        }

        return counts;
    }

    /// <summary>Whether <paramref name="name"/>, one name of a folder's listing, is <paramref name="appPath"/>'s own, by the
    /// path's own letter-case rules.</summary>
    private static bool IsNameOf(string appPath, string name) =>
        NormalizedPath.TryParse(appPath, out var path, out _)
        && path.Segments.Count > 0
        && (path with { Segments = [.. path.Segments.SkipLast(1)] }).Append([name]).IsSameAs(path);

    /// <summary>The folder holding <paramref name="appPath"/>.</summary>
    private static string FolderOf(string appPath) =>
        NormalizedPath.TryParse(appPath, out var path, out _) && path.Segments.Count > 0
            ? (path with { Segments = [.. path.Segments.SkipLast(1)] }).ToString()
            : appPath;
}
