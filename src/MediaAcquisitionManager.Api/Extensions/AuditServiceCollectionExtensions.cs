using MediaAcquisitionManager.Api.Services.Audit;

namespace MediaAcquisitionManager.Api.Extensions;

public static class AuditServiceCollectionExtensions
{
    /// <summary>The audit trail every feature records its actions into.</summary>
    public static IServiceCollection AddMediaAcquisitionManagerAudit(this IServiceCollection services)
    {
        // Scoped: it adds its rows to the request's DbContext, so they commit with the request's save.
        services.AddScoped<IAuditTrail, AuditTrail>();
        return services;
    }
}
