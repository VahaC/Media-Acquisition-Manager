namespace MediaAcquisitionManager.Api.Services.Releases;

/// <summary>One source's answer as <see cref="IReleaseStore.StoreAsync"/> stored it (MAM-10).</summary>
/// <param name="Outcome">What the source answered, with the name it was stored under: the integration's
/// name as it is now.</param>
/// <param name="ReleaseIds">The ids of the rows its hits were stored as, in its hits' order.</param>
public sealed record StoredReleaseSource(ReleaseSourceOutcome Outcome, IReadOnlyList<Guid> ReleaseIds);
