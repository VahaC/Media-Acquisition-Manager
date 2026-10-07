using MediaAcquisitionManager.Api.Auth;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Api.Http;
using MediaAcquisitionManager.Api.Services.Duplicates;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MediaAcquisitionManager.Api.Controllers.Library;

/// <summary>MAM-20 — the library's duplicates, grouped into the four classes of specification §3.3.</summary>
/// <remarks>
/// For every signed-in user, like the Library it reads (ADR 0028): it names no address, key or account. The sentences
/// that quote the owner's paths, and the name of who set a link, only the owner gets. Read only — confirming, dismissing
/// and keeping a copy are the Duplicates page's (MAM-21), and nothing here removes a file.
/// </remarks>
[ApiController]
[Authorize]
[Route("api/library/duplicates")]
public class LibraryDuplicatesController(ILibraryDuplicateService duplicates) : ControllerBase
{
    private Guid UserId => User.GetUserId();

    /// <summary>One page of duplicate groups, of one class or all, worked out from what the app holds now.</summary>
    [HttpGet]
    public async Task<ActionResult<LibraryDuplicateListResponse>> List([FromQuery] LibraryDuplicateListRequest request, CancellationToken cancellationToken)
    {
        var result = await duplicates.ListAsync(request.Class, request.Page, request.PageSize, UserId, cancellationToken);
        return result.Status switch
        {
            LibraryDuplicateListStatus.Listed => Ok(result.List),
            _ => this.BadRequestError(result.Error),
        };
    }
}
