namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>The query string of the destination list: a search and a page, both applied by the server
/// (MAM-11).</summary>
/// <param name="Search">Part of a destination's name, in any letter case; blank lists every one.</param>
/// <param name="Page">Which page, counted from 1.</param>
/// <param name="PageSize">Destinations per page, 1 to 100; 25 unless asked otherwise.</param>
public sealed record DestinationListRequest(string? Search = null, int Page = 1, int PageSize = 25);
