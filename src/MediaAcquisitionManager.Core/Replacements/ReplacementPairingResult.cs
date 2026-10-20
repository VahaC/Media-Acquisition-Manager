using MediaAcquisitionManager.Core.Texts;

namespace MediaAcquisitionManager.Core.Replacements;

/// <summary>MAM-25 — checks 4 and 3 of a replacement's new files, and the pairs (ADR 0032, ADR 0035).</summary>
/// <param name="Verdict">Verified, not yet, or the owner's review.</param>
/// <param name="Reason">The job's reason, as a message the Api renders (ADR 0042): the first problem, the ticked copies' by
/// their place first, then the new files' by theirs; <c>null</c> unless <see cref="ReplacementPairingVerdict.Review"/>.</param>
/// <param name="NewFiles">Each new file's work or problem, in the order given.</param>
/// <param name="OldCopies">Each ticked copy's pair or problem, in the order given.</param>
public sealed record ReplacementPairingResult(
    ReplacementPairingVerdict Verdict,
    Message? Reason,
    IReadOnlyList<ReplacementNewFileOutcome> NewFiles,
    IReadOnlyList<ReplacementOldCopyOutcome> OldCopies);
