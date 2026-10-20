using MediaAcquisitionManager.Api.Services.Replacements;
using MediaAcquisitionManager.Core.Replacements;
using MediaAcquisitionManager.Core.Texts;

namespace MediaAcquisitionManager.Api.Services.CopyRemovals;

// MAM-40 — the sentences of a removal: one readable sentence each, as the Duplicates page and Removed copies show them — a
// refusal, the removal's reason, a name's problem. Worded as a replacement's (ReplacementQuarantiner.Reasons.cs), for a copy
// the owner removes rather than an old copy a download replaces. MAM-47 (ADR 0042) — each a message: the removal's own
// sentences are duplicates.removal keys; check 5 and the move are the replacements.quarantine set's, the same key where
// the words are the same and a "-removal" variant where they differ; names and paths cleaned and cut (MessageText.Clean),
// their quotes the template's; the disk's own sentence nested whole after a colon; how far the move got as its tally.
public sealed partial class CopyRemovalService
{
    // ── the request ──

    private static readonly Message RequestIncompleteError = Message.Of("duplicates.removal.request-incomplete");

    // The identity the page showed is no longer the one the app read: a sync or an inspection since, and the owner would
    // confirm one file and remove another.
    private static readonly Message PageChangedReason = Message.Of("duplicates.removal.page-changed");

    // A platform whose write contract can't rename without risking a copy (anything but Linux), refused before the claim.
    private static readonly Message NotSupportedError = Message.Of("duplicates.removal.unsupported");

    private static readonly Message NoOtherCopyOnDiskReason = Message.Of("duplicates.removal.no-other-copy-on-disk");

    // ── the owner's later actions ──

    // Someone in this process — another click, or the quarantine's pass settling a cut — is writing for the removal now.
    private static readonly Message BeingWorkedOnError = Message.Of("duplicates.removal.busy");

    // The removal left the state the request claimed it in, between the claim and a later write: never written over.
    private static readonly Message ChangedMeanwhileError = Message.Of("duplicates.removal.changed-meanwhile");

    // ADR 0033 — the {error} of a stop when the switch was turned off between two names.
    private static readonly Message SwitchedOffMidwayError = Message.Of("duplicates.removal.switched-off-midway");

    // ── check 5's half, in a removal's words ──

    private static Message StopReasonOf(QuarantineCopyStop stop, string path, Message? error) => stop switch
    {
        QuarantineCopyStop.OutsideMediaFolders => Message.Of("replacements.quarantine.check.outside-media-folders", ("path", MessageText.Clean(path))),
        QuarantineCopyStop.NotTicked => Message.Of("replacements.quarantine.check.not-ticked", ("path", MessageText.Clean(path))),
        QuarantineCopyStop.DiscClip =>
            Message.Of("replacements.quarantine.check.disc-clip-removal", ("file", MessageText.Clean(ReplacementText.FileNameOf(path)))),
        QuarantineCopyStop.Gone => Message.Of("replacements.quarantine.check.gone-removal", ("path", MessageText.Clean(path))),
        QuarantineCopyStop.Changed => Message.Of("replacements.quarantine.check.changed-removal", ("path", MessageText.Clean(path))),
        // The disk's own sentence after the colon; a sentence of the app's when the disk gave none.
        _ when error is null => Message.Of("replacements.quarantine.check.unread-no-reason", ("path", MessageText.Clean(path))),
        _ => Message.Of("replacements.quarantine.check.unread", ("path", MessageText.Clean(path)), ("error", error)),
    };

    // ── the move ──

    private static Message SwitchedOffWhileMovingReason((int Count, int Total) moved) =>
        ReplacementNameTally.InQuarantine(moved.Count, moved.Total) is { } tally
            ? Message.Of("replacements.quarantine.move.switched-off-removal-tally", ("tally", tally))
            : Message.Of("replacements.quarantine.move.switched-off-removal");

    private static Message LeftTickedWhileMovingReason(string path, (int Count, int Total) moved) =>
        ReplacementNameTally.InQuarantine(moved.Count, moved.Total) is { } tally
            ? Message.Of("replacements.quarantine.move.left-ticked-removal-tally", ("path", MessageText.Clean(path)), ("tally", tally))
            : Message.Of("replacements.quarantine.move.left-ticked-removal", ("path", MessageText.Clean(path)));

    // The file system's sentence ends the reason's first sentence; how far the move got is a sentence of its own after it.
    private static Message NameNotMovedReason(string path, Message error, (int Count, int Total) moved)
    {
        var file = MessageText.Clean(ReplacementText.FileNameOf(path));
        return ReplacementNameTally.InQuarantine(moved.Count, moved.Total) is { } tally
            ? Message.Of("replacements.quarantine.move.failed-tally", ("file", file), ("error", error), ("tally", tally))
            : Message.Of("replacements.quarantine.move.failed", ("file", file), ("error", error));
    }

    // A rename the contract answered Swapped: another file took the name's place after the last look, and that file is what
    // moved. The quarantine path in full, as that is where the owner looks: the removal waits for review with no button to
    // press. No colon in it, as the reason nests it after "couldn't be moved into the quarantine:".
    private static Message SwappedWhileMovingProblem(string path, string quarantinePath) =>
        Message.Of(
            "replacements.quarantine.move.swapped-removal",
            ("quarantine", MessageText.Clean(quarantinePath)),
            ("file", MessageText.Clean(ReplacementText.FileNameOf(path))));
}
