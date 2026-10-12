using MediaAcquisitionManager.Core.Abstractions.Files;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Replacements;

namespace MediaAcquisitionManager.Api.Services.Replacements;

/// <summary>
/// MAM-26, MAM-40 — the two writes an owner runs over names in the quarantine (ADR 0030), Restore and Delete for good, as
/// <see cref="QuarantineNameWrite"/> strategies: a replacement's and a removal's run the same rules and sentences, only
/// their audit actions and the switch's sentence differ.
/// </summary>
internal static class QuarantineNameWrites
{
    /// <summary>Restore: each name renamed back to where it was, never over a name there now; the library told of every
    /// name that came back, each name's tick read again before it moves.</summary>
    public static QuarantineNameWrite Restore(
        IFileQuarantine quarantine, AuditAction intentAction, AuditAction doneAction, AuditAction notDoneAction, string switchedOffError) => new(
        ReplacementNameState.Restoring,
        ReplacementNameState.Restored,
        intentAction,
        doneAction,
        notDoneAction,
        TellsLibrary: true,
        switchedOffError,
        Closed: (row, gate) => QuarantineNameChecks.IsInTickedFolder(row, gate)
            ? null
            : $"{ReplacementText.Quote(row.AppPath)} is no longer in a folder ticked for replacement.",
        WriteAsync: (row, roots) =>
            quarantine.RestoreFromQuarantineAsync(row.QuarantinePath, QuarantineNameChecks.FactsOf(row), row.AppPath, roots, CancellationToken.None),
        Stamp: (row, now) => row.RestoredUtc = now,
        SwappedProblem: row =>
            $"A different file now sits at {ReplacementText.Quote(row.AppPath)}, as another file took the name's place in the quarantine just before the "
            + "rename back, and the old copy's own file wasn't found.",
        StopReason: (row, error, done, total) =>
            ReplacementNameTally.AfterSentence(
                $"{QuarantineNameChecks.NameOf(row)} couldn't be put back: {error}", ReplacementNameTally.BackWhereTheyWere(done, total)));

    /// <summary>Delete for good: each name deleted from the quarantine, nothing told to the library — it heard the names
    /// leave at the move — and no gate but the switch's read again before each name.</summary>
    public static QuarantineNameWrite Delete(
        IFileQuarantine quarantine, AuditAction intentAction, AuditAction doneAction, AuditAction notDoneAction, string switchedOffError) => new(
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
        // A deletion moves nothing, so the contract never answers it Swapped; the sentence is there should it ever.
        SwappedProblem: row =>
            $"The file deleted at {ReplacementText.Quote(row.QuarantinePath)} wasn't the one moved there, and the old copy's own file wasn't found.",
        StopReason: (row, error, done, total) =>
            ReplacementNameTally.AfterSentence($"{QuarantineNameChecks.NameOf(row)} couldn't be deleted: {error}", ReplacementNameTally.Deleted(done, total)));
}
