using Microsoft.AspNetCore.Diagnostics.HealthChecks;

namespace MediaAcquisitionManager.Api.Health;

public static class HealthEndpointExtensions
{
    /// <summary>Maps <c>/api/health</c> (liveness) and <c>/api/health/ready</c> (readiness). Both are
    /// anonymous: a container runtime or a status page probes them without credentials.</summary>
    public static IEndpointRouteBuilder MapMediaAcquisitionManagerHealthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // Liveness is "the process answers HTTP" and runs no checks: a broken database is not
        // something a restart fixes, so it must not make a supervisor restart the process in a loop.
        endpoints.MapHealthChecks("/api/health", new HealthCheckOptions
        {
            Predicate = _ => false,
            ResponseWriter = HealthResponseWriter.WriteAsync,
        });

        // Readiness runs every check tagged "ready"; any Unhealthy one turns the answer into 503.
        endpoints.MapHealthChecks("/api/health/ready", new HealthCheckOptions
        {
            Predicate = registration => registration.Tags.Contains(HealthCheckTags.Ready),
            ResponseWriter = HealthResponseWriter.WriteAsync,
        });

        return endpoints;
    }
}
