using MediaAcquisitionManager.Core.Abstractions.Media;
using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Core.Abstractions.Metadata;

/// <summary>What to look a work up by.</summary>
/// <param name="Kind">What the work is.</param>
/// <param name="Identifiers">Registry ids known for it.</param>
/// <param name="Title">A title, when looking up by title.</param>
/// <param name="Year">The year that goes with the title.</param>
public sealed record MetadataQuery(MediaWorkKind Kind, IReadOnlyList<MediaIdentifier> Identifiers, string? Title, int? Year);
