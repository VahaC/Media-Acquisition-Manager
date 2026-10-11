using System.ComponentModel.DataAnnotations;
using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Core.Entities;

/// <summary>
/// MAM-26 — one name a <see cref="ReplacementJobEntity"/> moves into the quarantine (ADR 0030): a ticked copy's video, or
/// one of its own companions, where it was, where it goes, the identity read right before the move, and what became of
/// it. The plan is written whole when the mover claims the job, before anything is renamed, so a restart can say how
/// far it got ("3 of 5 names are in the quarantine").
/// </summary>
/// <remarks>
/// <para>
/// The identity is the one the move expects and, a rename keeping every part of it, the one Restore and Delete for good
/// expect in the quarantine: a name that changed there is never put back or deleted. <see cref="AllowedRoot"/> is the
/// allowed root whose quarantine holds it, so the Media folders page can refuse a Save that would leave it outside every
/// allowed root.
/// </para>
/// <para>
/// No key into <see cref="ReplacementJobFileEntity"/> (the <see cref="ReplacementJobFileEntity.PairedNewFileId"/>
/// precedent): the id says which ticked copy the name belongs to, and the job's cascade removes both. No
/// <c>CreatedUtc</c>: part of its job, written with its plan and never alone.
/// </para>
/// </remarks>
public class ReplacementJobNameEntity : BaseEntity
{
    /// <summary>The longest <see cref="Problem"/> kept, as the job's own reason; a longer sentence is cut to it.</summary>
    public const int ProblemMaximumLength = ReplacementJobEntity.ReasonMaximumLength;

    /// <summary>The job; the row is removed with it.</summary>
    public Guid ReplacementJobId { get; set; }

    /// <summary>The 0-based position among the job's names, in the order they move: each ticked copy's video, then its
    /// companions. A later run's names come after the earlier ones'.</summary>
    public int Position { get; set; }

    /// <summary>The ticked copy (<see cref="ReplacementJobFileEntity"/>) the name belongs to. No key: a snapshot id.</summary>
    public Guid ReplacementJobFileId { get; set; }

    /// <summary>A video or a companion.</summary>
    public ReplacementNameKind Kind { get; set; }

    /// <summary>Where the name was, inside the app, canonical: where Restore puts it back.</summary>
    [Required, MaxLength(PhysicalFileEntity.ServicePathMaximumLength)]
    public string AppPath { get; set; } = default!;

    /// <summary>Where it goes in the quarantine, canonical: <c>&lt;root&gt;/.mam-quarantine/&lt;job id&gt;/…</c>
    /// (<c>QuarantineFolder.PathOf</c>).</summary>
    [Required, MaxLength(PhysicalFileEntity.ServicePathMaximumLength)]
    public string QuarantinePath { get; set; } = default!;

    /// <summary>The allowed root, canonical, whose quarantine holds it.</summary>
    [Required, MaxLength(PhysicalFileEntity.ServicePathMaximumLength)]
    public string AllowedRoot { get; set; } = default!;

    /// <summary><c>st_dev</c> right before the move, stored bit for bit like <see cref="PhysicalFileEntity.DeviceId"/>.</summary>
    public long DeviceId { get; set; }

    /// <summary><c>st_ino</c>, read with <see cref="DeviceId"/>.</summary>
    public long Inode { get; set; }

    /// <summary>The size the disk reported right before the move.</summary>
    public long SizeBytes { get; set; }

    /// <summary>The modification time the disk reported right before the move, to the tick.</summary>
    public DateTime ModifiedUtc { get; set; }

    /// <summary>How many names the file had then; <c>null</c> when the platform doesn't say. Above one, deleting it frees
    /// no space: another name keeps the data (ADR 0031).</summary>
    public int? LinkCount { get; set; }

    /// <summary>Where it stands.</summary>
    public ReplacementNameState State { get; set; }

    /// <summary>Why it stayed, wasn't put back or deleted, or was lost — for a companion a torrent holds, the hold's own
    /// sentence —, one readable sentence; <c>null</c> otherwise.</summary>
    [MaxLength(ProblemMaximumLength)]
    public string? Problem { get; set; }

    /// <summary>When it was renamed into the quarantine; <c>null</c> before.</summary>
    public DateTime? MovedUtc { get; set; }

    /// <summary>When it was put back; <c>null</c> before.</summary>
    public DateTime? RestoredUtc { get; set; }

    /// <summary>When it was deleted for good; <c>null</c> before.</summary>
    public DateTime? DeletedUtc { get; set; }
}
