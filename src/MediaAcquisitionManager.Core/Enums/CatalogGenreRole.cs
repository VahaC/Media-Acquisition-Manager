namespace MediaAcquisitionManager.Core.Enums;

/// <summary>
/// MAM-33 — what a catalog genre makes a work, for the recommendation tabs: a documentary goes to the
/// Documentaries tab, then an animation, anime included, to Cartoons; any other genre decides nothing.
/// Stored by name.
/// </summary>
public enum CatalogGenreRole
{
    /// <summary>A genre that decides no tab.</summary>
    Other = 1,

    /// <summary>Animation: the work is a cartoon, unless it is a documentary.</summary>
    Animation = 2,

    /// <summary>Documentary: the work is a documentary, whatever else it is.</summary>
    Documentary = 3,
}
