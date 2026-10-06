namespace MediaAcquisitionManager.Core.Abstractions.Metadata;

/// <summary>MAM-33 — a film's or a show's details (<see cref="IMetadataCatalog.GetWorkAsync"/>).</summary>
/// <param name="Summary">What a list gives of it too.</param>
/// <param name="ImdbId">Its IMDb id — MAM-34: a show's too; <c>null</c> when the source gives none.</param>
/// <param name="Collection">The collection a film belongs to; <c>null</c> for a show, or a film in none.</param>
/// <param name="Directors">A film's directors, or a show's creators.</param>
/// <param name="Seasons">A show's seasons, the specials (season 0) included; empty for a film.</param>
/// <param name="Recommendations">The works the source recommends beside it, best first, of the same kind.</param>
/// <param name="RuntimeMinutes">MAM-34 — a film's runtime, or a show's episodes', in minutes; <c>null</c> when the
/// source gives none. Appended with a default, like the two after it, so MAM-33's callers compile as they were.</param>
/// <param name="Overview">MAM-34 — what the work is about; <c>null</c> when the source gives none.</param>
/// <param name="AlternativeTitles">MAM-34 — the other titles the source knows it by, in its order, each once;
/// <c>null</c> reads as none.</param>
public sealed record CatalogWork(
    CatalogWorkSummary Summary,
    string? ImdbId,
    CatalogReference? Collection,
    IReadOnlyList<CatalogReference> Directors,
    IReadOnlyList<CatalogSeason> Seasons,
    IReadOnlyList<CatalogWorkSummary> Recommendations,
    int? RuntimeMinutes = null,
    string? Overview = null,
    IReadOnlyList<string>? AlternativeTitles = null);
