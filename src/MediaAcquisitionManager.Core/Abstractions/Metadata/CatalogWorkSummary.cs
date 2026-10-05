using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Core.Abstractions.Metadata;

/// <summary>MAM-33 — a film or a show as a catalog lists it among others: in a recommendation, a
/// collection, a person's works, a search or a discovery page.</summary>
/// <param name="Kind">A film (<see cref="MediaWorkKind.Movie"/>) or a show (<see cref="MediaWorkKind.Series"/>).</param>
/// <param name="Id">The source's id of the work, in the numbering of its kind.</param>
/// <param name="Title">The title in the catalog's language; the source's fallback when it has no translation.</param>
/// <param name="OriginalTitle">The title in the work's original language; <c>null</c> when the source gives none.</param>
/// <param name="OriginalLanguage">The original language's ISO 639-1 code; <c>null</c> when the source gives none.</param>
/// <param name="ReleaseDate">A film's first release or a show's first air date; <c>null</c> when the source
/// doesn't know it, as for a work still to come.</param>
/// <param name="GenreIds">The source's ids of its genres (<see cref="CatalogGenre"/>).</param>
/// <param name="Rating">The average rating, 0 to 10; <c>null</c> when nobody rated it.</param>
/// <param name="VoteCount">How many ratings the average is of.</param>
/// <param name="PosterPath">The source's path of its poster; <c>null</c> when it has none.</param>
public sealed record CatalogWorkSummary(
    MediaWorkKind Kind,
    string Id,
    string Title,
    string? OriginalTitle,
    string? OriginalLanguage,
    DateOnly? ReleaseDate,
    IReadOnlyList<int> GenreIds,
    double? Rating,
    int VoteCount,
    string? PosterPath);
