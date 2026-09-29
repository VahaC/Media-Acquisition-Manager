namespace MediaAcquisitionManager.Core.Abstractions.Library;

/// <summary>One file of a version, as the library reports it.</summary>
/// <param name="ServicePath">The path as the library sees it; the core maps it onto its own mounts
/// (<see cref="Paths.PathMapper"/>).</param>
/// <param name="SizeBytes">The size the library reports; <c>null</c> when unknown.</param>
public sealed record LibraryFile(string ServicePath, long? SizeBytes);
