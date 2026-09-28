using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>The body of <c>/api/health</c> and <c>/api/health/ready</c>.</summary>
/// <param name="Status">The worst status among the checks that ran; <c>Healthy</c> when none ran.</param>
/// <param name="Checks">One entry per check that ran, in registration order.</param>
public sealed record HealthResponse(HealthStatus Status, IReadOnlyList<HealthCheckDto> Checks);
