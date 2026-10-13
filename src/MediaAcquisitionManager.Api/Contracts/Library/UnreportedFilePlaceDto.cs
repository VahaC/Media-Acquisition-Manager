using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>MAM-42 — where a file in no library sits among the libraries' folders.</summary>
/// <param name="Kind">Under no library's folder, under one — MAM-43: in an extras folder there, or named as an extra —
/// or the folders aren't known.</param>
/// <param name="LibraryName">The library whose folder holds it ("Фільми"), the first by name when several do;
/// <c>null</c> for <c>NoLibraryFolder</c> and <c>FoldersUnknown</c>.</param>
/// <param name="ExtraName">MAM-43 — what makes it an extra: the folder's name as on the disk ("Shorts") for
/// <c>ExtrasFolder</c>, the rule its name matches ("-trailer", "sample") for <c>ExtraName</c>; <c>null</c>
/// otherwise.</param>
public sealed record UnreportedFilePlaceDto(UnreportedFilePlaceKind Kind, string? LibraryName, string? ExtraName = null);
