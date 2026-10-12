using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Paths;

namespace MediaAcquisitionManager.Api.Services.CopyRemovals;

/// <summary>
/// MAM-40 — one file of a duplicate group as the removal rules read it (<see cref="CopyRemovalRules"/>): where the app
/// finds it, what its last look read, and the works holding it — the Duplicates list's own name, so the list's verdict and
/// the Remove request judge it alike.
/// </summary>
/// <param name="FileId">The library file the list shows (<c>LibraryDuplicateFileDto.File.Id</c>).</param>
/// <param name="AppPath">Its path inside the app through its library's mappings now; <c>null</c> when they give none.</param>
/// <param name="ServicePath">Its path as its library reported it.</param>
/// <param name="InspectionStatus">What the app's last look at it found; <c>null</c> before any.</param>
/// <param name="DeviceId">Its device, from that look.</param>
/// <param name="Inode">Its inode, from that look.</param>
/// <param name="SizeBytes">Its size, from that look.</param>
/// <param name="ModifiedUtc">Its modification time, from that look.</param>
/// <param name="HolderWorkIds">The library works holding it; none for a name only the folder scan found.</param>
public sealed record CopyRemovalCandidate(
    Guid FileId,
    NormalizedPath? AppPath,
    string ServicePath,
    FileInspectionStatus? InspectionStatus,
    long? DeviceId,
    long? Inode,
    long? SizeBytes,
    DateTime? ModifiedUtc,
    IReadOnlyList<Guid> HolderWorkIds)
{
    /// <summary>Whether the app's last look read it as media and kept its identity — what removing it, or keeping another,
    /// stands on (MAM-40 rules 4 and 8).</summary>
    public bool IsReadable =>
        this is { InspectionStatus: FileInspectionStatus.Inspected, DeviceId: not null, Inode: not null, SizeBytes: not null, ModifiedUtc: not null };

    /// <summary>
    /// Whether this is a file the app read as media other than <paramref name="file"/> — another device and inode, not just
    /// another name of it —: a copy that stays when <paramref name="file"/> goes (MAM-40 rule 8 and check 10). A second name
    /// of one file is that file, and a member without an identity is no copy the app knows plays.
    /// </summary>
    public bool IsAnotherReadableFileThan(CopyRemovalCandidate file)
    {
        ArgumentNullException.ThrowIfNull(file);
        return FileId != file.FileId && IsReadable && (DeviceId != file.DeviceId || Inode != file.Inode);
    }
}
