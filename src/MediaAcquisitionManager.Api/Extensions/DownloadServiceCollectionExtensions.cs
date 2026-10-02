using MediaAcquisitionManager.Api.Services.Downloads;

namespace MediaAcquisitionManager.Api.Extensions;

public static class DownloadServiceCollectionExtensions
{
    /// <summary>Sending a release to a download client (MAM-13), and following it there until it finishes
    /// (MAM-14).</summary>
    public static IServiceCollection AddDownloadFeatures(this IServiceCollection services)
    {
        // Scoped: both work on the request's DbContext, and the service's job and audit row land in that
        // context's save. The adapters they call are singletons.
        services.AddScoped<IDownloadService, DownloadService>();
        services.AddScoped<IDownloadSender, DownloadSender>();
        // MAM-14 — scoped like the runners: one pass, one DbContext, opened by the tracker (a singleton, in
        // AddMediaAcquisitionManagerHostedServices) for each pass.
        services.AddScoped<IDownloadJobPoller, DownloadJobPoller>();
        return services;
    }
}
