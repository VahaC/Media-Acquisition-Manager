using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace MediaAcquisitionManager.TestInfrastructure.Http.Probes;

/// <summary>
/// A readiness check that throws instead of returning a result, the way a future vendor check
/// might, loaded by <see cref="ThrowingHealthCheckApiFactory"/> alone.
/// </summary>
public sealed class ThrowingHealthCheck : IHealthCheck
{
    public const string Name = "probe";

    /// <summary>What the check throws. It looks like what a real exception carries (a host, a file
    /// path), which is exactly what must never reach the anonymous health response.</summary>
    public const string ThrownMessage = @"Probe check failed: no answer from http://10.0.0.5:8080 using C:\probe\private\settings.json";

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException(ThrownMessage);
}
