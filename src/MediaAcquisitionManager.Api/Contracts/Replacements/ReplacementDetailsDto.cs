using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>
/// MAM-26 — one replacement as its owner sees it on the job's page (ADR 0030): where it stands, every name it moved or
/// planned with where it is now, the room the quarantine takes, and which of the owner's actions are offered. MAM-27 — with
/// who stopped it, how far the check of its new files got and until when it waits, every old copy ticked with its pair and
/// where it stands, the new files with what the check found of them, and the works they are expected to be.
/// </summary>
/// <param name="Id">The replacement.</param>
/// <param name="DownloadJobId">The download that brings the new copy.</param>
/// <param name="State">Where it stands.</param>
/// <param name="Reason">Why it stopped, the app's own sentence; <c>null</c> while it goes on.</param>
/// <param name="SwitchedOff">Replacement is switched off on the Media folders page right now: nothing is moved, put back
/// or deleted while it is off (ADR 0033).</param>
/// <param name="CanRetry">The mover stopped it, so the owner's Retry checks it again. MAM-27 — or the check of its new
/// files stopped it and no copy left in it has a problem only a drop settles, so Retry starts that check again.</param>
/// <param name="CanRestore">Some of its names are in the quarantine, so Restore puts them back.</param>
/// <param name="CanDeleteForGood">It rests in the quarantine, so Delete for good may remove its names.</param>
/// <param name="QuarantinedBytes">The sizes of its names in the quarantine, added up.</param>
/// <param name="FreesBytes">The room Delete for good would free: the names in the quarantine with no other name.</param>
/// <param name="Names">Every name of its plan, in order: each ticked copy's video, then its companions.</param>
/// <remarks>
/// MAM-27's members are <c>init</c> properties after MAM-26's positional ones, as the Jobs line's are: every construction
/// of the MAM-26 shape still compiles, and the JSON only gains keys.
/// </remarks>
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
    IReadOnlyList<ReplacementNameDto> Names)
{
    /// <summary>MAM-27 — Keep both is offered: none of its old copies has started to move or left its folder, and it hasn't
    /// ended. Offered while replacement is switched off too, as it writes nothing to the media.</summary>
    public bool CanCancel { get; init; }

    /// <summary>MAM-27 — which stage stopped it for review: its download, which the app couldn't follow, the check of its
    /// new files, or the mover; <c>null</c> unless it waits for review.</summary>
    public ReplacementStop? Stop { get; init; }

    /// <summary>MAM-27 — where the check of its new files is: reading them on the disk, or waiting for the library to
    /// identify them; <c>null</c> unless they are being checked.</summary>
    public ReplacementCheckStage? CheckStage { get; init; }

    /// <summary>MAM-27 — when the last check of its new files started; <c>null</c> before one did.</summary>
    public DateTime? CheckStartedUtc { get; init; }

    /// <summary>MAM-27 — when that check asked the library to look at the new files, the request having been taken;
    /// <c>null</c> before it did.</summary>
    public DateTime? LibraryAskedUtc { get; init; }

    /// <summary>MAM-27 — until when the running check waits for what it can't settle yet before it stops for review: an hour
    /// from when it started, or from when the library took the request to look; <c>null</c> unless the new files are being
    /// checked.</summary>
    public DateTime? CheckWaitsUntilUtc { get; init; }

    /// <summary>MAM-27 — how many of the copies still in it have a problem only dropping them, or keeping both copies,
    /// settles: Retry isn't offered while there is one.</summary>
    public int DropOnlyCopyCount { get; init; }

    /// <summary>MAM-27 — every old copy the owner ticked, dropped ones included, in the dialog's order.</summary>
    public IReadOnlyList<ReplacementJobCopyDto> Copies { get; init; } = [];

    /// <summary>MAM-27 — the new video files of its download the last check of them listed, in the client's order; none
    /// before a check did.</summary>
    public IReadOnlyList<ReplacementJobNewFileDto> NewFiles { get; init; } = [];

    /// <summary>MAM-27 — the works it expects the new files to be, each in the place of its first copy.</summary>
    public IReadOnlyList<ReplacementJobWorkDto> Works { get; init; } = [];
}
