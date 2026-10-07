namespace MediaAcquisitionManager.Core.Duplicates;

/// <summary>MAM-20 — a library work holding a file name, and the version it holds it through.</summary>
/// <param name="WorkId">The work.</param>
/// <param name="VersionId">The work's version the name is a file of.</param>
public sealed record DuplicateHolder(Guid WorkId, Guid VersionId);
