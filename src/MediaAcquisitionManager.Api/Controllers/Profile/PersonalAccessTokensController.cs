using MediaAcquisitionManager.Api.Auth;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Api.Http;
using MediaAcquisitionManager.Api.Services.PersonalAccessTokens;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace MediaAcquisitionManager.Api.Controllers.Profile;

/// <summary>MAM-5 — the signed-in user's own personal access tokens, behind the Access tokens card of their Profile page:
/// list, create, revoke (ADR 0040).</summary>
/// <remarks>
/// <para>
/// Every signed-in user's, each their own: by the session, never by a user id in the URL, and another user's token
/// answers the same bare 404 as one that never was. Beside <see cref="ProfileController"/>, under <c>api/profile</c>, for
/// its reasons: outside <c>Controllers/Settings</c>, whose writes are the owner's, and outside <c>api/auth</c>, whose
/// path the refresh cookie is sent to.
/// </para>
/// <para>
/// Refused to personal access tokens as a whole, the list included: a token can't make, find or revoke tokens, so one in
/// the wrong hands can't give itself a sibling that outlives it.
/// </para>
/// </remarks>
[ApiController]
[Authorize]
[DenyPersonalAccessToken]
[Route("api/profile/tokens")]
public class PersonalAccessTokensController(IPersonalAccessTokenService personalAccessTokens) : ControllerBase
{
    private Guid UserId => User.GetUserId();

    /// <summary>The caller's tokens not revoked, the expired ones included and flagged, newest first. Never a secret.</summary>
    [HttpGet]
    public async Task<ActionResult<PersonalAccessTokenListResponse>> List(CancellationToken cancellationToken) =>
        Ok(await personalAccessTokens.ListAsync(UserId, cancellationToken));

    /// <summary>
    /// Creates a token for the caller: 201 with the token and its secret, the one time the secret is given out; 400 with
    /// the sentence for a field missing or too long, or a body that isn't a token; the session check's own 401 when the
    /// caller's session ended while the request was under way. A POST: every call makes a new token.
    /// </summary>
    [HttpPost]
    [UnreadableBodyError(PersonalAccessTokenService.UnreadableBodyError)]
    public async Task<ActionResult<CreatedPersonalAccessTokenResponse>> Create(
        // An empty body reaches the service's "Give the token a name" instead of a validation body.
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] CreatePersonalAccessTokenRequest? request,
        CancellationToken cancellationToken)
    {
        // MAM-5 — the session the request came in on, which the service checks again where it stores the token: a token
        // never gets here ([DenyPersonalAccessToken]), so a sid is always there.
        var result = await personalAccessTokens.CreateAsync(UserId, User.GetSessionId(), request, cancellationToken);
        return result.Status switch
        {
            // The list is where a token lives; there is no read of one token to point at.
            PersonalAccessTokenCreateStatus.Created => CreatedAtAction(nameof(List), null, result.Created),
            // Ended by a reset, disable or delete that committed meanwhile: the challenge an ended session gets on its next
            // request, the same bytes (ADR 0002), so the web app treats it as it treats any ended session.
            PersonalAccessTokenCreateStatus.SessionEnded => Challenge(),
            _ => this.BadRequestError(result.Error!),
        };
    }

    /// <summary>Revokes the caller's token: 204, and it stops working with its next request; a bare 404 for another
    /// user's token, one already revoked and one that never was.</summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Revoke(Guid id, CancellationToken cancellationToken) =>
        await personalAccessTokens.RevokeAsync(UserId, id, cancellationToken) switch
        {
            PersonalAccessTokenRevokeStatus.Revoked => NoContent(),
            _ => NotFound(),
        };
}
