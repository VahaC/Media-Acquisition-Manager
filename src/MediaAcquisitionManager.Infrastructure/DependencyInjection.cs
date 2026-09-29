using MediaAcquisitionManager.Core.Storage;
using MediaAcquisitionManager.Infrastructure.Integrations;
using MediaAcquisitionManager.Infrastructure.Persistence;
using MediaAcquisitionManager.Infrastructure.Security;
using Microsoft.Extensions.DependencyInjection;

namespace MediaAcquisitionManager.Infrastructure;

/// <summary>
/// What the Infrastructure project contributes to the container. Each area keeps its registrations
/// in an internal <c>&lt;Area&gt;InfrastructureExtensions</c> file beside its code; this class is the
/// only public way in.
/// </summary>
public static class DependencyInjection
{
    /// <summary>Everything Infrastructure provides, persistence included.</summary>
    public static IServiceCollection AddMediaAcquisitionManagerInfrastructure(
        this IServiceCollection services, StorageLayout storage)
    {
        // One resolved layout for the whole process: whatever needs a path asks for this instead of
        // resolving configuration again against a possibly different base directory.
        services.AddSingleton(storage);
        services.AddMediaAcquisitionManagerPersistence(storage);
        services.AddSecurityInfrastructure();
        services.AddIntegrationsInfrastructure();
        return services;
    }

    /// <summary>
    /// The <see cref="ApplicationDbContext"/> over <see cref="StorageLayout.DatabasePath"/>, with the
    /// SQLite pragmas applied on every open and every registered save interceptor attached.
    /// </summary>
    public static IServiceCollection AddMediaAcquisitionManagerPersistence(
        this IServiceCollection services, StorageLayout storage) =>
        services.AddPersistenceInfrastructure(storage.DatabasePath);
}
