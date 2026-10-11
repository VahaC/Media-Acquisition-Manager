using MediaAcquisitionManager.Api.Contracts;

namespace MediaAcquisitionManager.Api.Services.Replacements;

/// <summary>
/// MAM-26 — the owner's own hand on a replacement (ADR 0030, ADR 0033): its details, Retry after the mover stopped it,
/// Restore of every name it holds in the quarantine, and Delete for good of them after a second confirmation. Only the
/// owner's own replacements — another's, or a missing one, is "not found" alike — and only the owner may call it,
/// enforced by the route's convention.
/// </summary>
/// <remarks>
/// <para>
/// Restore and Delete for good act on the whole job and run inside the request: the job is taken in this process
/// (<see cref="ReplacementJobsInHand"/>) for as long, so the mover's recovery never mistakes it for one a restart cut;
/// each name's write has its intent row committed before the call and its outcome row after it, and the switch is read
/// again before every write. Nothing is written before every check passed; a write refused part-way stops the job for
/// review with the names already done kept where they are. Retry writes nothing to the media.
/// </para>
/// <para>
/// Expected failures are results, never exceptions; each sentence is one the owner reads.
/// </para>
/// </remarks>
public interface IReplacementJobActions
{
    /// <summary>The caller's replacement <paramref name="replacementJobId"/> with every name and what the owner may do with
    /// it; <c>null</c> when the caller has none such.</summary>
    Task<ReplacementDetailsDto?> GetAsync(Guid replacementJobId, Guid callerUserId, CancellationToken cancellationToken = default);

    /// <summary>Puts a replacement the mover stopped back in its queue (<c>NeedsReview</c> to <c>Verified</c>), so its next
    /// pass checks it again.</summary>
    Task<ReplacementActionResult> RetryAsync(Guid replacementJobId, Guid callerUserId, CancellationToken cancellationToken = default);

    /// <summary>Renames every name the replacement holds in the quarantine back where it was, never over a name there now
    /// and never creating a folder.</summary>
    Task<ReplacementActionResult> RestoreAsync(Guid replacementJobId, Guid callerUserId, CancellationToken cancellationToken = default);

    /// <summary>Deletes every name the replacement holds in the quarantine for good, when <paramref name="names"/> — the
    /// owner's second confirmation — are still exactly those names.</summary>
    Task<ReplacementActionResult> DeleteForGoodAsync(
        Guid replacementJobId, IReadOnlyList<Guid>? names, Guid callerUserId, CancellationToken cancellationToken = default);
}
