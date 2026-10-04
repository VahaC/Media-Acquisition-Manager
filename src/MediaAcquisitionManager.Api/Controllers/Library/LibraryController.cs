using MediaAcquisitionManager.Api.Auth;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Api.Http;
using MediaAcquisitionManager.Api.Services.Library;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MediaAcquisitionManager.Api.Controllers.Library;

/// <summary>The Library page's works and the libraries to filter them by (MAM-8).</summary>
/// <remarks>
/// For every signed-in user: the library is what the household owns, and nothing here names an address,
/// a key or an account. Why a file has no app path quotes the owner's path mappings, so only the owner
/// gets it. The syncs that fill it are <see cref="LibrarySyncController"/>'s.
/// </remarks>
[ApiController]
[Authorize]
[Route("api/library")]
public class LibraryController(ILibraryService library) : ControllerBase
{
    private Guid UserId => User.GetUserId();

    /// <summary>One page of works, searched, filtered and ordered by the server.</summary>
    [HttpGet("works")]
    public async Task<ActionResult<LibraryWorkListResponse>> Works([FromQuery] LibraryWorkListRequest request, CancellationToken cancellationToken)
    {
        var result = await library.ListWorksAsync(
            request.Search, request.LocationId, request.Identifiers, request.Kind, request.Page, request.PageSize, UserId, cancellationToken);
        return result.Status switch
        {
            LibraryWorkListStatus.Listed => Ok(result.List),
            _ => this.BadRequestError(result.Error),
        };
    }

    /// <summary>
    /// MAM-32 — one page of a series' episodes, a season at a time, searched and filtered as the works
    /// are. A bare 404 when no series has the id: an episode or a movie has no episodes to list.
    /// </summary>
    [HttpGet("works/{id:guid}/episodes")]
    public async Task<ActionResult<LibraryWorkListResponse>> Episodes(Guid id, [FromQuery] LibraryEpisodeListRequest request, CancellationToken cancellationToken)
    {
        var result = await library.ListEpisodesAsync(
            id, request.Season, request.Search, request.LocationId, request.Identifiers, request.Page, request.PageSize, UserId, cancellationToken);
        return result.Status switch
        {
            LibraryWorkListStatus.Listed => Ok(result.List),
            LibraryWorkListStatus.NotFound => NotFound(),
            _ => this.BadRequestError(result.Error),
        };
    }

    /// <summary>The libraries that hold a file; a server has a handful, so never paged.</summary>
    [HttpGet("locations")]
    public async Task<ActionResult<LibraryLocationListResponse>> Locations(CancellationToken cancellationToken) =>
        Ok(await library.ListLocationsAsync(cancellationToken));
}
