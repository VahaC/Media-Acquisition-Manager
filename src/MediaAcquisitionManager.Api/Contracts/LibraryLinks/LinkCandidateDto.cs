using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>MAM-34 — a TMDb film or show suggested for a library work, with what speaks for and against it.</summary>
/// <param name="CatalogKind">A film (<c>Movie</c>) or a show (<c>Series</c>).</param>
/// <param name="CatalogId">TMDb's id of it.</param>
/// <param name="Title">Its title in Ukrainian, or TMDb's fallback.</param>
/// <param name="OriginalTitle">Its title in its original language.</param>
/// <param name="Year">Its year.</param>
/// <param name="Overview">What it is about.</param>
/// <param name="RuntimeMinutes">Its runtime (a show's episodes').</param>
/// <param name="PosterUrl">Where the app serves its poster (<c>/api/recommendations/posters/{file}</c>); <c>null</c>
/// when it has none.</param>
/// <param name="ImdbId">The IMDb id TMDb gives for it.</param>
/// <param name="IsConfident">Whether it is the work's one confident suggestion (decision 8).</param>
/// <param name="Evidence">The evidence, each piece always present but the neighbour.</param>
/// <param name="SeasonNumber">For a show suggested to a film or an episode with no series: the season to prefill, from
/// an episode marker of the work's names, or 1 when the show has a single season; <c>null</c> otherwise.</param>
/// <param name="EpisodeNumber">The episode to prefill, from the marker; <c>null</c> otherwise.</param>
public sealed record LinkCandidateDto(
    MediaWorkKind CatalogKind,
    string CatalogId,
    string Title,
    string? OriginalTitle,
    int? Year,
    string? Overview,
    int? RuntimeMinutes,
    string? PosterUrl,
    string? ImdbId,
    bool IsConfident,
    LinkEvidenceDto Evidence,
    int? SeasonNumber,
    int? EpisodeNumber);
