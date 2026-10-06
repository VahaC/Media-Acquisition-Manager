using MediaAcquisitionManager.Core.Recommendations;

namespace MediaAcquisitionManager.Api.Mapping;

/// <summary>
/// MAM-33, MAM-34 — where the app serves a catalog work's poster: <c>RecommendationsController</c>'s posters route, which
/// the SPA fetches with the user's token and refuses to send anywhere else. Shared by the recommendations and the TMDb
/// suggestions, so a suggestion's poster is the same URL — and the same cached file — as a recommendation's.
/// </summary>
public static class CatalogPosterUrls
{
    /// <summary>The route's prefix; the file name follows it.</summary>
    public const string Prefix = "/api/recommendations/posters/";

    /// <summary>The URL of <paramref name="posterPath"/>'s poster; <c>null</c> for none, or one the route wouldn't serve —
    /// so a card never asks for one that can only 404.</summary>
    public static string? Of(string? posterPath) =>
        RecommendationPosterNames.FileNameOf(posterPath) is { } fileName ? Prefix + fileName : null;
}
