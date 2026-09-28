using MediaAcquisitionManager.TestInfrastructure.Http.Probes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace MediaAcquisitionManager.TestInfrastructure.Http;

/// <summary>
/// The app plus <see cref="PipelineProbeController"/>: endpoints that exist only to push the
/// shared pipeline (the exception handler, the JSON options, the security headers) into a state no
/// production endpoint reaches on demand — and <c>SettingsProbeController</c>, a settings controller
/// only the settings convention guards. The base factory doesn't load them, so they can never
/// shadow a real route in the other suites.
/// </summary>
public sealed class PipelineProbeApiFactory : MediaAcquisitionManagerApiFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        // Controller discovery only scans the app's own assembly; this one has to be added by hand.
        builder.ConfigureTestServices(services =>
            services.AddControllers().AddApplicationPart(typeof(PipelineProbeController).Assembly));
    }
}
