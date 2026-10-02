using MediaAcquisitionManager.Core.Abstractions.Search;

namespace MediaAcquisitionManager.Core.Abstractions.Downloads;

/// <summary>
/// A release handed to a download client. Deliberately no save path: a destination is never
/// overridden by a conflicting path (specification §3.2).
/// </summary>
/// <param name="Payload">The release's payload, fetched by the server.</param>
/// <param name="DestinationKey">The client destination to download into.</param>
/// <param name="ExpectedInfoHash">The info hash the app worked out from the payload itself (MAM-13), for
/// correlating the job; never the listing's claim, which the payload can contradict.</param>
public sealed record DownloadSubmission(ReleasePayload Payload, string DestinationKey, string? ExpectedInfoHash);
