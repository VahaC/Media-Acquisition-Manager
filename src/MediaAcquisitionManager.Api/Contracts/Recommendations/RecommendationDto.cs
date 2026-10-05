using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>MAM-33 — one recommended work the library doesn't have yet, or a held series with seasons missing.</summary>
/// <param name="Id">The catalog work's id: the key of a card.</param>
/// <param name="Kind"><c>Movie</c> or <c>Series</c>; with <paramref name="CatalogId"/>, what "Not interested" is kept
/// by.</param>
/// <param name="CatalogId">The catalog's id of the work (<c>603</c>).</param>
/// <param name="Title">The catalog's title, in its language (Ukrainian).</param>
/// <param name="OriginalTitle">The title in the work's original language; <c>null</c> when the catalog gives
/// none.</param>
/// <param name="Year">The year of its release or first air date; <c>null</c> when the catalog has no date.</param>
/// <param name="Genres">The catalog's names of its genres, in alphabetical order.</param>
/// <param name="Rating">The catalog's average rating out of 10; <c>null</c> when nobody rated it.</param>
/// <param name="VoteCount">How many ratings the average is of.</param>
/// <param name="PosterUrl"><c>/api/recommendations/posters/{file}</c>, fetched with the user's token; <c>null</c> when
/// the catalog has no poster.</param>
/// <param name="Reasons">Why it is recommended, strongest first.</param>
/// <param name="Availability">What the last check of the release sources found; <c>null</c> before one.</param>
/// <param name="Downloading">Whether a download job a client is still working on — any user's — is of this
/// work.</param>
/// <param name="SearchText">What the Search page searches for: the release sources' query title
/// (<c>RecommendationQueryTitle</c>), then the year in brackets when known — <c>Alien (1979)</c>.</param>
/// <param name="SearchTmdbId">A film's TMDb id, which the Search page narrows by; <c>null</c> for a show, whose
/// search takes no id.</param>
public sealed record RecommendationDto(
    Guid Id,
    MediaWorkKind Kind,
    string CatalogId,
    string Title,
    string? OriginalTitle,
    int? Year,
    IReadOnlyList<string> Genres,
    double? Rating,
    int VoteCount,
    string? PosterUrl,
    IReadOnlyList<RecommendationReasonDto> Reasons,
    RecommendationAvailabilityDto? Availability,
    bool Downloading,
    string SearchText,
    string? SearchTmdbId);
