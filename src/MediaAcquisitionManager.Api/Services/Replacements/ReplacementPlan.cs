using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Api.Services.Replacements;

/// <summary>
/// MAM-24 — what a scheduled replacement writes with its download job, in its one save (ADR 0013, ADR 0032): the edition
/// the release claims, each ticked copy as it is on the disk now, and each matched work a ticked copy belongs to.
/// </summary>
/// <param name="ReleaseEditions">The edition words the release's title claims, as the dialog showed them.</param>
/// <param name="Files">The ticked copies, one per file, in the dialog's order.</param>
/// <param name="Works">The matched works those copies are listed under, in the dialog's order.</param>
public sealed record ReplacementPlan(
    IReadOnlyList<MediaEdition> ReleaseEditions,
    IReadOnlyList<ReplacementPlanFile> Files,
    IReadOnlyList<ReplacementPlanWork> Works);
