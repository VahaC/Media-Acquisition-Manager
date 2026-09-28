using MediaAcquisitionManager.Api.Health;
using MediaAcquisitionManager.TestInfrastructure.Http.Probes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace MediaAcquisitionManager.TestInfrastructure.Http;

/// <summary>
/// The app plus <see cref="ThrowingHealthCheck"/> among the readiness checks. A factory of its own,
/// so the other suites keep a readiness endpoint that is healthy.
/// </summary>
public sealed class ThrowingHealthCheckApiFactory : MediaAcquisitionManagerApiFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureTestServices(services => services.AddHealthChecks()
            .AddCheck<ThrowingHealthCheck>(ThrowingHealthCheck.Name, tags: [HealthCheckTags.Ready]));
    }
}
