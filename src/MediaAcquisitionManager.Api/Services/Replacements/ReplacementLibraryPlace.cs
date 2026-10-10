namespace MediaAcquisitionManager.Api.Services.Replacements;

/// <summary>MAM-25 — a media library whose folder holds a file the check of a replacement reads, and the file's path in
/// it (<see cref="ReplacementLibraryPlaces"/>, ADR 0035).</summary>
/// <param name="IntegrationId">The library integration to ask.</param>
/// <param name="LibraryName">The library's name ("Фільми").</param>
/// <param name="LibraryPath">The file's path in the library's own terms: what it is asked to look at and read by.</param>
public sealed record ReplacementLibraryPlace(Guid IntegrationId, string LibraryName, string LibraryPath);
