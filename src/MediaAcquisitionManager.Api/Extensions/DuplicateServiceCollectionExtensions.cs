using MediaAcquisitionManager.Api.Services.Duplicates;

namespace MediaAcquisitionManager.Api.Extensions;

public static class DuplicateServiceCollectionExtensions
{
    /// <summary>
    /// MAM-20 — the Duplicates list: the library's names grouped into the four classes of the specification. Its own
    /// extension rather than a line in <see cref="LibraryServiceCollectionExtensions"/>: it reads the library, the
    /// links and the file inventory alike. MAM-21 — and the owner's Confirm, Dismiss and Undo of its possible matches.
    /// </summary>
    public static IServiceCollection AddDuplicateFeatures(this IServiceCollection services)
    {
        // Scoped: it works on the request's DbContext.
        services.AddScoped<ILibraryDuplicateService, LibraryDuplicateService>();
        // Scoped: it saves on the request's DbContext, with the audit row the request's trail adds to it.
        services.AddScoped<IDuplicateDecisionService, DuplicateDecisionService>();
        return services;
    }
}
