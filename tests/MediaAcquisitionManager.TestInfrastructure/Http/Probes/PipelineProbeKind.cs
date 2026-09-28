namespace MediaAcquisitionManager.TestInfrastructure.Http.Probes;

/// <summary>An enum for <see cref="PipelineProbeInstantResponse"/>. The probe returns a non-zero
/// member, so "serialized as a number" and "serialized as a name" can't be confused.</summary>
public enum PipelineProbeKind
{
    /// <summary>The default member.</summary>
    First = 0,

    /// <summary>The member the probe returns.</summary>
    Second = 1,
}
