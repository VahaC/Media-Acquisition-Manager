using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Api.Services.LibraryLinks;

/// <summary>
/// MAM-34 — one work of an "Accept all confident" click, as <see cref="ILibraryLinkService.LinkBatchAsync"/> takes it:
/// the work and the film or show to link it to, or why the suggestions' side already turned it down.
/// </summary>
/// <param name="WorkId">The library work.</param>
/// <param name="CatalogKind">A film (<see cref="MediaWorkKind.Movie"/>) or a show (<see cref="MediaWorkKind.Series"/>).</param>
/// <param name="CatalogId">TMDb's id of it.</param>
/// <param name="SkipReason">The sentence saying why the work is skipped unlinked (the candidate is no longer the work's
/// confident one, the owner marked that none fits…); <c>null</c> to link it.</param>
public sealed record LibraryLinkBatchPick(Guid WorkId, MediaWorkKind CatalogKind, string CatalogId, string? SkipReason = null);
