namespace MediaAcquisitionManager.Core.Abstractions.Metadata;

/// <summary>MAM-33 — a film's or a show's details (<see cref="IMetadataCatalog.GetWorkAsync"/>).</summary>
/// <param name="Summary">What a list gives of it too.</param>
/// <param name="ImdbId">Its IMDb id; <c>null</c> when the source gives none.</param>
/// <param name="Collection">The collection a film belongs to; <c>null</c> for a show, or a film in none.</param>
/// <param name="Directors">A film's directors, or a show's creators.</param>
/// <param name="Seasons">A show's seasons, the specials (season 0) included; empty for a film.</param>
/// <param name="Recommendations">The works the source recommends beside it, best first, of the same kind.</param>
public sealed record CatalogWork(
    CatalogWorkSummary Summary,
    string? ImdbId,
    CatalogReference? Collection,
    IReadOnlyList<CatalogReference> Directors,
    IReadOnlyList<CatalogSeason> Seasons,
    IReadOnlyList<CatalogWorkSummary> Recommendations);
