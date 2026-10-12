using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Services.Replacements;

/// <summary>
/// MAM-26 — the owner's Restore (ADR 0030 "Restore"): every name the job holds in the quarantine renamed back to where it
/// was, never over a name there now and never into a folder that is gone, which the app doesn't make again.
/// </summary>
public sealed partial class ReplacementJobActions
{
    private const string NothingToRestoreError = "Nothing of this replacement is in the quarantine, so there's nothing to put back.";

    private const string RestoreSwitchedOffError =
        "Replacement is switched off on the Media folders page, so nothing can be put back. Switch it on, then press Restore again.";

    // MAM-26 — a platform whose write contract can't rename without risking a copy (anything but Linux): refused before
    // the claim, so the job isn't stopped for review over the platform it runs on.
    private const string RestoreNotSupportedError = "The app can't rename files on the platform it runs on, so nothing was put back.";

    // MAM-26 — a Delete for good stopped part-way deleted a video: what is left of its copy — a subtitle, an .nfo — would go
    // back to the library alone, and the job would end saying the old copy is back. Retry settles the job where it rests,
    // and Delete for good takes what is left.
    private const string VideoDeletedError =
        "A video of this replacement was deleted for good, so nothing is put back without it. Press Retry, then Delete for good what the quarantine still holds.";

    public async Task<ReplacementActionResult> RestoreAsync(Guid replacementJobId, Guid callerUserId, CancellationToken cancellationToken = default)
    {
        if (await FindOwnedAsync(replacementJobId, callerUserId, cancellationToken) is not { } job)
            return ReplacementActionResult.NotFound;

        var names = await QuarantinedNamesAsync(replacementJobId, cancellationToken);
        var videoDeleted = await db.ReplacementJobNames.AnyAsync(
            name => name.ReplacementJobId == replacementJobId && name.Kind == ReplacementNameKind.Video && name.State == ReplacementNameState.Deleted,
            cancellationToken);
        if (!ReplacementJobOffers.CanRestore(job.Replacement.State, names.Count > 0, videoDeleted))
            return ReplacementActionResult.Refused(names.Count > 0 && videoDeleted ? VideoDeletedError : NothingToRestoreError);
        // Every check before anything is written: a Restore refused here leaves every row and every file as it was.
        if (await RestoreRefusalAsync(names, cancellationToken) is { } refusal)
            return ReplacementActionResult.Refused(refusal);

        var actor = await ActorAsync(callerUserId, cancellationToken);
        if (!inHand.TryTake(replacementJobId))
            return ReplacementActionResult.Refused(BeingWorkedOnError);

        string? stopped;
        try
        {
            stopped = await RunAsync(job, job.Replacement.State, names, RestoreWrite(), actor);
        }
        finally
        {
            inHand.Release(replacementJobId);
        }

        return stopped is null ? await AnswerAsync(job, callerUserId) : ReplacementActionResult.Refused(stopped);
    }

    /// <summary>
    /// Why <paramref name="names"/> can't be put back now, each check in turn, nothing written; <c>null</c> when they can:
    /// the platform renames, replacement is on, and for every name its quarantine is within the Media folders, its old
    /// path in a ticked folder, the file in the quarantine still the one the move put there, nothing at its old path, and
    /// its old folder still there.
    /// </summary>
    private async Task<string?> RestoreRefusalAsync(IReadOnlyList<ReplacementJobNameEntity> names, CancellationToken cancellationToken)
    {
        if (!quarantine.Capabilities.CanRename)
            return RestoreNotSupportedError;

        // Read fresh, never cached (ADR 0033): the switch and the ticks as they are now.
        var gate = await gates.ReadAsync(cancellationToken);
        if (!gate.IsEnabled)
            return RestoreSwitchedOffError;

        // MAM-40 — name by name as a removal's Restore checks them (QuarantineNameChecks).
        return await QuarantineNameChecks.RestoreRefusalAsync(files, quarantine, names, gate, cancellationToken);
    }

    /// <summary>Restore as the write loop runs it: <c>Restoring</c> to <c>Restored</c>, the library told of every name
    /// that came back, each name's tick read again before it moves (<see cref="QuarantineNameWrites.Restore"/>).</summary>
    private OwnerWrite RestoreWrite() => new(
        ReplacementJobState.Restoring,
        ReplacementJobState.Restored,
        AuditAction.ReplacementRestoreStarted,
        AuditAction.ReplacementRestored,
        QuarantineNameWrites.Restore(
            quarantine,
            AuditAction.ReplacementNameRestoring,
            AuditAction.ReplacementNameRestored,
            AuditAction.ReplacementNameNotRestored,
            SwitchedOffMidwayError));
}
