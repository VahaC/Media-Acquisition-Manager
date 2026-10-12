using MediaAcquisitionManager.Core.Replacements;

namespace MediaAcquisitionManager.Api.Mapping;

/// <summary>
/// MAM-27 — what the caller counted of one replacement's rows for its Jobs line, read in a batch for the whole page
/// (MAM-24, R10), so the mapper takes one value rather than a list of counts that grows with every ticket.
/// </summary>
/// <param name="CopyCount">The ticked copies the owner hasn't dropped: the ones it would replace.</param>
/// <param name="CopiesOutsideTickedFolders">Of those, the ones still to move that lie outside a ticked folder now.</param>
/// <param name="QuarantinedCopyCount">The ticked copies whose video is in the quarantine now.</param>
/// <param name="StayedFileCount">The companions that stayed in their folder, in a job that moved something.</param>
/// <param name="DroppedCopyCount">The ticked copies the owner dropped.</param>
/// <param name="DropOnlyCopyCount">Of the copies it would replace, the ones whose problem only a drop settles.</param>
/// <param name="HoldsTakenNames">One of its names left its folder and hasn't come back
/// (<see cref="ReplacementNameStates.Taken"/>).</param>
/// <param name="HoldsQuarantinedNames">MAM-41 — one of its names, a video or a companion, is in the quarantine now.</param>
/// <param name="VideoDeleted">MAM-41 — one of its videos was deleted for good, by a Delete for good that may have stopped
/// part-way.</param>
public sealed record ReplacementLineCounts(
    int CopyCount,
    int CopiesOutsideTickedFolders,
    int QuarantinedCopyCount,
    int StayedFileCount,
    int DroppedCopyCount,
    int DropOnlyCopyCount,
    bool HoldsTakenNames,
    bool HoldsQuarantinedNames,
    bool VideoDeleted);
