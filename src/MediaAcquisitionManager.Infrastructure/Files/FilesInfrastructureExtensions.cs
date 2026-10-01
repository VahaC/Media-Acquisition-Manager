using MediaAcquisitionManager.Core.Abstractions.Files;
using Microsoft.Extensions.DependencyInjection;

namespace MediaAcquisitionManager.Infrastructure.Files;

/// <summary>
/// The local file inventory behind <see cref="DependencyInjection.AddMediaAcquisitionManagerInfrastructure"/>
/// (MAM-9): the app's own read-only look at files on its mounts, and the ffprobe it reads streams with.
/// </summary>
internal static class FilesInfrastructureExtensions
{
    public static IServiceCollection AddFilesInfrastructure(this IServiceCollection services)
    {
        // Stateless: each run starts its own ffprobe, so one instance serves every inspection at once. Built
        // by hand, because the type also has the constructor that tests use to pass a stub and a short limit.
        services.AddSingleton<IMediaProbeProcess>(_ => new FfprobeProcess());
        // Stateless too: the allowed roots come with every call, read by the caller from the database, so no
        // stale copy can outlive the owner's change. One registration of the Core seam, which the API test
        // host replaces with its fake.
        services.AddSingleton<IFileInventory, LocalFileInventory>();
        return services;
    }
}
