namespace MediaAcquisitionManager.Core.Enums;

/// <summary>
/// Which of a source's searches a query goes to (MAM-10). Never stored: the core picks it per query from
/// the source's capabilities (<c>ReleaseSearchPlanning</c>).
/// </summary>
public enum ReleaseSearchMode
{
    /// <summary>The source's search across every section, by text only.</summary>
    General = 1,

    /// <summary>The source's movie search, which may also take identifiers and a year.</summary>
    Movie = 2,

    /// <summary>The source's TV search, which may also take a show's identifiers and a season (MAM-32).</summary>
    Tv = 3,

    /// <summary>
    /// MAM-33 — the source's newest releases in the asked categories, with no text at all: what a
    /// tracker's front page lists. Recommendations read it every few hours ("New on Toloka"); Toloka's
    /// gateway answers its latest 90, newest first, and an offset pages through nothing (VendorSamples).
    /// </summary>
    Latest = 4,
}
