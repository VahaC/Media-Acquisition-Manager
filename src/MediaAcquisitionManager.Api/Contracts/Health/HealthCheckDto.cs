using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>One check's outcome inside a <see cref="HealthResponse"/>.</summary>
/// <param name="Name">The check's registration name, e.g. <c>database</c>.</param>
/// <param name="Status">Its outcome.</param>
/// <param name="Description">A readable sentence the UI shows as is. Never exception text: these
/// endpoints are anonymous, and an exception message can carry paths and connection details.</param>
public sealed record HealthCheckDto(string Name, HealthStatus Status, string? Description);
