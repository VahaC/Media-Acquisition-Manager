namespace MediaAcquisitionManager.Core.Enums;

/// <summary>
/// MAM-20 — what tells the files of one duplicate group apart, or the ids of its works (specification §3.3: "possible
/// distinctions"). A group with any is flagged for review. Written by name.
/// </summary>
public enum DuplicateDistinctionKind
{
    /// <summary>
    /// The group's works hold two values of one registry's id (Vasyl, 2026-10-07: they stay one work, the
    /// disagreement shown) — two works with one IMDb id and different TMDb ids, or a linked id beside another library's;
    /// for a series' id, two shows behind one episode's ids.
    /// </summary>
    IdentifierConflict = 1,

    /// <summary>
    /// Episodes of one series carrying one episode's ids under different season or episode numbers (S03E31 and S03E33,
    /// ADR 0017).
    /// </summary>
    EpisodeNumbers = 2,

    /// <summary>
    /// The files' editions differ (<see cref="MediaEdition"/>, read from the file names and the libraries' version
    /// names): another cut, or one labelled and one not.
    /// </summary>
    Edition = 3,

    /// <summary>The dynamic ranges the media inspector measured differ (HDR and SDR copies); unknown is ignored.</summary>
    DynamicRange = 4,
}
