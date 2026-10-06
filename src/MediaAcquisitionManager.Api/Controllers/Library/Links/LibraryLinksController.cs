using MediaAcquisitionManager.Api.Auth;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Api.Http;
using MediaAcquisitionManager.Api.Services.LibraryLinks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace MediaAcquisitionManager.Api.Controllers.Library.Links;

/// <summary>MAM-34 — a library work's TMDb link: its dialog, setting or replacing it, removing it, and writing it into
/// the library again.</summary>
/// <remarks>
/// <para>
/// The installation owner's alone, every action, by where this controller lives (<see cref="LibraryLinksAreOwnerOnlyConvention"/>,
/// R19): a link changes the owner's library, and the dialog says why the owner's library refused a write. Members see a
/// linked work's ids on its card (<c>LibraryController</c>).
/// </para>
/// <para>
/// A save answers once the link is saved and its library written, with the dialog as it stands then; a write that
/// failed or wasn't confirmed is the item's state, not the request's failure — the link counts in the app either way.
/// </para>
/// </remarks>
[ApiController]
[Authorize]
[Route("api/library/works/{id:guid}/tmdb")]
public class LibraryLinksController(ILibraryLinkService links) : ControllerBase
{
    private Guid UserId => User.GetUserId();

    /// <summary>The work's TMDb dialog: the work, its items and their links, and the suggestions. A bare 404 without
    /// such a work.</summary>
    [HttpGet]
    public async Task<ActionResult<LibraryLinkDialogResponse>> Dialog(Guid id, CancellationToken cancellationToken) =>
        Answer(await links.ReadDialogAsync(id, cancellationToken));

    /// <summary>
    /// Links the work's items (R17: the ones named, or every one) to a TMDb film or show, or replaces their link, and
    /// writes each into its library. 404 with the sentence for an id TMDb doesn't have (R18); 409 without a catalog, a
    /// library item, or while a write is under way; 502 when TMDb can't be read.
    /// </summary>
    [HttpPut]
    public async Task<ActionResult<LibraryLinkDialogResponse>> Set(
        Guid id,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] LibraryLinkRequest? request,
        CancellationToken cancellationToken) =>
        Answer(await links.SetAsync(id, request, UserId, cancellationToken));

    /// <summary>
    /// Removes the work's links, each after its library item is put back as the app found it (R11); 204 when the work
    /// has none. 502 when a library can't be changed back — that link stays —, 409 when it is switched off.
    /// </summary>
    [HttpDelete]
    public async Task<ActionResult<LibraryLinkRemoveResponse>> Remove(Guid id, CancellationToken cancellationToken)
    {
        var result = await links.RemoveAsync(id, UserId, cancellationToken);
        return result.Status == LibraryLinkStatus.Succeeded ? Ok(result.Response) : Failure(result.Status, result.Error);
    }

    /// <summary>Writes the work's links into their libraries again (Retry, Write again); 409 when there is nothing to
    /// write, the library is switched off, or a write is under way.</summary>
    [HttpPost("write")]
    public async Task<ActionResult<LibraryLinkDialogResponse>> Write(Guid id, CancellationToken cancellationToken) =>
        Answer(await links.WriteAgainAsync(id, UserId, cancellationToken));

    private ActionResult Answer(LibraryLinkResult result) =>
        result.Status == LibraryLinkStatus.Succeeded ? Ok(result.Dialog) : Failure(result.Status, result.Error);

    private ActionResult Failure(LibraryLinkStatus status, string? error) => status switch
    {
        LibraryLinkStatus.NotFound => NotFound(),
        LibraryLinkStatus.Invalid => this.BadRequestError(error),
        LibraryLinkStatus.CatalogWorkNotFound => this.NotFoundError(error),
        LibraryLinkStatus.Conflict => this.ConflictError(error),
        LibraryLinkStatus.NothingToRemove => NoContent(),
        _ => this.BadGatewayError(error),
    };
}
