namespace MediaAcquisitionManager.TestInfrastructure.Http.Probes;

/// <summary>What <see cref="PipelineProbeController.Instant"/> returns.</summary>
/// <param name="InstantUtc">An instant with <see cref="DateTimeKind.Unspecified"/>; it must leave with a trailing Z.</param>
/// <param name="Kind">An enum; it must leave as its name.</param>
public sealed record PipelineProbeInstantResponse(DateTime InstantUtc, PipelineProbeKind Kind);
