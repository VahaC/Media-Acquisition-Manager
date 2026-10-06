using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Api.Services.LibraryLinks;

/// <summary>MAM-34 — a cached catalog film or show with what a TMDb suggestion's evidence reads of it
/// (<see cref="LibraryLinkCatalogRows"/>).</summary>
/// <param name="Work">The catalog's row: titles, year, runtime, collection, overview, poster, IMDb id.</param>
/// <param name="GenreRoles">What its genres make it, each role once.</param>
/// <param name="AlternativeTitles">The other titles the catalog knows it by, in the catalog's order.</param>
/// <param name="DirectorIds">A film's directors or a show's creators, in the catalog's order.</param>
/// <param name="RegularSeasons">How many seasons a show has, its specials (season 0) not counted.</param>
public sealed record LibraryLinkCatalogRow(
    CatalogWorkEntity Work,
    IReadOnlyList<CatalogGenreRole> GenreRoles,
    IReadOnlyList<string> AlternativeTitles,
    IReadOnlyList<string> DirectorIds,
    int RegularSeasons);
