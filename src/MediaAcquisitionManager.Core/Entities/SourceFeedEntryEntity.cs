using System.ComponentModel.DataAnnotations;
using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Core.Entities;

/// <summary>
/// MAM-33 — one release a release source listed among its newest ("New on Toloka"): what it is called, and,
/// once resolved, the catalog work it most likely is. A run recommends a work published lately for that
/// reason (<see cref="RecommendationReasonKind.NewAtSource"/>).
/// </summary>
/// <remarks>
/// <para>
/// A fact of the release source (<see cref="ProvenanceSourceKind.ReleaseSearch"/>), its
/// <see cref="ProvenancedEntity.SourceItemId"/> the source's own id of the release (the Torznab guid). Unlike
/// every other fact, it goes with its source: a feed is nothing without the source that publishes it.
/// <see cref="AuditableEntity.CreatedUtc"/> is when the app first saw it in the feed, and
/// <see cref="ProvenancedEntity.ObservedUtc"/> when it last did.
/// </para>
/// <para>
/// Never a release a user can download from: no payload is kept. Pruned once older than the feed's lifetime.
/// </para>
/// </remarks>
public class SourceFeedEntryEntity : ProvenancedEntity
{
    /// <summary>The release's title as the source lists it.</summary>
    [Required, MaxLength(1000)]
    public string Title { get; set; } = default!;

    /// <summary>The release's description, where a source gives the original title (Toloka); <c>null</c>
    /// when it gives none.</summary>
    [MaxLength(1000)]
    public string? Description { get; set; }

    /// <summary>What the release is by its category (ADR 0020).</summary>
    public ReleaseKind Kind { get; set; }

    /// <summary>The source's categories of the release, comma-separated, as a release's are.</summary>
    [MaxLength(ReleaseEntity.CategoryIdsMaximumLength)]
    public string? CategoryIds { get; set; }

    /// <summary>When the source published it; <c>null</c> when it gives no date.</summary>
    public DateTime? PublishedUtc { get; set; }

    /// <summary>When the app looked up which catalog work it is — found or not; <c>null</c> until then.</summary>
    public DateTime? ResolvedUtc { get; set; }

    /// <summary>The catalog work it most likely is; <c>null</c> until resolved, when nothing usable was found,
    /// or once that work's row is gone.</summary>
    public Guid? CatalogWorkId { get; set; }
}
