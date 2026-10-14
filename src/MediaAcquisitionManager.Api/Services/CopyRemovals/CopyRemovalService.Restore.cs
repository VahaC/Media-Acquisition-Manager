using MediaAcquisitionManager.Api.Services.Replacements;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Services.CopyRemovals;

/// <summary>
/// MAM-40 — the owner's Restore of a removal (ADR 0030 "Restore"), as a replacement's (MAM-26): every name it holds in the
/// quarantine renamed back to where it was, never over a name there now and never into a folder that is gone, which the
/// app doesn't make again. Every check before anything is written; a write refused part-way stops it for review with the
/// names already back kept so.
/// </summary>
public sealed partial class CopyRemovalService
{
    private const string NothingToRestoreError = "Nothing of this removal is in the quarantine, so there's nothing to put back.";

    private const string RestoreSwitchedOffError =
        "Replacing and removing copies is switched off on the Media folders page, so nothing can be put back. Turn on “Replace copies after verification”, then press Restore again.";

    private const string RestoreNotSupportedError = "The app can't rename files on the platform it runs on, so nothing was put back.";

    // A Delete for good stopped part-way deleted the video: what is left of the copy — a subtitle, an .nfo — would go back
    // to the library alone, and the removal would end saying the copy is back. Delete for good takes what is left.
    private const string VideoDeletedError =
        "This copy's video was deleted permanently, so nothing is put back without it. Delete permanently what the quarantine still holds.";

    public async Task<CopyRemovalResult> RestoreAsync(Guid copyRemovalId, Guid callerUserId, CancellationToken cancellationToken = default)
    {
        if (await FindAsync(copyRemovalId, cancellationToken) is not { } removal)
            return CopyRemovalResult.NotFound;

        var names = await QuarantinedNamesAsync(copyRemovalId, cancellationToken);
        var videoDeleted = await db.CopyRemovalNames.AnyAsync(
            name => name.CopyRemovalId == copyRemovalId && name.Kind == ReplacementNameKind.Video && name.State == ReplacementNameState.Deleted,
            cancellationToken);
        if (!CopyRemovalOffers.CanRestore(removal.State, names.Count > 0, videoDeleted))
            return CopyRemovalResult.Refused(names.Count > 0 && videoDeleted ? VideoDeletedError : NothingToRestoreError);
        // Every check before anything is written: a Restore refused here leaves every row and every file as it was.
        if (await RestoreRefusalAsync(names, cancellationToken) is { } refusal)
            return CopyRemovalResult.Refused(refusal);

        var actor = await ActorAsync(callerUserId, cancellationToken);
        if (!inHand.TryTake(copyRemovalId))
            return CopyRemovalResult.Refused(BeingWorkedOnError);

        string? stopped;
        try
        {
            stopped = await RunAsync(removal, names, RestoreWrite(), actor);
        }
        finally
        {
            inHand.Release(copyRemovalId);
        }

        return stopped is null ? await AnswerAsync(copyRemovalId) : CopyRemovalResult.Refused(stopped);
    }

    /// <summary>
    /// Why <paramref name="names"/> can't be put back now, nothing written; <c>null</c> when they can: the platform renames,
    /// the switch is on, and every name passes <see cref="QuarantineNameChecks.RestoreRefusalAsync"/>.
    /// </summary>
    private async Task<string?> RestoreRefusalAsync(IReadOnlyList<CopyRemovalNameEntity> names, CancellationToken cancellationToken)
    {
        if (!quarantine.Capabilities.CanRename)
            return RestoreNotSupportedError;

        // Read fresh, never cached (ADR 0033): the switch and the ticks as they are now.
        var gate = await gates.ReadAsync(cancellationToken);
        if (!gate.IsEnabled)
            return RestoreSwitchedOffError;

        return await QuarantineNameChecks.RestoreRefusalAsync(files, quarantine, names, gate, cancellationToken);
    }

    /// <summary>Restore as the write loop runs it: <c>Restoring</c> to <c>Restored</c>, the library told of every name that
    /// came back, each name's tick read again before it moves.</summary>
    private RemovalWrite RestoreWrite() => new(
        CopyRemovalState.Restoring,
        CopyRemovalState.Restored,
        AuditAction.CopyRemovalRestoreStarted,
        AuditAction.CopyRemovalRestored,
        QuarantineNameWrites.Restore(
            quarantine,
            AuditAction.CopyRemovalNameRestoring,
            AuditAction.CopyRemovalNameRestored,
            AuditAction.CopyRemovalNameNotRestored,
            SwitchedOffMidwayError));
}
