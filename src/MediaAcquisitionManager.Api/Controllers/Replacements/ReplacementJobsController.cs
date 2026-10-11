using MediaAcquisitionManager.Api.Auth;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Api.Http;
using MediaAcquisitionManager.Api.Services.Replacements;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace MediaAcquisitionManager.Api.Controllers.Replacements;

/// <summary>
/// MAM-26 — the owner's own hand on a scheduled replacement, by its id (ADR 0030, ADR 0033): its details on the job's page,
/// Retry after the mover stopped it, Restore of what it holds in the quarantine, and Delete for good of that after a
/// second confirmation. MAM-27 — Retry after the check of its new files stopped too, Keep both, which cancels only the
/// replacement, and Drop of one ticked copy.
/// </summary>
/// <remarks>
/// <para>
/// The installation owner's alone, every action, reads included, by where this controller lives
/// (<see cref="ReplacementsAreOwnerOnlyConvention"/>): a member gets the owner sentence before anything is read or bound,
/// and no attribute here has to be remembered. A replacement is its owner's own: another's, or a missing one, is a bare
/// 404, as a download job is (ADR 0014).
/// </para>
/// <para>
/// Each write is a POST with its verb, not a DELETE: the job stays, as history (<c>DownloadsController.Cancel</c>'s
/// reason). It answers 200 with the download job and its replacement line as they stand after it, and 409 with the
/// sentence when the replacement can't take it now — or stopped part-way and waits for review.
/// </para>
/// </remarks>
[ApiController]
[Authorize]
[Route("api/replacements")]
public class ReplacementJobsController(IReplacementJobActions actions) : ControllerBase
{
    private Guid UserId => User.GetUserId();

    /// <summary>The owner's replacement with every name of its move, where each is now, the room the quarantine takes and
    /// which actions are offered; a bare 404 for another user's, like a missing one. Writes nothing.</summary>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ReplacementDetailsDto>> Get(Guid id, CancellationToken cancellationToken) =>
        await actions.GetAsync(id, UserId, cancellationToken) is { } details ? Ok(details) : NotFound();

    /// <summary>
    /// Puts a replacement the mover stopped back in its queue, so its next pass checks the copies again — MAM-27 — or one
    /// whose check of the new files stopped back to that check, which starts again from the files and asks the library
    /// again: 200 with the job; 409 when it isn't waiting for review, its download can't be followed, a copy can only be
    /// dropped, or it is being worked on. Writes nothing to the media or a client.
    /// </summary>
    [HttpPost("{id:guid}/retry")]
    public async Task<ActionResult<DownloadJobDto>> Retry(Guid id, CancellationToken cancellationToken) =>
        Answer(await actions.RetryAsync(id, UserId, cancellationToken));

    /// <summary>
    /// MAM-27 — Keep both: cancels only the replacement, before any old copy started to move: 200 with the job, its
    /// replacement now <c>Cancelled</c> and its download untouched; 409 when it has ended, part of it left its folders —
    /// Restore puts that back —, or it is being worked on. Writes nothing to the media, and sends nothing to the client.
    /// </summary>
    [HttpPost("{id:guid}/cancel")]
    public async Task<ActionResult<DownloadJobDto>> Cancel(Guid id, CancellationToken cancellationToken) =>
        Answer(await actions.CancelAsync(id, UserId, cancellationToken));

    /// <summary>
    /// MAM-27 — drops the ticked copy <paramref name="copyId"/> from a replacement that stopped for review: 200 with the
    /// job, its state unchanged and the copy left where it is; 409 when the replacement didn't stop for review or its
    /// download can't be followed, the copy is dropped already, left its folder or is the last one, or the job is being
    /// worked on; a bare 404 for a copy that isn't this replacement's, as for a missing replacement. Writes nothing to the
    /// media or a client.
    /// </summary>
    [HttpPost("{id:guid}/copies/{copyId:guid}/drop")]
    public async Task<ActionResult<DownloadJobDto>> DropCopy(Guid id, Guid copyId, CancellationToken cancellationToken) =>
        Answer(await actions.DropCopyAsync(id, copyId, UserId, cancellationToken));

    /// <summary>
    /// Puts every name the replacement holds in the quarantine back where it was: 200 with the job, now <c>Restored</c>;
    /// 409 with nothing written when a check refuses — switched off, a folder no longer ticked, a name changed in the
    /// quarantine, its old path taken or its folder gone —, or with the reason when a name couldn't be put back and the
    /// job now waits for review.
    /// </summary>
    [HttpPost("{id:guid}/restore")]
    public async Task<ActionResult<DownloadJobDto>> Restore(Guid id, CancellationToken cancellationToken) =>
        Answer(await actions.RestoreAsync(id, UserId, cancellationToken));

    /// <summary>
    /// Deletes every name the replacement holds in the quarantine for good, the ids the owner confirmed in the body: 200
    /// with the job, now <c>Completed</c>; 400 without them; 409 with nothing written when they are no longer exactly what
    /// it holds, it is switched off or a name changed, or with the reason when a deletion was refused part-way and the job
    /// now waits for review.
    /// </summary>
    [HttpPost("{id:guid}/delete-for-good")]
    [UnreadableBodyError(ReplacementJobActions.UnreadableBodyError)]
    public async Task<ActionResult<DownloadJobDto>> DeleteForGood(
        Guid id,
        // An empty body reaches the service's "needs the files you confirmed" instead of a validation body.
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] DeleteQuarantinedRequest? request,
        CancellationToken cancellationToken) =>
        Answer(await actions.DeleteForGoodAsync(id, request?.Names, UserId, cancellationToken));

    private ActionResult<DownloadJobDto> Answer(ReplacementActionResult result) => result.Status switch
    {
        ReplacementActionStatus.Done => Ok(result.Job),
        ReplacementActionStatus.Invalid => this.BadRequestError(result.Error),
        ReplacementActionStatus.Refused => this.ConflictError(result.Error),
        // The addressed row: missing and another user's alike, with no body.
        _ => NotFound(),
    };
}
