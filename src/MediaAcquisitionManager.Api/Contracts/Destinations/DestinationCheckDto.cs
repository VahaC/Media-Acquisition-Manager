using MediaAcquisitionManager.Core.Downloads;

namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>
/// Where a destination saves to and whether that is inside a media library (MAM-11), as worked out when
/// it was read: from what the download client says now and the media library folders the last syncs
/// stored, never stored itself (ADR 0003).
/// </summary>
/// <remarks>
/// MAM-11 — owner-only like the rest of the Destinations page: the paths are the owner's folders, and
/// the sentence can quote the client's address or the owner's path mappings.
/// </remarks>
/// <param name="Status">Where the folder is, or why the app can't say. Anything but <c>InLibrary</c> keeps
/// automatic replacement off for the destination (Phase C).</param>
/// <param name="ServicePath">The folder as the download client sees it; <c>null</c> when the client
/// doesn't say or wasn't asked.</param>
/// <param name="AppPath">The same folder inside the app, through the client's path mappings; <c>null</c>
/// when they give none.</param>
/// <param name="LibraryNames">The media libraries one of whose folders holds it, each once; empty unless
/// the status is <c>InLibrary</c>.</param>
/// <param name="Problem">One readable sentence saying what is wrong and what follows from it; <c>null</c>
/// only for <c>InLibrary</c>.</param>
public sealed record DestinationCheckDto(
    DestinationPathStatus Status,
    string? ServicePath,
    string? AppPath,
    IReadOnlyList<string> LibraryNames,
    string? Problem);
