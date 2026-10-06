namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>MAM-34 — a library work's year against a TMDb suggestion's.</summary>
/// <param name="LibraryYear">The work's.</param>
/// <param name="CatalogYear">The suggestion's.</param>
/// <param name="Difference">How many years apart; <c>null</c> when either isn't known.</param>
public sealed record YearEvidenceDto(int? LibraryYear, int? CatalogYear, int? Difference);
