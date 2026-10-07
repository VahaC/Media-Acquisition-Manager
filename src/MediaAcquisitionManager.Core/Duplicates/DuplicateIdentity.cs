namespace MediaAcquisitionManager.Core.Duplicates;

/// <summary>MAM-20 — the one file the names of a <see cref="Enums.DuplicateClass.SameFile"/> group lead to.</summary>
/// <param name="DeviceId">Its device, as the app read it.</param>
/// <param name="Inode">Its inode, as the app read it.</param>
/// <param name="LinkCount">How many names the file system says it has; <c>null</c> when unknown.</param>
/// <param name="NameCount">How many of them the app found.</param>
public sealed record DuplicateIdentity(long DeviceId, long Inode, int? LinkCount, int NameCount);
