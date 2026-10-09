namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>
/// MAM-24 — a download that replaces the old copies the installation owner ticked in the Download dialog (ADR 0032): a
/// send, as <see cref="SendDownloadRequest"/> names it, with the copies and the title-and-year matches he confirmed.
/// </summary>
/// <remarks>
/// Every field nullable and without <c>[Required]</c>, <see cref="SendDownloadRequest"/>'s reason: a missing one reaches
/// the rules' sentence. Ids only, never a path or an identity: the server reads both from the library and the disk when
/// the request comes, so a copy is scheduled as it is then, never as a browser says it was.
/// </remarks>
/// <param name="RequestId">An id the browser makes once per submission and sends again with every retry of it: the same
/// id from the same user with the same ticks answers the job it already made and sends nothing (ADR 0013).</param>
/// <param name="ReleaseId">The release, one of the caller's own searches' from the last day.</param>
/// <param name="DownloadClientId">The download client integration to send it to.</param>
/// <param name="DestinationId">One of that client's destinations.</param>
/// <param name="FileIds">The copies ticked: each a file id the dialog's read listed (<c>ReplacementCopyDto.File</c>), at
/// least one, each once.</param>
/// <param name="ConfirmedWorkIds">The works matched by title and year whose confirmation the owner ticked ("This release
/// is Alien (1979)"); each must be one that needs it, with a copy ticked.</param>
public sealed record ScheduleReplacementRequest(
    Guid? RequestId,
    Guid? ReleaseId,
    Guid? DownloadClientId,
    Guid? DestinationId,
    IReadOnlyList<Guid>? FileIds = null,
    IReadOnlyList<Guid>? ConfirmedWorkIds = null);
