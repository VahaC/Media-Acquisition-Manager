using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

// MAM-4 — declared in the settings namespace, not the folder's: SettingsWritesAreOwnerOnlyConvention
// matches controllers by namespace, and this one has to be matched the way a real settings
// controller is.
namespace MediaAcquisitionManager.Api.Controllers.Settings.Probes;

/// <summary>
/// A settings controller that relies on <c>SettingsWritesAreOwnerOnlyConvention</c> alone — no
/// <c>[RequireInstallationOwner]</c> — the way a settings controller someone forgot to mark would.
/// Loaded by <c>PipelineProbeApiFactory</c> only, so it never becomes part of the app the other
/// suites test.
/// </summary>
/// <remarks>
/// The app's own settings controller (<c>UsersController</c>) is owner-only as a whole, so its
/// attribute answers every 403 before the convention would; without this probe, removing the
/// convention would leave the suite green.
/// </remarks>
[ApiController]
[Authorize]
[Route("api/test/settings-probe")]
public class SettingsProbeController : ControllerBase
{
    public const string BaseUrl = "/api/test/settings-probe";

    /// <summary>The route of <see cref="AnyMethod"/>, under <see cref="BaseUrl"/>.</summary>
    public const string AnyMethodUrl = BaseUrl + "/any";

    /// <summary>A read, which the convention leaves open to every signed-in user.</summary>
    [HttpGet]
    public IActionResult Read() => Ok();

    [HttpPost]
    public IActionResult Create() => NoContent();

    [HttpPut]
    public IActionResult Replace() => NoContent();

    [HttpDelete]
    public IActionResult Delete() => NoContent();

    /// <summary>No HTTP-method attribute, so it answers every method: the convention must count it as
    /// a write (fail closed).</summary>
    [Route("any")]
    public IActionResult AnyMethod() => NoContent();
}
