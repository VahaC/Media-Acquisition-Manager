using MediaAcquisitionManager.Api.Contracts;

namespace MediaAcquisitionManager.Api.Services.CopyRemovals;

/// <summary>
/// MAM-40 — the owner's removal of one copy of a duplicate group from the Duplicates page into the quarantine of the allowed
/// root that holds it (ADR 0030), and his Restore and Delete for good of it under Removed copies. Only the owner may call
/// it, enforced by the route's convention (<c>ReplacementsAreOwnerOnlyConvention</c>).
/// </summary>
/// <remarks>
/// <para>
/// Remove runs inside the request, one at a time in the process (<see cref="CopyRemovalTurns"/>): the stored rules
/// (<see cref="CopyRemovalRules"/>), then the disk — the identity the page showed, another copy still there, the companions
/// — and every download client (check 6), each before anything is written; a refusal keeps no row. Then the claim — the
/// removal row, its whole plan of names and its audit row in one transaction —, the video's rename, then each companion's,
/// each with an intent row committed before the call and an outcome row after it; then its end, and the library's notice.
/// A video that can't move ends it <c>NotMoved</c>, nothing in the quarantine; one whose rename moved another file is lost,
/// and leaves it <c>NeedsReview</c> with where to look. Both are refused with the sentence.
/// </para>
/// <para>
/// Restore and Delete for good act on the whole removal, as a replacement's do (MAM-26): every check before anything is
/// written, the removal held in this process (<see cref="CopyRemovalsInHand"/>) for as long, so the quarantine's pass never
/// mistakes it for one a restart cut.
/// </para>
/// <para>
/// Expected failures are results, never exceptions; each sentence is one the owner reads.
/// </para>
/// </remarks>
public interface ICopyRemovalService
{
    /// <summary>One page of the removals still open, newest first, with the room every open removal takes in the
    /// quarantine; invalid for a page below 1 or a page size outside 1 to 100.</summary>
    Task<CopyRemovalListResult> ListAsync(int page, int pageSize, CancellationToken cancellationToken = default);

    /// <summary>Removes the file <c>request.FileId</c> of the group <c>request.GroupKey</c> into the quarantine, when every
    /// rule and check lets it; for the caller, the owner.</summary>
    Task<CopyRemovalResult> RemoveAsync(RemoveCopyRequest? request, Guid callerUserId, CancellationToken cancellationToken = default);

    /// <summary>Renames every name the removal holds in the quarantine back where it was, never over a name there now and
    /// never creating a folder.</summary>
    Task<CopyRemovalResult> RestoreAsync(Guid copyRemovalId, Guid callerUserId, CancellationToken cancellationToken = default);

    /// <summary>Deletes every name the removal holds in the quarantine for good: the owner confirmed it a second time on the
    /// page, which showed every path and size.</summary>
    Task<CopyRemovalResult> DeleteForGoodAsync(Guid copyRemovalId, Guid callerUserId, CancellationToken cancellationToken = default);
}
