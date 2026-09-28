using MediaAcquisitionManager.Api.Health;

namespace MediaAcquisitionManager.Api.Extensions;

public static class HealthServiceCollectionExtensions
{
    /// <summary>The checks behind <c>/api/health/ready</c>; the endpoints are mapped by
    /// <see cref="HealthEndpointExtensions.MapMediaAcquisitionManagerHealthEndpoints"/>.</summary>
    public static IServiceCollection AddMediaAcquisitionManagerHealthChecks(this IServiceCollection services)
    {
        // Resolved from a per-run scope by the health-check service, so the scoped DbContext it
        // takes is a fresh one each time.
        services.AddHealthChecks()
            .AddCheck<DatabaseHealthCheck>(DatabaseHealthCheck.Name, tags: [HealthCheckTags.Ready]);

        return services;
    }
}
