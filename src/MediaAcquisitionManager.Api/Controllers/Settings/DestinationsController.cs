using MediaAcquisitionManager.Api.Auth;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Api.Http;
using MediaAcquisitionManager.Api.Services.Destinations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MediaAcquisitionManager.Api.Controllers.Settings;

/// <summary>The download destinations, behind the Destinations settings page (MAM-11).</summary>
/// <remarks>
/// <para>
/// MAM-11 — owner-only as a whole, the reads included: a row quotes the client's folders, the owner's
/// path mappings and what the client said, the owner's business like the integrations' addresses. The
/// writes would be owner-only here without the attribute, by <see cref="SettingsWritesAreOwnerOnlyConvention"/>.
/// </para>
/// <para>
/// Every read asks the download clients live and answers 200 whatever they said (ADR 0005): a client that
/// couldn't be asked is part of each destination's check, and of the list of what it offers. A save is
/// different (decision 3): it needs the client to confirm the category, so a client that was asked and
/// couldn't answer is 502 with its sentence, and one that can't be asked at all is 409.
/// </para>
/// </remarks>
[ApiController]
[Authorize]
[RequireInstallationOwner]
[Route("api/settings/destinations")]
public class DestinationsController(IDestinationService destinations) : ControllerBase
{
    /// <summary>MAM-30 — a <c>destinationId</c> that isn't an id the app gives.</summary>
    private const string EditedDestinationInvalidError = "The destination being edited isn't a valid id. Reload the page and open it again.";

    private Guid UserId => User.GetUserId();

    [HttpGet]
    public async Task<ActionResult<DestinationListResponse>> List([FromQuery] DestinationListRequest request, CancellationToken cancellationToken)
    {
        var result = await destinations.ListAsync(request.Search, request.Page, request.PageSize, cancellationToken);
        return result.Status switch
        {
            DestinationListStatus.Listed => Ok(result.List),
            _ => this.BadRequestError(result.Error),
        };
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<DestinationDto>> Get(Guid id, CancellationToken cancellationToken) =>
        await destinations.GetAsync(id, cancellationToken) is { } destination ? Ok(destination) : NotFound();

    [HttpPost]
    public async Task<ActionResult<DestinationDto>> Create([FromBody] CreateDestinationRequest request, CancellationToken cancellationToken)
    {
        var result = await destinations.CreateAsync(request, UserId, cancellationToken);
        return result.Status switch
        {
            DestinationChangeStatus.Succeeded => CreatedAtAction(nameof(Get), new { id = result.Destination!.Id }, result.Destination),
            _ => Failure(result),
        };
    }

    /// <summary>Replaces the destination's settings; the default left out is kept.</summary>
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateDestinationRequest request, CancellationToken cancellationToken)
    {
        var result = await destinations.UpdateAsync(id, request, UserId, cancellationToken);
        return result.Status switch
        {
            DestinationChangeStatus.Succeeded => NoContent(),
            _ => Failure(result),
        };
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var result = await destinations.DeleteAsync(id, UserId, cancellationToken);
        return result.Status switch
        {
            DestinationChangeStatus.Succeeded => NoContent(),
            _ => NotFound(),
        };
    }

    /// <summary>What the download client offers right now, each category checked — 200 whatever the
    /// client said; a bare 404 when the id is not a download client integration. MAM-30 — with the Edit dialog's
    /// <c>destinationId</c>, a folder client lists that destination's own folder too; a malformed id is 400.</summary>
    [HttpGet("client-destinations/{downloadClientId:guid}")]
    public async Task<ActionResult<ClientDestinationListResponse>> ClientDestinations(
        Guid downloadClientId, [FromQuery] ClientDestinationListRequest request, CancellationToken cancellationToken)
    {
        Guid? editedDestinationId = null;
        if (!string.IsNullOrEmpty(request.DestinationId))
        {
            if (!Guid.TryParse(request.DestinationId, out var parsed))
                return this.BadRequestError(EditedDestinationInvalidError);
            editedDestinationId = parsed;
        }

        return await destinations.ListClientDestinationsAsync(downloadClientId, editedDestinationId, cancellationToken) is { } list ? Ok(list) : NotFound();
    }

    /// <summary>MAM-32 — every release source's sections, each with the destination it is mapped to: 200
    /// whatever the sources said, a source that couldn't be asked answering its own sentence beside the
    /// others' sections.</summary>
    [HttpGet("sections")]
    public async Task<ActionResult<DestinationSectionListResponse>> Sections(CancellationToken cancellationToken) =>
        Ok(await destinations.ListSectionsAsync(cancellationToken));

    private ActionResult Failure(DestinationChangeResult result) => result.Status switch
    {
        // The addressed destination: the same bare 404 as every missing row.
        DestinationChangeStatus.NotFound => NotFound(),
        DestinationChangeStatus.Invalid => this.BadRequestError(result.Error),
        // A client id in the body, not the addressed row, so the owner gets the sentence; (MAM-32) a
        // section's source id too.
        DestinationChangeStatus.DownloadClientMissing or DestinationChangeStatus.SourceMissing => this.NotFoundError(result.Error),
        // Asked, and it couldn't answer: the client's failure, in its own sentence.
        DestinationChangeStatus.ClientFailed => this.BadGatewayError(result.Error),
        // The name is taken, the client can't be asked (switched off, a type this build can't use, a
        // secret it can't read), or it has no such category.
        _ => this.ConflictError(result.Error),
    };
}
