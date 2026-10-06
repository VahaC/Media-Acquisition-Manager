namespace MediaAcquisitionManager.Core.Library;

/// <summary>MAM-34 — a library work's measured duration against a TMDb candidate's runtime, in minutes.</summary>
/// <param name="LibraryMinutes">The work's version closest to the candidate's runtime (the first version's when that
/// runtime isn't known); <c>null</c> when nothing was measured.</param>
/// <param name="CatalogMinutes">The candidate's; <c>null</c> when not known.</param>
/// <param name="Agrees">Whether the two are within <see cref="LinkSuggestionRanking"/>'s tolerance; <c>null</c> when
/// either is not known.</param>
public sealed record RuntimeEvidence(int? LibraryMinutes, int? CatalogMinutes, bool? Agrees);
