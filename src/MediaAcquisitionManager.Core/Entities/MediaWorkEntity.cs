using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Core.Entities;

/// <summary>
/// One work — a movie, an episode or a series (MAM-32) — independent of any source that knows it
/// (ADR 0006). It has no source id: a library item is not a work, and which work an item belongs to is
/// the core's decision (MAM-8, 12).
/// </summary>
/// <remarks>
/// Nothing but its kind: every title, year, identifier and episode position is a row of its own with the
/// source that asserted it, so two sources that disagree both stay visible instead of one overwriting the
/// other.
/// </remarks>
public class MediaWorkEntity : AuditableEntity
{
    /// <summary>What the work is.</summary>
    public MediaWorkKind Kind { get; set; }
}
