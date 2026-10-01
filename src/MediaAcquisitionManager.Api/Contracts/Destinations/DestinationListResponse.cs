namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>One page of the destination list (MAM-11).</summary>
/// <param name="Items">The destinations on this page, ordered by name, then by their download client's
/// name, letter case ignored; each checked live.</param>
/// <param name="TotalCount">Every destination matching the search, across all pages.</param>
/// <param name="Page">The page, counted from 1, as asked for.</param>
/// <param name="PageSize">The page size, as asked for.</param>
public sealed record DestinationListResponse(IReadOnlyList<DestinationDto> Items, int TotalCount, int Page, int PageSize);
