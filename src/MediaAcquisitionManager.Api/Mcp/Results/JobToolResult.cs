using MediaAcquisitionManager.Api.Contracts;

namespace MediaAcquisitionManager.Api.Mcp.Results;

/// <summary>MAM-45 — <c>get_job</c>'s answer: one of the caller's download jobs with its first page of files, as the
/// poller last read them.</summary>
/// <param name="Job">The job: where it stands, and why it stopped if it did.</param>
/// <param name="Files">Its first files, in the download client's order, with their sizes and progress.</param>
public sealed record JobToolResult(DownloadJobDto Job, DownloadJobFileListResponse Files);
