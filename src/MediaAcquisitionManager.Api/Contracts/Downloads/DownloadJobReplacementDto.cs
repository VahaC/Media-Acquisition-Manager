using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>MAM-24 — the replacement of old copies the installation owner scheduled with a download, as the Jobs page and
/// the Download dialog show it under the job (ADR 0030–0034).</summary>
/// <param name="State">Where it stands: it mirrors its download until the download is done, then (MAM-25)
/// <c>Inspecting</c> while its new files are checked, and <c>Verified</c>, <c>NeedsReview</c> or <c>Failed</c>; then
/// (MAM-26) <c>Quarantining</c> while the old copies move into the quarantine, <c>Quarantined</c> while it keeps them,
/// <c>Restoring</c> or <c>Deleting</c> while the owner's Restore or Delete for good runs, and <c>Restored</c> or
/// <c>Completed</c> at the end.</param>
/// <param name="CopyCount">How many old copies it would replace.</param>
/// <param name="Reason">Why it stopped — failed, needs review or cancelled —, the app's own sentence; <c>null</c> while it
/// goes on.</param>
/// <param name="SwitchedOff">Replacement is switched off on the Media folders page right now: nothing moves while it is
/// off (ADR 0033).</param>
/// <param name="CopiesOutsideTickedFolders">How many of its copies lie in a folder no longer ticked for replacement right
/// now, which won't move while it isn't — (MAM-26) of the copies still to move, any of which stops the whole move for
/// review.</param>
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

    /// <summary>MAM-26 — whether the owner's Retry is offered: the job waits for review after the mover stopped it.</summary>
    public bool CanRetry { get; init; }
}
