using MediaAcquisitionManager.Api.Auth;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Api.Services.LibraryLinks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MediaAcquisitionManager.Api.Controllers.Library.Links;

/// <summary>
/// MAM-34 — the repair (Vasyl's decisions of 2026-10-06; the TMDb page's "Update Jellyfin", the route keeps its old name):
/// the links the library was never confirmed to show — those set before the app identified items, and any whose
/// identification was refused or failed before anything was sent — identified on the owner's click: what it would
/// identify, and the click that queues it.
/// </summary>
/// <remarks>
/// The installation owner's alone, both actions, by where this controller lives (<see cref="LibraryLinksAreOwnerOnlyConvention"/>,
/// R19): the click changes the owner's library. The identifications are long work done by the background writer, in
/// bounded lanes (202, the rows are the queue, the reconciler); the answer carries the batch's progress, which the page
/// polls through <c>GET</c> again. Nothing happens without the click.
/// </remarks>
[ApiController]
[Authorize]
[Route("api/library/tmdb/catch-up")]
public class LibraryLinkCatchUpController(ILibraryLinkService links) : ControllerBase
{
    /// <summary>How many links the repair would identify now, and the progress of the batch under way.</summary>
    [HttpGet]
    public async Task<ActionResult<LibraryLinkCatchUpResponse>> Read(CancellationToken cancellationToken) =>
        Ok(await links.ReadCatchUpAsync(cancellationToken));

    /// <summary>Queues those links under one new batch for the background writer, which identifies each; 202 with how many it
    /// queued and the batch's progress.</summary>
    [HttpPost]
    public async Task<ActionResult<LibraryLinkCatchUpResponse>> Start(CancellationToken cancellationToken) =>
        Accepted(await links.StartCatchUpAsync(cancellationToken));
}
