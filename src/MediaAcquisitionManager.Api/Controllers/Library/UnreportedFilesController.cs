using MediaAcquisitionManager.Api.Auth;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Api.Http;
using MediaAcquisitionManager.Api.Services.Files;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MediaAcquisitionManager.Api.Controllers.Library;

/// <summary>MAM-42 — the files the scan of the media folders found that no library reports, and the owner's Ignore.</summary>
/// <remarks>
/// <para>
/// The list is for every signed-in user, as the Library's paths are; why the app couldn't read a file only the owner
/// gets, as everywhere else (MAM-9).
/// </para>
/// <para>
/// Ignoring and counting again are the installation owner's alone (Vasyl, 2026-10-13): outside
/// <c>Controllers/Settings</c>, so each write declares it. Neither changes anything on the disk or in a library; they
/// only take a file off the counts or put it back.
/// </para>
/// </remarks>
[ApiController]
[Authorize]
[Route("api/library/unreported-files")]
public class UnreportedFilesController(IUnreportedFileService files) : ControllerBase
{
    private Guid UserId => User.GetUserId();

    /// <summary>One page of the files in no library of one view, or of every file the app counts, with what each view
    /// holds.</summary>
    [HttpGet]
    public async Task<ActionResult<UnreportedFileListResponse>> List([FromQuery] UnreportedFileListRequest request, CancellationToken cancellationToken)
    {
        var result = await files.ListAsync(request.View, request.Page, request.PageSize, UserId, cancellationToken);
        return result.Status switch
        {
            UnreportedFileListStatus.Listed => Ok(result.List),
            _ => this.BadRequestError(result.Error!),
        };
    }

    /// <summary>Stops counting the file: 204, also when it already was ignored; a bare 404 for an id no name the scan
    /// stored has.</summary>
    [HttpPut("{id:guid}/ignored")]
    [RequireInstallationOwner]
    public async Task<IActionResult> Ignore(Guid id, CancellationToken cancellationToken) =>
        Answer(await files.IgnoreAsync(id, UserId, cancellationToken));

    /// <summary>Counts the file again: 204, also when it wasn't ignored; a bare 404 for an id no name the scan stored
    /// has.</summary>
    [HttpDelete("{id:guid}/ignored")]
    [RequireInstallationOwner]
    public async Task<IActionResult> UndoIgnore(Guid id, CancellationToken cancellationToken) =>
        Answer(await files.UndoIgnoreAsync(id, UserId, cancellationToken));

    private IActionResult Answer(UnreportedFileIgnoreStatus status) =>
        status == UnreportedFileIgnoreStatus.Done ? NoContent() : NotFound();
}
