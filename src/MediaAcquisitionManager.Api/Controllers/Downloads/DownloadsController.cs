using MediaAcquisitionManager.Api.Auth;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Api.Http;
using MediaAcquisitionManager.Api.Services.Downloads;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace MediaAcquisitionManager.Api.Controllers.Downloads;

/// <summary>Sending a release to a download client, behind the search page's Download dialog (MAM-13); the
/// jobs it made, their files, and cancelling one, behind the Jobs page (MAM-14).</summary>
/// <remarks>
/// <para>
/// For every signed-in user (MAM-13's decision 1), as the search the release comes from is. Outside
/// <c>Controllers/Settings</c> and <c>Controllers/Library</c> on purpose: both make every write owner-only.
/// A release is sent from its creator's own search only, and a job is its sender's: another user's answers
/// as missing, like their searches (ADR 0011).
/// </para>
/// <para>
/// A send answers 201 with the job whatever came of the sending — the job says whether the client took it,
/// and why not — and 200 with the same job when the same request comes again (ADR 0013): a client and a
/// source that couldn't be asked are the job's outcome, never this request's status.
/// </para>
/// </remarks>
[ApiController]
[Authorize]
[Route("api/downloads")]
public class DownloadsController(IDownloadService downloads) : ControllerBase
{
    private Guid UserId => User.GetUserId();

    /// <summary>What the release can be sent to; no client is asked.</summary>
    [HttpGet("options")]
    public async Task<ActionResult<DownloadOptionsResponse>> Options([FromQuery] DownloadOptionsRequest request, CancellationToken cancellationToken)
    {
        var result = await downloads.GetOptionsAsync(request.ReleaseId, UserId, cancellationToken);
        return result.Status switch
        {
            DownloadOptionsStatus.Listed => Ok(result.Options),
            DownloadOptionsStatus.Invalid => this.BadRequestError(result.Error),
            // A release id in the query, not the addressed row, so the dialog gets the sentence.
            _ => this.NotFoundError(result.Error),
        };
    }

    /// <summary>
    /// MAM-38 — whether a release sent to the destination is checked for files it would write over, or why its
    /// folder can't be: the dialog warns of it before Send. Its client is asked where the category saves.
    /// </summary>
    [HttpGet("overwrite-check")]
    public async Task<ActionResult<DownloadOverwriteCheckResponse>> OverwriteCheck(
        [FromQuery] DownloadOverwriteCheckRequest request, CancellationToken cancellationToken)
    {
        var result = await downloads.GetOverwriteCheckAsync(request.DestinationId, cancellationToken);
        return result.Status switch
        {
            DownloadOverwriteCheckStatus.Answered => Ok(result.Answer),
            DownloadOverwriteCheckStatus.Invalid => this.BadRequestError(result.Error),
            // A destination id in the query, not the addressed row, so the dialog gets the sentence.
            _ => this.NotFoundError(result.Error),
        };
    }

    /// <summary>
    /// Sends the release once per request id: 201 with the new job, 200 with the job the same request made
    /// before. A POST: it writes a job and sends a download, so nothing may repeat it but the request id.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<DownloadJobDto>> Create(
        // An empty body reaches the rules' "request id" sentence instead of a validation body.
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] SendDownloadRequest? request,
        CancellationToken cancellationToken)
    {
        var result = await downloads.SendAsync(request ?? new SendDownloadRequest(null, null, null, null), UserId, cancellationToken);
        return result.Status switch
        {
            DownloadSendStatus.Created => CreatedAtAction(nameof(Get), new { id = result.Job!.Id }, result.Job),
            DownloadSendStatus.Replayed => Ok(result.Job),
            DownloadSendStatus.Invalid => this.BadRequestError(result.Error),
            // Ids in the body, not the addressed row, so the dialog gets the sentence.
            DownloadSendStatus.ReleaseMissing or DownloadSendStatus.ClientMissing or DownloadSendStatus.DestinationMissing =>
                this.NotFoundError(result.Error),
            // The request id used for something else, a client or a source that can't be used, or a release
            // already sent to the client.
            _ => this.ConflictError(result.Error),
        };
    }

    /// <summary>The caller's job; a bare 404 for another user's, like a missing one.</summary>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<DownloadJobDto>> Get(Guid id, CancellationToken cancellationToken) =>
        await downloads.GetAsync(id, UserId, cancellationToken) is { } job ? Ok(job) : NotFound();

    /// <summary>MAM-14 — one page of the caller's jobs, newest first, filtered by status by the server: the
    /// Jobs page. No client is asked. MAM-41 — and by the page's tab, with the counts that label the tabs.</summary>
    [HttpGet]
    public async Task<ActionResult<DownloadJobListResponse>> List([FromQuery] DownloadJobListRequest request, CancellationToken cancellationToken)
    {
        var result = await downloads.ListAsync(request.Status, request.View, request.Page, request.PageSize, UserId, cancellationToken);
        return result.Status switch
        {
            DownloadJobListStatus.Listed => Ok(result.List),
            _ => this.BadRequestError(result.Error),
        };
    }

    /// <summary>MAM-14 — one page of the files of the caller's job, as its client last listed them; a bare 404
    /// for another user's job, like a missing one.</summary>
    [HttpGet("{id:guid}/files")]
    public async Task<ActionResult<DownloadJobFileListResponse>> Files(
        Guid id, [FromQuery] DownloadJobFileListRequest request, CancellationToken cancellationToken)
    {
        var result = await downloads.ListFilesAsync(id, request.Page, request.PageSize, UserId, cancellationToken);
        return result.Status switch
        {
            DownloadJobFileListStatus.Listed => Ok(result.List),
            DownloadJobFileListStatus.Invalid => this.BadRequestError(result.Error),
            _ => NotFound(),
        };
    }

    /// <summary>
    /// MAM-14 — stops following the caller's job: 200 with the job, now <c>Cancelled</c>; 409 when it already
    /// ended, or is still being sent; a bare 404 for another user's, like a missing one. Nothing is sent to the
    /// client, which keeps the torrent and its files (ADR 0014). A POST, not a DELETE: the job stays, as
    /// history.
    /// </summary>
    [HttpPost("{id:guid}/cancel")]
    public async Task<ActionResult<DownloadJobDto>> Cancel(Guid id, CancellationToken cancellationToken)
    {
        var result = await downloads.CancelAsync(id, UserId, cancellationToken);
        return result.Status switch
        {
            DownloadCancelStatus.Cancelled => Ok(result.Job),
            DownloadCancelStatus.NothingToStop or DownloadCancelStatus.StillBeingSent => this.ConflictError(result.Error),
            _ => NotFound(),
        };
    }
}
