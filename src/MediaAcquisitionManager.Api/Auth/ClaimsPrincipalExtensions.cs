using System.Security.Claims;
using Microsoft.IdentityModel.JsonWebTokens;

namespace MediaAcquisitionManager.Api.Auth;

/// <summary>
/// Reads the ids an access token carries. The claims keep their JWT names (<c>sub</c>, <c>sid</c>)
/// because the bearer handler runs with <c>MapInboundClaims = false</c>.
/// </summary>
public static class ClaimsPrincipalExtensions
{
    /// <summary>The signed-in user's id (the <c>sub</c> claim).</summary>
    /// <exception cref="InvalidOperationException">The principal carries no such claim — only
    /// possible outside <c>[Authorize]</c>.</exception>
    public static Guid GetUserId(this ClaimsPrincipal principal) =>
        GetGuid(principal, JwtRegisteredClaimNames.Sub);

    /// <summary>The id of the session the access token belongs to (the <c>sid</c> claim).</summary>
    /// <exception cref="InvalidOperationException">The principal carries no such claim — only
    /// possible outside <c>[Authorize]</c>.</exception>
    public static Guid GetSessionId(this ClaimsPrincipal principal) =>
        GetGuid(principal, JwtRegisteredClaimNames.Sid);

    // Throws rather than returning Guid.Empty: an empty id would silently match nothing and turn a
    // wiring mistake (an action missing [Authorize]) into a puzzling 404.
    private static Guid GetGuid(ClaimsPrincipal principal, string claimType) =>
        Guid.TryParse(principal.FindFirst(claimType)?.Value, out var value)
            ? value
            : throw new InvalidOperationException(
                $"The principal has no usable '{claimType}' claim. Read it only in actions behind [Authorize].");
}
