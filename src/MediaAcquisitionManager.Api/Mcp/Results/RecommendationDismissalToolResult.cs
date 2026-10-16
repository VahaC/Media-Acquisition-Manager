namespace MediaAcquisitionManager.Api.Mcp.Results;

/// <summary>MAM-45 — <c>dismiss_recommendation</c>'s answer: the work now marked Not interested, as the mark names it.
/// The HTTP API answers 204 with no body; a tool says what it did.</summary>
/// <param name="Kind"><c>Movie</c> or <c>Series</c>.</param>
/// <param name="CatalogId">The work's TMDb id.</param>
public sealed record RecommendationDismissalToolResult(string Kind, string CatalogId);
