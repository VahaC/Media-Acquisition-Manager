namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>One page of the integration list.</summary>
/// <param name="Items">The integrations on this page, ordered by name, letter case ignored.</param>
/// <param name="TotalCount">Every integration matching the search and kind, across all pages.</param>
/// <param name="Page">The page, counted from 1, as asked for.</param>
/// <param name="PageSize">The page size, as asked for.</param>
public sealed record IntegrationListResponse(IReadOnlyList<IntegrationDto> Items, int TotalCount, int Page, int PageSize);
