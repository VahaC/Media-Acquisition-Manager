using MediaAcquisitionManager.Api.Services.Files;

namespace MediaAcquisitionManager.Api.Extensions;

public static class FileServiceCollectionExtensions
{
    /// <summary>The allowed roots and the inspection of the library's files (MAM-9). The worker and the
    /// reconciler are hosted services, registered in <see cref="HostedServiceCollectionExtensions"/> with
    /// the others; the file inventory is Infrastructure's.</summary>
    public static IServiceCollection AddFileFeatures(this IServiceCollection services)
    {
        // Scoped: each works on its scope's DbContext — a request's, or the one the worker opens per run.
        services.AddScoped<IAllowedRootsService, AllowedRootsService>();
        services.AddScoped<IFileInspectionService, FileInspectionService>();
        services.AddScoped<IFileInspectionRunner, FileInspectionRunner>();
        // Singleton: the one queue the triggers write to and the worker reads.
        services.AddSingleton<FileInspectionQueue>();
        return services;
    }
}
