namespace MediaAcquisitionManager.Core.Entities;

/// <summary>
/// MAM-33 — a user's own recommendation settings, saved from their Profile (Vasyl's decision 5): which tabs
/// they see, and how many release-source searches the app may run for their lists in a day.
/// </summary>
/// <remarks>
/// A row only once the user saves; until then the defaults hold (<c>RecommendationSettingsRules.Default</c>:
/// every tab on, 60 searches). A table of its own rather than columns on the user: the Users table stays the
/// accounts' alone, and a user who never saved needs no row at all. <see cref="AuditableEntity.CreatedUtc"/>
/// is the first save.
/// </remarks>
public class UserRecommendationSettingsEntity : AuditableEntity
{
    /// <summary>The user; one row per user.</summary>
    public Guid UserId { get; set; }

    /// <summary>Whether the Movies tab is on.</summary>
    public bool MoviesEnabled { get; set; }

    /// <summary>Whether the Series tab is on.</summary>
    public bool SeriesEnabled { get; set; }

    /// <summary>Whether the Cartoons tab is on.</summary>
    public bool CartoonsEnabled { get; set; }

    /// <summary>Whether the Documentaries tab is on.</summary>
    public bool DocumentariesEnabled { get; set; }

    /// <summary>How many release-source searches the app may run for this user's lists in 24 hours; 0 runs
    /// none.</summary>
    public int DailySearchLimit { get; set; }
}
