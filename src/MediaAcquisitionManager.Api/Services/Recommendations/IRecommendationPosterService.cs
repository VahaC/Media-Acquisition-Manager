namespace MediaAcquisitionManager.Api.Services.Recommendations;

/// <summary>
/// MAM-33 — the posters of recommended works, served by the app itself: the browser is never sent to the catalog's
/// image host (ADR 0007's "a credential goes only to the saved address" has its mirror here — nobody's browser tells a
/// third party what the household looks at), and the page's CSP keeps <c>img-src 'self' data:</c>. Expected failures
/// come back as result statuses, never as exceptions.
/// </summary>
/// <remarks>
/// <para>
/// Only a file name that is some catalog work's poster path is served, so the endpoint fetches nothing the catalog
/// didn't name and reads no file it didn't write. A poster is fetched once, through the catalog's adapter
/// (<c>IMetadataCatalog.GetPosterAsync</c>), and kept in <c>&lt;data&gt;/posters/</c>: a catalog's poster path names one
/// image for good (a changed poster gets a new path), so a kept file never goes stale.
/// </para>
/// <para>
/// Kept files are never pruned: a w342 poster is tens of kilobytes, so even the few thousand works a library
/// recommends over the years stay far below a library's own size.
/// </para>
/// </remarks>
public interface IRecommendationPosterService
{
    /// <summary>
    /// The poster <paramref name="fileName"/> names (<c>abc.jpg</c> for the catalog's <c>/abc.jpg</c>): from the cache,
    /// else fetched from the catalog and cached. <see cref="RecommendationPosterStatus.NotFound"/> for a name that is no
    /// catalog work's poster, a poster the image host doesn't have, or one not cached while no catalog is connected;
    /// <see cref="RecommendationPosterStatus.Failed"/> when the fetch failed — with the catalog's sentence, masked, for
    /// the installation owner <paramref name="callerUserId"/>, and a plain one for anyone else.
    /// </summary>
    Task<RecommendationPosterResult> GetAsync(string fileName, Guid callerUserId, CancellationToken cancellationToken = default);
}
