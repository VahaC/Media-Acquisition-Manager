using System.ComponentModel.DataAnnotations;
using MediaAcquisitionManager.Core.Accounts;
using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Core.Entities;

/// <summary>
/// MAM-21 — the installation owner's decision on one possible match of the Duplicates list: confirmed or dismissed, kept
/// by the group's key (ADR 0029). <see cref="AuditableEntity.CreatedUtc"/> is when the group was first decided.
/// </summary>
/// <remarks>
/// <para>
/// By the key, not by foreign keys to the works or files: the group itself is stored nowhere (ADR 0028), and its key is
/// what names it on every read while its members stay. A group whose members change gets another key and is asked about
/// again; the row of the old key is kept, matching nothing, and comes back into force if the same members do.
/// </para>
/// <para>
/// A decision never makes the works one, never changes whether the group needs review and never allows a removal.
/// </para>
/// </remarks>
public class DuplicateMatchDecisionEntity : AuditableEntity
{
    /// <summary>The length of every group key: 32 lowercase hex characters (<c>DuplicateGrouping</c>).</summary>
    public const int GroupKeyLength = 32;

    /// <summary>The key of the group decided on, as the Duplicates list gives it.</summary>
    [Required, MaxLength(GroupKeyLength)]
    public string GroupKey { get; set; } = default!;

    /// <summary>What the owner said, last.</summary>
    public DuplicateMatchDecision Decision { get; set; }

    /// <summary>The user who decided last; <c>null</c> once that user is deleted.</summary>
    public Guid? DecidedByUserId { get; set; }

    /// <summary>Their user name at the time, copied (the audit-row pattern).</summary>
    [Required, MaxLength(UserNameRules.MaximumLength)]
    public string DecidedByUserName { get; set; } = default!;

    /// <summary>When <see cref="Decision"/> was last set.</summary>
    public DateTime DecidedUtc { get; set; }
}
