using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>
/// MAM-26 — one replacement as its owner sees it on the job's page (ADR 0030): where it stands, every name it moved or
/// planned with where it is now, the room the quarantine takes, and which of the owner's actions are offered.
/// </summary>
/// <param name="Id">The replacement.</param>
/// <param name="DownloadJobId">The download that brings the new copy.</param>
/// <param name="State">Where it stands.</param>
/// <param name="Reason">Why it stopped, the app's own sentence; <c>null</c> while it goes on.</param>
/// <param name="SwitchedOff">Replacement is switched off on the Media folders page right now: nothing is moved, put back
/// or deleted while it is off (ADR 0033).</param>
/// <param name="CanRetry">The mover stopped it, so the owner's Retry checks it again.</param>
/// <param name="CanRestore">Some of its names are in the quarantine, so Restore puts them back.</param>
/// <param name="CanDeleteForGood">It rests in the quarantine, so Delete for good may remove its names.</param>
/// <param name="QuarantinedBytes">The sizes of its names in the quarantine, added up.</param>
/// <param name="FreesBytes">The room Delete for good would free: the names in the quarantine with no other name.</param>
/// <param name="Names">Every name of its plan, in order: each ticked copy's video, then its companions.</param>
public sealed record ReplacementDetailsDto(
    Guid Id,
    Guid DownloadJobId,
    ReplacementJobState State,
    string? Reason,
    bool SwitchedOff,
    bool CanRetry,
    bool CanRestore,
    bool CanDeleteForGood,
    long QuarantinedBytes,
    long FreesBytes,
    IReadOnlyList<ReplacementNameDto> Names);
