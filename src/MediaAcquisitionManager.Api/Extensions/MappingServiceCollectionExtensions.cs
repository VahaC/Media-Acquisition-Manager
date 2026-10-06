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
        services.AddSingleton<IReleaseSearchMapper, ReleaseSearchMapper>();
        services.AddSingleton<ISearchMapper, SearchMapper>();
        services.AddSingleton<IDestinationMapper, DestinationMapper>();
        services.AddSingleton<IDownloadMapper, DownloadMapper>();
        services.AddSingleton<IRecommendationMapper, RecommendationMapper>();
        services.AddSingleton<ILibraryLinkMapper, LibraryLinkMapper>();
        services.AddSingleton<ILibraryLinkSuggestionMapper, LibraryLinkSuggestionMapper>();
        return services;
    }
}
