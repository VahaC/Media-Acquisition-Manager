namespace MediaAcquisitionManager.Core.Abstractions.Files;

/// <summary>What the file system says about one file.</summary>
/// <param name="SizeBytes">Its size.</param>
/// <param name="ModifiedUtc">When it was last written, in UTC.</param>
/// <param name="DeviceId"><c>st_dev</c>; <c>null</c> where the platform gives none.</param>
/// <param name="Inode"><c>st_ino</c>; <c>null</c> where the platform gives none.</param>
/// <param name="LinkCount">How many names the file has; <c>null</c> where the platform gives none.</param>
public sealed record FileFacts(long SizeBytes, DateTime ModifiedUtc, long? DeviceId, long? Inode, int? LinkCount);
