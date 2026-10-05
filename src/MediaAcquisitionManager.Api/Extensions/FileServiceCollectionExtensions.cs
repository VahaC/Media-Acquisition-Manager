using MediaAcquisitionManager.Api.Services.Files;

namespace MediaAcquisitionManager.Api.Extensions;

public static class FileServiceCollectionExtensions
{
    /// <summary>The allowed roots, the inspection of the library's files (MAM-9) and the scan of the media
    /// folders (MAM-18). The workers and the reconcilers are hosted services, registered in
    /// <see cref="HostedServiceCollectionExtensions"/> with the others; the file inventory is
    /// Infrastructure's.</summary>
    public static IServiceCollection AddFileFeatures(this IServiceCollection services)
    {
        // Scoped: each works on its scope's DbContext — a request's, or the one the worker opens per run.
        services.AddScoped<IAllowedRootsService, AllowedRootsService>();
        services.AddScoped<IFileInspectionService, FileInspectionService>();
        services.AddScoped<IFileInspectionRunner, FileInspectionRunner>();
        services.AddScoped<IFolderScanService, FolderScanService>();
        services.AddScoped<IFolderScanRunner, FolderScanRunner>();
        // Singleton: the one queue the triggers write to and the worker reads.
        services.AddSingleton<FileInspectionQueue>();
        // Singleton: the one queue the owner's Scan folders writes to and the worker reads.
        services.AddSingleton<FolderScanQueue>();
        return services;
    }
}
