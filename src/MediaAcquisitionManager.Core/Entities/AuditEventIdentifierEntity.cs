using System.ComponentModel.DataAnnotations;
using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Core.Entities;

/// <summary>
/// MAM-34 — an id an audit row records: what a link held before it was set, replaced or removed, or what the library
/// held for an item before and after an identification. They are rows, not a text the app would have to parse back;
/// nothing is restored from them since 0.2.1 (ADR 0026), they are the history the owner reads.
/// </summary>
/// <remarks>Written in the same save as its audit row, and removed with it.</remarks>
public class AuditEventIdentifierEntity : BaseEntity
{
    /// <summary>The audit row.</summary>
    public Guid AuditEventId { get; set; }

    /// <summary>Whether it is what the change found, or what it left.</summary>
    public AuditIdentifierRole Role { get; set; }

    /// <summary>The registry the value belongs to.</summary>
    public MediaIdentifierScheme Scheme { get; set; }

    /// <summary>The id (<c>tt0056164</c>, <c>223021</c>).</summary>
    [Required, MaxLength(LibraryItemLinkEntity.ValueMaximumLength)]
    public string Value { get; set; } = default!;

    /// <summary>Where the audit row lists it, from 0, over both roles.</summary>
    public int Position { get; set; }
}
