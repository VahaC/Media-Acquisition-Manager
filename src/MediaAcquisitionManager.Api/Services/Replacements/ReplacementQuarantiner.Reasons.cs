using MediaAcquisitionManager.Core.Abstractions.Files;
using MediaAcquisitionManager.Core.Replacements;
using MediaAcquisitionManager.Core.Texts;

namespace MediaAcquisitionManager.Api.Services.Replacements;

// The reasons, as the Jobs page and the job's page show them — a job's reason, a copy's or a name's problem. MAM-47 (ADR
// 0042) — each a message of the replacements.quarantine keys, rendered in the reader's language: names and paths cleaned
// and cut (MessageText.Clean), their quotes the template's; a sentence of the disk's or a client's own nested whole after a
// colon; how far a write got nested as its tally (ReplacementNameTally), in a "-tally" key of its own when there is one.
public sealed partial class ReplacementQuarantiner
{
    // ── check 5 ──

    private static Message OutsideMediaFoldersReason(string path) =>
        Message.Of("replacements.quarantine.check.outside-media-folders", ("path", MessageText.Clean(path)));

    private static Message NotTickedReason(string path) =>
        Message.Of("replacements.quarantine.check.not-ticked", ("path", MessageText.Clean(path)));

    private static Message DiscClipReason(string path) =>
        Message.Of("replacements.quarantine.check.disc-clip", ("file", MessageText.Clean(ReplacementText.FileNameOf(path))));

    private static Message GoneReason(string path) => Message.Of("replacements.quarantine.check.gone", ("path", MessageText.Clean(path)));

    private static Message ChangedReason(string path) => Message.Of("replacements.quarantine.check.changed", ("path", MessageText.Clean(path)));

    // The disk's own sentence after the colon; a sentence of the app's when the disk gave none.
    private static Message NotReadReason(string path, Message? error) =>
        error is null
            ? Message.Of("replacements.quarantine.check.unread-no-reason", ("path", MessageText.Clean(path)))
            : Message.Of("replacements.quarantine.check.unread", ("path", MessageText.Clean(path)), ("error", error));

    private static Message NewFileReason(string path) => Message.Of("replacements.quarantine.check.new-file", ("path", MessageText.Clean(path)));

    private static Message PairChangedReason(string path) =>
        Message.Of("replacements.quarantine.check.pair-changed", ("file", MessageText.Clean(ReplacementText.FileNameOf(path))));

    // MAM-40 — the companions' sentences are QuarantineCompanionListing's, and check 6's QuarantineHoldingCheck's, worded
    // for a replacement (QuarantineWording.Replacement): a removal says them too.

    // ── the move ──

    private static Message SwitchedOffWhileMovingReason((int Count, int Total) moved) =>
        ReplacementNameTally.InQuarantine(moved.Count, moved.Total) is { } tally
            ? Message.Of("replacements.quarantine.move.switched-off-tally", ("tally", tally))
            : Message.Of("replacements.quarantine.move.switched-off");

    private static Message LeftTickedWhileMovingReason(string path, (int Count, int Total) moved) =>
        ReplacementNameTally.InQuarantine(moved.Count, moved.Total) is { } tally
            ? Message.Of("replacements.quarantine.move.left-ticked-tally", ("path", MessageText.Clean(path)), ("tally", tally))
            : Message.Of("replacements.quarantine.move.left-ticked", ("path", MessageText.Clean(path)));

    // The file system's sentence ends the reason's first sentence; how far the move got is a sentence of its own after it.
    private static Message NameNotMovedReason(string path, Message error, (int Count, int Total) moved)
    {
        var file = MessageText.Clean(ReplacementText.FileNameOf(path));
        return ReplacementNameTally.InQuarantine(moved.Count, moved.Total) is { } tally
            ? Message.Of("replacements.quarantine.move.failed-tally", ("file", file), ("error", error), ("tally", tally))
            : Message.Of("replacements.quarantine.move.failed", ("file", file), ("error", error));
    }

    // The contract gives a sentence with every refusal; this is for one that broke it. The status stays its API name.
    private static Message NoSentenceError(FileQuarantineStatus status) =>
        Message.Of("replacements.quarantine.move.refused", ("status", status.ToString()));

    // A rename the contract answered Swapped: another file took the name's place after the last look, and that file is
    // what moved. No colon in it, as the job's reason nests it after "couldn't be moved into the quarantine:".
    private static Message SwappedWhileMovingProblem(string path) =>
        Message.Of("replacements.quarantine.move.swapped", ("file", MessageText.Clean(ReplacementText.FileNameOf(path))));

    // ── a restart's cut ──

    private static Message CutWhileMovingReason((int Count, int Total) moved) =>
        ReplacementNameTally.InQuarantine(moved.Count, moved.Total) is { } tally
            ? Message.Of("replacements.quarantine.cut.moving-tally", ("tally", tally))
            : Message.Of("replacements.quarantine.cut.moving");

    private static Message CutWhileRestoringReason((int Count, int Total) restored) =>
        ReplacementNameTally.BackWhereTheyWere(restored.Count, restored.Total) is { } tally
            ? Message.Of("replacements.quarantine.cut.restoring-tally", ("tally", tally))
            : Message.Of("replacements.quarantine.cut.restoring");

    private static Message CutWhileDeletingReason((int Count, int Total) deleted) =>
        ReplacementNameTally.Deleted(deleted.Count, deleted.Total) is { } tally
            ? Message.Of("replacements.quarantine.cut.deleting-tally", ("tally", tally))
            : Message.Of("replacements.quarantine.cut.deleting");
}
