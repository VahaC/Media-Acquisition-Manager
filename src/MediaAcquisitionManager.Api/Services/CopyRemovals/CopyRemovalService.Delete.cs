using MediaAcquisitionManager.Api.Services.Replacements;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Texts;

namespace MediaAcquisitionManager.Api.Services.CopyRemovals;

/// <summary>
/// MAM-40 — the owner's Delete for good of a removal (ADR 0030 "Kept until the owner says"), as a replacement's (MAM-26):
/// every name it holds in the quarantine deleted, each re-read first and refused if it changed. Only what the quarantine
/// holds; nothing outside it is ever deleted. The second confirmation is the page's, which showed every path and size: the
/// route takes no body.
/// </summary>
public sealed partial class CopyRemovalService
{
    private static readonly Message NothingToDeleteError = Message.Of("duplicates.removal.delete.nothing");

    private static readonly Message DeleteSwitchedOffError = Message.Of("duplicates.removal.delete.switched-off");

    // A replacement's own words: the same platform and the same deletes.
    private static readonly Message DeleteNotSupportedError = Message.Of("replacements.delete-for-good.unsupported");

    public async Task<CopyRemovalResult> DeleteForGoodAsync(Guid copyRemovalId, Guid callerUserId, CancellationToken cancellationToken = default)
    {
        if (await FindAsync(copyRemovalId, cancellationToken) is not { } removal)
            return CopyRemovalResult.NotFound;

        var quarantined = await QuarantinedNamesAsync(copyRemovalId, cancellationToken);
        if (!CopyRemovalOffers.CanDeleteForGood(removal.State, quarantined.Count > 0))
            return CopyRemovalResult.Refused(NothingToDeleteError);
        // Every check before anything is written: a Delete for good refused here leaves every row and every file as it was.
        if (await DeleteRefusalAsync(quarantined, cancellationToken) is { } refusal)
            return CopyRemovalResult.Refused(refusal);

        var actor = await ActorAsync(callerUserId, cancellationToken);
        if (!inHand.TryTake(copyRemovalId))
            return CopyRemovalResult.Refused(BeingWorkedOnError);

        Message? stopped;
        try
        {
            stopped = await RunAsync(removal, quarantined, DeleteWrite(), actor);
        }
        finally
        {
            inHand.Release(copyRemovalId);
        }

        return stopped is null ? await AnswerAsync(copyRemovalId) : CopyRemovalResult.Refused(stopped);
    }

    /// <summary>
    /// Why <paramref name="names"/> can't be deleted now, nothing written; <c>null</c> when they can: the platform writes, the
    /// switch is on, and every name passes <see cref="QuarantineNameChecks.DeleteRefusalAsync"/>. No tick is read: the
    /// quarantine belongs to the allowed root, not to a library folder (ADR 0033).
    /// </summary>
    private async Task<Message?> DeleteRefusalAsync(IReadOnlyList<CopyRemovalNameEntity> names, CancellationToken cancellationToken)
    {
        if (!quarantine.Capabilities.CanRename)
            return DeleteNotSupportedError;

        // Read fresh, never cached (ADR 0033): the switch as it is now.
        var gate = await gates.ReadAsync(cancellationToken);
        if (!gate.IsEnabled)
            return DeleteSwitchedOffError;

        return await QuarantineNameChecks.DeleteRefusalAsync(files, names, gate, cancellationToken);
    }

    /// <summary>Delete for good as the write loop runs it: <c>Deleting</c> to <c>Deleted</c>, nothing told to the library —
    /// it heard the names leave at the move — and no gate but the switch's read again before each name.</summary>
    private RemovalWrite DeleteWrite() => new(
        CopyRemovalState.Deleting,
        CopyRemovalState.Deleted,
        AuditAction.CopyRemovalDeleteStarted,
        AuditAction.CopyRemovalDeletedForGood,
        QuarantineNameWrites.Delete(
            quarantine,
            AuditAction.CopyRemovalNameDeleting,
            AuditAction.CopyRemovalNameDeleted,
            AuditAction.CopyRemovalNameNotDeleted,
            SwitchedOffMidwayError));
}
