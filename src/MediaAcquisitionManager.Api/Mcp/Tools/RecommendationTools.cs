using System.ComponentModel;
using System.Globalization;
using System.Security.Claims;
using MediaAcquisitionManager.Api.Auth;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Api.Mcp.Results;
using MediaAcquisitionManager.Api.Services.Recommendations;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Recommendations;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MediaAcquisitionManager.Api.Mcp.Tools;

/// <summary>MAM-45 — <c>list_recommendations</c> and <c>dismiss_recommendation</c>: the web app's Recommendations page
/// and its Not interested. The mark's undo stays the web app's, as for a token on the HTTP API (ADR 0040).</summary>
/// <remarks>
/// The services as <c>GET /api/recommendations</c> and <c>PUT /api/recommendations/dismissals/{kind}/{catalogId}</c> call
/// them, with the caller's own tabs and marks. The typed arguments go to the list as the query string's text, so every
/// value — a page of 0, a genre of -18 — is checked by the same rules and refused with the same sentence; a tab the caller
/// switched off is the service's sentence too. Built per call by the SDK, with the request's services.
/// </remarks>
[McpServerToolType]
public sealed class RecommendationTools(
    IRecommendationListService lists, IRecommendationDismissalService dismissals, McpToolResults results)
{
    [McpServerTool(
        Name = "list_recommendations",
        Title = "List recommendations",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(RecommendationListResponse))]
    [Description(
        "Lists what MAM recommends to you on one tab of the web app's Recommendations page, best first: each work with "
        + "why it is recommended, its rating, and whether the release sources carry it and how well. Works you marked Not "
        + "interested are left out. To find one, pass its searchText (and, for a film, its searchTmdbId as tmdbId) to "
        + "search_releases. A tab switched off in your profile answers an error saying so.")]
    public async Task<CallToolResult> ListRecommendationsAsync(
        ClaimsPrincipal user,
        [Description("The tab: Movies, Series, Cartoons or Documentaries.")]
        RecommendationTab tab,
        [Description("Keep only the works recommended for this reason: Collection (a missing part of a collection the "
            + "library holds), Director or Creator (of works the library holds), Recommended (by the catalog beside works "
            + "the library holds), MissingSeasons (seasons of a held series it has no episode of), TopRated or NewAtSource "
            + "(just published by a release source).")]
        RecommendationReasonKind? reason = null,
        [Description("true keeps only the works a release source carries.")]
        bool availableOnly = false,
        [Description("Keep only one genre: its catalog id, as the answer's facets list them, such as 18.")]
        int? genreId = null,
        [Description("Keep only one decade, by its first year, such as 1990.")]
        int? decade = null,
        [Description("Movie or Series, on a tab that holds both.")]
        string? kind = null,
        [Description("The order: Score (the default; the best recommended first), Rating, Votes, Newest, Oldest, Title, "
            + "or Releases (what the sources carry most first).")]
        RecommendationSort? sort = null,
        [Description("The page, from 1.")]
        int page = 1,
        [Description("Works per page, 1 to 100.")]
        int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var request = new RecommendationListRequest(
            tab.ToString(),
            reason?.ToString(),
            // The page's unticked box sends nothing: false filters nothing, as a blank value does.
            availableOnly ? bool.TrueString : null,
            genreId?.ToString(CultureInfo.InvariantCulture),
            decade?.ToString(CultureInfo.InvariantCulture),
            kind,
            sort?.ToString(),
            page.ToString(CultureInfo.InvariantCulture),
            pageSize.ToString(CultureInfo.InvariantCulture));
        var result = await lists.ListAsync(request, user.GetUserId(), cancellationToken);
        if (result.Status != RecommendationListStatus.Listed)
            return McpToolResults.Error(result.Error!);

        var list = result.List!;
        var works = list.TotalCount == 1 ? "1 recommendation" : $"{list.TotalCount} recommendations";
        return results.Success($"{works} on the {tab} tab in all; page {list.Page} lists {list.Items.Count}.", list);
    }

    [McpServerTool(
        Name = "dismiss_recommendation",
        Title = "Mark Not interested",
        ReadOnly = false,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(RecommendationDismissalToolResult))]
    [PersonalAccessTokenWrite]
    [Description(
        "Marks a recommended work Not interested, so it no longer appears in your recommendations, on any tab. Do it only "
        + "when the user says so. Marking it again changes nothing; undoing the mark is done in the web app. Needs an "
        + "access token with Read and download access.")]
    public async Task<CallToolResult> DismissRecommendationAsync(
        ClaimsPrincipal user,
        [Description("The work's kind as list_recommendations gives it: Movie or Series.")]
        string kind,
        [Description("The work's catalogId as list_recommendations gives it: its TMDb id, such as 603.")]
        string catalogId,
        CancellationToken cancellationToken = default)
    {
        var result = await dismissals.DismissAsync(kind, catalogId, user.GetUserId(), cancellationToken);
        if (result.Status != RecommendationDismissalStatus.Done)
            return McpToolResults.Error(result.Error!);

        // The mark as it is kept — "series" read as Series, the id trimmed —, which the service doesn't hand back: its
        // own rules, run again on values they have just accepted.
        RecommendationDismissalRules.Validate(kind, catalogId, out var markedKind, out var markedCatalogId);
        var marked = new RecommendationDismissalToolResult(markedKind!.Value.ToString(), markedCatalogId!);
        return results.Success(
            $"{marked.Kind} {marked.CatalogId} is marked Not interested and leaves your recommendations; the web app can undo it.",
            marked);
    }
}
