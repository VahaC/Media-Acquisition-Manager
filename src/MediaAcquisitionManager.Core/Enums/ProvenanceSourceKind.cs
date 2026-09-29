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

    /// <summary>The app read it from the file itself (MAM-9); no integration was involved.</summary>
    FileInspection = 4,
}
