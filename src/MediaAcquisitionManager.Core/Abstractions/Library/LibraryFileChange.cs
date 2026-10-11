namespace MediaAcquisitionManager.Core.Abstractions.Library;

/// <summary>
/// MAM-26 — what happened at the paths a library is asked to look at (<see cref="IMediaLibraryFileLookup.RequestScanAsync"/>):
/// a file appeared, or a file went. Said in the request as a library's own folder watcher would report it; the library looks
/// at each path whatever the change says, so it never decides what the library does with it (ADR 0030).
/// </summary>
public enum LibraryFileChange
{
    /// <summary>A file appeared at the path: a download's new file (MAM-25), or an old copy put back where it was.</summary>
    Created = 1,

    /// <summary>The file at the path went: an old copy moved into the quarantine.</summary>
    Deleted = 2,
}
