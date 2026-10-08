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
    /// for a series' id, two shows behind one episode's ids — at one place when the shows share an id or a title, as
    /// MAM-37 keeps one show's places apart, but at any places when they share neither, since nothing then says they
    /// are one show. Never two episodes of one show at different places, which are never one group (MAM-37).
    /// </summary>
    IdentifierConflict = 1,

    /// <summary>
    /// The files' editions differ (<see cref="MediaEdition"/>, read from the file names and the libraries' version
    /// names): another cut, or one labelled and one not. A possible match's only since MAM-37 (Vasyl, 2026-10-08:
    /// different cuts are not duplicates): copies of one work whose names say different editions are no copies of each
    /// other, so no same-work group holds them.
    /// </summary>
    Edition = 3,

    /// <summary>The dynamic ranges the media inspector measured differ (HDR and SDR copies); unknown is ignored.</summary>
    DynamicRange = 4,

    /// <summary>
    /// MAM-37 (Vasyl, 2026-10-08) — the copies of one work run very differently: the shortest measured copy is under
    /// three quarters of the longest (<see cref="Duplicates.RunningTimeSpread"/>) — a featurette under the film's ids,
    /// or another film a library gave the first one's ids. A copy is what one version holds, its running time the sum
    /// of its files' measured durations; a copy with a file not measured decides nothing. Copies of one work only.
    /// </summary>
    RunningTimes = 5,
}
