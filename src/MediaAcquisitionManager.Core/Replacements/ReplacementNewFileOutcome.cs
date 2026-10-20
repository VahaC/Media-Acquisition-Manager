using MediaAcquisitionManager.Core.Texts;

namespace MediaAcquisitionManager.Core.Replacements;

/// <summary>MAM-25 — what the pairing made of one new file (ADR 0035).</summary>
/// <param name="NewFileId">Its row's id.</param>
/// <param name="WorkId">The matched work the library's ids identify it as; <c>null</c> while none does, and for an item
/// that is no work (an extra).</param>
/// <param name="Problem">Why it stops the job — or, once the library's wait ran out, why it stopped it —, as a message the
/// Api renders (ADR 0042); <c>null</c> when it doesn't.</param>
public sealed record ReplacementNewFileOutcome(Guid NewFileId, Guid? WorkId, Message? Problem);
