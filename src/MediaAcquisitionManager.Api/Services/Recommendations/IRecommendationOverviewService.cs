namespace MediaAcquisitionManager.Api.Services.Recommendations;

/// <summary>
/// What a recommended work is about, for the Description dialog of its card: asked of the catalog the first time someone
/// opens it, in the language they read, and kept per language (<c>CatalogWorkOverviews</c>) for the next. Expected
/// failures come back as result statuses, never as exceptions.
/// </summary>
/// <remarks>
/// <para>
/// Vasyl's decisions of 2026-10-14: asked on the click, not computed with the lists — so every card has one at once, at
/// the cost of a short wait the first time; and English for now, as not every user reads Ukrainian. The language is the
/// caller's, so once the app speaks more than one the dialog asks in the user's and falls back to English when the
/// catalog has none in it (TMDb has a Ukrainian overview for about half the films).
/// </para>
/// <para>
/// Only a work the catalog has is asked about, so the endpoint is no open proxy to the catalog. A kept answer — the
/// "none" too — is read again once older than the catalog's refresh age; until then, and whenever the catalog can't be
/// asked, the kept one is served.
/// </para>
/// </remarks>
public interface IRecommendationOverviewService
{
    /// <summary>
    /// The overview of the catalog work <paramref name="kind"/> <paramref name="catalogId"/> in
    /// <paramref name="language"/> (English when none is given), else in English, else none.
    /// <see cref="RecommendationOverviewStatus.Invalid"/> for a kind, an id or a language the request doesn't take;
    /// <see cref="RecommendationOverviewStatus.NotFound"/> for a work the catalog doesn't have;
    /// <see cref="RecommendationOverviewStatus.NoCatalog"/> or <see cref="RecommendationOverviewStatus.Failed"/> when
    /// nothing is kept and the catalog can't answer — the catalog's sentence, masked, for the installation owner
    /// <paramref name="callerUserId"/>, a plain one for anyone else.
    /// </summary>
    Task<RecommendationOverviewResult> GetAsync(
        string? kind, string? catalogId, string? language, Guid callerUserId, CancellationToken cancellationToken = default);
}
