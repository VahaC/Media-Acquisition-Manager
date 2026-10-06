using MediaAcquisitionManager.Api.Auth;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Api.Http;
using MediaAcquisitionManager.Api.Services.LibraryLinks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MediaAcquisitionManager.Api.Controllers.Library.Links;

/// <summary>MAM-34 — the TMDb page's links one by one: the lists of links by what needs the owner (gone, waiting, in
/// conflict, not written, lost), Retry or Write again of one link, removing one, and the results of an "Accept all
/// confident" click.</summary>
/// <remarks>
/// The installation owner's alone, every action, by where this controller lives
/// (<see cref="LibraryLinksAreOwnerOnlyConvention"/>, R19). Lists are filtered, paged and ordered by the server.
/// </remarks>
[ApiController]
[Authorize]
[Route("api/library/tmdb")]
public class LibraryLinkListsController(ILibraryLinkService links) : ControllerBase
{
    private Guid UserId => User.GetUserId();

    /// <summary>One page of the links of a filter — <c>Unattached</c>, <c>Pending</c>, <c>Conflict</c>, <c>NotWritten</c>
    /// or <c>Lost</c>, required —, newest first.</summary>
    [HttpGet("links")]
    public async Task<ActionResult<LibraryLinkListResponse>> List([FromQuery] LibraryLinkListRequest request, CancellationToken cancellationToken)
    {
        var result = await links.ListAsync(request.Filter, request.Page, request.PageSize, cancellationToken);
        return result.Status switch
        {
            LibraryLinkStatus.Succeeded => Ok(result.List),
            _ => this.BadRequestError(result.Error),
        };
    }

    /// <summary>Writes one link into its library again — Retry or Write again for one item —, with its work's dialog
    /// as it then stands; a bare 404 without such a link.</summary>
    [HttpPost("links/{linkId:guid}/write")]
    public async Task<ActionResult<LibraryLinkDialogResponse>> WriteLink(Guid linkId, CancellationToken cancellationToken) =>
        Answer(await links.WriteLinkAsync(linkId, UserId, cancellationToken));

    /// <summary>Removes one link — the Unattached list's Remove — after its library item is put back when it still has
    /// one (R11); 204.</summary>
    [HttpDelete("links/{linkId:guid}")]
    public async Task<IActionResult> RemoveLink(Guid linkId, CancellationToken cancellationToken)
    {
        var result = await links.RemoveLinkAsync(linkId, UserId, cancellationToken);
        return result.Status == LibraryLinkStatus.Succeeded ? NoContent() : Answer(result);
    }

    /// <summary>The links of an "Accept all confident" click with their write states as they are now, polled while
    /// they are written; a bare 404 for a click no link has.</summary>
    [HttpGet("batches/{batchId:guid}")]
    public async Task<ActionResult<LibraryLinkBatchResponse>> Batch(Guid batchId, CancellationToken cancellationToken) =>
        await links.ReadBatchAsync(batchId, cancellationToken) is { } batch ? Ok(batch) : NotFound();

    private ActionResult Answer(LibraryLinkResult result) => result.Status switch
    {
        LibraryLinkStatus.Succeeded => Ok(result.Dialog),
        LibraryLinkStatus.NotFound => NotFound(),
        LibraryLinkStatus.Conflict => this.ConflictError(result.Error),
        _ => this.BadGatewayError(result.Error),
    };
}
