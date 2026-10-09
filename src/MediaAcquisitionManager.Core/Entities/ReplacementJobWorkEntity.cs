using System.ComponentModel.DataAnnotations;
using MediaAcquisitionManager.Core.Abstractions.Metadata;
using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Core.Entities;

/// <summary>
/// MAM-24 — one matched work a <see cref="ReplacementJobEntity"/> replaces copies of, as the dialog showed it when the
/// owner scheduled (ADR 0032): the work the new file is expected to be, how the release matched it, and whether the
/// owner confirmed a match by title and year. Its registry ids then are <see cref="ReplacementJobWorkIdentifierEntity"/>
/// rows.
/// </summary>
/// <remarks>
/// <see cref="MediaWorkId"/> carries no key: a sync reassigns and deletes works, and the snapshot — title, year, ids —
/// is what MAM-25 compares the new file with, whatever happened to the row since. No <c>CreatedUtc</c>: written with its
/// job and never alone.
/// </remarks>
public class ReplacementJobWorkEntity : BaseEntity
{
    /// <summary>The longest <see cref="ConfirmationText"/>: a title of the longest kept, its year, its seasons and its
    /// libraries.</summary>
    public const int ConfirmationTextMaximumLength = 1000;

    /// <summary>The job; the row is removed with it.</summary>
    public Guid ReplacementJobId { get; set; }

    /// <summary>The matched work: a film, or a series for a TV release.</summary>
    public Guid MediaWorkId { get; set; }

    /// <summary>Its kind.</summary>
    public MediaWorkKind Kind { get; set; }

    /// <summary>Its title as the dialog showed it; <c>null</c> when it has none.</summary>
    [MaxLength(MediaWorkTitleEntity.TextMaximumLength)]
    public string? Title { get; set; }

    /// <summary>Its year as the dialog showed it; <c>null</c> when unknown.</summary>
    public int? Year { get; set; }

    /// <summary>How the release matched it: by an id, or by title and year (title alone and conflicting ids are never
    /// scheduled).</summary>
    public MatchEvidence Evidence { get; set; }

    /// <summary>Whether the owner ticked the confirmation a match by title and year needs; <c>false</c> for a match by
    /// an id, which needs none.</summary>
    public bool ConfirmedByOwner { get; set; }

    /// <summary>The confirmation's own words as the dialog showed them — "This release is Alien (1979)" —, whether or
    /// not this match needed it.</summary>
    [Required, MaxLength(ConfirmationTextMaximumLength)]
    public string ConfirmationText { get; set; } = default!;
}
