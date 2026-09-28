namespace MediaAcquisitionManager.Api;

internal static class TimeProviderRegistration
{
    public static IServiceCollection TryAddSingletonTimeProvider(this IServiceCollection services)
    {
        if (!services.Any(s => s.ServiceType == typeof(TimeProvider)))
            services.AddSingleton<TimeProvider>(TimeProvider.System);
        return services;
    }
}
