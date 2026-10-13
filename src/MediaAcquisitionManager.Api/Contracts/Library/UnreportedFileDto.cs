namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>MAM-42 — one file the scan of the media folders found that no library reports now.</summary>
/// <param name="Id">The name's id, which Ignore and Stop ignoring take.</param>
/// <param name="AppPath">The file's path in the app, as the scan found it.</param>
/// <param name="SizeBytes">Its size as the app's last look read it; <c>null</c> until read.</param>
/// <param name="FirstSeenUtc">When a scan first stored it.</param>
/// <param name="IgnoredUtc">When the owner told the app to stop counting it; <c>null</c> while it counts.</param>
/// <param name="Place">Where it sits among the libraries' folders, which says why a library most likely doesn't list
/// it.</param>
/// <param name="Inspection">What the app's last look at it found, the reason it couldn't read it for the owner only;
/// <c>null</c> until it has looked.</param>
public sealed record UnreportedFileDto(
    Guid Id,
    string AppPath,
    long? SizeBytes,
    DateTime FirstSeenUtc,
    DateTime? IgnoredUtc,
    UnreportedFilePlaceDto Place,
    LibraryFileInspectionDto? Inspection);
