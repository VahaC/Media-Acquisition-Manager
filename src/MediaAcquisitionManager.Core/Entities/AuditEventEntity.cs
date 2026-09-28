using System.ComponentModel.DataAnnotations;
using MediaAcquisitionManager.Core.Accounts;
using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Core.Entities;

/// <summary>
/// Something a user did that someone may later have to account for. Generic on purpose: user
/// management writes these now, downloads and replacements later, all into this one table.
/// </summary>
/// <remarks>
/// <see cref="AuditableEntity.CreatedUtc"/> is when it happened. The actor's and the target's names
/// are copied onto the row because the row must outlive both: an audit trail that goes blank when
/// the user it names is deleted can't answer "who deleted whom".
/// </remarks>
public class AuditEventEntity : AuditableEntity
{
    /// <summary>What happened.</summary>
    public AuditAction Action { get; set; }

    /// <summary>The user who did it; <c>null</c> once that user is deleted.</summary>
    public Guid? ActorUserId { get; set; }

    /// <summary>The actor's user name at the time.</summary>
    [MaxLength(UserNameRules.MaximumLength)]
    public string? ActorUserName { get; set; }

    /// <summary>What kind of row <see cref="TargetId"/> names.</summary>
    public AuditTargetKind? TargetKind { get; set; }

    /// <summary>What it was done to. No foreign key: the target can be a row of any table, and may
    /// be gone.</summary>
    public Guid? TargetId { get; set; }

    /// <summary>The target's display name at the time.</summary>
    [MaxLength(200)]
    public string? TargetName { get; set; }
}
