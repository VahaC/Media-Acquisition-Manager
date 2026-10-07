using MediaAcquisitionManager.Api.Auth;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Api.Http;
using MediaAcquisitionManager.Api.Services.Library;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MediaAcquisitionManager.Api.Controllers.Library;

/// <summary>The Library page's works, their posters (MAM-35) and the libraries to filter them by (MAM-8).</summary>
/// <remarks>
/// For every signed-in user: the library is what the household owns, and nothing here names an address,
/// a key or an account. Why a file has no app path quotes the owner's path mappings, so only the owner
/// gets it. The syncs that fill it are <see cref="LibrarySyncController"/>'s.
/// </remarks>
[ApiController]
[Authorize]
[Route("api/library")]
public class LibraryController(ILibraryService library, ILibraryPosterService posters) : ControllerBase
{
    /// <summary>
    /// MAM-35 — how long the browser may keep a poster without asking again: 30 days, <c>immutable</c> — the file name
    /// carries the tag the library gives one version of the image, so a new picture is a new URL — and <c>private</c>, as
    /// the answer needs the user's token. The recommendations' posters' rule (MAM-33).
    /// </summary>
    private const string PosterCacheControl = "private, max-age=2592000, immutable";

    private Guid UserId => User.GetUserId();

    /// <summary>One page of works, searched, filtered and ordered by the server.</summary>
    [HttpGet("works")]
    public async Task<ActionResult<LibraryWorkListResponse>> Works([FromQuery] LibraryWorkListRequest request, CancellationToken cancellationToken)
    {
        var result = await library.ListWorksAsync(
            request.Search, request.LocationId, request.Identifiers, request.Kind, request.Sort, request.Page, request.PageSize, UserId, cancellationToken);
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

    /// <summary>
    /// MAM-35 — a work's poster, as the list's <c>posterUrl</c> names it: the library's own image, from the app's cache or
    /// fetched from the library on the first request; a bare 404 for a name no library reported, as for a missing row, and
    /// for a poster the library doesn't have; 502 when the library couldn't send it. Signed in: the SPA fetches it with the
    /// token and shows it as a <c>data:</c> URL, so the CSP stays <c>img-src 'self' data:</c> and no endpoint answers
    /// anonymously. A GET outside <c>Controllers/Library/Links</c>, so every signed-in user may read it, as the works.
    /// </summary>
    [HttpGet("posters/{fileName}")]
    public async Task<IActionResult> Poster(string fileName, CancellationToken cancellationToken)
    {
        var result = await posters.GetAsync(fileName, UserId, cancellationToken);
        switch (result.Status)
        {
            case LibraryPosterStatus.Served:
                Response.Headers.CacheControl = PosterCacheControl;
                return File(result.Content!, "image/jpeg");
            case LibraryPosterStatus.NotFound:
                return NotFound();
            default:
                return this.BadGatewayError(result.Error);
        }
    }

    /// <summary>The libraries that hold a file; a server has a handful, so never paged.</summary>
    [HttpGet("locations")]
    public async Task<ActionResult<LibraryLocationListResponse>> Locations(CancellationToken cancellationToken) =>
        Ok(await library.ListLocationsAsync(cancellationToken));
}
