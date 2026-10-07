namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>MAM-20 — the one file the names of a group lead to, as the app read it (ADR 0006).</summary>
/// <param name="DeviceId">Its device.</param>
/// <param name="Inode">Its inode.</param>
/// <param name="LinkCount">How many names the file system says it has; <c>null</c> when unknown.</param>
/// <param name="NameCount">How many of them the app found.</param>
public sealed record DuplicateIdentityDto(long DeviceId, long Inode, int? LinkCount, int NameCount);
