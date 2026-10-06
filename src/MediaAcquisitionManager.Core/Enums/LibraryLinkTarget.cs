namespace MediaAcquisitionManager.Core.Enums;

/// <summary>
/// MAM-34 — what a library item's TMDb link says the item is. Stored by name.
/// </summary>
/// <remarks>
/// TMDb numbers films and shows apart (ADR 0017), so the target decides which number space the link's value is in and
/// what may be written into the library: only an item that <em>is</em> the catalog work can carry its TMDb id there.
/// </remarks>
public enum LibraryLinkTarget
{
    /// <summary>
    /// The item is the catalog work: a Movie work linked to a TMDb film, a Series work to a TMDb show. Written into the
    /// library's ids when the library writes that kind of item — Jellyfin: a film or a series.
    /// </summary>
    Work = 1,

    /// <summary>
    /// The item is an episode of the TMDb show, optionally at a season and an episode: a Movie item the library lists as
    /// a film (Planet Earth's E08), or an Episode work with no series (Tom &amp; Jerry Kids). The show's id is never
    /// written into the library, whose film id would be the wrong number space; MAM-34 follow-up — on a film item, with
    /// its season and episode, the episode's own IMDb id TMDb gives is (Vasyl, 2026-10-06).
    /// </summary>
    ShowEpisode = 2,
}
