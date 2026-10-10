using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>MAM-24 — the replacement of old copies the installation owner scheduled with a download, as the Jobs page and
/// the Download dialog show it under the job (ADR 0030–0034).</summary>
/// <param name="State">Where it stands: it mirrors its download until the download is done, then (MAM-25)
/// <c>Inspecting</c> while its new files are checked, and <c>Verified</c>, <c>NeedsReview</c> or <c>Failed</c>; nothing
/// moves in this version.</param>
/// <param name="CopyCount">How many old copies it would replace.</param>
/// <param name="Reason">Why it stopped — failed, needs review or cancelled —, the app's own sentence; <c>null</c> while it
/// goes on.</param>
/// <param name="SwitchedOff">Replacement is switched off on the Media folders page right now: nothing moves while it is
/// off (ADR 0033).</param>
/// <param name="CopiesOutsideTickedFolders">How many of its copies lie in a folder no longer ticked for replacement right
/// now, which won't move while it isn't.</param>
public sealed record DownloadJobReplacementDto(
    ReplacementJobState State,
    int CopyCount,
    string? Reason,
    bool SwitchedOff,
    int CopiesOutsideTickedFolders);
