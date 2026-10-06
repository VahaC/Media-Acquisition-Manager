namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>MAM-34 — a library work's measured duration against a TMDb suggestion's runtime, in minutes.</summary>
/// <param name="LibraryMinutes">The work's version nearest the suggestion's runtime; <c>null</c> when nothing was
/// measured.</param>
/// <param name="CatalogMinutes">The suggestion's.</param>
/// <param name="Agrees">Whether they agree; <c>null</c> when either isn't known.</param>
public sealed record RuntimeEvidenceDto(int? LibraryMinutes, int? CatalogMinutes, bool? Agrees);
