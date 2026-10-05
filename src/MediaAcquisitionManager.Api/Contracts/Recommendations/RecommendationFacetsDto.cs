namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>
/// MAM-33 — what a tab's filters offer, each value with how many works have it. Counted over the tab as the caller
/// sees it — without their "Not interested" works and the works a download job ended, a fallback only once found — and
/// before the request's own filters, so an option doesn't vanish once it is picked. A value no work has isn't offered.
/// </summary>
/// <param name="Reasons">The reason kinds, in the cards' order: strongest first.</param>
/// <param name="Genres">The catalog genres, in alphabetical order.</param>
/// <param name="Decades">The decades, earliest first; a work without a date is in none.</param>
/// <param name="Kinds">Films, then shows.</param>
public sealed record RecommendationFacetsDto(
    IReadOnlyList<RecommendationReasonFacetDto> Reasons,
    IReadOnlyList<RecommendationGenreFacetDto> Genres,
    IReadOnlyList<RecommendationDecadeFacetDto> Decades,
    IReadOnlyList<RecommendationKindFacetDto> Kinds);
