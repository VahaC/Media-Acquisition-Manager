using System.ComponentModel.DataAnnotations;
using MediaAcquisitionManager.Core.Accounts;
using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Core.Entities;

/// <summary>
/// MAM-40 — the installation owner's removal of one copy of a duplicate group from the Duplicates page (ADR 0030's
/// quarantine): the copy's video and its own companions renamed into
/// <c>&lt;allowed root&gt;/.mam-quarantine/&lt;removal id&gt;/…</c>, name by name in its <see cref="CopyRemovalNameEntity"/>
/// rows, and kept there until the owner restores them or deletes them for good. Written inside the owner's request, after
/// every check passed, with its names and its <c>CopyRemovalStarted</c> audit row in one transaction.
/// </summary>
/// <remarks>
/// <para>
/// A snapshot, as a replacement's rows are: no key into the library's rows a sync rebuilds (works, versions, files) — the
/// group's key and class, its heading and the file's id are copied — so a sync between the removal and its restore can't
/// take its history away.
/// </para>
/// <para>
/// The explicit consent is the row itself with <see cref="RequestedByUserId"/>, <see cref="AuditableEntity.CreatedUtc"/>
/// and its audit row; the state, its reason and its time are below.
/// </para>
/// </remarks>
public class CopyRemovalEntity : AuditableEntity
{
    /// <summary>The longest <see cref="Reason"/> kept, as a replacement's.</summary>
    public const int ReasonMaximumLength = ReplacementJobEntity.ReasonMaximumLength;

    /// <summary>The longest <see cref="Title"/> kept: a series' title, a place and an episode's title, each up to 500
    /// characters in the library, are cut to it with an ellipsis.</summary>
    public const int TitleMaximumLength = 1000;

    /// <summary>The length of a duplicate group's key: 32 lowercase hex characters.</summary>
    public const int GroupKeyLength = 32;

    /// <summary>The owner who removed it; <c>null</c> once that account is deleted.</summary>
    public Guid? RequestedByUserId { get; set; }

    /// <summary>That user's name when it was removed, copied (the audit-row pattern).</summary>
    [Required, MaxLength(UserNameRules.MaximumLength)]
    public string RequestedByUserName { get; set; } = default!;

    /// <summary>Where it stands.</summary>
    public CopyRemovalState State { get; set; }

    /// <summary>When <see cref="State"/> last changed.</summary>
    public DateTime StateChangedUtc { get; set; }

    /// <summary>Why it waits for review or didn't move, one readable sentence; <c>null</c> otherwise.</summary>
    [MaxLength(ReasonMaximumLength)]
    public string? Reason { get; set; }

    /// <summary>The group's heading when it was removed, as the Duplicates page showed it: "The Gods Must Be Crazy
    /// (1980)", or an episode's "Clarkson's Farm · S02E03 · Title"; the video's own name for a group no library
    /// reported.</summary>
    [Required, MaxLength(TitleMaximumLength)]
    public string Title { get; set; } = default!;

    /// <summary>What the group's files were to each other: <see cref="DuplicateClass.SameWork"/> or
    /// <see cref="DuplicateClass.ByteIdentical"/>, the only groups a copy is removed from.</summary>
    public DuplicateClass GroupClass { get; set; }

    /// <summary>The group's key when it was removed (ADR 0028).</summary>
    [Required, MaxLength(GroupKeyLength)]
    public string GroupKey { get; set; } = default!;

    /// <summary>The library file (<see cref="PhysicalFileEntity"/>) the owner removed. No key: a snapshot id, as a sync
    /// replaces those rows.</summary>
    public Guid PhysicalFileId { get; set; }

    /// <summary>The video's path inside the app, canonical: where Restore puts it back.</summary>
    [Required, MaxLength(PhysicalFileEntity.ServicePathMaximumLength)]
    public string AppPath { get; set; } = default!;

    /// <summary>The video's path as its library reported it: the spelling the library is told of the move by, as a
    /// replacement's copy is.</summary>
    [Required, MaxLength(PhysicalFileEntity.ServicePathMaximumLength)]
    public string ServicePath { get; set; } = default!;

    /// <summary>When the removal reached <see cref="CopyRemovalState.Quarantined"/>; <c>null</c> before, and for one that
    /// never did.</summary>
    public DateTime? QuarantinedUtc { get; set; }

    /// <summary>
    /// Since when the library is owed a notice that the names left or came back (ADR 0030: Jellyfin hears
    /// <c>POST /Library/Media/Updated</c> after every move and every restore); <c>null</c> while none is owed. Cleared once
    /// the library took it, or was given up on.
    /// </summary>
    public DateTime? LibraryNoticeDueUtc { get; set; }
}
