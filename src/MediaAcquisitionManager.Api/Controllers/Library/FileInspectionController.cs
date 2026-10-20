using MediaAcquisitionManager.Api.Auth;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Api.Http;
using MediaAcquisitionManager.Api.Services.Files;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MediaAcquisitionManager.Api.Controllers.Library;

/// <summary>Inspecting the library's files, and one inspection run (MAM-9).</summary>
/// <remarks>
/// <para>
/// Reading a run is for every signed-in user, as the library is. Asking for an inspection is the
/// installation owner's alone, like a sync: it reads the owner's disks. Outside <c>Controllers/Settings</c>,
/// so the owner-only write is declared on the action.
/// </para>
/// <para>
/// A request answers 202 with the queued run at once (202 + run row + reconciler) — the one it queued, or
/// the one already waiting, which it joins — and the Library page polls <c>sources</c>, which carries
/// where inspections stand.
/// </para>
/// </remarks>
[ApiController]
[Authorize]
[Route("api/library/inspections")]
public class FileInspectionController(IFileInspectionService inspections) : ControllerBase
{
    private Guid UserId => User.GetUserId();

    /// <summary>Queues an inspection of every stored file name: the libraries' and those a scan of the media
    /// folders found that no library reported (MAM-18).</summary>
    [HttpPost]
    [RequireInstallationOwner]
    public async Task<ActionResult<FileInspectionRunDto>> Start(CancellationToken cancellationToken)
    {
        var result = await inspections.StartAsync(UserId, cancellationToken);
        return result.Status switch
        {
            FileInspectionStartStatus.Queued => AcceptedAtAction(nameof(GetRun), new { runId = result.Run!.Id }, result.Run),
            // Nothing the app may read: a state the owner changes in the settings, not a bad request.
            FileInspectionStartStatus.NoAllowedRoots => this.ConflictError(result.Error!),
            _ => this.UnavailableError(result.Error!),
        };
    }

    [HttpGet("{runId:guid}")]
    public async Task<ActionResult<FileInspectionRunDto>> GetRun(Guid runId, CancellationToken cancellationToken) =>
        await inspections.GetRunAsync(runId, UserId, cancellationToken) is { } run ? Ok(run) : NotFound();
}
