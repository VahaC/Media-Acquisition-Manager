using System.ComponentModel.DataAnnotations;

namespace MediaAcquisitionManager.Core.Entities;

/// <summary>
/// What the media inspector read from one set of bytes (MAM-9, ADR 0009): the cache of specification §3.3,
/// keyed by the file's identity, size and modification time, so it is read once and read again only
/// when any of the four changes. <see cref="AuditableEntity.CreatedUtc"/> is when it was read.
/// </summary>
/// <remarks>
/// <para>
/// Keyed by identity, not by a file name: two names of one inode (hardlinks) share one row, and a name
/// that a library sync replaces (a moved file, a re-created version) finds its facts again without a
/// second read. A file name points at its row (<see cref="PhysicalFileEntity.MediaInspectionId"/>); a
/// row no name points at any more is removed by the next inspection.
/// </para>
/// <para>
/// Not a <see cref="ProvenancedEntity"/>: every row is the app's own reading, so the table itself says
/// who asserted it (<see cref="Enums.ProvenanceSourceKind.FileInspection"/>), and no integration could
/// ever be named on it (ADR 0006).
/// </para>
/// <para>
/// A value the stream didn't give stays <c>null</c>, never a guess: the page shows it as unknown
/// (specification §3.1).
/// </para>
/// </remarks>
public class MediaInspectionEntity : AuditableEntity
{
    /// <summary>The longest <see cref="Error"/> the column holds; a longer sentence is cut to it.</summary>
    public const int ErrorMaximumLength = 1000;

    /// <summary><c>st_dev</c> of the file read, stored bit for bit like
    /// <see cref="PhysicalFileEntity.DeviceId"/>.</summary>
    public long DeviceId { get; set; }

    /// <summary><c>st_ino</c> of the file read.</summary>
    public long Inode { get; set; }

    /// <summary>Its size in bytes when it was read.</summary>
    public long SizeBytes { get; set; }

    /// <summary>When it was last written, in UTC, as the file system said when it was read.</summary>
    public DateTime ModifiedUtc { get; set; }

    /// <summary>
    /// Why the media inspector could not read it, one readable sentence; <c>null</c> when it could. A
    /// failure is cached like a success, so a damaged file is not read again on every inspection.
    /// </summary>
    [MaxLength(ErrorMaximumLength)]
    public string? Error { get; set; }

    /// <summary>How long it plays, in milliseconds; <c>null</c> when the container doesn't say.</summary>
    public long? DurationMilliseconds { get; set; }

    /// <summary>The overall bit rate in bits per second; <c>null</c> when the container doesn't say.</summary>
    public long? BitRate { get; set; }
}
