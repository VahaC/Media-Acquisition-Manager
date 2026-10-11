using MediaAcquisitionManager.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Services.Replacements;

// MAM-26 — the library notice's one write (IReplacementLibraryNotice, ADR 0030): settling a notice owed since a given time.
public static partial class ReplacementJobUpdates
{
    /// <summary>
    /// MAM-26 — settles the library notice the replacement <paramref name="replacementJobId"/> owes since
    /// <paramref name="dueUtc"/>, while that is still the notice it owes: one a move or a restore made due again meanwhile,
    /// at a later time, stays owed for its own paths. Never the job's state, which a notice never changes. How many it
    /// wrote (0 or 1).
    /// </summary>
    public static Task<int> SettleLibraryNoticeAsync(this IQueryable<ReplacementJobEntity> jobs, Guid replacementJobId, DateTime dueUtc) =>
        jobs.Where(job => job.Id == replacementJobId && job.LibraryNoticeDueUtc == dueUtc)
            .ExecuteUpdateAsync(setters => setters.SetProperty(job => job.LibraryNoticeDueUtc, (DateTime?)null), CancellationToken.None);
}
