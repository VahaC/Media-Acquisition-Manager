using MediaAcquisitionManager.Api.Services.Releases;

namespace MediaAcquisitionManager.Api.Extensions;

public static class ReleaseSearchServiceCollectionExtensions
{
    /// <summary>The release search (MAM-10), and the asking and keeping it shares with the stored search
    /// (MAM-12).</summary>
    public static IServiceCollection AddReleaseSearchFeatures(this IServiceCollection services)
    {
        // Scoped: it works on the request's DbContext. The adapters it asks are singletons.
        services.AddScoped<IReleaseSearchService, ReleaseSearchService>();
        // Scoped, each on its scope's DbContext: the sources read their rows before they fan out, and the
        // store writes in a transaction of that context.
        services.AddScoped<IReleaseSources, ReleaseSources>();
        services.AddScoped<IReleaseStore, ReleaseStore>();
        return services;
    }
}
