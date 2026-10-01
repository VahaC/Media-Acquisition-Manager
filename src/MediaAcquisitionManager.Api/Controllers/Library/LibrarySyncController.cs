using MediaAcquisitionManager.Api.Auth;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Api.Http;
using MediaAcquisitionManager.Api.Services.Library;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MediaAcquisitionManager.Api.Controllers.Library;

/// <summary>The media library integrations the Library page syncs from, their runs, and starting a sync
/// (MAM-8).</summary>
/// <remarks>
/// <para>
/// Reading is for every signed-in user, as the library it fills is. Starting a sync is the installation
/// owner's alone: it sends the owner's stored key to the service and replaces the snapshot everyone reads.
/// Outside <c>Controllers/Settings</c>, so the owner-only write is declared on the action.
/// </para>
/// <para>
/// A start answers 202 with the queued run at once (202 + run row + reconciler); the sync runs in the
/// background, and the page polls <c>sources</c>. A run's error can name the service's address, a
/// source's path problem quotes the owner's path mappings, and who started a run is the owner's sign-in
/// name, so only the owner gets them.
/// </para>
/// </remarks>
[ApiController]
[Authorize]
[Route("api/library")]
public class LibrarySyncController(ILibrarySyncService syncs) : ControllerBase
{
    private Guid UserId => User.GetUserId();

    /// <summary>Every media library integration with where its syncs stand; never paged.</summary>
    [HttpGet("sources")]
    public async Task<ActionResult<LibrarySourceListResponse>> Sources(CancellationToken cancellationToken) =>
        Ok(await syncs.ListSourcesAsync(UserId, cancellationToken));

    /// <summary>Queues a sync of the integration's library.</summary>
    [HttpPost("sources/{integrationId:guid}/syncs")]
    [RequireInstallationOwner]
    public async Task<ActionResult<LibrarySyncRunDto>> Start(Guid integrationId, CancellationToken cancellationToken)
    {
        var result = await syncs.StartAsync(integrationId, UserId, cancellationToken);
        return result.Status switch
        {
            LibrarySyncStartStatus.Queued => AcceptedAtAction(nameof(GetRun), new { runId = result.Run!.Id }, result.Run),
            // Missing, or not a media library: the same bare 404 as every missing row.
            LibrarySyncStartStatus.NotFound => NotFound(),
            // Switched off, a type this version can't read, or a sync already under way.
            LibrarySyncStartStatus.Conflict => this.ConflictError(result.Error),
            _ => this.UnavailableError(result.Error),
        };
    }

    [HttpGet("syncs/{runId:guid}")]
    public async Task<ActionResult<LibrarySyncRunDto>> GetRun(Guid runId, CancellationToken cancellationToken) =>
        await syncs.GetRunAsync(runId, UserId, cancellationToken) is { } run ? Ok(run) : NotFound();
}
