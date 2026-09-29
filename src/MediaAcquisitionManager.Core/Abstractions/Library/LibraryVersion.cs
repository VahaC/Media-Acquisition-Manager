namespace MediaAcquisitionManager.Core.Abstractions.Library;

/// <summary>One version of a library item.</summary>
/// <param name="SourceVersionId">The library's id of the version inside its item; <c>null</c> when the
/// library has no such notion.</param>
/// <param name="EditionLabel">The edition as the library names it; <c>null</c> when none.</param>
/// <param name="Files">Its files: one, or several stacked parts.</param>
public sealed record LibraryVersion(string? SourceVersionId, string? EditionLabel, IReadOnlyList<LibraryFile> Files);
