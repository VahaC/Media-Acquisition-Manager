using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Api.Serialization;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace MediaAcquisitionManager.Api.Health;

/// <summary>
/// Writes a <see cref="HealthReport"/> as a <see cref="HealthResponse"/>. The built-in writer
/// answers plain text ("Healthy"), which tells the UI nothing about which check failed.
/// </summary>
public static class HealthResponseWriter
{
    public static Task WriteAsync(HttpContext context, HealthReport report)
    {
        var response = new HealthResponse(
            report.Status,
            [.. report.Entries.Select(entry => new HealthCheckDto(entry.Key, entry.Value.Status, DescriptionOf(entry.Key, entry.Value)))]);

        // Written straight to the body, outside MVC, so the controllers' JSON options don't apply;
        // StreamingJson.Options mirrors them (camelCase, enums as strings).
        return context.Response.WriteAsJsonAsync(response, StreamingJson.Options, context.RequestAborted);
    }

    // For a check that throws or times out, the health-check service makes the exception's message
    // the description. These endpoints are anonymous and that message can carry paths, hosts and SQL,
    // so it is replaced here, for every check present and future, instead of trusting each check to
    // catch its own exceptions. The service has already logged the exception at Error.
    private static string? DescriptionOf(string name, HealthReportEntry entry) =>
        entry.Exception is null ? entry.Description : $"The {name} check failed; see the server log.";
}
