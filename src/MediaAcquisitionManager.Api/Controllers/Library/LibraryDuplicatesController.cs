using MediaAcquisitionManager.Api.Auth;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Api.Http;
using MediaAcquisitionManager.Api.Services.Duplicates;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace MediaAcquisitionManager.Api.Controllers.Library;

/// <summary>MAM-20 — the library's duplicates, grouped into the four classes of specification §3.3.</summary>
/// <remarks>
/// <para>
/// The list is for every signed-in user, like the Library it reads (ADR 0028): it names no address, key or account. The
/// sentences that quote the owner's paths, and the names of who set a link and who decided a match, only the owner gets.
/// </para>
/// <para>
/// MAM-21 — confirming, dismissing and undoing a possible match are the installation owner's alone (Vasyl, 2026-10-07):
/// outside <c>Controllers/Settings</c>, so each write declares it. A decision is kept by the group's key and never removes,
/// moves or links anything (ADR 0029); nothing here removes a file. MAM-40 — Keep both of copies is the same decision,
/// <c>Dismissed</c>, on the same routes.
/// </para>
/// </remarks>
[ApiController]
[Authorize]
[Route("api/library/duplicates")]
public class LibraryDuplicatesController(ILibraryDuplicateService duplicates, IDuplicateDecisionService decisions) : ControllerBase
{
    private Guid UserId => User.GetUserId();

    /// <summary>One page of duplicate groups, of one class or all, worked out from what the app holds now; MAM-21 — the
    /// dismissed possible matches only on asking for them; MAM-45 — the most recoverable room first on asking for
    /// it.</summary>
    [HttpGet]
    public async Task<ActionResult<LibraryDuplicateListResponse>> List([FromQuery] LibraryDuplicateListRequest request, CancellationToken cancellationToken)
    {
        var result = await duplicates.ListAsync(
            request.Class, request.Decision, request.Sort, request.Page, request.PageSize, UserId, cancellationToken);
        return result.Status switch
        {
            LibraryDuplicateListStatus.Listed => Ok(result.List),
            _ => this.BadRequestError(result.Error!),
        };
    }

    /// <summary>
    /// MAM-21 — confirms or dismisses the possible match the key names now; MAM-40 — or keeps copies of one work or
    /// byte-identical copies as they are (<c>Dismissed</c>): 204, also when it was already decided so; a bare 404 for a
    /// key no group has; 400 with the sentence for a decision missing or not one, or one the group's class never takes.
    /// </summary>
    /// <remarks>The key is not constrained in the route: a key of another shape is the same bare 404 as one no group has,
    /// not a route that doesn't exist.</remarks>
    [HttpPut("{key}/decision")]
    [RequireInstallationOwner]
    [UnreadableBodyError(DuplicateDecisionService.UnreadableBodyError)]
    public async Task<IActionResult> Decide(
        string key,
        // An empty body reaches the service's "A decision is needed" instead of a validation body.
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] DuplicateDecisionRequest? request,
        CancellationToken cancellationToken) =>
        Answer(await decisions.DecideAsync(key, request?.Decision, UserId, cancellationToken));

    /// <summary>
    /// MAM-21 — forgets the decision on the group the key names now, which is asked about again: 204, also when it had
    /// none; a bare 404 for a key no group has; 400 with the sentence for one file's names, which are never decided.
    /// </summary>
    [HttpDelete("{key}/decision")]
    [RequireInstallationOwner]
    public async Task<IActionResult> Undo(string key, CancellationToken cancellationToken) =>
        Answer(await decisions.UndoAsync(key, UserId, cancellationToken));

    private IActionResult Answer(DuplicateDecisionResult result) => result.Status switch
    {
        DuplicateDecisionStatus.Done => NoContent(),
        DuplicateDecisionStatus.NotFound => NotFound(),
        _ => this.BadRequestError(result.Error!),
    };
}
