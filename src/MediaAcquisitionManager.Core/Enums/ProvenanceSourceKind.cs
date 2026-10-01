namespace MediaAcquisitionManager.Core.Enums;

/// <summary>
/// What kind of source asserted a stored fact — a title, an identifier, a version, a file name, a
/// release (ADR 0006). Stored by name, and kept when the integration that reported the fact is
/// removed, so "who said so" still has an answer.
/// </summary>
public enum ProvenanceSourceKind
{
    /// <summary>A media library integration reported it (Jellyfin in the reference installation).</summary>
    MediaLibrary = 1,

    /// <summary>A release search integration listed it.</summary>
    ReleaseSearch = 2,

    /// <summary>A download client reported it.</summary>
    DownloadClient = 3,

    /// <summary>The app read it from the file itself; no integration was involved.</summary>
    /// <remarks>
    /// MAM-9 writes no row of this kind: it writes what it found on the library's own file names, and its
    /// readings, keyed by the file's identity, are the app's own by their table (ADR 0009). A name the app
    /// finds on disk that no library reported (Phase B's scan) would be one.
    /// </remarks>
    FileInspection = 4,
}
