using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Services.Releases;

/// <summary>
/// The one rule for whose stored searches, and so whose releases, a request may use (ADR 0011): the caller's own, and
/// only while younger than their releases' lifetime (<see cref="ReleaseStore.UnusedReleaseLifetime"/>) — gone then,
/// whether or not a later search has pruned them yet, so nothing answers with a release that may vanish under it.
/// </summary>
/// <remarks>
/// MAM-24 — one place for what the search page's reads, the download send and the replacement's dialog read and send
/// each check, where two of them had a copy of it until then.
/// </remarks>
public static class CallerSearches
{
    /// <summary>The caller's searches still in their lifetime at <paramref name="time"/>'s now, unread.</summary>
    public static IQueryable<SearchEntity> Of(ApplicationDbContext db, Guid callerUserId, TimeProvider time)
    {
        var cutoff = time.GetUtcNow().UtcDateTime - ReleaseStore.UnusedReleaseLifetime;
        return db.Searches.AsNoTracking().Where(search => search.UserId == callerUserId && search.CreatedUtc >= cutoff);
    }

    /// <summary>
    /// The release <paramref name="releaseId"/>, when one of the caller's searches still in their lifetime found it,
    /// with that search and its source's place; <c>null</c> otherwise. A release a plain release search found (MAM-10)
    /// belongs to no search, and one whose search was removed to none any more: both answer as missing, as another
    /// user's does.
    /// </summary>
    public static Task<CallerRelease?> FindReleaseAsync(
        ApplicationDbContext db, Guid releaseId, Guid callerUserId, TimeProvider time, CancellationToken cancellationToken = default) =>
        (
            from release in db.Releases.AsNoTracking()
            join searchSource in db.SearchSources.AsNoTracking() on release.SearchSourceId equals (Guid?)searchSource.Id
            join search in Of(db, callerUserId, time) on searchSource.SearchId equals search.Id
            where release.Id == releaseId
            select new CallerRelease(release, search, searchSource.Position, searchSource.Name)).SingleOrDefaultAsync(cancellationToken);
}
