using System.ComponentModel.DataAnnotations;

namespace MediaAcquisitionManager.Core.Entities;

/// <summary>
/// The SHA-256 of one set of bytes (MAM-19, ADR 0022): the hash cache of specification §3.3, keyed like
/// <see cref="MediaInspectionEntity"/> by the file's identity, size and modification time, so a file is
/// read in full once and again only when any of the four changes. <see cref="AuditableEntity.CreatedUtc"/>
/// is when it was hashed.
/// </summary>
/// <remarks>
/// <para>
/// Keyed by identity, not by a file name: two names of one inode share one hash, and a name a sync
/// replaces finds it again without a read. No name points at it: a name's hash is the one stored under
/// the key the app's last look read on the name (<see cref="PhysicalFileEntity.DeviceId"/>,
/// <see cref="PhysicalFileEntity.Inode"/>, <see cref="PhysicalFileEntity.ReadSizeBytes"/>,
/// <see cref="PhysicalFileEntity.ReadModifiedUtc"/>), so a changed file's old hash is simply never
/// matched again, and the inspection forgets it.
/// </para>
/// <para>
/// Only a hash that was read whole is stored: a file that changed while it was read, or couldn't be read,
/// leaves nothing here, and the next inspection tries again. Not a <see cref="ProvenancedEntity"/>: every
/// row is the app's own reading (ADR 0006).
/// </para>
/// </remarks>
public class FileHashEntity : AuditableEntity
{
    /// <summary>The length of <see cref="Sha256"/>: 32 bytes in hex.</summary>
    public const int Sha256Length = 64;

    /// <summary><c>st_dev</c> of the file read, stored bit for bit like
    /// <see cref="PhysicalFileEntity.DeviceId"/>.</summary>
    public long DeviceId { get; set; }

    /// <summary><c>st_ino</c> of the file read.</summary>
    public long Inode { get; set; }

    /// <summary>Its size in bytes when it was read.</summary>
    public long SizeBytes { get; set; }

    /// <summary>When it was last written, in UTC, as the file system said when it was read.</summary>
    public DateTime ModifiedUtc { get; set; }

    /// <summary>The SHA-256 of every byte, in lowercase hex — what <c>sha256sum</c> prints.</summary>
    [Required, MaxLength(Sha256Length)]
    public string Sha256 { get; set; } = default!;
}
