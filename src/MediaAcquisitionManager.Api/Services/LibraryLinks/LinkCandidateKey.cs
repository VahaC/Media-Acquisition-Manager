using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Api.Services.LibraryLinks;

/// <summary>MAM-34 — a TMDb film or show a library work could be linked to, named the way a stored candidate names it:
/// by kind, as TMDb numbers films and shows apart (ADR 0017), and id.</summary>
/// <param name="Kind">A film (<see cref="MediaWorkKind.Movie"/>) or a show (<see cref="MediaWorkKind.Series"/>).</param>
/// <param name="CatalogId">TMDb's id of it.</param>
public sealed record LinkCandidateKey(MediaWorkKind Kind, string CatalogId);
