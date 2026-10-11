using MediaAcquisitionManager.Core.Entities;

namespace MediaAcquisitionManager.TestInfrastructure.Replacements;

/// <summary>MAM-26 — a replacement <see cref="ReplacementQuarantinePlanter"/> planted, as saved.</summary>
/// <param name="Download">Its download job, done.</param>
/// <param name="Replacement">The replacement.</param>
/// <param name="Copies">Its ticked copies, one per video name, in order — MAM-27 — then the copies planted without a
/// name, in theirs.</param>
/// <param name="Names">Its names, in their plan's order.</param>
/// <param name="NewFiles">MAM-27 — the new files its check found, in the client's order; none unless planted.</param>
public sealed record PlantedReplacement(
    DownloadJobEntity Download,
    ReplacementJobEntity Replacement,
    IReadOnlyList<ReplacementJobFileEntity> Copies,
    IReadOnlyList<ReplacementJobNameEntity> Names,
    IReadOnlyList<ReplacementJobNewFileEntity> NewFiles);
