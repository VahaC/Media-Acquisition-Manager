namespace MediaAcquisitionManager.Core.Abstractions.Downloads;

/// <summary>One file of a download client's job.</summary>
/// <param name="ServicePath">The path as the client sees it.</param>
/// <param name="SizeBytes">Its size; <c>null</c> when unknown.</param>
/// <param name="Progress">From 0 to 1; <c>null</c> when unknown.</param>
/// <param name="IsWanted">Whether the client downloads it; <c>null</c> when it doesn't say.</param>
public sealed record ClientJobFile(string ServicePath, long? SizeBytes, double? Progress, bool? IsWanted);
