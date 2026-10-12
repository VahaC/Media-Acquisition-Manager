using MediaAcquisitionManager.Api.Auth;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Api.Http;
using MediaAcquisitionManager.Api.Services.CopyRemovals;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace MediaAcquisitionManager.Api.Controllers.Replacements;

/// <summary>
/// MAM-40 — the owner's removal of one copy of a duplicate group into the quarantine (ADR 0030), from the Duplicates page's
/// Remove this copy, and his Restore and Delete for good of it under Removed copies.
/// </summary>
/// <remarks>
/// <para>
/// The installation owner's alone, every action, the list included, by where this controller lives
/// (<see cref="ReplacementsAreOwnerOnlyConvention"/>, ADR 0033): a member gets the owner sentence before anything is read or
/// bound. The removals are the installation's, as its library is: a missing one is a bare 404.
/// </para>
/// <para>
/// Each write is a POST with its verb: a removal stays, as history. Remove answers 200 with the removal as it stands — in
/// the quarantine, or waiting for review —; 409 with the sentence when a gate or a check refused and nothing moved, or when
/// the video couldn't be moved and the removal ended there; a bare 404 for a group key no group has or a file not in it.
/// </para>
/// </remarks>
[ApiController]
[Authorize]
[Route("api/replacements/removals")]
public class CopyRemovalsController(ICopyRemovalService removals) : ControllerBase
{
    private Guid UserId => User.GetUserId();

    /// <summary>One page of the removals still open, newest first, with the room their names take in the quarantine; 400
    /// for a page out of range. Reads the disk for the page's link counts; writes nothing.</summary>
    [HttpGet]
    public async Task<ActionResult<CopyRemovalListResponse>> List([FromQuery] CopyRemovalListRequest request, CancellationToken cancellationToken)
    {
        var result = await removals.ListAsync(request.Page, request.PageSize, cancellationToken);
        return result.Status switch
        {
            CopyRemovalStatus.Done => Ok(result.List),
            _ => this.BadRequestError(result.Error),
        };
    }

    /// <summary>
    /// Removes a file of a group of exact copies or of one work's copies into the quarantine, with its own companions: 200
    /// with the removal; 400 without the group or the file; 404 for a group or file not found; 409 when a rule, the disk or
    /// a download client refuses — nothing moved, no row kept —, or when the video couldn't be moved.
    /// </summary>
    [HttpPost]
    [UnreadableBodyError(CopyRemovalService.UnreadableBodyError)]
    public async Task<ActionResult<CopyRemovalDto>> Remove(
        // An empty body reaches the service's "needs the group and the file" instead of a validation body.
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] RemoveCopyRequest? request,
        CancellationToken cancellationToken) =>
        Answer(await removals.RemoveAsync(request, UserId, cancellationToken));

    /// <summary>
    /// Puts every name the removal holds in the quarantine back where it was: 200 with the removal, now <c>Restored</c>; 409
    /// with nothing written when a check refuses — switched off, a folder no longer ticked, a name changed in the quarantine,
    /// its old path taken or its folder gone —, or with the reason when a name couldn't be put back and it now waits for
    /// review.
    /// </summary>
    [HttpPost("{id:guid}/restore")]
    public async Task<ActionResult<CopyRemovalDto>> Restore(Guid id, CancellationToken cancellationToken) =>
        Answer(await removals.RestoreAsync(id, UserId, cancellationToken));

    /// <summary>
    /// Deletes every name the removal holds in the quarantine for good — the whole removal, which the page confirmed a second
    /// time showing every path and size: 200 with the removal, now <c>Deleted</c>; 409 with nothing written when it holds
    /// nothing there, it is switched off or a name changed, or with the reason when a deletion was refused part-way.
    /// </summary>
    [HttpPost("{id:guid}/delete-for-good")]
    public async Task<ActionResult<CopyRemovalDto>> DeleteForGood(Guid id, CancellationToken cancellationToken) =>
        Answer(await removals.DeleteForGoodAsync(id, UserId, cancellationToken));

    private ActionResult<CopyRemovalDto> Answer(CopyRemovalResult result) => result.Status switch
    {
        CopyRemovalStatus.Done => Ok(result.Removal),
        CopyRemovalStatus.Invalid => this.BadRequestError(result.Error),
        CopyRemovalStatus.Refused => this.ConflictError(result.Error),
        // The addressed group, file or removal: missing, with no body.
        _ => NotFound(),
    };
}
