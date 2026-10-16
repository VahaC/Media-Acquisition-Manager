using MediaAcquisitionManager.Api.Contracts;

namespace MediaAcquisitionManager.Api.Mcp.Results;

/// <summary>MAM-45 — <c>get_status</c>'s answer: who the caller is, which MAM answers, and how the sources a user can see
/// the state of stand — never the Settings pages' health, which is the owner's in the web app (Vasyl's decision f).</summary>
/// <param name="Caller">Who the tools act as, and the calling token.</param>
/// <param name="Version">MAM's version, as this build was stamped (<c>0.5.0+&lt;revision&gt;</c>).</param>
/// <param name="Library">The media library sources with their latest and last successful syncs, as the Library page
/// lists them (<c>GET /api/library/sources</c>): their errors and path problems for the owner only.</param>
/// <param name="LatestRecommendationRun">The recommendations run under way, else the latest one; <c>null</c> before the
/// first (<c>GET /api/recommendations/runs/latest</c>).</param>
/// <param name="FailedRecommendationSources">The release sources whose last call for the recommendations failed, as the
/// Recommendations page names them: their sentences for the owner only.</param>
public sealed record StatusToolResult(
    McpCallerDto Caller,
    string Version,
    LibrarySourceListResponse Library,
    RecommendationRunDto? LatestRecommendationRun,
    IReadOnlyList<RecommendationSourceFailureDto> FailedRecommendationSources);
