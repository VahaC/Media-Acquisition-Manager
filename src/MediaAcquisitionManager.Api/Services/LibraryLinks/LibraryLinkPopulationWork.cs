using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Api.Services.LibraryLinks;

/// <summary>MAM-34 — one library work the TMDb suggestions are for (<see cref="LibraryLinkPopulation"/>).</summary>
/// <param name="Id">The work.</param>
/// <param name="Kind">A film, a series, or an episode with no series.</param>
/// <param name="Title">The title its card shows; <c>null</c> when it has none.</param>
public sealed record LibraryLinkPopulationWork(Guid Id, MediaWorkKind Kind, string? Title);
