namespace MediaAcquisitionManager.Core.Abstractions.Files;

/// <summary>
/// MAM-20, ADR 0028 — the type of file system a device holds, as the system's mount table names it
/// (<see cref="IFileInventory.DescribeFileSystemsAsync"/>).
/// </summary>
/// <param name="DeviceId">The device, as <see cref="FileFacts.DeviceId"/> gives it (<c>st_dev</c>).</param>
/// <param name="Type">The file system's type as the mount table writes it (<c>ext4</c>, <c>btrfs</c>, <c>zfs</c>,
/// <c>fuse.mergerfs</c>); <c>null</c> when unknown — no mount table, no line for the device, or lines that
/// disagree.</param>
public sealed record DeviceFileSystem(long DeviceId, string? Type);
