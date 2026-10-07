namespace MediaAcquisitionManager.Api.Services.Duplicates;

/// <summary>
/// MAM-21 — the installation owner's Confirm, Dismiss and Undo of a possible match of the Duplicates list (Vasyl's
/// decisions of 2026-10-07, ADR 0029): one decision per group, kept by the group's key, each change with its audit row.
/// Both calls are idempotent: the same decision again, or an undo with nothing to undo, writes nothing. Expected failures
/// come back as result statuses, never as exceptions.
/// </summary>
/// <remarks>
/// A decision is a note on the list and nothing more: it never makes the works one, never changes whether the group
/// needs review, never removes, moves or links anything. Only a possible match is decided — the other classes are one
/// by the files' identity, bytes or registry ids — and only the group the key names <em>now</em>: a group whose members
/// changed has another key, so it is asked about again.
/// </remarks>
public interface IDuplicateDecisionService
{
    /// <summary>
    /// Decides the possible match <paramref name="key"/> names now as <paramref name="decision"/> (a
    /// <c>DuplicateMatchDecision</c> by name, in any letter case), by <paramref name="ownerUserId"/>, recording
    /// <c>DuplicateMatchConfirmed</c> or <c>DuplicateMatchDismissed</c>. <see cref="DuplicateDecisionStatus.Invalid"/> for
    /// a decision missing or not one (checked first), <see cref="DuplicateDecisionStatus.NotFound"/> for a key no group
    /// has, <see cref="DuplicateDecisionStatus.Invalid"/> for a group of another class.
    /// </summary>
    Task<DuplicateDecisionResult> DecideAsync(string key, string? decision, Guid ownerUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Forgets the decision on the possible match <paramref name="key"/> names now, by <paramref name="ownerUserId"/>,
    /// recording <c>DuplicateMatchDecisionUndone</c>; <see cref="DuplicateDecisionStatus.Done"/> also when there was none.
    /// <see cref="DuplicateDecisionStatus.NotFound"/> and <see cref="DuplicateDecisionStatus.Invalid"/> as for
    /// <see cref="DecideAsync"/>.
    /// </summary>
    Task<DuplicateDecisionResult> UndoAsync(string key, Guid ownerUserId, CancellationToken cancellationToken = default);
}
