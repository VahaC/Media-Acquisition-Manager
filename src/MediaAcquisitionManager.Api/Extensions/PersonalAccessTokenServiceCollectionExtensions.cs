using MediaAcquisitionManager.Api.Services.PersonalAccessTokens;

namespace MediaAcquisitionManager.Api.Extensions;

public static class PersonalAccessTokenServiceCollectionExtensions
{
    /// <summary>MAM-5 — users' personal access tokens: managing them, and recording their refusals. The scheme that lets a
    /// token in and the policy that judges it are part of the auth module (ADR 0040).</summary>
    public static IServiceCollection AddPersonalAccessTokenFeatures(this IServiceCollection services)
    {
        // Scoped: it works on the request's DbContext, and a revoke at the owner's hand has to land in the account
        // service's own save and transaction.
        services.AddScoped<IPersonalAccessTokenService, PersonalAccessTokenService>();
        // Scoped, for the request's DbContext; resolved per refusal by the authorization result handler, a singleton.
        services.AddScoped<IPersonalAccessTokenRefusals, PersonalAccessTokenRefusals>();
        return services;
    }
}
