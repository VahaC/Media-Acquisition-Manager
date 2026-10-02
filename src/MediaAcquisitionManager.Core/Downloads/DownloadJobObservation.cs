using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Core.Downloads;

/// <summary>What one look at a followed download's job in its client says about it (MAM-14).</summary>
/// <param name="Status">The status the job has now; the one it had when nothing moved it.</param>
/// <param name="Progress">The progress to store, from 0 to 1; <c>null</c> to keep the stored one, when the
/// client's says nothing about the download (none at all, or a re-check's).</param>
/// <param name="ClientReportsError">Whether the client reports an error for the job: a reason the job shows
/// while it keeps its status and goes on being followed (MAM-14's decision 2).</param>
public sealed record DownloadJobObservation(DownloadJobStatus Status, double? Progress, bool ClientReportsError);
