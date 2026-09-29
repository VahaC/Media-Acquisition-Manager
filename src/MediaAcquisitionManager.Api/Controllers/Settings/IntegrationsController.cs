using MediaAcquisitionManager.Api.Auth;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Api.Http;
using MediaAcquisitionManager.Api.Services.Integrations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MediaAcquisitionManager.Api.Controllers.Settings;

/// <summary>The installation owner's integrations, behind the Integrations settings page.</summary>
/// <remarks>
/// MAM-7 — owner-only as a whole, the reads included: the list names every service, address and
/// account on the owner's network, and a test sends stored credentials off the box. The writes would
/// be owner-only here without the attribute, by <see cref="SettingsWritesAreOwnerOnlyConvention"/>. A
/// connection test answers 200 whatever the service said (ADR 0005): "no, because …" is the answer to
/// "does it connect?", not a failure of this API.
/// </remarks>
[ApiController]
[Authorize]
[RequireInstallationOwner]
[Route("api/settings/integrations")]
public class IntegrationsController(IIntegrationConnectionService integrations) : ControllerBase
{
    private Guid UserId => User.GetUserId();

    [HttpGet]
    public async Task<ActionResult<IntegrationListResponse>> List([FromQuery] IntegrationListRequest request, CancellationToken cancellationToken)
    {
        var result = await integrations.ListAsync(request.Search, request.Kind, request.Page, request.PageSize, cancellationToken);
        return result.Status switch
        {
            IntegrationListStatus.Listed => Ok(result.List),
            _ => this.BadRequestError(result.Error),
        };
    }

    /// <summary>Every type of service this build connects to; fixed by the build, so never paged.</summary>
    [HttpGet("adapter-types")]
    public ActionResult<IntegrationAdapterTypeListResponse> AdapterTypes() => Ok(integrations.ListAdapterTypes());

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<IntegrationDto>> Get(Guid id, CancellationToken cancellationToken) =>
        await integrations.GetAsync(id, cancellationToken) is { } integration ? Ok(integration) : NotFound();

    [HttpPost]
    public async Task<ActionResult<IntegrationDto>> Create([FromBody] CreateIntegrationRequest request, CancellationToken cancellationToken)
    {
        var result = await integrations.CreateAsync(request, UserId, cancellationToken);
        return result.Status switch
        {
            IntegrationChangeStatus.Succeeded => CreatedAtAction(nameof(Get), new { id = result.Integration!.Id }, result.Integration),
            IntegrationChangeStatus.Invalid => this.BadRequestError(result.Error),
            _ => this.ConflictError(result.Error),
        };
    }

    /// <summary>Replaces the integration's settings; a secret or field left out is kept.</summary>
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateIntegrationRequest request, CancellationToken cancellationToken)
    {
        var result = await integrations.UpdateAsync(id, request, UserId, cancellationToken);
        return result.Status switch
        {
            IntegrationChangeStatus.Succeeded => NoContent(),
            IntegrationChangeStatus.NotFound => NotFound(),
            IntegrationChangeStatus.Invalid => this.BadRequestError(result.Error),
            // The name is taken, or this build no longer connects to the integration's type.
            _ => this.ConflictError(result.Error),
        };
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var result = await integrations.DeleteAsync(id, UserId, cancellationToken);
        return result.Status switch
        {
            IntegrationChangeStatus.Succeeded => NoContent(),
            _ => NotFound(),
        };
    }

    /// <summary>Tests the saved integration with its stored address and credentials.</summary>
    [HttpPost("{id:guid}/test")]
    public async Task<ActionResult<ConnectionTestResponse>> Test(Guid id, CancellationToken cancellationToken) =>
        ToActionResult(await integrations.TestAsync(id, cancellationToken));

    /// <summary>Tests an unsaved draft from the Add or Edit dialog; nothing is stored.</summary>
    [HttpPost("test")]
    public async Task<ActionResult<ConnectionTestResponse>> TestDraft([FromBody] TestIntegrationRequest request, CancellationToken cancellationToken) =>
        ToActionResult(await integrations.TestDraftAsync(request, cancellationToken));

    private ActionResult<ConnectionTestResponse> ToActionResult(IntegrationTestResult result) => result.Status switch
    {
        IntegrationTestStatus.Tested => Ok(result.Response),
        // The addressed integration: the same bare 404 as every missing row.
        IntegrationTestStatus.NotFound => NotFound(),
        // An id in a draft's body, not the addressed row, so the owner gets the sentence.
        IntegrationTestStatus.EditedIntegrationMissing => this.NotFoundError(result.Error),
        IntegrationTestStatus.Invalid => this.BadRequestError(result.Error),
        // This build can't connect to the type, or a stored secret can't be read.
        _ => this.ConflictError(result.Error),
    };
}
