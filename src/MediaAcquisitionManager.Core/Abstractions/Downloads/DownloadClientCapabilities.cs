using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Core.Abstractions.Downloads;

/// <summary>What a type of download client can take and report.</summary>
/// <param name="PayloadKinds">The releases it takes (specification §3.2, step 2).</param>
/// <param name="DestinationsHavePaths">Whether a destination resolves to a folder the app can check;
/// false turns automatic replacement off for it (§3.2).</param>
/// <param name="SubmissionReturnsJobId">Whether a submission answers with the job's id; false means
/// correlating by info hash or by the job list (§3.2, step 6).</param>
public sealed record DownloadClientCapabilities(
    IReadOnlyList<ReleasePayloadKind> PayloadKinds,
    bool DestinationsHavePaths,
    bool SubmissionReturnsJobId);
