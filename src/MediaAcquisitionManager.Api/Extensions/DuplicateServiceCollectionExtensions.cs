using MediaAcquisitionManager.Api.Services.Duplicates;

namespace MediaAcquisitionManager.Api.Extensions;

public static class DuplicateServiceCollectionExtensions
{
    /// <summary>
    /// MAM-20 — the Duplicates list: the library's names grouped into the four classes of the specification. Its own
    /// extension rather than a line in <see cref="LibraryServiceCollectionExtensions"/>: it reads the library, the
    /// links and the file inventory alike, and the page that confirms and dismisses its groups (MAM-21) adds here.
    /// </summary>
    public static IServiceCollection AddDuplicateFeatures(this IServiceCollection services)
    {
        // Scoped: it works on the request's DbContext.
        services.AddScoped<ILibraryDuplicateService, LibraryDuplicateService>();
        return services;
    }
}
