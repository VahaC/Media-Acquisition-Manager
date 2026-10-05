namespace MediaAcquisitionManager.Api.Services.Recommendations;

/// <summary>
/// MAM-33, ADR 0024 — one pass of the availability check: asks the release sources whether they carry the
/// recommended works that are due, within each user's daily search limit, and stores what they found per work
/// (<c>ReleaseAvailabilityEntity</c>). <see cref="RecommendationAvailabilityChecker"/> runs a pass every
/// <see cref="RecommendationAvailabilityChecker.PassInterval"/>; tests run one by hand, as the test host runs no
/// hosted services.
/// </summary>
/// <remarks>
/// <para>
/// Nothing is kept in memory between passes: what is due comes from the stored checks, and each user's budget
/// from the searches logged in the last 24 hours (<c>RecommendationSearchEntity</c>), so a restart resumes where
/// the rows are.
/// </para>
/// <para>
/// The release sources are other people's trackers, asked for nothing a person typed: the pass never runs more
/// than <see cref="RecommendationAvailabilityPass.SearchesPerPass"/> searches, never one past a user's limit, and
/// stops at the first search no source answered. A work no source could even be asked for — none searches its kind —
/// sends nothing, counts nothing and waits, with the other works of its kind, for the next pass. Nothing is
/// downloaded and nothing is stored of the releases themselves (ADR 0023).
/// </para>
/// </remarks>
public interface IRecommendationAvailabilityPass
{
    /// <summary>
    /// Checks the works due, at most <see cref="RecommendationAvailabilityPass.SearchesPerPass"/> of them, the
    /// users served in turn. Nothing at all without an enabled release source. Only a cancellation by the caller
    /// propagates.
    /// </summary>
    Task RunAsync(CancellationToken cancellationToken = default);
}
