using MediaAcquisitionManager.Api.Auth;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Api.Http;
using MediaAcquisitionManager.Api.Services.LibraryLinks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace MediaAcquisitionManager.Api.Controllers.Library.Links;

/// <summary>MAM-34 — the TMDb page's review of the works without a TMDb id: the queue one work at a time, the confident
/// suggestions with each library's numbers, "Accept all confident", and the preparation of the suggestions.</summary>
/// <remarks>
/// The installation owner's alone, every action, by where this controller lives (<see cref="LibraryLinksAreOwnerOnlyConvention"/>,
/// R19). Lists are filtered, paged and ordered by the server. Preparing the suggestions is long work: it is queued (202 +
/// run row + reconciler) and the page polls <c>runs/latest</c>.
/// </remarks>
[ApiController]
[Authorize]
[Route("api/library/tmdb")]
public class LibraryLinkReviewController(
    ILibraryLinkSuggestionService suggestions,
    ILibraryLinkSuggestionRunService runs) : ControllerBase
{
    private Guid UserId => User.GetUserId();

    /// <summary>The review queue — the works without a TMDb id, a link or "none fits", by title — with the one at an
    /// index (from 0) as its dialog; none past its end. 400 for an index that isn't one.</summary>
    [HttpGet("queue")]
    public async Task<ActionResult<LibraryLinkQueueResponse>> Queue([FromQuery] LibraryLinkQueueRequest request, CancellationToken cancellationToken)
    {
        var result = await suggestions.ReadQueueAsync(request.Index, cancellationToken);
        return result.Status == LibraryLinkStatus.Succeeded ? Ok(result.Queue) : this.BadRequestError(result.Error!);
    }

    /// <summary>One page of the works whose suggestion is confident, by title, of one library or all, with each library's
    /// numbers; 400 for a bad library id or page.</summary>
    [HttpGet("confident")]
    public async Task<ActionResult<ConfidentSuggestionListResponse>> Confident(
        [FromQuery] ConfidentSuggestionListRequest request, CancellationToken cancellationToken)
    {
        var result = await suggestions.ListConfidentAsync(request.LocationId, request.Page, request.PageSize, cancellationToken);
        return result.Status == LibraryLinkStatus.Succeeded ? Ok(result.List) : this.BadRequestError(result.Error!);
    }

    /// <summary>
    /// "Accept all confident": links each ticked work (at most 100) while its suggestion is still the confident one, and
    /// queues the identifications in the libraries; 202 with what happened to each work, polled at <c>batches/{batchId}</c>. 400
    /// for a malformed list, 409 without a catalog.
    /// </summary>
    [HttpPost("confident")]
    public async Task<ActionResult<LibraryLinkBatchResponse>> AcceptConfident(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] LibraryLinkAcceptRequest? request,
        CancellationToken cancellationToken)
    {
        var result = await suggestions.AcceptConfidentAsync(request, UserId, cancellationToken);
        return result.Status switch
        {
            LibraryLinkStatus.Succeeded => Accepted(result.Batch),
            LibraryLinkStatus.Conflict => this.ConflictError(result.Error!),
            _ => this.BadRequestError(result.Error!),
        };
    }

    /// <summary>The preparation under way, else the latest one; 204 before the first.</summary>
    [HttpGet("runs/latest")]
    public async Task<ActionResult<LibraryLinkSuggestionRunDto>> LatestRun(CancellationToken cancellationToken) =>
        await runs.GetLatestAsync(cancellationToken) is { } run ? Ok(run) : NoContent();

    /// <summary>The owner's "Prepare again": queues a preparation that searches every work again (R21), or joins the one
    /// waiting; 409 without a catalog, 503 when the queue is full.</summary>
    [HttpPost("runs")]
    public async Task<ActionResult<LibraryLinkSuggestionRunDto>> StartRun(CancellationToken cancellationToken)
    {
        var result = await runs.StartAsync(UserId, cancellationToken);
        return result.Status switch
        {
            // Named: with a bare null the (action, controller, route values) overload wins, and the run would go out as
            // route values behind an empty body.
            LibraryLinkSuggestionRunStartStatus.Queued => AcceptedAtAction(nameof(LatestRun), routeValues: null, value: result.Run),
            LibraryLinkSuggestionRunStartStatus.NoCatalog => this.ConflictError(result.Error!),
            _ => this.UnavailableError(result.Error!),
        };
    }
}
