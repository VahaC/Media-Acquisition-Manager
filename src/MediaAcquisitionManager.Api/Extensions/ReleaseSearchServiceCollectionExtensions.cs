using MediaAcquisitionManager.Api.Services.Releases;

namespace MediaAcquisitionManager.Api.Extensions;

public static class ReleaseSearchServiceCollectionExtensions
{
    /// <summary>The release search (MAM-10).</summary>
    public static IServiceCollection AddReleaseSearchFeatures(this IServiceCollection services)
    {
        // Scoped: it works on the request's DbContext. The adapters it asks are singletons.
        services.AddScoped<IReleaseSearchService, ReleaseSearchService>();
        return services;
    }
}
