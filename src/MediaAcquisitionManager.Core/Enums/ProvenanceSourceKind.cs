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
    /// readings, keyed by the file's identity, are the app's own by their table (ADR 0009). MAM-18's scan
    /// writes one for each file it finds in the media folders that no library reported: a file name with
    /// no version (ADR 0021).
    /// </remarks>
    FileInspection = 4,

    /// <summary>
    /// MAM-33 — a metadata catalog reported it: a film or show the app recommends, kept in tables of its
    /// own and never on the library's works (ADR 0025).
    /// </summary>
    Metadata = 5,

    /// <summary>
    /// MAM-34 — a person set it by hand in the app: the owner's TMDb link of a library item, kept in a table of its
    /// own (never a row of the library's facts, which a sync replaces) and shown with who set it and when.
    /// </summary>
    Person = 6,
}
