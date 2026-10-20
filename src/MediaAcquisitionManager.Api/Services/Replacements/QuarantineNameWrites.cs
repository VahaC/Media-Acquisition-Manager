using MediaAcquisitionManager.Core.Abstractions.Files;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Texts;

namespace MediaAcquisitionManager.Api.Services.Replacements;

/// <summary>
/// MAM-26, MAM-40 — the two writes an owner runs over names in the quarantine (ADR 0030), Restore and Delete for good, as
/// <see cref="QuarantineNameWrite"/> strategies: a replacement's and a removal's run the same rules and sentences, only
/// their audit actions, the switch's sentence and — for a name a Restore found swapped — what the copy is called differ.
/// </summary>
internal static class QuarantineNameWrites
{
    /// <summary>Restore: each name renamed back to where it was, never over a name there now; the library told of every
    /// name that came back, each name's tick read again before it moves.</summary>
    public static QuarantineNameWrite Restore(
        IFileQuarantine quarantine,
        AuditAction intentAction,
        AuditAction doneAction,
        AuditAction notDoneAction,
        Message switchedOffError,
        QuarantineWording wording) => new(
        ReplacementNameState.Restoring,
        ReplacementNameState.Restored,
        intentAction,
        doneAction,
        notDoneAction,
        TellsLibrary: true,
        switchedOffError,
        Closed: (row, gate) => QuarantineNameChecks.IsInTickedFolder(row, gate)
            ? null
            : Message.Of("replacements.quarantine.restore.left-ticked", ("path", MessageText.Clean(row.AppPath))),
        WriteAsync: (row, roots) =>
            quarantine.RestoreFromQuarantineAsync(row.QuarantinePath, QuarantineNameChecks.FactsOf(row), row.AppPath, roots, CancellationToken.None),
        Stamp: (row, now) => row.RestoredUtc = now,
        // MAM-47 — a removal's copy is "this copy", as every other sentence of a removal calls it (the old copy before).
        SwappedProblem: row => wording == QuarantineWording.Replacement
            ? Message.Of("replacements.quarantine.restore.swapped-replacement", ("path", MessageText.Clean(row.AppPath)))
            : Message.Of("replacements.quarantine.restore.swapped-removal", ("path", MessageText.Clean(row.AppPath))),
        StopReason: (row, error, done, total) => StopReason(row, error, ReplacementNameTally.BackWhereTheyWere(done, total), restoring: true));

    /// <summary>Delete for good: each name deleted from the quarantine, nothing told to the library — it heard the names
    /// leave at the move — and no gate but the switch's read again before each name.</summary>
    public static QuarantineNameWrite Delete(
        IFileQuarantine quarantine, AuditAction intentAction, AuditAction doneAction, AuditAction notDoneAction, Message switchedOffError) => new(
        ReplacementNameState.Deleting,
        ReplacementNameState.Deleted,
        intentAction,
        doneAction,
        notDoneAction,
        TellsLibrary: false,
        switchedOffError,
        Closed: (_, _) => null,
        WriteAsync: (row, roots) => quarantine.DeleteFromQuarantineAsync(row.QuarantinePath, QuarantineNameChecks.FactsOf(row), roots, CancellationToken.None),
        Stamp: (row, now) => row.DeletedUtc = now,
        // A deletion moves nothing, so the contract never answers it Swapped (LocalFileQuarantine checks a name only after
        // a rename): no sentence of the app's for it — MAM-47 removed the one no reader ever met.
        SwappedProblem: null,
        StopReason: (row, error, done, total) => StopReason(row, error, ReplacementNameTally.Deleted(done, total), restoring: false));

    // The name, then the file system's or the gate's sentence; how far the write got as a sentence of its own after it.
    private static Message StopReason(QuarantinedNameEntity row, Message error, Message? tally, bool restoring)
    {
        var file = QuarantineNameChecks.NameOf(row);
        return (restoring, tally) switch
        {
            (true, null) => Message.Of("replacements.quarantine.restore.failed", ("file", file), ("error", error)),
            (true, { } written) => Message.Of("replacements.quarantine.restore.failed-tally", ("file", file), ("error", error), ("tally", written)),
            (false, null) => Message.Of("replacements.quarantine.delete.failed", ("file", file), ("error", error)),
            (false, { } written) => Message.Of("replacements.quarantine.delete.failed-tally", ("file", file), ("error", error), ("tally", written)),
        };
    }
}
