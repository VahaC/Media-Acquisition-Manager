using MediaAcquisitionManager.Api.Auth;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Api.Http;
using MediaAcquisitionManager.Api.Services.Files;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MediaAcquisitionManager.Api.Controllers.Settings;

/// <summary>The allowed roots, behind the Media folders settings page (MAM-9).</summary>
/// <remarks>
/// MAM-9 — owner-only as a whole, the read included: the list says how the host's folders are laid out,
/// which is the owner's business like the integrations' addresses. The write would be owner-only here
/// without the attribute, by <see cref="SettingsWritesAreOwnerOnlyConvention"/>. MAM-24 — the page's replacement
/// switch and per-folder ticks are read and saved here with the roots (ADR 0033).
/// </remarks>
[ApiController]
[Authorize]
[RequireInstallationOwner]
[Route("api/settings/allowed-roots")]
public class AllowedRootsController(IAllowedRootsService allowedRoots) : ControllerBase
{
    private Guid UserId => User.GetUserId();

    [HttpGet]
    public async Task<ActionResult<AllowedRootsResponse>> Get(CancellationToken cancellationToken) =>
        Ok(await allowedRoots.GetAsync(cancellationToken));

    /// <summary>Replaces the whole list, the replacement switch and the ticks; an empty list allows nothing, and a
    /// missing switch or tick list is off and none (MAM-24, fail closed).</summary>
    [HttpPut]
    public async Task<IActionResult> Update([FromBody] UpdateAllowedRootsRequest request, CancellationToken cancellationToken)
    {
        var result = await allowedRoots.UpdateAsync(
            request.Paths ?? [],
            request.ReplacementEnabled ?? false,
            request.ReplacementFolders ?? [],
            UserId,
            cancellationToken);
        return result.Status switch
        {
            AllowedRootsUpdateStatus.Saved => NoContent(),
            _ => this.BadRequestError(result.Error),
        };
    }
}
