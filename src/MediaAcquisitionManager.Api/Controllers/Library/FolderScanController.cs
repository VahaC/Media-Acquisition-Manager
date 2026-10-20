using MediaAcquisitionManager.Api.Auth;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Api.Http;
using MediaAcquisitionManager.Api.Services.Files;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MediaAcquisitionManager.Api.Controllers.Library;

/// <summary>Scanning the media folders for the files no library reported, and one scan (MAM-18).</summary>
/// <remarks>
/// <para>
/// Reading a scan is for every signed-in user, as the library is. Asking for one is the installation
/// owner's alone, like an inspection: it walks the owner's disks. Outside <c>Controllers/Settings</c>, so
/// the owner-only write is declared on the action.
/// </para>
/// <para>
/// A request answers 202 with the queued scan at once (202 + run row + reconciler) — the one it queued, or
/// the one already waiting, which it joins — and the Library page polls <c>sources</c>, which carries
/// where scans stand.
/// </para>
/// </remarks>
[ApiController]
[Authorize]
[Route("api/library/scans")]
public class FolderScanController(IFolderScanService scans) : ControllerBase
{
    private Guid UserId => User.GetUserId();

    /// <summary>Queues a scan of every allowed media folder.</summary>
    [HttpPost]
    [RequireInstallationOwner]
    public async Task<ActionResult<FolderScanRunDto>> Start(CancellationToken cancellationToken)
    {
        var result = await scans.StartAsync(UserId, cancellationToken);
        return result.Status switch
        {
            FolderScanStartStatus.Queued => AcceptedAtAction(nameof(GetRun), new { runId = result.Run!.Id }, result.Run),
            // Nothing the app may walk: a state the owner changes in the settings, not a bad request.
            FolderScanStartStatus.NoAllowedRoots => this.ConflictError(result.Error!),
            _ => this.UnavailableError(result.Error!),
        };
    }

    [HttpGet("{runId:guid}")]
    public async Task<ActionResult<FolderScanRunDto>> GetRun(Guid runId, CancellationToken cancellationToken) =>
        await scans.GetRunAsync(runId, UserId, cancellationToken) is { } run ? Ok(run) : NotFound();
}
