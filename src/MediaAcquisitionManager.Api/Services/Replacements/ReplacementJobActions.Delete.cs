using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Texts;

namespace MediaAcquisitionManager.Api.Services.Replacements;

/// <summary>
/// MAM-26 — the owner's Delete for good (ADR 0030 "Kept until the owner says"): every name the job holds in the
/// quarantine deleted, after his second confirmation named every one of them, each re-read first and refused if it
/// changed. Only what the quarantine holds; nothing outside it is ever deleted.
/// </summary>
public sealed partial class ReplacementJobActions
{
    private static readonly Message NothingToDeleteError = Message.Of("replacements.delete-for-good.nothing");

    private static readonly Message NamesMissingError = Message.Of("replacements.delete-for-good.names-missing");

    private static readonly Message NamesChangedError = Message.Of("replacements.delete-for-good.names-changed");

    private static readonly Message DeleteSwitchedOffError = Message.Of("replacements.delete-for-good.switched-off");

    // MAM-26 — as Restore's: refused before the claim on a platform whose write contract writes nothing.
    private static readonly Message DeleteNotSupportedError = Message.Of("replacements.delete-for-good.unsupported");

    public async Task<ReplacementActionResult> DeleteForGoodAsync(
        Guid replacementJobId, IReadOnlyList<Guid>? names, Guid callerUserId, CancellationToken cancellationToken = default)
    {
        if (await FindOwnedAsync(replacementJobId, callerUserId, cancellationToken) is not { } job)
            return ReplacementActionResult.NotFound;
        // The second confirmation is the list the owner was shown: without it, nothing is deleted.
        if (names is not { Count: > 0 })
            return ReplacementActionResult.Invalid(NamesMissingError);

        var quarantined = await QuarantinedNamesAsync(replacementJobId, cancellationToken);
        if (!ReplacementJobOffers.CanDeleteForGood(job.Replacement.State, quarantined.Count > 0))
            return ReplacementActionResult.Refused(NothingToDeleteError);
        // Exactly the names he confirmed, as a set: one more, one fewer or another, and he confirmed something else.
        if (!quarantined.Select(name => name.Id).ToHashSet().SetEquals(names))
            return ReplacementActionResult.Refused(NamesChangedError);
        // Every check before anything is written: a Delete for good refused here leaves every row and every file as it was.
        if (await DeleteRefusalAsync(quarantined, cancellationToken) is { } refusal)
            return ReplacementActionResult.Refused(refusal);

        var actor = await ActorAsync(callerUserId, cancellationToken);
        if (!inHand.TryTake(replacementJobId))
            return ReplacementActionResult.Refused(BeingWorkedOnError);

        Message? stopped;
        try
        {
            stopped = await RunAsync(job, ReplacementJobState.Quarantined, quarantined, DeleteWrite(), actor);
        }
        finally
        {
            inHand.Release(replacementJobId);
        }

        return stopped is null ? await AnswerAsync(job, callerUserId) : ReplacementActionResult.Refused(stopped);
    }

    /// <summary>
    /// Why <paramref name="names"/> can't be deleted now, nothing written; <c>null</c> when they can: the platform writes,
    /// replacement is on, and every name's quarantine is within the Media folders with the file there still the one the
    /// move put there. No tick is read: the quarantine belongs to the allowed root, not to a library folder (ADR 0033).
    /// </summary>
    private async Task<Message?> DeleteRefusalAsync(IReadOnlyList<ReplacementJobNameEntity> names, CancellationToken cancellationToken)
    {
        if (!quarantine.Capabilities.CanRename)
            return DeleteNotSupportedError;

        // Read fresh, never cached (ADR 0033): the switch as it is now.
        var gate = await gates.ReadAsync(cancellationToken);
        if (!gate.IsEnabled)
            return DeleteSwitchedOffError;

        // MAM-40 — name by name as a removal's Delete for good checks them (QuarantineNameChecks).
        return await QuarantineNameChecks.DeleteRefusalAsync(files, names, gate, cancellationToken);
    }

    /// <summary>Delete for good as the write loop runs it: <c>Deleting</c> to <c>Completed</c>, nothing told to the
    /// library — it heard the names leave at the move — and no gate but the switch's read again before each name
    /// (<see cref="QuarantineNameWrites.Delete"/>).</summary>
    private OwnerWrite DeleteWrite() => new(
        ReplacementJobState.Deleting,
        ReplacementJobState.Completed,
        AuditAction.ReplacementDeleteStarted,
        AuditAction.ReplacementCompleted,
        QuarantineNameWrites.Delete(
            quarantine,
            AuditAction.ReplacementNameDeleting,
            AuditAction.ReplacementNameDeleted,
            AuditAction.ReplacementNameNotDeleted,
            SwitchedOffMidwayError));
}
