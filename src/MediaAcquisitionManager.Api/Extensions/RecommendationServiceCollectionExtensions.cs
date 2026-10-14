using MediaAcquisitionManager.Api.Services.Catalog;
using MediaAcquisitionManager.Api.Services.Recommendations;

namespace MediaAcquisitionManager.Api.Extensions;

public static class RecommendationServiceCollectionExtensions
{
    /// <summary>MAM-33 — the metadata catalog's cache and the recommendations' computation, availability check and feed;
    /// the lists, "Not interested", the posters, the descriptions and the Profile's settings. The worker, the reconciler and the two loops
    /// are hosted services, registered in <see cref="HostedServiceCollectionExtensions"/> with the others; the catalog's
    /// adapter is Infrastructure's.</summary>
    public static IServiceCollection AddRecommendationFeatures(this IServiceCollection services)
    {
        // Scoped: each works on its scope's DbContext — a request's, or the one the worker opens per run and each loop
        // per pass.
        services.AddScoped<ICatalogConnector, CatalogConnector>();
        services.AddScoped<ICatalogStore, CatalogStore>();
        services.AddScoped<IRecommendationSettingsReader, RecommendationSettingsReader>();
        services.AddScoped<IRecommendationRunService, RecommendationRunService>();
        services.AddScoped<IRecommendationRunner, RecommendationRunner>();
        services.AddScoped<IRecommendationAvailabilityPass, RecommendationAvailabilityPass>();
        services.AddScoped<IRecommendationFeedPass, RecommendationFeedPass>();
        services.AddScoped<IRecommendationListService, RecommendationListService>();
        services.AddScoped<IRecommendationDismissalService, RecommendationDismissalService>();
        services.AddScoped<IRecommendationPosterService, RecommendationPosterService>();
        services.AddScoped<IRecommendationOverviewService, RecommendationOverviewService>();
        services.AddScoped<IRecommendationSettingsService, RecommendationSettingsService>();
        // Singleton: the one queue the triggers write to and the worker reads.
        services.AddSingleton<RecommendationRunQueue>();
        // Singleton (MAM-31): what the two loops last heard from each release source, which the lists read.
        services.AddSingleton<RecommendationSourceFailures>();
        return services;
    }
}
