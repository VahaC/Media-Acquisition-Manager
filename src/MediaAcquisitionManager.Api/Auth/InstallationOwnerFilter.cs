using MediaAcquisitionManager.Api.Http;
using MediaAcquisitionManager.Core.Texts;
using MediaAcquisitionManager.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Auth;

/// <summary>
/// Lets a request through only when the signed-in user is the installation owner; anyone else gets
/// 403 <c>{ "error": "Only the installation owner can do this.", "code": "account.owner-only" }</c>. Applied by
/// <see cref="RequireInstallationOwnerAttribute"/>, by <see cref="SettingsWritesAreOwnerOnlyConvention"/>, — MAM-34 —
/// by <see cref="LibraryLinksAreOwnerOnlyConvention"/> and — MAM-24 — by <see cref="ReplacementsAreOwnerOnlyConvention"/>.
/// </summary>
/// <remarks>
/// <para>
/// MAM-4 — an authorization filter, so it runs before model binding and validation: a non-owner is
/// refused the same way whatever the body holds, and a 400 never tells them what a valid request
/// would look like. Ownership is read from the installation row on every request rather than from
/// the token, which carries ids only, so nothing a token says can make its holder the owner.
/// </para>
/// <para>
/// A request with no signed-in user is challenged (the 401 every protected endpoint answers), never
/// refused with a 403 and never let through. Behind <c>[Authorize]</c> such a request is stopped
/// before it gets here; the challenge is for an action someone marked <c>[AllowAnonymous]</c>, where
/// skipping the check would open an owner-only action to anyone.
/// </para>
/// </remarks>
public sealed class InstallationOwnerFilter(ApplicationDbContext db, ILogger<InstallationOwnerFilter> logger) : IAsyncAuthorizationFilter
{
    /// <summary>The 403's message.</summary>
    public static readonly Message OwnerOnly = Message.Of("account.owner-only");

    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var principal = context.HttpContext.User;
        if (principal.Identity?.IsAuthenticated != true)
        {
            context.Result = new ChallengeResult();
            return;
        }

        var userId = principal.GetUserId();
        if (await db.Installations.AnyAsync(installation => installation.OwnerUserId == userId, context.HttpContext.RequestAborted))
            return;

        // Warning, so it ships off the box: the SPA offers these actions to the owner only, so a
        // refusal is someone opening a URL by hand at best and someone probing at worst.
        logger.LogWarning(
            "User {UserId} was refused {Method} {Path}: only the installation owner may do that.",
            userId,
            context.HttpContext.Request.Method,
            context.HttpContext.Request.Path);
        // The same { error, code } body ForbiddenError writes; a filter has no controller to call it on.
        context.Result = new ObjectResult(ApiErrorBody.For(context.HttpContext, OwnerOnly)) { StatusCode = StatusCodes.Status403Forbidden };
    }
}
