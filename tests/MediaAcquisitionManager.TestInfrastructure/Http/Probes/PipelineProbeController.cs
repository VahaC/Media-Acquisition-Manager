using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace MediaAcquisitionManager.TestInfrastructure.Http.Probes;

/// <summary>
/// Test-only endpoints, loaded by <see cref="PipelineProbeApiFactory"/> alone. Each one puts the
/// shared pipeline into a state no production endpoint can be asked to produce.
/// </summary>
[ApiController]
[Route("api/test/pipeline-probe")]
public class PipelineProbeController : ControllerBase
{
    public const string BaseUrl = "/api/test/pipeline-probe";

    /// <summary>What <see cref="Throw"/> throws. It looks like the details a real exception carries
    /// (a file path, a connection string), which is exactly what must never reach the client.</summary>
    public const string ThrownMessage = @"Probe failure: cannot open Data Source=C:\probe\private\mam.db";

    /// <summary>The instant <see cref="Instant"/> returns — <see cref="DateTimeKind.Unspecified"/>, the
    /// way EF Core's SQLite provider hands every stored DateTime back.</summary>
    public static readonly DateTime ProbeInstant = new(2026, 3, 29, 1, 30, 15, DateTimeKind.Unspecified);

    /// <summary>The Referrer-Policy <see cref="PresetReferrerPolicy"/> sets itself.</summary>
    public const string OwnReferrerPolicy = "strict-origin-when-cross-origin";

    /// <summary>The largest body <see cref="LimitedBody"/> accepts.</summary>
    public const int BodyLimitBytes = 16;

    /// <summary>An exception nothing catches — for the global exception handler.</summary>
    [HttpGet("throw")]
    public IActionResult Throw() => throw new InvalidOperationException(ThrownMessage);

    /// <summary>
    /// A body over <see cref="BodyLimitBytes"/> — for the exception handler's client-fault path.
    /// TestServer enforces no request-body size (it has no <c>IHttpMaxRequestBodySizeFeature</c>, so
    /// <c>[RequestSizeLimit]</c> does nothing here), so the probe applies its own limit and throws
    /// exactly what Kestrel throws past <c>MaxRequestBodySize</c>.
    /// </summary>
    [HttpPost("limited-body")]
    public async Task<IActionResult> LimitedBody(CancellationToken cancellationToken)
    {
        var buffer = new byte[BodyLimitBytes + 1];
        var read = await Request.Body.ReadAtLeastAsync(buffer, buffer.Length, throwOnEndOfStream: false, cancellationToken);
        if (read > BodyLimitBytes)
            throw new BadHttpRequestException(
                $"Request body too large. The max request body size is {BodyLimitBytes} bytes.",
                StatusCodes.Status413PayloadTooLarge);

        return NoContent();
    }

    /// <summary>A DateTime without a zone and an enum — for the JSON options.</summary>
    [HttpGet("instant")]
    public ActionResult<PipelineProbeInstantResponse> Instant() =>
        Ok(new PipelineProbeInstantResponse(ProbeInstant, PipelineProbeKind.Second));

    /// <summary>A response that sets its own Referrer-Policy — the security headers must keep it.</summary>
    [HttpGet("referrer-policy")]
    public IActionResult PresetReferrerPolicy()
    {
        Response.Headers["Referrer-Policy"] = OwnReferrerPolicy;
        return NoContent();
    }
}
