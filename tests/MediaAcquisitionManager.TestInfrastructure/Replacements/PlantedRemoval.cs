using MediaAcquisitionManager.Core.Entities;

namespace MediaAcquisitionManager.TestInfrastructure.Replacements;

/// <summary>MAM-40 — a removal of a copy <see cref="CopyRemovalPlanter"/> planted, as saved.</summary>
/// <param name="Removal">The removal.</param>
/// <param name="Names">Its names, in their plan's order.</param>
public sealed record PlantedRemoval(CopyRemovalEntity Removal, IReadOnlyList<CopyRemovalNameEntity> Names);
