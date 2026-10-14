namespace MediaAcquisitionManager.Core.Abstractions.Downloads;

/// <summary>
/// MAM-30 — what a download client's destination is (ADR 0010, ADR 0039): one of its own categories, which save where
/// the client says, or a folder the app proposes and the client confirms it can reach. Stored on a download job by
/// name, so a value added later never shifts what was kept.
/// </summary>
public enum DownloadDestinationKind
{
    /// <summary>The client's own category (qBittorrent's): its key is the category's name, compared exactly as the
    /// client spells it, and the client says which folder it saves to.</summary>
    Category = 1,

    /// <summary>A folder as the client sees it (Transmission's <c>download-dir</c>): its key is the folder in
    /// <see cref="Paths.NormalizedPath"/> canonical form, compared folder name by folder name
    /// (<see cref="Paths.NormalizedPath.IsSameAs"/>).</summary>
    Folder = 2,
}
