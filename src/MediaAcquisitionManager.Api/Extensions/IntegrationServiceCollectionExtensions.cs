using MediaAcquisitionManager.Api.Services.Integrations;

namespace MediaAcquisitionManager.Api.Extensions;

public static class IntegrationServiceCollectionExtensions
{
    /// <summary>The installation owner's integration settings.</summary>
    public static IServiceCollection AddIntegrationFeatures(this IServiceCollection services)
    {
        // Scoped: it works on the request's DbContext, and its audit rows land in that context's save.
        services.AddScoped<IIntegrationConnectionService, IntegrationConnectionService>();
        // Stateless apart from the singleton encryption service: one instance opens every stored secret,
        // for a connection test and a library sync alike (MAM-8).
        services.AddSingleton<IIntegrationSettingsFactory, IntegrationSettingsFactory>();
        return services;
    }
}
