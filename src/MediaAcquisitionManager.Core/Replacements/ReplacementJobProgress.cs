using MediaAcquisitionManager.Core.Abstractions.Library;
using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Core.Replacements;

/// <summary>
/// MAM-27 — where a replacement and each of its rows stand, as the Jobs line and the job's page show it (ADR 0035, ADR
/// 0030): which stage stopped a review, where a running check is and until when it waits, each ticked copy's status and
/// how far the check got with each new file. Derived from the stored rows on every read and never stored, so the page
/// never works the pairing out again, and the line, the page and the owner's actions read one rule.
/// </summary>
/// <remarks>
/// Pure: each function judges what the caller read. A state a function isn't about answers <c>null</c>, so a caller can
/// send its answer as it is.
/// </remarks>
public static class ReplacementJobProgress
{
    /// <summary>
    /// Which stage stopped a job in <see cref="ReplacementJobState.NeedsReview"/>; <c>null</c> in any other state. The
    /// mover marks every job it took up (<paramref name="quarantineCheckedUtc"/>, never cleared), so its stop wins; a check
    /// of the new files that started (<paramref name="inspectionStartedUtc"/>) stopped it otherwise; and a review with
    /// neither is the download's following's, whose check never started.
    /// </summary>
    public static ReplacementStop? StopOf(ReplacementJobState state, DateTime? inspectionStartedUtc, DateTime? quarantineCheckedUtc) =>
        state != ReplacementJobState.NeedsReview ? null
        : quarantineCheckedUtc is not null ? ReplacementStop.Move
        : inspectionStartedUtc is not null ? ReplacementStop.Check
        : ReplacementStop.Download;

    /// <summary>
    /// Where a job in <see cref="ReplacementJobState.Inspecting"/> stands; <c>null</c> in any other state: the library's
    /// stage once it was asked to look (<paramref name="libraryAskedUtc"/>), the files' before.
    /// </summary>
    public static ReplacementCheckStage? StageOf(ReplacementJobState state, DateTime? libraryAskedUtc) =>
        state != ReplacementJobState.Inspecting ? null
        : libraryAskedUtc is not null ? ReplacementCheckStage.Library
        : ReplacementCheckStage.Files;

    /// <summary>
    /// Until when a job in <see cref="ReplacementJobState.Inspecting"/> waits for what it can't settle yet before it stops
    /// for review — the library's hour from when it was asked to look, or the files' hour from when the check started
    /// (<see cref="ReplacementVerificationRules"/>) —; <c>null</c> in any other state, and when the time it is reckoned from
    /// isn't stored.
    /// </summary>
    public static DateTime? CheckWaitsUntilUtc(ReplacementJobState state, DateTime? inspectionStartedUtc, DateTime? libraryAskedUtc) =>
        state != ReplacementJobState.Inspecting ? null
        : libraryAskedUtc is { } askedUtc ? askedUtc + ReplacementVerificationRules.LibraryWait
        : inspectionStartedUtc + ReplacementVerificationRules.FilesWait;

    /// <summary>
    /// Where one ticked copy stands in a job in <paramref name="jobState"/>: dropped by the owner (<paramref name="dropped"/>)
    /// before anything else, as it is out of the job; then where its video's name is (<paramref name="videoNameState"/>,
    /// <c>null</c> when the move planned none), as a name that left its folder says more than the check did; an ended job
    /// — cancelled, failed, restored or deleted for good — leaves the rest where they are, a copy a move that stopped part-way
    /// never reached included; then its own <paramref name="problem"/> once it stopped the job for review — a pass that
    /// still waits on another file keeps the problem it found, which stops nothing yet —, its pair
    /// (<paramref name="pairedNewFileId"/>) — a stopping pair keeps its problem first —, and whether the check is over
    /// without one.
    /// </summary>
    public static ReplacementCopyStatus CopyStatusOf(
        ReplacementJobState jobState, bool dropped, Guid? pairedNewFileId, string? problem, ReplacementNameState? videoNameState) =>
        dropped ? ReplacementCopyStatus.Dropped
        : videoNameState switch
        {
            ReplacementNameState.Quarantined => ReplacementCopyStatus.InQuarantine,
            ReplacementNameState.Deleted => ReplacementCopyStatus.Deleted,
            ReplacementNameState.Restored => ReplacementCopyStatus.Restored,
            ReplacementNameState.Lost => ReplacementCopyStatus.Lost,
            ReplacementNameState.Moving or ReplacementNameState.Restoring or ReplacementNameState.Deleting => ReplacementCopyStatus.Moving,
            // Planned, Stayed or no name: still at its old path, so the job and the check say the rest.
            _ => jobState is ReplacementJobState.Cancelled or ReplacementJobState.Failed or ReplacementJobState.Restored or ReplacementJobState.Completed
                    ? ReplacementCopyStatus.Stays
                : problem is not null && jobState == ReplacementJobState.NeedsReview ? ReplacementCopyStatus.Stopped
                : pairedNewFileId is not null ? ReplacementCopyStatus.Paired
                : jobState is ReplacementJobState.Verified or ReplacementJobState.NeedsReview or ReplacementJobState.Quarantining
                    or ReplacementJobState.Quarantined
                    ? ReplacementCopyStatus.NotPaired
                : ReplacementCopyStatus.Waiting,
        };

    /// <summary>
    /// How far the check got with one new file of a job in <paramref name="jobState"/>, from its evidence row, in the order
    /// the check gathers it: its own <paramref name="problem"/> first once the check is over — while it still runs
    /// (<see cref="ReplacementJobState.Inspecting"/>) a pass that waits on another file keeps the problem it found, which
    /// stops nothing yet, so the file says how far the check got with it —; then the client's progress, the disk's reading (<paramref name="checkedUtc"/>),
    /// whether the job's library was asked to look (<paramref name="libraryAskedUtc"/>) and has been read since
    /// (<paramref name="readUtc"/>), whether the library lists the file (<paramref name="libraryItemId"/>) and finished
    /// reading it (<paramref name="refreshed"/>), what it made of it (<paramref name="kind"/>: neither a film nor an episode
    /// is an extra, simply added), and whether it identified it as one of the job's works (<paramref name="mediaWorkId"/>) —
    /// an episode only once it is numbered too.
    /// </summary>
    public static ReplacementNewFileProgress NewFileProgressOf(
        ReplacementJobState jobState,
        string? problem,
        double? clientProgress,
        DateTime? checkedUtc,
        DateTime? libraryAskedUtc,
        DateTime? readUtc,
        string? libraryItemId,
        LibraryFileKind? kind,
        bool refreshed,
        Guid? mediaWorkId,
        int? seasonNumber,
        int? episodeNumber) =>
        problem is not null && jobState != ReplacementJobState.Inspecting ? ReplacementNewFileProgress.Stopped
        : clientProgress < 1 ? ReplacementNewFileProgress.Downloading
        : checkedUtc is null ? ReplacementNewFileProgress.NotChecked
        : libraryAskedUtc is null ? ReplacementNewFileProgress.Checked
        : readUtc is null ? ReplacementNewFileProgress.AskedToLook
        : libraryItemId is null ? ReplacementNewFileProgress.NotListed
        : !refreshed ? ReplacementNewFileProgress.NotRead
        : kind is not (LibraryFileKind.Movie or LibraryFileKind.Episode) ? ReplacementNewFileProgress.Extra
        : mediaWorkId is null ? ReplacementNewFileProgress.NoId
        : kind == LibraryFileKind.Episode && (seasonNumber is null || episodeNumber is null) ? ReplacementNewFileProgress.NotNumbered
        : ReplacementNewFileProgress.Identified;
}
