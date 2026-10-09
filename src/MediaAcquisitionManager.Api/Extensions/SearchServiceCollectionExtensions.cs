using MediaAcquisitionManager.Api.Services.Search;

namespace MediaAcquisitionManager.Api.Extensions;

public static class SearchServiceCollectionExtensions
{
    /// <summary>The search page (MAM-12). It asks the sources and keeps their releases through the release
    /// search's services (<see cref="ReleaseSearchServiceCollectionExtensions"/>).</summary>
    public static IServiceCollection AddSearchFeatures(this IServiceCollection services)
    {
        // Scoped: it works on the request's DbContext, and opens a scope of its own for the library's side
        // while the sources are asked through the request's.
        services.AddScoped<ISearchService, SearchService>();
        // Scoped: it reads through its scope's DbContext — the request's, or the one the search opens to
        // read the library beside the sources.
        services.AddScoped<ILibraryMatcher, LibraryMatcher>();
        // MAM-24 — scoped with the matcher it reads through: the search page's release list and the replacement's
        // dialog read and send match a release by this one rule.
        services.AddScoped<IReleaseMatchReader, ReleaseMatchReader>();
        return services;
    }
}
