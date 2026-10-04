namespace MediaAcquisitionManager.Api.Services.Releases;

/// <summary>
/// Searches every enabled release source at once for one title (MAM-10) and keeps what they list, so a
/// release can later be sent to a download client by its id without its download link ever leaving the
/// server (ADR 0004). Every signed-in user searches; only the owner reads why a source failed.
/// </summary>
public interface IReleaseSearchService
{
    /// <summary>
    /// Asks each enabled release source, concurrently, by the queries <c>ReleaseSearchPlanning</c> makes
    /// from what that source can search by, and stores each release found as a new release row. A source
    /// that fails, or that the app can't ask, answers with its own status and sentence while the others
    /// still list theirs. Releases stored more than a day ago that no download job points at are removed
    /// on the way, and — MAM-12, the prune the stored search shares — the stored searches made more than
    /// a day ago, with their sources. <see cref="ReleaseSearchStatus.Invalid"/>, before any source is
    /// asked, for a missing or too long title, a year that isn't one, an id that isn't an IMDb or a
    /// TMDb id, or (MAM-32) a season that isn't one. MAM-32 — a <paramref name="season"/> is searched for
    /// in TV alone. The sentence of a failed source only when <paramref name="callerUserId"/> is the
    /// installation owner: it can name the service's address.
    /// </summary>
    Task<ReleaseSearchResult> SearchAsync(
        string? text,
        string? year,
        string? imdbId,
        string? tmdbId,
        string? season,
        Guid callerUserId,
        CancellationToken cancellationToken = default);
}
