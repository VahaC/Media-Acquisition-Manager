using MediaAcquisitionManager.Api.Services.PersonalAccessTokens;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;

namespace MediaAcquisitionManager.Api.Auth;

/// <summary>
/// MAM-5 — answers a refusal by <see cref="PersonalAccessTokenAccessHandler"/> with 403 <c>{ "error": "…" }</c> and, for
/// a personal access token, records it (<see cref="IPersonalAccessTokenRefusals"/>); every other outcome — any other
/// 403, any 401 — goes to the framework's own handler unchanged (ADR 0040).
/// </summary>
/// <remarks>
/// <para>
/// A failed requirement otherwise becomes a forbid with an empty body, which a script can't explain to anyone. The
/// refusal is recorded before the answer is written, so its audit row exists by the time the caller reads the 403.
/// A singleton, as the framework's is; the recorder is scoped and comes from the request's services.
/// </para>
/// <para>
/// MAM-45 — a signed-in session refused on an endpoint for tokens alone (<c>/mcp</c>) gets its sentence the same way,
/// but nothing is recorded: the refusals are a token's, kept on its row, and a session has none.
/// </para>
/// </remarks>
public sealed class PersonalAccessTokenRefusalResultHandler : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _framework = new();

    public async Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy, PolicyAuthorizationResult authorizeResult)
    {
        if (!authorizeResult.Forbidden
            || authorizeResult.AuthorizationFailure?.FailureReasons.OfType<PersonalAccessTokenFailureReason>().FirstOrDefault() is not { } failure)
        {
            await _framework.HandleAsync(next, context, policy, authorizeResult);
            return;
        }

        if (context.User.GetPersonalAccessTokenId() is { } tokenId)
        {
            await context.RequestServices.GetRequiredService<IPersonalAccessTokenRefusals>().RecordAsync(
                tokenId,
                context.User.GetUserId(),
                failure.Reason,
                context.Request.Method,
                context.Request.Path,
                context.RequestAborted);
        }

        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await context.Response.WriteAsJsonAsync(new { error = failure.Message }, context.RequestAborted);
    }
}
