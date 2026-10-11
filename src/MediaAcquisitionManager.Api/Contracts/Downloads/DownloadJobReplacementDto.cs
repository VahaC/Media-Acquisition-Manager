using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>MAM-24 — the replacement of old copies the installation owner scheduled with a download, as the Jobs page and
/// the Download dialog show it under the job (ADR 0030–0034).</summary>
/// <param name="State">Where it stands: it mirrors its download until the download is done, then (MAM-25)
/// <c>Inspecting</c> while its new files are checked, and <c>Verified</c>, <c>NeedsReview</c> or <c>Failed</c>; then
/// (MAM-26) <c>Quarantining</c> while the old copies move into the quarantine, <c>Quarantined</c> while it keeps them,
/// <c>Restoring</c> or <c>Deleting</c> while the owner's Restore or Delete for good runs, and <c>Restored</c> or
/// <c>Completed</c> at the end.</param>
/// <param name="CopyCount">How many old copies it would replace — (MAM-27) those the owner hasn't dropped from it.</param>
/// <param name="Reason">Why it stopped — failed, needs review or cancelled —, the app's own sentence; <c>null</c> while it
/// goes on.</param>
/// <param name="SwitchedOff">Replacement is switched off on the Media folders page right now: nothing moves while it is
/// off (ADR 0033).</param>
/// <param name="CopiesOutsideTickedFolders">How many of its copies lie in a folder no longer ticked for replacement right
/// now, which won't move while it isn't — (MAM-26) of the copies still to move, any of which stops the whole move for
/// review; (MAM-27) a dropped copy never moves, so its folder's tick stops nothing.</param>
public sealed record DownloadJobReplacementDto(
    ReplacementJobState State,
    int CopyCount,
    string? Reason,
    bool SwitchedOff,
    int CopiesOutsideTickedFolders)
{
    /// <summary>MAM-26 — the replacement's own id, which its details and the owner's Retry, Restore and Delete for good
    /// are addressed by (<c>api/replacements/{id}</c>).</summary>
    public Guid Id { get; init; }

    /// <summary>MAM-26 — how many of its ticked copies have their video in the quarantine now.</summary>
    public int QuarantinedCopyCount { get; init; }

    /// <summary>MAM-26 — how many companion files stayed in their folder — a torrent holds them, or their rename was
    /// refused — in a job that moved something.</summary>
    public int StayedFileCount { get; init; }

    /// <summary>MAM-26 — whether the owner's Retry is offered: the job waits for review after the mover stopped it.
    /// MAM-27 — or after the check of its new files stopped it, unless a copy it would replace has a problem only a drop
    /// settles (<see cref="DropOnlyCopyCount"/>).</summary>
    public bool CanRetry { get; init; }

    /// <summary>MAM-27 — how many of its ticked copies the owner dropped from it: each stays where it is.</summary>
    public int DroppedCopyCount { get; init; }

    /// <summary>MAM-27 — how many of the copies it would replace stopped it for a reason only dropping that copy, or
    /// keeping both, settles: no fix in the library or the client changes it, so Retry isn't offered while one is in.</summary>
    public int DropOnlyCopyCount { get; init; }

    /// <summary>MAM-27 — whether the owner's Keep both is offered, which cancels only the replacement: no old copy has
    /// started to move, and none of its names left its folder.</summary>
    public bool CanCancel { get; init; }

    /// <summary>MAM-27 — which stage stopped it while it waits for review — the download's following, the check of its new
    /// files, or the mover —; <c>null</c> in any other state.</summary>
    public ReplacementStop? Stop { get; init; }

    /// <summary>MAM-27 — while its new files are checked, whether the check reads them or waits for the library to identify
    /// them; <c>null</c> in any other state.</summary>
    public ReplacementCheckStage? CheckStage { get; init; }

    /// <summary>MAM-27 — while its new files are checked, until when the check waits for what it can't settle yet before it
    /// stops for review; <c>null</c> in any other state.</summary>
    public DateTime? CheckWaitsUntilUtc { get; init; }
}
