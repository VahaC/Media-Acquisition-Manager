namespace MediaAcquisitionManager.Core.Abstractions.Downloads;

/// <summary>
/// MAM-24 — one job of a download client together with its files, as the whole listing of a client gives them
/// (<see cref="IDownloadClient.ListJobsWithFilesAsync"/>), for telling which job holds a library file (ADR 0031).
/// </summary>
/// <param name="Job">The job, read exactly as <see cref="IDownloadClient.FindJobsAsync"/> reads one.</param>
/// <param name="Files">
/// Its files, in the client's order, each path relative to <see cref="ClientJob.ServiceSavePath"/> and exactly as the
/// client names it (<see cref="ClientJobFile.RelativePath"/>) — or <c>null</c> when the client doesn't know them yet: a
/// magnet still fetching its metadata. Never read as "no files": such a job may still write to a path, so a caller
/// counts it as one it couldn't place.
/// </param>
public sealed record ClientJobWithFiles(ClientJob Job, IReadOnlyList<ClientJobFile>? Files);
