using System.ComponentModel.DataAnnotations;
using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Core.Entities;

/// <summary>
/// MAM-33 — one work the last successful run recommends, in the tab its genres put it, with its score and
/// the reasons it is recommended for (<see cref="RecommendationReasonEntity"/>). Replaced as a whole by the
/// next successful run; <see cref="AuditableEntity.CreatedUtc"/> is when that run stored it.
/// </summary>
/// <remarks>
/// For every user at once: a user's own tabs, "Not interested" and the download jobs are applied when the
/// list is read, never here, so one run serves everyone (ADR 0023).
/// </remarks>
public class RecommendationCandidateEntity : AuditableEntity
{
    /// <summary>The longest <see cref="MissingSeasons"/> list kept: far past any show's seasons.</summary>
    public const int MissingSeasonsMaximumLength = 200;

    /// <summary>The catalog work recommended; one candidate per work.</summary>
    public Guid CatalogWorkId { get; set; }

    /// <summary>The tab its genres put it in.</summary>
    public RecommendationTab Tab { get; set; }

    /// <summary>How strongly it is recommended (ADR 0023's formula); the list's order, highest first.</summary>
    public double Score { get; set; }

    /// <summary>
    /// Whether it is listed only once a release source was found to carry it: a fallback "top rated" work,
    /// recommended for nothing in the library, earns its place only by being there to download.
    /// </summary>
    public bool RequiresAvailability { get; set; }

    /// <summary>
    /// The aired seasons a held series lacks, ascending and comma-separated ("4,5"); <c>null</c> for any other
    /// candidate. The availability check looks for releases of these seasons only.
    /// </summary>
    [MaxLength(MissingSeasonsMaximumLength)]
    public string? MissingSeasons { get; set; }
}
