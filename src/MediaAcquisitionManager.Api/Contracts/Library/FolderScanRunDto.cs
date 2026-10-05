using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>One scan of the media folders for the files no library reported, from the request to its
/// outcome (MAM-18).</summary>
/// <param name="Id">The run's id.</param>
/// <param name="Status">Where it stands; <c>Queued</c> and <c>Running</c> are under way.</param>
/// <param name="RequestedByUserName">Who asked for it. For the installation owner only, like an
/// inspection's (MAM-9): <c>null</c> for everyone else.</param>
/// <param name="CreatedUtc">When it was queued.</param>
/// <param name="StartedUtc">When the app began walking; <c>null</c> while queued.</param>
/// <param name="FinishedUtc">When it succeeded or failed; <c>null</c> while under way.</param>
/// <param name="FilesFound">How many media files the walk has reached so far — each once, however many
/// links lead to it; the total once it succeeded.</param>
/// <param name="FilesUnreported">How many of them no library reported; <c>null</c> until the scan stored
/// what it found.</param>
/// <param name="ProblemCount">How many folders, links and names the walk didn't go into or through.</param>
/// <param name="Problem">The first of those problems' sentences. For the installation owner only, as it
/// quotes the owner's paths: <c>null</c> for everyone else and when there was none.</param>
/// <param name="Error">Why it failed. For the installation owner only: <c>null</c> for everyone
/// else.</param>
public sealed record FolderScanRunDto(
    Guid Id,
    FolderScanStatus Status,
    string? RequestedByUserName,
    DateTime CreatedUtc,
    DateTime? StartedUtc,
    DateTime? FinishedUtc,
    int FilesFound,
    int? FilesUnreported,
    int ProblemCount,
    string? Problem,
    string? Error);
