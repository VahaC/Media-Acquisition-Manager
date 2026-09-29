using System.ComponentModel.DataAnnotations;

namespace MediaAcquisitionManager.Core.Entities;

/// <summary>
/// One name of a file of a version, as a source reports it — a name, not one copy of the bytes.
/// </summary>
/// <remarks>
/// Two names of one inode (hardlinks) are two rows sharing <see cref="DeviceId"/> and
/// <see cref="Inode"/>: one set of bytes, counted once. A byte-identical copy is a different identity
/// (Phase B hashes it). Occupied space is counted per identity, never per row.
/// </remarks>
public class PhysicalFileEntity : ProvenancedEntity
{
    /// <summary>
    /// The version; forgetting a version forgets the names reported for it — never a file on disk.
    /// </summary>
    public Guid MediaVersionId { get; set; }

    /// <summary>
    /// The path exactly as the reporting service sees it; 4096 is Linux's <c>PATH_MAX</c>.
    /// </summary>
    /// <remarks>
    /// The app's own path is never stored: <see cref="Paths.PathMapper"/> works it out through the
    /// integration's current mappings when it is needed, so an edited mapping can't leave stale paths
    /// behind — and a mapped path is only where to look, not proof of identity (ADR 0003).
    /// </remarks>
    [Required, MaxLength(4096)]
    public string ServicePath { get; set; } = default!;

    /// <summary>The size in bytes as reported; <c>null</c> when unknown.</summary>
    public long? SizeBytes { get; set; }

    /// <summary>
    /// <c>st_dev</c>, once the app has read it itself (MAM-9, Phase B); <c>null</c> until then and
    /// never inferred. An unsigned value is stored bit for bit (<c>unchecked</c>): only equality
    /// matters.
    /// </summary>
    public long? DeviceId { get; set; }

    /// <summary><c>st_ino</c>, read and stored like <see cref="DeviceId"/>.</summary>
    public long? Inode { get; set; }
}
