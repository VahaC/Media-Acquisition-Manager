using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>MAM-34 — a TMDb suggestion's genres against what the library its work sits in mostly holds.</summary>
/// <param name="LibraryRole">The library's genre; <c>null</c> when it has none.</param>
/// <param name="Agreement">Whether the suggestion has it.</param>
public sealed record GenreEvidenceDto(CatalogGenreRole? LibraryRole, LinkGenreAgreement Agreement);
