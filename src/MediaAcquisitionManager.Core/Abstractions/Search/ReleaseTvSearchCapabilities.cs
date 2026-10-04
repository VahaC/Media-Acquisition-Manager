using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Core.Abstractions.Search;

/// <summary>What a source's dedicated TV search takes (MAM-32).</summary>
/// <param name="TakesText">Whether it takes search text.</param>
/// <param name="IdentifierSchemes">The registry ids it takes: a show's (<see cref="MediaIdentifierScheme.Tvdb"/>,
/// <see cref="MediaIdentifierScheme.Imdb"/>, <see cref="MediaIdentifierScheme.TmdbTv"/>).</param>
/// <param name="TakesSeason">Whether it takes a season number.</param>
public sealed record ReleaseTvSearchCapabilities(
    bool TakesText,
    IReadOnlyList<MediaIdentifierScheme> IdentifierSchemes,
    bool TakesSeason);
