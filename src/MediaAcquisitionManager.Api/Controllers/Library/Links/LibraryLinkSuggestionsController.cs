using MediaAcquisitionManager.Api.Auth;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Api.Http;
using MediaAcquisitionManager.Api.Services.LibraryLinks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MediaAcquisitionManager.Api.Controllers.Library.Links;

/// <summary>MAM-34 — finding a library work's TMDb film or show besides its prepared suggestions: a free search, an id the
/// owner pasted, and the owner's "none of them fits".</summary>
/// <remarks>
/// The installation owner's alone, every action, by where this controller lives (<see cref="LibraryLinksAreOwnerOnlyConvention"/>,
/// R19): a search and a pasted id call the owner's catalog. A bare 404 without such a work; 409 without a catalog; 502 when
/// the catalog can't be read.
/// </remarks>
[ApiController]
[Authorize]
[Route("api/library/works/{id:guid}/tmdb")]
public class LibraryLinkSuggestionsController(ILibraryLinkSuggestionService suggestions) : ControllerBase
{
    private Guid UserId => User.GetUserId();

    /// <summary>The catalog's first page of films and shows by a title — of a year when one is given —, ranked for the
    /// work with their evidence; 400 for a blank title or a bad year.</summary>
    [HttpGet("search")]
    public async Task<ActionResult<LinkCandidateListResponse>> Search(
        Guid id, [FromQuery] LibraryLinkSearchRequest request, CancellationToken cancellationToken) =>
        Answer(await suggestions.SearchAsync(id, request.Title, request.Year, cancellationToken));

    /// <summary>
    /// The film or show a pasted TMDb or IMDb link, IMDb id or TMDb number names, ranked for the work; 400 for a paste it
    /// can't read or a bare number without film or show, 404 with the sentence for an id TMDb doesn't have (R18).
    /// </summary>
    [HttpGet("lookup")]
    public async Task<ActionResult<LinkCandidateListResponse>> Lookup(
        Guid id, [FromQuery] LibraryLinkLookupRequest request, CancellationToken cancellationToken) =>
        Answer(await suggestions.LookupAsync(id, request.Text, request.Kind, cancellationToken));

    /// <summary>The owner's "none fits" (the review's 0): the work leaves the review queue and the confident list; 204.</summary>
    [HttpPut("none-fits")]
    public async Task<IActionResult> MarkNoneFits(Guid id, CancellationToken cancellationToken) =>
        await suggestions.MarkNoneFitsAsync(id, UserId, cancellationToken) == LibraryLinkStatus.Succeeded ? NoContent() : NotFound();

    /// <summary>Takes "none fits" back; 204.</summary>
    [HttpDelete("none-fits")]
    public async Task<IActionResult> ClearNoneFits(Guid id, CancellationToken cancellationToken) =>
        await suggestions.ClearNoneFitsAsync(id, cancellationToken) == LibraryLinkStatus.Succeeded ? NoContent() : NotFound();

    private ActionResult Answer(LinkCandidateListResult result) => result.Status switch
    {
        LibraryLinkStatus.Succeeded => Ok(result.List),
        LibraryLinkStatus.NotFound => NotFound(),
        LibraryLinkStatus.Invalid => this.BadRequestError(result.Error),
        LibraryLinkStatus.CatalogWorkNotFound => this.NotFoundError(result.Error),
        // Nothing to search without a catalog: a state the owner changes under Integrations, not a bad request.
        LibraryLinkStatus.Conflict => this.ConflictError(result.Error),
        _ => this.BadGatewayError(result.Error),
    };
}
