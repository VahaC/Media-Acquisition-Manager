using MediaAcquisitionManager.Api.Mapping;

namespace MediaAcquisitionManager.Api.Extensions;

public static class MappingServiceCollectionExtensions
{
    /// <summary>The entity-to-contract mappers. Stateless, so singletons.</summary>
    public static IServiceCollection AddMediaAcquisitionManagerMappers(this IServiceCollection services)
    {
        services.AddSingleton<IUserMapper, UserMapper>();
        services.AddSingleton<IIntegrationMapper, IntegrationMapper>();
        services.AddSingleton<ILibraryMapper, LibraryMapper>();
        // MAM-20 — the Duplicates list's groups, through the Library's own file and ids mapping.
        services.AddSingleton<ILibraryDuplicateMapper, LibraryDuplicateMapper>();
        services.AddSingleton<IReleaseSearchMapper, ReleaseSearchMapper>();
        services.AddSingleton<ISearchMapper, SearchMapper>();
        services.AddSingleton<IDestinationMapper, DestinationMapper>();
        services.AddSingleton<IDownloadMapper, DownloadMapper>();
        services.AddSingleton<IRecommendationMapper, RecommendationMapper>();
        services.AddSingleton<ILibraryLinkMapper, LibraryLinkMapper>();
        services.AddSingleton<ILibraryLinkSuggestionMapper, LibraryLinkSuggestionMapper>();
        // MAM-24 — the Download dialog's answer about old copies; their files come mapped by the Library's own mapper.
        services.AddSingleton<IReplacementMapper, ReplacementMapper>();
        // MAM-40 — the Removed copies list's removals, their names in a replacement's shape.
        services.AddSingleton<ICopyRemovalMapper, CopyRemovalMapper>();
        return services;
    }
}
