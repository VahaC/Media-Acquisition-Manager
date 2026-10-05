using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Core.Entities;

/// <summary>
/// MAM-33 — one search of the release sources run for a user's recommendations: the budget log a user's
/// daily search limit is counted from (ADR 0024). Pruned once older than the budget's window needs.
/// </summary>
public class RecommendationSearchEntity : BaseEntity
{
    /// <summary>The user whose list the search served; the one it counts against.</summary>
    public Guid UserId { get; set; }

    /// <summary>The tab of that list the work is in.</summary>
    public RecommendationTab Tab { get; set; }

    /// <summary>
    /// The catalog work searched for. No foreign key: the log counts searches, and a search still counts
    /// once its work's row is replaced or gone.
    /// </summary>
    public Guid CatalogWorkId { get; set; }

    /// <summary>When it was run.</summary>
    public DateTime SearchedUtc { get; set; }
}
