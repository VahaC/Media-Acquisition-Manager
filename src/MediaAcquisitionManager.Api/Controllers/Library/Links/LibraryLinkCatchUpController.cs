using MediaAcquisitionManager.Api.Auth;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Api.Services.LibraryLinks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MediaAcquisitionManager.Api.Controllers.Library.Links;

/// <summary>
/// MAM-34 — the catch-up (Vasyl's decision of 2026-10-06): the links set before the app wrote series and episodes into
/// the library and asked it to look a written item up, and any link not in it yet, brought to today's rules on the
/// owner's click — what it would send, and the click that queues it.
/// </summary>
/// <remarks>
/// The installation owner's alone, both actions, by where this controller lives (<see cref="LibraryLinksAreOwnerOnlyConvention"/>,
/// R19): the click writes into the owner's library. The writes are long work done by the background writer, one link at a
/// time (202, the rows are the queue, the reconciler); the page follows them through the links list's <c>Pending</c>
/// filter. Nothing happens without the click.
/// </remarks>
[ApiController]
[Authorize]
[Route("api/library/tmdb/catch-up")]
public class LibraryLinkCatchUpController(ILibraryLinkService links) : ControllerBase
{
    /// <summary>How many links the catch-up would write, and how many it would ask the library to look up.</summary>
    [HttpGet]
    public async Task<ActionResult<LibraryLinkCatchUpResponse>> Read(CancellationToken cancellationToken) =>
        Ok(await links.ReadCatchUpAsync(cancellationToken));

    /// <summary>Queues those links for the background writer, which writes each and asks the library to look its item up;
    /// 202 with how many it queued of each.</summary>
    [HttpPost]
    public async Task<ActionResult<LibraryLinkCatchUpResponse>> Start(CancellationToken cancellationToken) =>
        Accepted(await links.StartCatchUpAsync(cancellationToken));
}
