namespace MediaAcquisitionManager.Api.Services.Recommendations;

/// <summary>
/// MAM-33 — one read of the release sources' newest releases ("New on Toloka"): each enabled source asked once for
/// its latest, every release it lists kept as a feed entry (<c>SourceFeedEntryEntity</c>), and the entries not yet
/// resolved looked up in the metadata catalog, so a run can recommend the works they are
/// (<c>RecommendationReasonKind.NewAtSource</c>). <see cref="RecommendationFeedReader"/> runs a pass every
/// <see cref="RecommendationFeedReader.PassInterval"/>; tests run one by hand.
/// </summary>
/// <remarks>
/// Nothing at all unless some user who can sign in has a tab on and a metadata catalog is connected: without either,
/// no list would ever show what the feed found. Nothing is downloaded and no release is kept for download (ADR 0023).
/// </remarks>
public interface IRecommendationFeedPass
{
    /// <summary>
    /// Reads the feeds, resolves at most <see cref="RecommendationFeedPass.ResolvePerPass"/> entries, newest first,
    /// queues a run when any of them turned out to be a catalog work, and forgets the entries no source listed for
    /// <see cref="RecommendationFeedPass.FeedEntryLifetime"/>. A source that fails is logged and the others still
    /// count. Only a cancellation by the caller propagates.
    /// </summary>
    Task RunAsync(CancellationToken cancellationToken = default);
}
