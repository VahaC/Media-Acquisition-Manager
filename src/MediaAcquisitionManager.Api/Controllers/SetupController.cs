using MediaAcquisitionManager.Api.Auth;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Api.Http;
using MediaAcquisitionManager.Api.RateLimiting;
using MediaAcquisitionManager.Api.Services.Auth;
using MediaAcquisitionManager.Api.Services.Setup;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace MediaAcquisitionManager.Api.Controllers;

/// <summary>The first-run setup: creates the installation owner and signs them in.</summary>
/// <remarks>
/// Anonymous as a whole: it serves a visitor before any account exists. What keeps it from being an
/// open sign-up is the service, which refuses once an owner exists.
/// </remarks>
[ApiController]
[AllowAnonymous]
[Route("api/setup")]
public class SetupController(ISetupService setup) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<SetupStatusResponse>> Get(CancellationToken cancellationToken) =>
        Ok(await setup.GetStatusAsync(cancellationToken));

    /// <summary>Rate limited like sign-in: until the owner exists, whoever reaches the port can call
    /// it, and each call costs a password hash.</summary>
    [HttpPost]
    [EnableRateLimiting(SignInRateLimitPolicy.Name)]
    public async Task<ActionResult<SignInResponse>> Complete([FromBody] SetupRequest request, CancellationToken cancellationToken)
    {
        // A missing field is a blank one: the rules answer both with the same sentence.
        var result = await setup.CompleteAsync(request.UserName ?? "", request.Password ?? "", cancellationToken);
        return result.Status switch
        {
            SetupCompletionStatus.Completed => SignedIn(result.Grant!),
            SetupCompletionStatus.Invalid => this.BadRequestError(result.Error),
            _ => this.ConflictError(result.Error),
        };
    }

    // The owner is signed in by the same response that creates them, like a sign-in answers.
    private OkObjectResult SignedIn(SessionGrant grant)
    {
        RefreshTokenCookie.Append(HttpContext, grant.RefreshToken, grant.SessionExpiresUtc);
        return Ok(grant.Response);
    }
}
