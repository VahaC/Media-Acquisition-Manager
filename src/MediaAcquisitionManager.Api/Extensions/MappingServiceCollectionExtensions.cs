using MediaAcquisitionManager.Api.Mapping;

namespace MediaAcquisitionManager.Api.Extensions;

public static class MappingServiceCollectionExtensions
{
    /// <summary>The entity-to-contract mappers. Stateless, so singletons.</summary>
    public static IServiceCollection AddMediaAcquisitionManagerMappers(this IServiceCollection services)
    {
        services.AddSingleton<IUserMapper, UserMapper>();
        return services;
    }
}
