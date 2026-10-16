using MediaAcquisitionManager.Api.Contracts;

namespace MediaAcquisitionManager.Api.Mcp.Results;

/// <summary>MAM-45 — <c>search_releases</c>' answer: the search the Search page would make, and the first page of its
/// releases, in one call.</summary>
/// <param name="Search">The stored search: its id (the <c>searchId</c> <c>list_search_releases</c> takes), how each
/// release source answered, and the library's works it matched.</param>
/// <param name="Releases">The first page of its releases, filtered and ordered as asked; each one's id is the
/// <c>releaseId</c> <c>get_download_options</c> and <c>send_download</c> take.</param>
public sealed record ReleaseSearchToolResult(SearchResponse Search, SearchReleaseListResponse Releases);
