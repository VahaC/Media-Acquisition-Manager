using MediaAcquisitionManager.Api.Auth;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Api.Controllers.Downloads;
using MediaAcquisitionManager.Api.Http;
using MediaAcquisitionManager.Api.Services.Downloads;
using MediaAcquisitionManager.Api.Services.Replacements;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace MediaAcquisitionManager.Api.Controllers.Replacements;

/// <summary>
/// MAM-24 — replacing specific old copies with a download, behind the Download dialog's Replace after verification
/// (ADR 0030–0034).
/// </summary>
/// <remarks>
/// The installation owner's alone, every action, by where this controller lives (<see cref="ReplacementsAreOwnerOnlyConvention"/>,
/// ADR 0033): a member gets the owner sentence before anything is read, and keeps Keep both. Outside
/// <c>Controllers/Downloads</c> on purpose, whose sends are every signed-in user's.
/// </remarks>
[ApiController]
[Authorize]
[Route("api/replacements")]
public class ReplacementsController(IReplacementOptionsReader options, IDownloadService downloads) : ControllerBase
{
    private Guid UserId => User.GetUserId();

    /// <summary>
    /// What of the release the owner may replace through the destination: the reason nothing can, or the matched works
    /// with their copies, each copy's reason and marks. The destination's client is asked where its category saves; every
    /// download client's torrents only with <c>holdings=true</c>, and only while a copy can be ticked. Writes nothing.
    /// </summary>
    [HttpGet("options")]
    public async Task<ActionResult<ReplacementOptionsResponse>> Options(
        [FromQuery] ReplacementOptionsRequest request, CancellationToken cancellationToken)
    {
        var result = await options.ReadAsync(request.ReleaseId, request.DestinationId, request.Holdings, UserId, cancellationToken);
        return result.Status switch
        {
            ReplacementOptionsStatus.Listed => Ok(result.Options),
            ReplacementOptionsStatus.Invalid => this.BadRequestError(result.Error!),
            // Ids in the query, not the addressed row, so the dialog gets the sentence.
            _ => this.NotFoundError(result.Error!),
        };
    }

    /// <summary>
    /// Sends the release once per request id, scheduling the replacement of the copies ticked, and answers as a send
    /// does: 201 with the new job, its replacement on it, whatever came of the sending — a strict refusal is a Failed
    /// job, nothing sent —; 200 with the job the same request with the same ticks made before. 400 when the ticks don't
    /// fit the release's works, 409 when a ticked copy can't be replaced now, each with the sentence why and nothing
    /// written. A POST: it writes a job and sends a download, so nothing may repeat it but the request id.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<DownloadJobDto>> Schedule(
        // An empty body reaches the rules' "request id" sentence instead of a validation body.
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] ScheduleReplacementRequest? request,
        CancellationToken cancellationToken)
    {
        var result = await downloads.ScheduleReplacementAsync(
            request ?? new ScheduleReplacementRequest(null, null, null, null), UserId, cancellationToken);
        return result.Status switch
        {
            // The job is a download job, read where every job is (R4).
            DownloadSendStatus.Created => CreatedAtAction(nameof(DownloadsController.Get), "Downloads", new { id = result.Job!.Id }, result.Job),
            DownloadSendStatus.Replayed => Ok(result.Job),
            DownloadSendStatus.Invalid or DownloadSendStatus.ReplacementInvalid => this.BadRequestError(result.Error!),
            // Ids in the body, not the addressed row, so the dialog gets the sentence.
            DownloadSendStatus.ReleaseMissing or DownloadSendStatus.ClientMissing or DownloadSendStatus.DestinationMissing =>
                this.NotFoundError(result.Error!),
            // A copy that can't be replaced now, the request id used for something else, a client or a source that can't
            // be used, or a release already sent to the client.
            _ => this.ConflictError(result.Error!),
        };
    }
}
