using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>MAM-20 — one file name of a duplicate group, with what the app read of it and the works holding it.</summary>
/// <param name="File">The name as the Library page shows a file (<see cref="LibraryFileDto"/>): its app path, path
/// status, inspection and facts, the owner-only sentences <c>null</c> for everyone else; no library for a name only the
/// folder scan found.</param>
/// <param name="ReadSizeBytes">Its file's size as the app's last look read it; <c>null</c> when unknown.</param>
/// <param name="DeviceId">Its file's device, from the app's last look; <c>null</c> when unknown.</param>
/// <param name="Inode">Its file's inode, from the app's last look; <c>null</c> when unknown.</param>
/// <param name="LinkCount">How many names its file has; <c>null</c> when unknown.</param>
/// <param name="Sha256">Its bytes' SHA-256, when the app hashed them as they are now; <c>null</c> otherwise.</param>
/// <param name="Editions">The editions its file name and its versions' names say it is.</param>
/// <param name="Works">The library works holding it, each through a version, in title order; none for a name no
/// library reported.</param>
/// <param name="Removal">MAM-40 — whether Remove this copy may take it into the quarantine, and why not; <c>null</c> for a
/// group of one file's names or a possible match, which offers no removal.</param>
public sealed record LibraryDuplicateFileDto(
    LibraryFileDto File,
    long? ReadSizeBytes,
    long? DeviceId,
    long? Inode,
    int? LinkCount,
    string? Sha256,
    IReadOnlyList<MediaEdition> Editions,
    IReadOnlyList<LibraryDuplicateWorkDto> Works,
    CopyRemovalVerdictDto? Removal = null);
