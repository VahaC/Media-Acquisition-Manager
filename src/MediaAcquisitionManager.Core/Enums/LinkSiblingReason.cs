namespace MediaAcquisitionManager.Core.Enums;

/// <summary>
/// MAM-34 — why a TMDb candidate looks like the neighbour of a work already linked: a linked work in the same folder
/// shares this with it.
/// </summary>
public enum LinkSiblingReason
{
    /// <summary>Both films are in one TMDb collection.</summary>
    SameCollection = 1,

    /// <summary>Both share a director (a show: a creator).</summary>
    SameDirector = 2,

    /// <summary>The candidate is the show the neighbour is linked to as an episode, or as the show itself.</summary>
    SameShow = 3,
}
