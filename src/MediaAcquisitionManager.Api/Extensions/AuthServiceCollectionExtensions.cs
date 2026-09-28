using MediaAcquisitionManager.Api.Auth;
using MediaAcquisitionManager.Api.Services.Auth;
using MediaAcquisitionManager.Core.Options;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;

namespace MediaAcquisitionManager.Api.Extensions;

public static class AuthServiceCollectionExtensions
{
    /// <summary>
    /// Bearer-token authentication with a per-request session check, authorization, and the services
    /// that sign users in and keep their sessions.
    /// </summary>
    public static IServiceCollection AddMediaAcquisitionManagerAuth(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSectionOptions<JwtOptions>(configuration);

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                // Claims keep their JWT names (sub, sid, name) instead of being renamed to the long
                // WS-Federation URIs, so what the token service writes is what the app reads.
                options.MapInboundClaims = false;
                // Resolved from the request's scope: the session check needs the request's DbContext.
                options.EventsType = typeof(SessionCheckingJwtBearerEvents);
            });
        // Configured from the container rather than inside AddJwtBearer: the key comes from options
        // (filled from the first-run secrets), and the lifetime check needs the TimeProvider the test
        // host replaces. Nothing reads the secret before the first request needs it, so `dotnet ef`,
        // which may build the host without any secrets, never trips over a missing one.
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtOptions>, TimeProvider>((options, jwt, time) =>
                options.TokenValidationParameters = JwtAccessTokens.CreateValidationParameters(jwt.Value, time));
        services.AddScoped<SessionCheckingJwtBearerEvents>();
        services.AddAuthorization();

        // Stateless apart from the signing key, which is the same for the whole process.
        services.AddSingleton<IAccessTokenService, AccessTokenService>();
        // One per process: it is the point of the thing that it is made once.
        services.AddSingleton<UnknownUserPasswordHash>();
        // Scoped: it works on the request's DbContext.
        services.AddScoped<ISessionService, SessionService>();

        return services;
    }
}
