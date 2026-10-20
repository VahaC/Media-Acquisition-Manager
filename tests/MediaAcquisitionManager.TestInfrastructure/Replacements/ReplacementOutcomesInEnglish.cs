using MediaAcquisitionManager.Core.Replacements;
using MediaAcquisitionManager.TestInfrastructure.Texts;

namespace MediaAcquisitionManager.TestInfrastructure.Replacements;

/// <summary>
/// MAM-47 (ADR 0042) — the pairing's outcomes with their problems in English, in their order:
/// <c>Assert.Equal([new EnglishOldCopyOutcome(copy.Id, null, reason)], result.OldCopies.InEnglish())</c>. Strict, so a
/// problem whose message misses a value or a key fails the test that reached it.
/// </summary>
public static class ReplacementOutcomesInEnglish
{
    /// <summary>Each ticked copy's outcome, its problem in English.</summary>
    public static IReadOnlyList<EnglishOldCopyOutcome> InEnglish(this IEnumerable<ReplacementOldCopyOutcome> outcomes) =>
        [.. outcomes.Select(outcome => new EnglishOldCopyOutcome(
            outcome.OldCopyId, outcome.PairedNewFileId, TestTexts.En(outcome.Problem), outcome.OnlyDropSettles))];

    /// <summary>Each new file's outcome, its problem in English.</summary>
    public static IReadOnlyList<EnglishNewFileOutcome> InEnglish(this IEnumerable<ReplacementNewFileOutcome> outcomes) =>
        [.. outcomes.Select(outcome => new EnglishNewFileOutcome(outcome.NewFileId, outcome.WorkId, TestTexts.En(outcome.Problem)))];
}
