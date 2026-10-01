using MediaAcquisitionManager.Core.Entities;

namespace MediaAcquisitionManager.Api.Services.Releases;

/// <summary>
/// Keeps what the release sources listed, so a release can later be sent to a download client by its id
/// without its download link ever leaving the server (ADR 0004), and removes what nothing needs any more
/// (MAM-10). Shared by the plain release search and the stored search (MAM-12).
/// </summary>
public interface IReleaseStore
{
    /// <summary>
    /// Every hit of every source as a new release row, its download link sealed to the row (ADR 0007), in
    /// one transaction; each source with the ids of its rows in its hits' order. A source removed while it
    /// was being searched is left out, rows and all, and each source is stored under its current name.
    /// MAM-12 — with a <paramref name="search"/>, that search row too, stamped with the time its releases
    /// are stored at (so the two expire together), one source row per stored source in the outcomes'
    /// order with how it answered, and every release linked to its source row.
    /// </summary>
    /// <param name="outcomes">What each source answered, in the order they were asked.</param>
    /// <param name="search">The stored search the releases belong to, not yet saved; <c>null</c> for a
    /// plain release search.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    Task<IReadOnlyList<StoredReleaseSource>> StoreAsync(
        IReadOnlyList<ReleaseSourceOutcome> outcomes,
        SearchEntity? search,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes the releases stored more than a day ago that no download job points at, then the stored
    /// searches made more than a day ago with their sources (MAM-12); a release a job keeps stays, no
    /// longer linked to a search. Never fails the search that triggered it: a failure is logged, and the
    /// next search prunes again.
    /// </summary>
    Task PruneAsync(CancellationToken cancellationToken = default);
}
