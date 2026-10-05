using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Recommendations;

namespace MediaAcquisitionManager.Api.Mapping;

/// <summary>MAM-33 — the recommendations' rows as their contracts.</summary>
public interface IRecommendationMapper
{
    /// <summary><paramref name="run"/> as the page reads it; its requester and error only
    /// <paramref name="forOwner"/>.</summary>
    RecommendationRunDto ToDto(RecommendationRunEntity run, bool forOwner);

    /// <summary>
    /// The recommended <paramref name="work"/> as a card reads it: filed under <paramref name="genres"/> (names, in
    /// their order), recommended for <paramref name="reasons"/> (in their order), with what the last check of the
    /// release sources found (<paramref name="availability"/>, <c>null</c> before one) and whether a download job is
    /// working on it.
    /// </summary>
    RecommendationDto ToDto(
        CatalogWorkEntity work,
        IReadOnlyList<string> genres,
        IReadOnlyList<RecommendationReasonEntity> reasons,
        ReleaseAvailabilityEntity? availability,
        bool downloading);

    /// <summary>A user's <paramref name="settings"/> as their Profile reads them, with the searches run for them in the
    /// last 24 hours.</summary>
    RecommendationSettingsDto ToDto(RecommendationSettings settings, int searchesLast24Hours);
}
