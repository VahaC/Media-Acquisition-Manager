namespace MediaAcquisitionManager.Core.Library;

/// <summary>MAM-34 — a library work's year against a TMDb candidate's.</summary>
/// <param name="LibraryYear">The work's; <c>null</c> when not known.</param>
/// <param name="CatalogYear">The candidate's; <c>null</c> when not known.</param>
/// <param name="Difference">How many years apart they are; <c>null</c> when either is not known.</param>
public sealed record YearEvidence(int? LibraryYear, int? CatalogYear, int? Difference);
