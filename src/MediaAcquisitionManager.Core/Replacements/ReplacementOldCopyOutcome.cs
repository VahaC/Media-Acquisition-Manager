using MediaAcquisitionManager.Core.Texts;

namespace MediaAcquisitionManager.Core.Replacements;

/// <summary>MAM-25 — what the pairing made of one ticked old copy (ADR 0035).</summary>
/// <param name="OldCopyId">Its row's id.</param>
/// <param name="PairedNewFileId">The one new file of its work it is paired with; <c>null</c> when none is. Set for a pair
/// that stops the job too (another edition, too short), so the owner sees which file it was.</param>
/// <param name="Problem">Why it stops the job — or, once the library's wait ran out, why it stopped it —, as a message the
/// Api renders (ADR 0042); <c>null</c> when it doesn't.</param>
/// <param name="OnlyDropSettles">MAM-27 — whether <paramref name="Problem"/> is one no fix in the library or the client can
/// change, so only dropping the copy, or keeping both copies, settles it and Retry is withheld; <c>false</c> for every other
/// problem, for a sentence of what is still awaited, and when there is no problem.</param>
public sealed record ReplacementOldCopyOutcome(Guid OldCopyId, Guid? PairedNewFileId, Message? Problem, bool OnlyDropSettles = false);
