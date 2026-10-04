namespace MediaAcquisitionManager.Core.Abstractions.Search;

/// <summary>One category of a search source.</summary>
/// <param name="Id">The source's id of the category.</param>
/// <param name="Name">Its name as the source gives it.</param>
/// <param name="ParentId">The category it belongs under; <c>null</c> for a top-level one.</param>
/// <param name="HoldsMovies">Whether the source files movies under it (MAM-10). The adapter knows its
/// protocol's numbering; the core searches movies in these and nothing else, so an audio-only release
/// that a text search also finds is left out (ADR 0004).</param>
/// <param name="HoldsTv">Whether the source files TV under it — shows, seasons, episodes, cartoon series
/// (MAM-32); the core searches TV in these.</param>
/// <param name="IsSourceSpecific">Whether it is the source's own section rather than a standard category
/// every source shares (MAM-32): a tracker's «Мультсеріали» beside the standard "TV". Mapped to a
/// destination, the source's own section says more and wins (ADR 0020).</param>
public sealed record ReleaseCategory(
    string Id,
    string Name,
    string? ParentId,
    bool HoldsMovies,
    bool HoldsTv = false,
    bool IsSourceSpecific = false);
