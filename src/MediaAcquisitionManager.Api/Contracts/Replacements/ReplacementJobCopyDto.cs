using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>
/// MAM-27 — one old copy the owner ticked for a replacement, as the job's page lists it (ADR 0032, ADR 0035): the copy as it
/// was scheduled, what the check found of it, the new file it pairs with, why it stopped the job, and where it stands now.
/// The raw facts beside the server's own status, so the page never works a pair out again.
/// </summary>
/// <param name="Id">The copy's row: what the owner's Drop addresses (<c>api/replacements/{id}/copies/{copyId}/drop</c>).</param>
/// <param name="Position">Its 0-based place among the replacement's copies, in the dialog's order.</param>
/// <param name="AppPath">Its path inside the app when it was scheduled.</param>
/// <param name="SizeBytes">Its size on the disk then.</param>
/// <param name="WorkId">The work the release matched — the film, or the episode's series —, one of the replacement's
/// works.</param>
/// <param name="SeasonNumber">An episode's season, as its own library item placed it; <c>null</c> for a film.</param>
/// <param name="EpisodeNumber">An episode's number there; <c>null</c> for a film, and for an episode the library numbers
/// none.</param>
/// <param name="EpisodeNumberEnd">The last episode a copy of several covers, as the library last reported it; <c>null</c>
/// for a film, and before the library was read.</param>
/// <param name="EditionLabel">Its version's own edition label then; <c>null</c> when the library gave none.</param>
/// <param name="DurationMilliseconds">Its measured running time; <c>null</c> when it was never read, which leaves the new
/// file's running time uncompared.</param>
/// <param name="PairedNewFileId">The new file it is paired with, one of the replacement's new files; <c>null</c> while none
/// is.</param>
/// <param name="Problem">Why it stopped the replacement, the app's own sentence; <c>null</c> when it didn't. While the check
/// still runs, what a pass that waits on another file found, which stops nothing yet (<paramref name="Status"/> isn't
/// Stopped then). A dropped copy keeps its last one as history.</param>
/// <param name="OnlyDropSettles">In a replacement stopped for review, no fix in the library or the download client changes
/// <paramref name="Problem"/>: only dropping the copy, or keeping both copies, settles it, and Retry isn't offered while it
/// is in the replacement. <c>false</c> in any other state, but for a dropped copy, which keeps its last one as history.</param>
/// <param name="DroppedUtc">When the owner dropped it from the replacement: it stays where it is and never moves with this
/// job. <c>null</c> while it is in it.</param>
/// <param name="Status">Where it stands now.</param>
/// <param name="CanDrop">The owner's Drop is offered for it now: the replacement stopped for review after its check or
/// before its move, the copy isn't dropped, none of its names left its folder, and it isn't the last copy left.</param>
public sealed record ReplacementJobCopyDto(
    Guid Id,
    int Position,
    string AppPath,
    long SizeBytes,
    Guid WorkId,
    int? SeasonNumber,
    int? EpisodeNumber,
    int? EpisodeNumberEnd,
    string? EditionLabel,
    long? DurationMilliseconds,
    Guid? PairedNewFileId,
    string? Problem,
    bool OnlyDropSettles,
    DateTime? DroppedUtc,
    ReplacementCopyStatus Status,
    bool CanDrop);
