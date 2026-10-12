using System.ComponentModel.DataAnnotations;
using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Core.Entities;

/// <summary>
/// MAM-40 — what every name the app moves into a quarantine keeps (ADR 0030): a video or one of its own companions, where
/// it was, where it goes, the identity read right before the move, and what became of it. A replacement's names
/// (<see cref="ReplacementJobNameEntity"/>, MAM-26) and a removal's (<see cref="CopyRemovalNameEntity"/>) share these
/// columns, so one recovery, one write loop and one set of conditional updates serve both.
/// </summary>
/// <remarks>
/// Not mapped by EF Core as an entity of its own — no <c>DbSet</c>, no configuration —: each subclass is its own table
/// with these columns, as <see cref="BaseEntity"/>'s <c>Id</c> is every table's, and extracting it left the
/// <c>ReplacementJobNames</c> table as it was.
/// </remarks>
public abstract class QuarantinedNameEntity : BaseEntity
{
    /// <summary>The longest <see cref="Problem"/> kept, as a replacement's own reason; a longer sentence is cut to it.</summary>
    public const int ProblemMaximumLength = ReplacementJobEntity.ReasonMaximumLength;

    /// <summary>The 0-based position among its owner's names, in the order they move: each video, then its companions. A
    /// later run's names come after the earlier ones'.</summary>
    public int Position { get; set; }

    /// <summary>A video or a companion.</summary>
    public ReplacementNameKind Kind { get; set; }

    /// <summary>Where the name was, inside the app, canonical: where Restore puts it back.</summary>
    [Required, MaxLength(PhysicalFileEntity.ServicePathMaximumLength)]
    public string AppPath { get; set; } = default!;

    /// <summary>Where it goes in the quarantine, canonical: <c>&lt;root&gt;/.mam-quarantine/&lt;owner's id&gt;/…</c>
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
