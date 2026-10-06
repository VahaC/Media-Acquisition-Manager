using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Api.Services.LibraryLinks;

/// <summary>MAM-34 — a library work whose TMDb film or show is known, and the libraries its files sit in
/// (<see cref="LibraryLinkProfiles"/>).</summary>
/// <param name="WorkId">The library work.</param>
/// <param name="CatalogKind">A film (<see cref="MediaWorkKind.Movie"/>) or a show (<see cref="MediaWorkKind.Series"/>).</param>
/// <param name="CatalogId">TMDb's id of it: the owner's link when the work has one, else its library's id.</param>
/// <param name="LocationIds">The libraries its files — a series' episodes' — sit in.</param>
public sealed record LibraryLinkIdentifiedWork(Guid WorkId, MediaWorkKind CatalogKind, string CatalogId, IReadOnlyList<Guid> LocationIds);
