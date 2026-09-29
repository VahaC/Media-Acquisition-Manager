using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Core.Abstractions.Metadata;

/// <summary>What a metadata resolver can look up by.</summary>
/// <param name="IdentifierSchemes">The registry ids it takes.</param>
/// <param name="SearchesByTitle">Whether it searches by title.</param>
public sealed record MetadataResolverCapabilities(IReadOnlyList<MediaIdentifierScheme> IdentifierSchemes, bool SearchesByTitle);
