using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Core.Abstractions.Search;

/// <summary>What one search source can search by.</summary>
/// <param name="HasTextSearch">Whether it has a general search by text (MAM-10).</param>
/// <param name="HasMovieSearch">Whether it has a dedicated movie search.</param>
/// <param name="MovieSearchTakesText">Whether the movie search takes search text (MAM-10).</param>
/// <param name="MovieSearchIdentifierSchemes">The registry ids the movie search takes.</param>
/// <param name="MovieSearchTakesYear">Whether the movie search takes a year.</param>
/// <param name="ResultLimit">The most releases one search may ask for; <c>null</c> when the source
/// doesn't say (MAM-10).</param>
/// <param name="Categories">The source's categories.</param>
/// <param name="TvSearch">Its dedicated TV search (MAM-32); <c>null</c> when it has none.</param>
public sealed record ReleaseSearchCapabilities(
    bool HasTextSearch,
    bool HasMovieSearch,
    bool MovieSearchTakesText,
    IReadOnlyList<MediaIdentifierScheme> MovieSearchIdentifierSchemes,
    bool MovieSearchTakesYear,
    int? ResultLimit,
    IReadOnlyList<ReleaseCategory> Categories,
    ReleaseTvSearchCapabilities? TvSearch = null);
