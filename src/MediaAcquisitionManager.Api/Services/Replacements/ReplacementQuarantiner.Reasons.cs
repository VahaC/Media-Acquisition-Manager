using MediaAcquisitionManager.Core.Abstractions.Files;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Replacements;

namespace MediaAcquisitionManager.Api.Services.Replacements;

// The reasons: one readable sentence each, as the Jobs page and the job's page show them — a job's reason, a copy's or a
// name's problem. Names and paths quoted with ReplacementText.Quote; a sentence of the disk's or a client's own quoted
// whole after a colon.
public sealed partial class ReplacementQuarantiner
{
    // ── check 5 ──

    private static string OutsideMediaFoldersReason(string path) =>
        $"{ReplacementText.Quote(path)} is no longer inside the Media folders, so it stays where it is.";

    private static string NotTickedReason(string path) =>
        $"{ReplacementText.Quote(path)} is no longer in a folder ticked for replacement, so it stays where it is.";

    private static string DiscClipReason(string path) =>
        $"{ReplacementText.Quote(ReplacementText.FileNameOf(path))} is part of a disc folder, which can't be replaced one file at a time.";

    private static string GoneReason(string path) => $"{ReplacementText.Quote(path)} is gone, so there is nothing to move.";

    private static string ChangedReason(string path) =>
        $"{ReplacementText.Quote(path)} changed since it was picked for replacement, so it stays where it is.";

    private static string NotReadReason(string path, string? error) =>
        $"The app couldn't read {ReplacementText.Quote(path)} again, so it stays where it is: {error ?? "the disk gave no reason."}";

    private static string NewFileReason(string path) => $"{ReplacementText.Quote(path)} is the new file's own path, so it stays where it is.";

    private static string PairChangedReason(string path) =>
        $"The new file paired with {ReplacementText.Quote(ReplacementText.FileNameOf(path))} changed or went since it was verified, so nothing moves.";

    // MAM-40 — the companions' sentences are QuarantineCompanionListing's, and check 6's QuarantineHoldingCheck's, worded
    // for a replacement (QuarantineWording.Replacement): a removal says them too.

    // ── the move ──

    private static string SwitchedOffWhileMovingReason((int Count, int Total) moved) =>
        ReplacementNameTally.AfterColon(
            "Replacement was switched off while the old copies were moving", ReplacementNameTally.InQuarantine(moved.Count, moved.Total));

    private static string LeftTickedWhileMovingReason(string path, (int Count, int Total) moved) =>
        ReplacementNameTally.AfterColon(
            $"{ReplacementText.Quote(path)} left the folders ticked for replacement while the old copies were moving",
            ReplacementNameTally.InQuarantine(moved.Count, moved.Total));

    private static string NameNotMovedReason(string path, string error, (int Count, int Total) moved) =>
        ReplacementNameTally.AfterSentence(
            $"{ReplacementText.Quote(ReplacementText.FileNameOf(path))} couldn't be moved into the quarantine: {error}",
            ReplacementNameTally.InQuarantine(moved.Count, moved.Total));

    // The contract gives a sentence with every refusal; this is for one that broke it.
    private static string NoSentenceError(FileQuarantineStatus status) => $"the file system refused it ({status}).";

    // A rename the contract answered Swapped: another file took the name's place after the last look, and that file is
    // what moved. No colon in it, as the job's reason quotes it after "couldn't be moved into the quarantine:".
    private static string SwappedWhileMovingProblem(string path) =>
        $"A different file now sits in the quarantine under {ReplacementText.Quote(ReplacementText.FileNameOf(path))}, as another file took its place "
        + "just before the rename, and the old copy's own file wasn't found.";

    // ── a restart's cut ──

    private static string CutWhileMovingReason((int Count, int Total) moved) =>
        ReplacementNameTally.AfterColon("The app stopped while moving the old copies", ReplacementNameTally.InQuarantine(moved.Count, moved.Total));

    private static string CutWhileRestoringReason((int Count, int Total) restored) =>
        ReplacementNameTally.AfterColon(
            "The app stopped while putting the old copies back", ReplacementNameTally.BackWhereTheyWere(restored.Count, restored.Total));

    private static string CutWhileDeletingReason((int Count, int Total) deleted) =>
        ReplacementNameTally.AfterColon(
            "The app stopped while deleting the old copies for good", ReplacementNameTally.Deleted(deleted.Count, deleted.Total));
}
