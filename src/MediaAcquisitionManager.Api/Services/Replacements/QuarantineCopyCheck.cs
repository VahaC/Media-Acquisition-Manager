using MediaAcquisitionManager.Core.Abstractions.Files;
using MediaAcquisitionManager.Core.Files;
using MediaAcquisitionManager.Core.Paths;

namespace MediaAcquisitionManager.Api.Services.Replacements;

/// <summary>
/// MAM-40 — the half of check 5 (ADR 0030, ADR 0033) a replacement's old copy and a removed copy share right before either
/// moves into the quarantine: inside an allowed root, in a ticked folder, no clip of a disc, and still the file the app
/// read — path, device and inode, size and modification time —, looked at within the one root whose quarantine takes it.
/// What it found, never a sentence: the mover (MAM-26) and the owner's Remove each word their own.
/// </summary>
/// <remarks>Reads the disk only; writes nothing.</remarks>
internal static class QuarantineCopyCheck
{
    /// <summary>The allowed roots of <paramref name="gate"/> that parse, each with its stored text: what
    /// <see cref="LookAsync"/> finds a copy's root among.</summary>
    public static IReadOnlyList<(string Text, NormalizedPath Path)> RootsOf(ReplacementGateState gate) =>
    [
        .. gate.AllowedRoots
            .Select(text => (Text: text, Path: NormalizedPath.TryParse(text, out var path, out _) ? path : null))
            .Where(root => root.Path is not null)
            .Select(root => (root.Text, Path: root.Path!)),
    ];

    /// <summary>
    /// The copy at <paramref name="appPath"/>, expected to be the file of <paramref name="deviceId"/> and
    /// <paramref name="inode"/>, <paramref name="sizeBytes"/> long and last written at <paramref name="modifiedUtc"/>,
    /// checked now inside <paramref name="gate"/>'s allowed roots (<paramref name="roots"/>, <see cref="RootsOf"/>).
    /// </summary>
    public static async Task<QuarantineCopyLook> LookAsync(
        IFileInventory files,
        string appPath,
        long deviceId,
        long inode,
        long sizeBytes,
        DateTime modifiedUtc,
        ReplacementGateState gate,
        IReadOnlyList<(string Text, NormalizedPath Path)> roots,
        CancellationToken cancellationToken)
    {
        if (!NormalizedPath.TryParse(appPath, out var parsed, out _) || roots.FirstOrDefault(root => parsed.IsUnder(root.Path)) is not { Text: not null } root)
            return QuarantineCopyLook.Stopped(QuarantineCopyStop.OutsideMediaFolders);
        if (!gate.IsInTickedFolder(parsed))
            return QuarantineCopyLook.Stopped(QuarantineCopyStop.NotTicked);
        if (DiscFolders.HoldsClip(parsed))
            return QuarantineCopyLook.Stopped(QuarantineCopyStop.DiscClip);

        // Within the root whose quarantine takes it alone, as the move itself goes (ADR 0030): a copy a folder link leads to
        // in another allowed root is that root's, and a Restore, which reaches back through this one, could never put it back.
        var described = await files.DescribeAsync(appPath, [root.Text], cancellationToken);
        return described switch
        {
            { Status: FileInventoryStatus.NotFound } => QuarantineCopyLook.Stopped(QuarantineCopyStop.Gone),
            { Status: FileInventoryStatus.Found, Value: { } fresh }
                when fresh.DeviceId == deviceId && fresh.Inode == inode && fresh.SizeBytes == sizeBytes && fresh.ModifiedUtc == modifiedUtc =>
                QuarantineCopyLook.Found(parsed, root.Text, root.Path, fresh),
            { Status: FileInventoryStatus.Found } => QuarantineCopyLook.Stopped(QuarantineCopyStop.Changed),
            _ => QuarantineCopyLook.Stopped(QuarantineCopyStop.NotRead, described.Error),
        };
    }
}
