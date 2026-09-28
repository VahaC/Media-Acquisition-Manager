using MediaAcquisitionManager.Api.Services.Accounts;

namespace MediaAcquisitionManager.Api.Extensions;

public static class AccountServiceCollectionExtensions
{
    /// <summary>The installation owner's user management.</summary>
    public static IServiceCollection AddAccountFeatures(this IServiceCollection services)
    {
        // Scoped: it works on the request's DbContext, and its audit rows and session revocations
        // have to land in that same context's save and transaction.
        services.AddScoped<IUserAccountService, UserAccountService>();
        return services;
    }
}
