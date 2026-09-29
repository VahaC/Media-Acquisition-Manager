using MediaAcquisitionManager.Core.Abstractions.Media;

namespace MediaAcquisitionManager.Core.Abstractions.Search;

/// <summary>What to search a source for.</summary>
/// <param name="Text">The search text.</param>
/// <param name="Year">The year, when the search should take one.</param>
/// <param name="Identifiers">Registry ids to search by, where the source takes them.</param>
/// <param name="CategoryIds">The source's categories to search in; empty for all.</param>
public sealed record ReleaseSearchQuery(
    string Text,
    int? Year,
    IReadOnlyList<MediaIdentifier> Identifiers,
    IReadOnlyList<string> CategoryIds);
