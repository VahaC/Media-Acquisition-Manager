using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>MAM-42 — where a file in no library sits among the libraries' folders.</summary>
/// <param name="Kind">Under no library's folder, under one, or the folders aren't known.</param>
/// <param name="LibraryName">The library whose folder holds it ("Фільми"), the first by name when several do;
/// <c>null</c> unless <paramref name="Kind"/> is <c>LibraryFolder</c>.</param>
public sealed record UnreportedFilePlaceDto(UnreportedFilePlaceKind Kind, string? LibraryName);
