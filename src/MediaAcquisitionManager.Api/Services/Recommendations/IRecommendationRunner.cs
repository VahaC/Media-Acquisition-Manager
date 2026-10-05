namespace MediaAcquisitionManager.Api.Services.Recommendations;

/// <summary>
/// MAM-33 — runs one queued computation of the recommendations: claims it, reads the library into memory, finds
/// each work's catalog work (its own id, else a possible match by its IMDb id or its title and year), reads their
/// details from the catalog through the cache, collects every signal — collections, directors and creators, the
/// catalog's recommendations, missing seasons, what a source just published, and a top-rated fallback for a tab
/// the library holds little of — and replaces the candidates in one transaction.
/// </summary>
/// <remarks>
/// Only the tabs some user who can sign in has on are computed, and a kind of work no such tab holds is never
/// asked of the catalog. The run fails as a whole — the previous candidates kept — when the catalog fails it (a
/// work it doesn't have is skipped, never a failure); nothing is ever written to the library's tables (ADR 0025).
/// </remarks>
public interface IRecommendationRunner
{
    /// <summary>
    /// Runs the queued run <paramref name="runId"/>; does nothing when it is no longer queued (another worker took
    /// it, or the reconciler failed it). Only a cancellation by the caller (the app shutting down) propagates,
    /// leaving the run running for the reconciler.
    /// </summary>
    Task RunAsync(Guid runId, CancellationToken cancellationToken = default);
}
