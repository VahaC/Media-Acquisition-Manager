using MediaAcquisitionManager.Api.Http;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MediaAcquisitionManager.Api.Controllers;

/// <summary>
/// Answers any request under <c>/api</c> that no endpoint matched — any method, any depth — with
/// 404 <c>{ "error": "No API endpoint matches GET /api/…." }</c>.
/// </summary>
/// <remarks>
/// Without it such a request falls through to the SPA fallback and gets <c>index.html</c> with
/// 200: a typo'd URL or a call to an endpoint this build doesn't have then fails in the browser
/// as "unexpected token &lt;" while parsing HTML as JSON, far away from the cause, and a POST gets
/// a puzzling 405. Literal routes (controllers, the health endpoints) always outrank this
/// catch-all, so it only ever sees what nothing else claimed; the action has no HTTP-method
/// attribute on purpose, so it matches every method.
/// <para>
/// MAM-4 — anonymous, and says so, as every action declares its access: an unknown path must answer
/// 404 to everyone, signed in or not. Behind sign-in, a visitor's typo would come back as 401 and
/// read as an expired session.
/// </para>
/// </remarks>
[ApiController]
[AllowAnonymous]
[Route("api/{**path}")]
public class UnknownApiController : ControllerBase
{
    public IActionResult Unmatched() =>
        this.NotFoundError($"No API endpoint matches {Request.Method} {Request.Path}.");
}
