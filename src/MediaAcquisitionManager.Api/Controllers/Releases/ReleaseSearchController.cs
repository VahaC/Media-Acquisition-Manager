using MediaAcquisitionManager.Api.Auth;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Api.Http;
using MediaAcquisitionManager.Api.Services.Releases;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace MediaAcquisitionManager.Api.Controllers.Releases;

/// <summary>Searching the release sources (MAM-10).</summary>
/// <remarks>
/// For every signed-in user (Vasyl's choice): a search changes nothing the household owns, as browsing
/// the Library doesn't. Outside <c>Controllers/Library</c> and <c>Controllers/Settings</c> on purpose:
/// both make every write owner-only, and this POST is a search. Why a source failed can name its
/// address, so only the owner reads it.
/// </remarks>
[ApiController]
[Authorize]
[Route("api/releases")]
public class ReleaseSearchController(IReleaseSearchService releases) : ControllerBase
{
    private Guid UserId => User.GetUserId();

    /// <summary>
    /// Searches every enabled release source at once. 200 whatever the sources answered — each says how it
    /// went beside its releases, so a failing one never hides the others (ADR 0005's rule for a connection
    /// test, applied per source); 400 only for a search the app doesn't take. A POST, not a GET: every
    /// release found is stored, so a refetch must not repeat it.
    /// </summary>
    [HttpPost("search")]
    public async Task<ActionResult<ReleaseSearchResponse>> Search(
        // An empty body reaches the rules' "enter the title" sentence instead of a validation body.
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] ReleaseSearchRequest? request,
        CancellationToken cancellationToken)
    {
        var result = await releases.SearchAsync(request?.Text, request?.Year, request?.ImdbId, request?.TmdbId, UserId, cancellationToken);
        return result.Status switch
        {
            ReleaseSearchStatus.Searched => Ok(result.Response),
            _ => this.BadRequestError(result.Error),
        };
    }
}
