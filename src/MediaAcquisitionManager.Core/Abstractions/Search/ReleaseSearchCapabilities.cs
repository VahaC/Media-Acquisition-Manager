using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Core.Abstractions.Search;

/// <summary>What one search source can search by.</summary>
/// <param name="HasMovieSearch">Whether it has a dedicated movie search.</param>
/// <param name="MovieSearchIdentifierSchemes">The registry ids the movie search takes.</param>
/// <param name="MovieSearchTakesYear">Whether the movie search takes a year.</param>
/// <param name="Categories">The source's categories.</param>
public sealed record ReleaseSearchCapabilities(
    bool HasMovieSearch,
    IReadOnlyList<MediaIdentifierScheme> MovieSearchIdentifierSchemes,
    bool MovieSearchTakesYear,
    IReadOnlyList<ReleaseCategory> Categories);
