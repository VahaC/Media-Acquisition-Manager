using System.ComponentModel.DataAnnotations;

namespace MediaAcquisitionManager.Core.Entities;

/// <summary>
/// MAM-24 — one old copy the owner ticked for a <see cref="ReplacementJobEntity"/>, as it was when scheduled (ADR 0032):
/// which file of which version and work, where it is, and its identity read fresh from the disk at that moment.
/// </summary>
/// <remarks>
/// <para>
/// A snapshot, not keys: a sync replaces file rows, versions and works whenever the library reports an item anew, and a
/// key into them would rebuild those tables (ADR 0009) or lose the target — the
/// <see cref="LibraryItemLinkFileEntity"/> precedent. The ids say which rows they were, to be found again by path and
/// identity; nothing joins on them.
/// </para>
/// <para>
/// No <c>CreatedUtc</c>: a file row is part of its job, written with it and never alone.
/// </para>
/// </remarks>
public class ReplacementJobFileEntity : BaseEntity
{
    /// <summary>The job; the row is removed with it.</summary>
    public Guid ReplacementJobId { get; set; }

    /// <summary>The 0-based position among the job's files, in the dialog's order.</summary>
    public int Position { get; set; }

    /// <summary>The file row it was picked as.</summary>
    public Guid PhysicalFileId { get; set; }

    /// <summary>The version holding it.</summary>
    public Guid MediaVersionId { get; set; }

    /// <summary>The work it is a copy of: the film, or the episode.</summary>
    public Guid MediaWorkId { get; set; }

    /// <summary>The work the release matched: the film, or the episode's series.</summary>
    public Guid MatchedWorkId { get; set; }

    /// <summary>An episode's season, as its own library item places it inside the release's claim; <c>null</c> for a
    /// film.</summary>
    public int? SeasonNumber { get; set; }

    /// <summary>An episode's number there; <c>null</c> for a film, and for an episode the library numbers none.</summary>
    public int? EpisodeNumber { get; set; }

    /// <summary>The path as its library reported it.</summary>
    [Required, MaxLength(PhysicalFileEntity.ServicePathMaximumLength)]
    public string ServicePath { get; set; } = default!;

    /// <summary>The path inside the app, canonical, through the library's mappings when it was scheduled.</summary>
    [Required, MaxLength(PhysicalFileEntity.ServicePathMaximumLength)]
    public string AppPath { get; set; } = default!;

    /// <summary><c>st_dev</c>, read from the disk when it was scheduled, stored bit for bit like
    /// <see cref="PhysicalFileEntity.DeviceId"/>.</summary>
    public long DeviceId { get; set; }

    /// <summary><c>st_ino</c>, read with <see cref="DeviceId"/>.</summary>
    public long Inode { get; set; }

    /// <summary>The size the disk reported then.</summary>
    public long SizeBytes { get; set; }

    /// <summary>The modification time the disk reported then, to the tick.</summary>
    public DateTime ModifiedUtc { get; set; }

    /// <summary>How many names the file had then; <c>null</c> when the platform doesn't say.</summary>
    public int? LinkCount { get; set; }

    /// <summary>The version's own edition label then; <c>null</c> when the library gave none.</summary>
    [MaxLength(MediaVersionEntity.EditionLabelMaximumLength)]
    public string? EditionLabel { get; set; }
}
