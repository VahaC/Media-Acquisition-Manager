using MediaAcquisitionManager.Api.Services.Setup;

namespace MediaAcquisitionManager.Api.Extensions;

public static class SetupServiceCollectionExtensions
{
    /// <summary>The first-run setup that creates the installation owner.</summary>
    public static IServiceCollection AddSetupFeatures(this IServiceCollection services)
    {
        services.AddScoped<ISetupService, SetupService>();
        return services;
    }
}
