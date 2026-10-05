using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>One inspection of the library's files, from the request to its outcome (MAM-9).</summary>
/// <param name="Id">The run's id.</param>
/// <param name="Trigger">What started it.</param>
/// <param name="Status">Where it stands; <c>Queued</c> and <c>Running</c> are under way.</param>
/// <param name="RequestedByUserName">Who asked for it. For the installation owner only, like a sync's
/// (MAM-8): <c>null</c> for everyone else, and when a sync, a folder scan or the app's start queued it.</param>
/// <param name="CreatedUtc">When it was queued.</param>
/// <param name="StartedUtc">When the app began looking; <c>null</c> while queued.</param>
/// <param name="FinishedUtc">When it succeeded or failed; <c>null</c> while under way.</param>
/// <param name="FilesTotal">How many file names it looks at; <c>null</c> until it has listed them.</param>
/// <param name="FilesChecked">How many it has looked at so far.</param>
/// <param name="FilesProbed">How many the media inspector read in this run; the rest came from the
/// cache or couldn't be read at all.</param>
/// <param name="Hashing">Where its last step, the hashing of the files a byte-identical copy could hide
/// among, stands (MAM-19).</param>
/// <param name="Error">Why it failed. For the installation owner only: <c>null</c> for everyone
/// else.</param>
public sealed record FileInspectionRunDto(
    Guid Id,
    FileInspectionTrigger Trigger,
    FileInspectionRunStatus Status,
    string? RequestedByUserName,
    DateTime CreatedUtc,
    DateTime? StartedUtc,
    DateTime? FinishedUtc,
    int? FilesTotal,
    int FilesChecked,
    int FilesProbed,
    FileHashingDto Hashing,
    string? Error);
