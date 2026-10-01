using MediaAcquisitionManager.Api.Services.Destinations;

namespace MediaAcquisitionManager.Api.Extensions;

public static class DestinationServiceCollectionExtensions
{
    /// <summary>The download destinations (MAM-11).</summary>
    public static IServiceCollection AddDestinationFeatures(this IServiceCollection services)
    {
        // Scoped: both work on the request's DbContext, and the service's audit rows land in that context's
        // save. The adapters the checker asks are singletons.
        services.AddScoped<IDestinationService, DestinationService>();
        services.AddScoped<IDestinationChecker, DestinationChecker>();
        return services;
    }
}
