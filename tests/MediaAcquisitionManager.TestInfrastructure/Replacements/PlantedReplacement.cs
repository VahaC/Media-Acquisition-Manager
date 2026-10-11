using MediaAcquisitionManager.Core.Entities;

namespace MediaAcquisitionManager.TestInfrastructure.Replacements;

/// <summary>MAM-26 — a replacement <see cref="ReplacementQuarantinePlanter"/> planted, as saved.</summary>
/// <param name="Download">Its download job, done.</param>
/// <param name="Replacement">The replacement.</param>
/// <param name="Copies">Its ticked copies, one per video name, in order.</param>
/// <param name="Names">Its names, in their plan's order.</param>
public sealed record PlantedReplacement(
    DownloadJobEntity Download,
    ReplacementJobEntity Replacement,
    IReadOnlyList<ReplacementJobFileEntity> Copies,
    IReadOnlyList<ReplacementJobNameEntity> Names);
