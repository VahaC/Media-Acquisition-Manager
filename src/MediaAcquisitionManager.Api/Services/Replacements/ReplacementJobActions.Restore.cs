using MediaAcquisitionManager.Core.Abstractions.Files;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Paths;
using MediaAcquisitionManager.Core.Replacements;
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

        foreach (var row in names)
        {
            if (RootsOf(row, gate) is not { } roots)
                return $"{ReplacementText.Quote(QuarantineOf(row))} is no longer inside the Media folders, so nothing was put back.";
            if (!IsInTickedFolder(row, gate))
                return $"{ReplacementText.Quote(row.AppPath)} is no longer in a folder ticked for replacement, so nothing was put back.";
            if (await ChangedInQuarantineAsync(row, roots, cancellationToken))
                return $"{ReplacementText.Quote(row.QuarantinePath)} changed in the quarantine since it was moved there, so nothing was put back.";

            // The folder the name goes back into, listed through the write contract, which never makes it again; and
            // anything at the old path among its names — a file, a folder, a link, one leading nowhere too, which a look
            // following it would find nothing at — is something a rename back would have to go around.
            var folder = await quarantine.ListFolderNamesAsync(row.AppPath, roots, cancellationToken);
            if (folder.Status == FileQuarantineStatus.NotFound)
                return $"The folder {ReplacementText.Quote(FolderOf(row.AppPath))} is gone, so nothing was put back; the app creates no folder.";
            if (folder is not { Status: FileQuarantineStatus.Done, Value: { } namesThere })
                return $"The app couldn't look in the folder of {ReplacementText.Quote(row.AppPath)}: {folder.Error ?? NoReasonError} Nothing was put back.";
            if (namesThere.Any(name => IsNameOf(row.AppPath, name)))
                return $"{ReplacementText.Quote(row.AppPath)} already holds a file, so nothing was put back.";
        }

        return null;
    }

    /// <summary>Restore as the write loop runs it: <c>Restoring</c> to <c>Restored</c>, the library told of every name
    /// that came back, each name's tick read again before it moves.</summary>
    private OwnerWrite RestoreWrite() => new(
        ReplacementJobState.Restoring,
        ReplacementJobState.Restored,
        ReplacementNameState.Restoring,
        ReplacementNameState.Restored,
        AuditAction.ReplacementRestoreStarted,
        AuditAction.ReplacementNameRestoring,
        AuditAction.ReplacementNameRestored,
        AuditAction.ReplacementNameNotRestored,
        AuditAction.ReplacementRestored,
        TellsLibrary: true,
        Closed: (row, gate) => IsInTickedFolder(row, gate)
            ? null
            : $"{ReplacementText.Quote(row.AppPath)} is no longer in a folder ticked for replacement.",
        WriteAsync: (row, roots) => quarantine.RestoreFromQuarantineAsync(row.QuarantinePath, FactsOf(row), row.AppPath, roots, CancellationToken.None),
        Stamp: (row, now) => row.RestoredUtc = now,
        SwappedProblem: row =>
            $"A different file now sits at {ReplacementText.Quote(row.AppPath)}, as another file took the name's place in the quarantine just before the "
            + "rename back, and the old copy's own file wasn't found.",
        StopReason: (row, error, done, total) =>
            ReplacementNameTally.AfterSentence($"{NameOf(row)} couldn't be put back: {error}", ReplacementNameTally.BackWhereTheyWere(done, total)));

    /// <summary>Whether <paramref name="row"/>'s old path lies in a ticked folder now (ADR 0033): the gate the move went
    /// through, which Restore goes through again.</summary>
    private static bool IsInTickedFolder(ReplacementJobNameEntity row, ReplacementGateState gate) =>
        NormalizedPath.TryParse(row.AppPath, out var path, out _) && gate.IsInTickedFolder(path);

    /// <summary>Whether <paramref name="name"/>, one name of a folder's listing, is <paramref name="appPath"/>'s own, by the
    /// path's own letter-case rules.</summary>
    private static bool IsNameOf(string appPath, string name) =>
        NormalizedPath.TryParse(appPath, out var path, out _)
        && path.Segments.Count > 0
        && (path with { Segments = [.. path.Segments.SkipLast(1)] }).Append([name]).IsSameAs(path);

    /// <summary>The folder holding <paramref name="appPath"/>.</summary>
    private static string FolderOf(string appPath) =>
        NormalizedPath.TryParse(appPath, out var path, out _) && path.Segments.Count > 0
            ? (path with { Segments = [.. path.Segments.SkipLast(1)] }).ToString()
            : appPath;
}
