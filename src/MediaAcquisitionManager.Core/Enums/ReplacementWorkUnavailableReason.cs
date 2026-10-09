namespace MediaAcquisitionManager.Core.Enums;

/// <summary>
/// MAM-24 — why no copy of one matched work can be replaced by a release, the first that applies in this order
/// (ADR 0032). Written by name.
/// </summary>
public enum ReplacementWorkUnavailableReason
{
    /// <summary>Matched by a title alone, which never allows a replacement (specification §3.1).</summary>
    TitleOnly = 1,

    /// <summary>The release's ids and the work's disagree, or the work holds two of one scheme.</summary>
    ConflictingIds = 2,

    /// <summary>A TV release claiming seasons or episodes of which the library holds none.</summary>
    NoClaimedEpisodes = 3,

    /// <summary>A TV release whose title names no season, so nothing says which episodes it would replace.</summary>
    NoClaimedSeason = 4,
}
