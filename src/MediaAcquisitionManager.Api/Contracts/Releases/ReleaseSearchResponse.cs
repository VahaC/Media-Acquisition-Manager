namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>What every enabled release source answered to one search (MAM-10).</summary>
/// <remarks>
/// One entry per source, so a source that failed says so beside the ones that answered (specification
/// §3.1: "a failure in one source does not hide results from other sources"). Never paged: each source
/// lists at most 100 releases (<c>ReleaseSearchPlanning.MaximumResults</c>). Sorting and filtering across
/// sources are the search page's to add, on the server (MAM-12).
/// </remarks>
/// <param name="Sources">Every enabled release source, ordered by name.</param>
public sealed record ReleaseSearchResponse(IReadOnlyList<ReleaseSourceResultDto> Sources);
