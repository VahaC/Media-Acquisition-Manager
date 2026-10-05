using MediaAcquisitionManager.Api.Services.Recommendations;

namespace MediaAcquisitionManager.TestInfrastructure.Http;

/// <summary>MAM-33 — the recommendations' availability check, driven by hand: the test host runs no hosted services,
/// so a test runs the checker's passes itself, one at a time.</summary>
public static class RecommendationAvailabilityHttpExtensions
{
    /// <summary>
    /// Runs one pass of the app's own <see cref="RecommendationAvailabilityChecker"/> — a scope of its own, every
    /// release source asked as the running app asks it — and returns once it is over. Arm the sources first, e.g.
    /// with <c>FakeTorznabServer</c>; an unarmed one answers 503.
    /// </summary>
    public static Task CheckRecommendationAvailabilityAsync(this MediaAcquisitionManagerApiFactory factory, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(factory);
        return factory.Resolve<RecommendationAvailabilityChecker>().CheckOnceAsync(cancellationToken);
    }
}
