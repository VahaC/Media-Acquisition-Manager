using MediaAcquisitionManager.Core.Enums;
using Microsoft.AspNetCore.Authorization;

namespace MediaAcquisitionManager.Api.Auth;

/// <summary>
/// MAM-5 — judges <see cref="PersonalAccessTokenAccessRequirement"/> from the endpoint's metadata: what a personal
/// access token may call, deny by default (ADR 0040).
/// </summary>
/// <remarks>
/// <para>
/// A signed-in session passes untouched. For a token, in order: <see cref="DenyPersonalAccessTokenAttribute"/> refuses
/// any token; a GET or HEAD reads, as does a POST marked <see cref="PersonalAccessTokenReadAttribute"/>; a write marked
/// <see cref="PersonalAccessTokenWriteAttribute"/> is open to Read and download only; everything else — every write
/// added later included — refuses any token.
/// </para>
/// <para>
/// Fail closed: an endpoint with no HTTP-method constraint answers every method, so it counts as a write even when the
/// request is a GET; so does a request whose endpoint can't be read, and a token whose access claim names nothing. The
/// endpoint comes from the <see cref="HttpContext"/> the authorization middleware passes as the resource. Runs before
/// every MVC filter, so a token's refusal wins over <see cref="InstallationOwnerFilter"/>'s and its sentence is the
/// token's.
/// </para>
/// <para>Stateless, so a singleton.</para>
/// </remarks>
public sealed class PersonalAccessTokenAccessHandler : AuthorizationHandler<PersonalAccessTokenAccessRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, PersonalAccessTokenAccessRequirement requirement)
    {
        if (!context.User.IsPersonalAccessToken())
        {
            context.Succeed(requirement);
            return Task.CompletedTask;
        }

        var httpContext = context.Resource as HttpContext;
        var metadata = httpContext?.GetEndpoint()?.Metadata;
        if (httpContext is null || metadata is null || metadata.GetMetadata<DenyPersonalAccessTokenAttribute>() is not null)
            return Refuse(context, PersonalAccessTokenRefusalReason.NotForTokens);

        if (IsRead(httpContext.Request.Method, metadata) || metadata.GetMetadata<PersonalAccessTokenReadAttribute>() is not null)
        {
            context.Succeed(requirement);
            return Task.CompletedTask;
        }

        if (metadata.GetMetadata<PersonalAccessTokenWriteAttribute>() is null)
            return Refuse(context, PersonalAccessTokenRefusalReason.NotForTokens);
        if (context.User.GetPersonalAccessTokenAccess() != PersonalAccessTokenAccess.ReadAndDownload)
            return Refuse(context, PersonalAccessTokenRefusalReason.ReadOnly);

        context.Succeed(requirement);
        return Task.CompletedTask;
    }

    // A read only when the request is a GET or HEAD and the endpoint is limited to methods at all: routing has already
    // matched the method to the endpoint's, and one with no constraint answers POST and DELETE too.
    private static bool IsRead(string method, EndpointMetadataCollection metadata) =>
        (HttpMethods.IsGet(method) || HttpMethods.IsHead(method))
        && metadata.GetMetadata<IHttpMethodMetadata>() is { HttpMethods.Count: > 0 };

    private Task Refuse(AuthorizationHandlerContext context, PersonalAccessTokenRefusalReason reason)
    {
        context.Fail(new PersonalAccessTokenFailureReason(this, reason));
        return Task.CompletedTask;
    }
}
