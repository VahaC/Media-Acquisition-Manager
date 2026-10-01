using System.ComponentModel.DataAnnotations;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Library;

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
    /// <summary>Linux's <c>PATH_MAX</c>: the longest <see cref="ServicePath"/> and
    /// <see cref="NormalizedServicePath"/> hold; an adapter never cuts a path, it takes a longer one for
    /// none (MAM-8).</summary>
    public const int ServicePathMaximumLength = 4096;

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
    [Required, MaxLength(ServicePathMaximumLength)]
    public string ServicePath { get; set; } = default!;

    /// <summary>
    /// <see cref="LibrarySearchText.Normalize"/> of <see cref="ServicePath"/>, so the library search
    /// finds a folder name in any letter case, Cyrillic included (MAM-8).
    /// </summary>
    [Required, MaxLength(ServicePathMaximumLength)]
    public string NormalizedServicePath { get; set; } = default!;

    /// <summary>
    /// The library location of the reporting source that holds the file
    /// (<see cref="LibraryLocationMatcher"/>, MAM-8); <c>null</c> when none or several cover it, or the
    /// source isn't a library. Decided when the source reports the file; a location that disappears
    /// leaves the file known, just without a library.
    /// </summary>
    public Guid? LibraryLocationId { get; set; }

    /// <summary>The size in bytes as reported; <c>null</c> when unknown.</summary>
    public long? SizeBytes { get; set; }

    /// <summary>
    /// <c>st_dev</c>, once the app has read it itself (MAM-9, Phase B); <c>null</c> until then and
    /// never inferred. An unsigned value is stored bit for bit (<c>unchecked</c>): only equality
    /// matters.
    /// </summary>
    /// <remarks>
    /// MAM-9 — the identity of the file the name leads to, links inside the allowed roots followed;
    /// set back to <c>null</c> when the last look found nothing there.
    /// </remarks>
    public long? DeviceId { get; set; }

    /// <summary><c>st_ino</c>, read and stored like <see cref="DeviceId"/>.</summary>
    public long? Inode { get; set; }

    /// <summary>
    /// How many names the file has (<c>st_nlink</c>), read with <see cref="DeviceId"/> (MAM-9); recorded
    /// for Phase B, which counts a file with several names once. <c>null</c> until read.
    /// </summary>
    public int? LinkCount { get; set; }

    /// <summary>
    /// What the app's last look at this name found (MAM-9); <c>null</c> until it has looked. A library
    /// sync never touches it, nor anything below.
    /// </summary>
    public FileInspectionStatus? InspectionStatus { get; set; }

    /// <summary>The sentence saying why the last look didn't read the file's streams; <c>null</c> when
    /// it did, or hasn't looked yet.</summary>
    [MaxLength(MediaInspectionEntity.ErrorMaximumLength)]
    public string? InspectionError { get; set; }

    /// <summary>When the app last looked at this name; <c>null</c> until it has.</summary>
    public DateTime? InspectedUtc { get; set; }

    /// <summary>
    /// The last reading of the bytes the name led to (MAM-9); <c>null</c> until the media inspector, or
    /// the cache, gave one. Kept when a later look finds nothing there or can't read the file, so a
    /// mount that was away costs no reading; its facts count only while <see cref="InspectionStatus"/>
    /// is <see cref="FileInspectionStatus.Inspected"/>. No foreign key: see <c>ApplicationDbContext</c>.
    /// </summary>
    public Guid? MediaInspectionId { get; set; }
}
