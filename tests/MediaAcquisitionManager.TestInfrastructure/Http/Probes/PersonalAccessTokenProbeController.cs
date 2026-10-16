using MediaAcquisitionManager.Api.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MediaAcquisitionManager.TestInfrastructure.Http.Probes;

/// <summary>
/// MAM-5 — a controller as one added tomorrow would be: plain <c>[Authorize]</c>, outside <c>Controllers.Settings</c>, so
/// only the default policy's token check (<c>PersonalAccessTokenAccessHandler</c>) stands between a personal access
/// token and its writes. Loaded by <c>PipelineProbeApiFactory</c> only, so it never becomes part of the app the other
/// suites test. Every action answers at once and changes nothing.
/// </summary>
/// <remarks>
/// The app's own writes each carry a mark or sit under a convention, so without this probe a check that let unmarked
/// writes through would leave the suite green: deny by default has to be pinned on an endpoint nobody thought about.
/// </remarks>
[ApiController]
[Authorize]
[Route("api/test/token-probe")]
public class PersonalAccessTokenProbeController : ControllerBase
{
    public const string BaseUrl = "/api/test/token-probe";

    /// <summary>The route of <see cref="AnyMethod"/>, under <see cref="BaseUrl"/>.</summary>
    public const string AnyMethodUrl = BaseUrl + "/any";

    /// <summary>The route of <see cref="MarkedRead"/>, under <see cref="BaseUrl"/>.</summary>
    public const string MarkedReadUrl = BaseUrl + "/read";

    /// <summary>The route of <see cref="MarkedWrite"/>, under <see cref="BaseUrl"/>.</summary>
    public const string MarkedWriteUrl = BaseUrl + "/write";

    /// <summary>The route of <see cref="Denied"/>, under <see cref="BaseUrl"/>.</summary>
    public const string DeniedUrl = BaseUrl + "/denied";

    /// <summary>A read, open to every token.</summary>
    [HttpGet]
    public IActionResult Read() => Ok();

    /// <summary>An unmarked write: refused to every token.</summary>
    [HttpPost]
    public IActionResult Create() => NoContent();

    /// <summary>An unmarked write: refused to every token.</summary>
    [HttpPut]
    public IActionResult Replace() => NoContent();

    /// <summary>An unmarked write: refused to every token.</summary>
    [HttpDelete]
    public IActionResult Delete() => NoContent();

    /// <summary>No HTTP-method attribute, so it answers every method: a write for a token even on a GET (fail
    /// closed).</summary>
    [Route("any")]
    public IActionResult AnyMethod() => NoContent();

    /// <summary>A POST marked as a read: open to every token.</summary>
    [HttpPost("read")]
    [PersonalAccessTokenRead]
    public IActionResult MarkedRead() => Ok();

    /// <summary>A write marked for Read and download: refused to a Read only token.</summary>
    [HttpPost("write")]
    [PersonalAccessTokenWrite]
    public IActionResult MarkedWrite() => NoContent();

    /// <summary>A GET no token may call.</summary>
    [HttpGet("denied")]
    [DenyPersonalAccessToken]
    public IActionResult Denied() => Ok();
}
