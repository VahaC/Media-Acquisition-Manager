namespace MediaAcquisitionManager.Core.Abstractions.Library;

/// <summary>What a type of media library can report — the specification's optional version
/// capabilities.</summary>
/// <param name="GroupsVersionsUnderOneItem">Whether one library item can hold several versions of a
/// work (then <see cref="LibraryVersion.SourceVersionId"/> tells them apart).</param>
public sealed record MediaLibraryCapabilities(bool GroupsVersionsUnderOneItem);
