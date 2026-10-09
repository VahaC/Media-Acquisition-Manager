using System.ComponentModel.DataAnnotations;
using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Core.Entities;

/// <summary>
/// MAM-24 — one registry id a <see cref="ReplacementJobWorkEntity"/>'s work had when the replacement was scheduled, as
/// the app's readers count them (the owner's TMDb links applied, MAM-34): what MAM-25 compares the new file's ids with
/// (ADR 0032), kept whatever a later sync does to the work's own rows. A series' are its show's ids.
/// </summary>
public class ReplacementJobWorkIdentifierEntity : BaseEntity
{
    /// <summary>The work row; the id is removed with it.</summary>
    public Guid ReplacementJobWorkId { get; set; }

    /// <summary>The registry.</summary>
    public MediaIdentifierScheme Scheme { get; set; }

    /// <summary>The id, as read.</summary>
    [Required, MaxLength(64)]
    public string Value { get; set; } = default!;

    /// <summary>Whether it is the owner's own TMDb link rather than a library's report (ADR 0026: it counts as the old
    /// work's).</summary>
    public bool SetByOwner { get; set; }
}
