using MediaAcquisitionManager.Api.Services.Library;

namespace MediaAcquisitionManager.Api.Extensions;

public static class HostedServiceCollectionExtensions
{
    /// <summary>
    /// Every background service of the app, in the order they start. Called last in <c>Program</c>, so
    /// the list reads in one place.
    /// </summary>
    /// <remarks>
    /// Each is registered once as a singleton and handed to the host from there, so a test resolves the
    /// very instance the host would run and drives it by hand (the test host removes the hosted
    /// services, never the singletons).
    /// </remarks>
    public static IServiceCollection AddMediaAcquisitionManagerHostedServices(this IServiceCollection services)
    {
        // MAM-8 — hosted services start in registration order, each StartAsync finishing before the next
        // begins: the reconciler fails the syncs a stop interrupted before the worker takes new ones, so
        // it can never fail a run the worker has just started.
        services.AddSingleton<LibrarySyncReconciler>();
        services.AddHostedService(provider => provider.GetRequiredService<LibrarySyncReconciler>());
        services.AddSingleton<LibrarySyncWorker>();
        services.AddHostedService(provider => provider.GetRequiredService<LibrarySyncWorker>());
        return services;
    }
}
