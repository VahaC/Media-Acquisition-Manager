using MediaAcquisitionManager.Api.Services.Library;

namespace MediaAcquisitionManager.Api.Extensions;

public static class LibraryServiceCollectionExtensions
{
    /// <summary>The Library page and its syncs (MAM-8). The worker and the reconciler are hosted
    /// services, registered in <see cref="HostedServiceCollectionExtensions"/> with the others.</summary>
    public static IServiceCollection AddLibraryFeatures(this IServiceCollection services)
    {
        // Scoped: each works on its scope's DbContext — a request's, or the one the worker opens per run.
        services.AddScoped<ILibraryService, LibraryService>();
        services.AddScoped<ILibrarySyncService, LibrarySyncService>();
        services.AddScoped<ILibrarySyncRunner, LibrarySyncRunner>();
        services.AddScoped<ILibrarySnapshotStore, LibrarySnapshotStore>();
        // Singleton: the one queue the starts write to and the worker reads.
        services.AddSingleton<LibrarySyncQueue>();
        return services;
    }
}
