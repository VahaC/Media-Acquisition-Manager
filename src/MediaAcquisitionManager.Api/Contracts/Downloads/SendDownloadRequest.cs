namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>A release to send to a download client, from the Download dialog (MAM-13).</summary>
/// <remarks>
/// MAM-13 — every field nullable and without <c>[Required]</c> (the <see cref="CreateDestinationRequest"/>
/// reason): a missing one reaches the rules' sentence. No save path, and no field that could carry one: the
/// destination's category decides where the download goes (specification §3.2).
/// </remarks>
/// <param name="RequestId">An id the browser makes once per submission and sends again with every retry of
/// it: the same id from the same user answers the job it already made and sends nothing (ADR 0013).</param>
/// <param name="ReleaseId">The release, one of the caller's own searches' from the last day.</param>
/// <param name="DownloadClientId">The download client integration to send it to.</param>
/// <param name="DestinationId">One of that client's destinations.</param>
public sealed record SendDownloadRequest(Guid? RequestId, Guid? ReleaseId, Guid? DownloadClientId, Guid? DestinationId);
