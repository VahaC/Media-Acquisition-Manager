namespace MediaAcquisitionManager.Api.Services.Duplicates;

/// <summary>
/// MAM-21 — the installation owner's Confirm, Dismiss and Undo of a possible match of the Duplicates list (Vasyl's
/// decisions of 2026-10-07, ADR 0029): one decision per group, kept by the group's key, each change with its audit row.
/// Both calls are idempotent: the same decision again, or an undo with nothing to undo, writes nothing. Expected failures
/// come back as result statuses, never as exceptions.
/// </summary>
/// <remarks>
/// A decision is a note on the list and nothing more: it never makes the works one, never changes whether the group
/// needs review, never removes, moves or links anything. A possible match is confirmed or dismissed; MAM-40 — copies of
/// one work and byte-identical copies are only kept as they are (Keep both, stored as dismissed with the members kept),
/// and one file's names are never decided. Only the group the key names <em>now</em> is decided: a group whose members
/// changed has another key, so it is asked about again — and kept copies a new file has joined, whose key stays, are
/// asked about again too (<c>DuplicateDecisionRules.IsInForce</c>).
/// </remarks>
public interface IDuplicateDecisionService
{
    /// <summary>
    /// Decides the group <paramref name="key"/> names now as <paramref name="decision"/> (a
    /// <c>DuplicateMatchDecision</c> by name, in any letter case), by <paramref name="ownerUserId"/>, recording
    /// <c>DuplicateMatchConfirmed</c> or <c>DuplicateMatchDismissed</c> for a possible match, <c>DuplicateCopiesKept</c>
    /// for kept copies. <see cref="DuplicateDecisionStatus.Invalid"/> for a decision missing or not one (checked first),
    /// <see cref="DuplicateDecisionStatus.NotFound"/> for a key no group has, <see cref="DuplicateDecisionStatus.Invalid"/>
    /// for a class that never takes the decision.
    /// </summary>
    Task<DuplicateDecisionResult> DecideAsync(string key, string? decision, Guid ownerUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Forgets the decision on the group <paramref name="key"/> names now, by <paramref name="ownerUserId"/>, recording
    /// <c>DuplicateMatchDecisionUndone</c> for a possible match, <c>DuplicateCopiesKeptUndone</c> for kept copies;
    /// <see cref="DuplicateDecisionStatus.Done"/> also when there was none in force. <see cref="DuplicateDecisionStatus.NotFound"/>
    /// as for <see cref="DecideAsync"/>, <see cref="DuplicateDecisionStatus.Invalid"/> for one file's names.
    /// </summary>
    Task<DuplicateDecisionResult> UndoAsync(string key, Guid ownerUserId, CancellationToken cancellationToken = default);
}
