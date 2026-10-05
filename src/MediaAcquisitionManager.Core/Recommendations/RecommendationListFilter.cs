using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Core.Recommendations;

/// <summary>MAM-33 — what one request for a page of a recommendation tab asks for, read by
/// <see cref="RecommendationListRules"/>.</summary>
/// <param name="Tab">The tab listed.</param>
/// <param name="Reason">Only the works recommended for this reason; <c>null</c> for every reason.</param>
/// <param name="AvailableOnly">Only the works the last check found at a release source.</param>
/// <param name="GenreId">Only the works the catalog files under this genre id; <c>null</c> for every genre.</param>
/// <param name="Decade">Only the works released in the decade beginning that year (1990); <c>null</c> for every
/// decade.</param>
/// <param name="Kind">Only films or only shows; <c>null</c> for both.</param>
/// <param name="Sort">The order the works are listed in.</param>
/// <param name="Page">The page, counted from 1.</param>
/// <param name="PageSize">The works per page.</param>
public sealed record RecommendationListFilter(
    RecommendationTab Tab,
    RecommendationReasonKind? Reason,
    bool AvailableOnly,
    int? GenreId,
    int? Decade,
    MediaWorkKind? Kind,
    RecommendationSort Sort,
    int Page,
    int PageSize);
