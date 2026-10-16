using System.Security.Claims;
using MediaAcquisitionManager.Core.Enums;
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

    /// <summary>MAM-5 — whether the request came in on a personal access token rather than a signed-in session.</summary>
    public static bool IsPersonalAccessToken(this ClaimsPrincipal principal) =>
        principal.GetPersonalAccessTokenId() is not null;

    /// <summary>MAM-5 — the id of the personal access token the request came in on; <c>null</c> for a signed-in
    /// session.</summary>
    public static Guid? GetPersonalAccessTokenId(this ClaimsPrincipal principal) =>
        Guid.TryParse(principal.FindFirst(PersonalAccessTokenClaims.TokenId)?.Value, out var tokenId) ? tokenId : null;

    /// <summary>MAM-5 — what the personal access token the request came in on may do; <c>null</c> for a signed-in
    /// session, and for a claim that names no access, which the access check then treats as the least.</summary>
    public static PersonalAccessTokenAccess? GetPersonalAccessTokenAccess(this ClaimsPrincipal principal) =>
        Enum.TryParse<PersonalAccessTokenAccess>(principal.FindFirst(PersonalAccessTokenClaims.Access)?.Value, ignoreCase: false, out var access)
            && Enum.IsDefined(access)
            ? access
            : null;

    // Throws rather than returning Guid.Empty: an empty id would silently match nothing and turn a
    // wiring mistake (an action missing [Authorize]) into a puzzling 404.
    private static Guid GetGuid(ClaimsPrincipal principal, string claimType) =>
        Guid.TryParse(principal.FindFirst(claimType)?.Value, out var value)
            ? value
            : throw new InvalidOperationException(
                $"The principal has no usable '{claimType}' claim. Read it only in actions behind [Authorize].");
}
