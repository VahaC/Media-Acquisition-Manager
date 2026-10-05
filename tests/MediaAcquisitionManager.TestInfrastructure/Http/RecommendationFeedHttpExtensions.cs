using MediaAcquisitionManager.Api.Services.Recommendations;

namespace MediaAcquisitionManager.TestInfrastructure.Http;

/// <summary>MAM-33 — the release sources' newest releases ("New on Toloka"), read by hand: the test host runs no
/// hosted services, so a test runs the reader's passes itself, one at a time.</summary>
public static class RecommendationFeedHttpExtensions
{
    /// <summary>
    /// Runs one pass of the app's own <see cref="RecommendationFeedReader"/> — a scope of its own, every release source
    /// and the metadata catalog asked as the running app asks them — and returns once it is over. Arm them first, e.g.
    /// with <c>FakeTorznabServer</c> and <c>FakeTmdbServer</c>; an unarmed one answers 503.
    /// </summary>
    public static Task ReadRecommendationFeedAsync(this MediaAcquisitionManagerApiFactory factory, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(factory);
        return factory.Resolve<RecommendationFeedReader>().ReadOnceAsync(cancellationToken);
    }
}
