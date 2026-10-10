namespace MediaAcquisitionManager.Core.Replacements;

/// <summary>MAM-25 — what the pairing made of one ticked old copy (ADR 0035).</summary>
/// <param name="OldCopyId">Its row's id.</param>
/// <param name="PairedNewFileId">The one new file of its work it is paired with; <c>null</c> when none is. Set for a pair
/// that stops the job too (another edition, too short), so the owner sees which file it was.</param>
/// <param name="Problem">Why it stops the job — or, once the library's wait ran out, why it stopped it —, one readable
/// sentence; <c>null</c> when it doesn't.</param>
public sealed record ReplacementOldCopyOutcome(Guid OldCopyId, Guid? PairedNewFileId, string? Problem);
