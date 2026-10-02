using MediaAcquisitionManager.Api.Services.Downloads;

namespace MediaAcquisitionManager.Api.Extensions;

public static class DownloadServiceCollectionExtensions
{
    /// <summary>Sending a release to a download client (MAM-13).</summary>
    public static IServiceCollection AddDownloadFeatures(this IServiceCollection services)
    {
        // Scoped: both work on the request's DbContext, and the service's job and audit row land in that
        // context's save. The adapters they call are singletons.
        services.AddScoped<IDownloadService, DownloadService>();
        services.AddScoped<IDownloadSender, DownloadSender>();
        return services;
    }
}
