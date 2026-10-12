using MediaAcquisitionManager.Api.Services.Replacements;
using MediaAcquisitionManager.Core.Replacements;

namespace MediaAcquisitionManager.Api.Services.CopyRemovals;

// MAM-40 — the sentences of a removal: one readable sentence each, as the Duplicates page and Removed copies show them — a
// refusal, the removal's reason, a name's problem. Names and paths quoted with ReplacementText.Quote; a sentence of the
// disk's own quoted whole after a colon. Worded as a replacement's (ReplacementQuarantiner.Reasons.cs), for a copy the owner
// removes rather than an old copy a download replaces.
public sealed partial class CopyRemovalService
{
    // ── the request ──

    private const string RequestIncompleteError = "Remove needs the group and the file you pressed it on.";

    // The identity the page showed is no longer the one the app read: a sync or an inspection since, and the owner would
    // confirm one file and remove another.
    private const string PageChangedReason = "This copy changed since the page showed it. Reload the page to see it as it is now.";

    // A platform whose write contract can't rename without risking a copy (anything but Linux), refused before the claim.
    private const string NotSupportedError = "The app can't rename files on the platform it runs on, so nothing was removed.";

    private const string NoOtherCopyOnDiskReason =
        "No other copy of this group is on the disk as the app last read it, so this one stays. Inspect the library's files again to see what is there.";

    // ── the owner's later actions ──

    // Someone in this process — another click, or the quarantine's pass settling a cut — is writing for the removal now.
    private const string BeingWorkedOnError = "This removal is being worked on right now. Try again in a moment.";

    // The removal left the state the request claimed it in, between the claim and a later write: never written over.
    private const string ChangedMeanwhileError = "This removal changed while the app was working on it, so it stopped. Reload the page to see where it stands.";

    // ADR 0033 — the {error} of a stop when the switch was turned off between two names.
    private const string SwitchedOffMidwayError = "Replacing and removing copies was switched off on the Media folders page.";

    // ── check 5's half, in a removal's words ──

    private static string StopReasonOf(QuarantineCopyStop stop, string path, string? error) => stop switch
    {
        QuarantineCopyStop.OutsideMediaFolders => $"{ReplacementText.Quote(path)} is no longer inside the Media folders, so it stays where it is.",
        QuarantineCopyStop.NotTicked => $"{ReplacementText.Quote(path)} is no longer in a folder ticked for replacement, so it stays where it is.",
        QuarantineCopyStop.DiscClip =>
            $"{ReplacementText.Quote(ReplacementText.FileNameOf(path))} is part of a disc folder, which can't be removed one file at a time.",
        QuarantineCopyStop.Gone => $"{ReplacementText.Quote(path)} is gone, so there is nothing to remove.",
        QuarantineCopyStop.Changed =>
            $"{ReplacementText.Quote(path)} changed since the app last read it, so it stays where it is. Inspect the library's files again, then press Remove again.",
        _ => $"The app couldn't read {ReplacementText.Quote(path)} again, so it stays where it is: {error ?? "the disk gave no reason."}",
    };

    // ── the move ──

    private static string SwitchedOffWhileMovingReason((int Count, int Total) moved) =>
        ReplacementNameTally.AfterColon(
            "Replacing and removing copies was switched off while this copy was moving", ReplacementNameTally.InQuarantine(moved.Count, moved.Total));

    private static string LeftTickedWhileMovingReason(string path, (int Count, int Total) moved) =>
        ReplacementNameTally.AfterColon(
            $"{ReplacementText.Quote(path)} left the folders ticked for replacement while this copy was moving",
            ReplacementNameTally.InQuarantine(moved.Count, moved.Total));

    private static string NameNotMovedReason(string path, string error, (int Count, int Total) moved) =>
        ReplacementNameTally.AfterSentence(
            $"{ReplacementText.Quote(ReplacementText.FileNameOf(path))} couldn't be moved into the quarantine: {error}",
            ReplacementNameTally.InQuarantine(moved.Count, moved.Total));

    // A rename the contract answered Swapped: another file took the name's place after the last look, and that file is what
    // moved. The quarantine path in full, as that is where the owner looks: the removal waits for review with no button to
    // press. No colon in it, as the reason quotes it after "couldn't be moved into the quarantine:".
    private static string SwappedWhileMovingProblem(string path, string quarantinePath) =>
        $"A different file now sits in the quarantine at {ReplacementText.Quote(quarantinePath)}, as another file took the place of "
        + $"{ReplacementText.Quote(ReplacementText.FileNameOf(path))} just before the rename, and this copy's own file wasn't found. Look there to see which file it is.";
}
