namespace MediaAcquisitionManager.Core.Entities;

/// <summary>
/// MAM-34 — the identity of one file of a linked library item (decision 10): what lets the link find its item again
/// when the library re-creates it under a new item id.
/// </summary>
/// <remarks>
/// <para>
/// A copy, not a key into <see cref="PhysicalFileEntity"/>: the sync replaces file rows whenever the library reports an
/// item anew — the new rows carry no identity until the inspection reads them — and the rows that held it are gone by
/// then (ADR 0009). So the link keeps its own, captured when it is set and refreshed by every sync and inspection.
/// </para>
/// <para>
/// All three must agree (R2): an inode number is reused once its file is deleted, and the size makes a new file that
/// happens to get it no match. A series item has no files; its link keeps its episodes' (R3).
/// </para>
/// </remarks>
public class LibraryItemLinkFileEntity : BaseEntity
{
    /// <summary>The link; its identities are removed with it.</summary>
    public Guid LibraryItemLinkId { get; set; }

    /// <summary><c>st_dev</c>, stored bit for bit like <see cref="PhysicalFileEntity.DeviceId"/>.</summary>
    public long DeviceId { get; set; }

    /// <summary><c>st_ino</c>, stored like <see cref="DeviceId"/>.</summary>
    public long Inode { get; set; }

    /// <summary>The size the disk reported (<see cref="PhysicalFileEntity.ReadSizeBytes"/>).</summary>
    public long SizeBytes { get; set; }
}
