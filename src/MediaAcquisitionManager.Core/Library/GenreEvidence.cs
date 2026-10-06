using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Core.Library;

/// <summary>MAM-34 — a TMDb candidate's genres against what the library its work sits in mostly holds.</summary>
/// <param name="LibraryRole">The library's genre; <c>null</c> when it has none.</param>
/// <param name="Agreement">Whether the candidate has it.</param>
public sealed record GenreEvidence(CatalogGenreRole? LibraryRole, LinkGenreAgreement Agreement);
