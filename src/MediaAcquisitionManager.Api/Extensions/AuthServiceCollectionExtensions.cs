using MediaAcquisitionManager.Api.Auth;
using MediaAcquisitionManager.Api.Services.Auth;
using MediaAcquisitionManager.Core.Options;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace MediaAcquisitionManager.Api.Extensions;

public static class AuthServiceCollectionExtensions
{
    /// <summary>
    /// Bearer-token authentication with a per-request session check, authorization, and the services
    /// that sign users in and keep their sessions. MAM-5 — and personal access tokens beside the
    /// session's JWT: their scheme, and the default policy's check of what a token may call.
    /// </summary>
    public static IServiceCollection AddMediaAcquisitionManagerAuth(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSectionOptions<JwtOptions>(configuration);

        // MAM-5 — the default is a policy scheme that sends a `Bearer mam_pat_…` request to the token
        // handler and every other to JwtBearer, for authenticating, challenging and forbidding alike, so a
        // script uses its token the way the web app uses its access token (ADR 0040). JwtBearer keeps
        // its name ("Bearer"), so its named options below still bind.
        services.AddAuthentication(PersonalAccessTokenDefaults.PolicyScheme)
            .AddPolicyScheme(PersonalAccessTokenDefaults.PolicyScheme, displayName: null, options =>
                options.ForwardDefaultSelector = context => PersonalAccessTokenBearer.TryRead(context.Request, out _)
                    ? PersonalAccessTokenDefaults.AuthenticationScheme
                    : JwtBearerDefaults.AuthenticationScheme)
            // Built per request from the request's services, as every handler is: the token check needs
            // the request's DbContext.
            .AddScheme<AuthenticationSchemeOptions, PersonalAccessTokenAuthenticationHandler>(
                PersonalAccessTokenDefaults.AuthenticationScheme, displayName: null, configureOptions: null)
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
        services.AddAuthorization(options =>
            // MAM-5 — what a personal access token may call is part of the default policy, so every
            // plain [Authorize] enforces it, deny by default, and nothing has to remember to (ADR 0040).
            // No fallback policy: an endpoint without [Authorize] stays anonymous, as before.
            options.DefaultPolicy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .AddRequirements(new PersonalAccessTokenAccessRequirement())
                .Build());
        // Stateless: it reads the endpoint and the claims.
        services.AddSingleton<IAuthorizationHandler, PersonalAccessTokenAccessHandler>();
        // Replaces the framework's (registered with TryAdd), which it hands everything but a token's
        // refusal to; a singleton like it.
        services.AddSingleton<IAuthorizationMiddlewareResultHandler, PersonalAccessTokenRefusalResultHandler>();

        // Stateless apart from the signing key, which is the same for the whole process.
        services.AddSingleton<IAccessTokenService, AccessTokenService>();
        // One per process: it is the point of the thing that it is made once.
        services.AddSingleton<UnknownUserPasswordHash>();
        // Scoped: it works on the request's DbContext.
        services.AddScoped<ISessionService, SessionService>();

        return services;
    }
}
