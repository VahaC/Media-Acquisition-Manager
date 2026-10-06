using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Core.Library;

/// <summary>MAM-34 — a film or show the catalog found for a library work, as the catalog describes it.</summary>
/// <param name="Kind">A film (<see cref="MediaWorkKind.Movie"/>) or a show (<see cref="MediaWorkKind.Series"/>).</param>
/// <param name="Id">The catalog's id of it.</param>
/// <param name="Title">Its title in the catalog's language.</param>
/// <param name="OriginalTitle">Its title in its original language; <c>null</c> when not known.</param>
/// <param name="AlternativeTitles">The other titles the catalog knows it by.</param>
/// <param name="Year">Its year; <c>null</c> when not known.</param>
/// <param name="RuntimeMinutes">Its runtime (a show's episodes'); <c>null</c> when not known.</param>
/// <param name="GenreRoles">What its genres make it.</param>
/// <param name="CollectionId">A film's collection; <c>null</c> for none.</param>
/// <param name="DirectorIds">A film's directors, or a show's creators.</param>
/// <param name="Position">Where the catalog listed it among the work's search results, from 0.</param>
public sealed record LinkCandidateFacts(
    MediaWorkKind Kind,
    string Id,
    string Title,
    string? OriginalTitle,
    IReadOnlyList<string> AlternativeTitles,
    int? Year,
    int? RuntimeMinutes,
    IReadOnlyList<CatalogGenreRole> GenreRoles,
    string? CollectionId,
    IReadOnlyList<string> DirectorIds,
    int Position);
