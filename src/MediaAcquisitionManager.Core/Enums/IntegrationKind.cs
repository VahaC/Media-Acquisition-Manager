namespace MediaAcquisitionManager.Core.Enums;

/// <summary>
/// What the core uses an integration for. Stored by name on the connection row, copied from its
/// adapter's descriptor when the row is created (MAM-7), so the list filters by kind in SQL and a row
/// whose adapter type a later build no longer registers still says what it was.
/// </summary>
public enum IntegrationKind
{
    /// <summary>A media server whose library the app reads: what the owner already has (MAM-8).</summary>
    MediaLibrary = 1,

    /// <summary>A source the app searches for releases (MAM-10).</summary>
    ReleaseSearch = 2,

    /// <summary>A client that downloads the release the owner picked (MAM-11 onwards).</summary>
    DownloadClient = 3,

    /// <summary>
    /// MAM-33 — a catalog of films and shows the app looks works up in and recommends from: what the
    /// library doesn't have yet. Never a source of the library's own facts (ADR 0025).
    /// </summary>
    Metadata = 4,
}
