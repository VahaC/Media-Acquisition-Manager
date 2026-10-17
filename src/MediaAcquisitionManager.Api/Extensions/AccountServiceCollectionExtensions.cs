using MediaAcquisitionManager.Api.Services.Accounts;

namespace MediaAcquisitionManager.Api.Extensions;

public static class AccountServiceCollectionExtensions
{
    /// <summary>The installation owner's user management, and — MAM-46 — every user's own language.</summary>
    public static IServiceCollection AddAccountFeatures(this IServiceCollection services)
    {
        // Scoped: it works on the request's DbContext, and its audit rows and session revocations
        // have to land in that same context's save and transaction.
        services.AddScoped<IUserAccountService, UserAccountService>();
        // MAM-46 — every user's own language for the web app, from their Profile.
        services.AddScoped<IUserLanguageService, UserLanguageService>();
        return services;
    }
}
