namespace MediaAcquisitionManager.Core.Abstractions.Downloads;

/// <summary>What a client answered to a submission.</summary>
/// <param name="ClientJobId">The job's id, when the client gives one reliably; <c>null</c>
/// otherwise.</param>
public sealed record SubmissionReceipt(string? ClientJobId);
