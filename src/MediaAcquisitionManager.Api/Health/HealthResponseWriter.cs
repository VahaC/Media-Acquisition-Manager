using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Api.Serialization;
using MediaAcquisitionManager.Api.Texts;
using MediaAcquisitionManager.Core.Texts;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace MediaAcquisitionManager.Api.Health;

/// <summary>
/// Writes a <see cref="HealthReport"/> as a <see cref="HealthResponse"/>. The built-in writer
/// answers plain text ("Healthy"), which tells the UI nothing about which check failed.
/// </summary>
public static class HealthResponseWriter
{
    /// <summary>MAM-47 (ADR 0042) — the data entry a check puts its sentence under, as a <see cref="Message"/>; the
    /// writer renders it in the request's language (a probe sends no <c>Accept-Language</c>, so it reads English).</summary>
    public const string MessageKey = "message";

    public static Task WriteAsync(HttpContext context, HealthReport report)
    {
        var texts = context.RequestServices.GetRequiredService<IRequestTexts>();
        var response = new HealthResponse(
            report.Status,
            [
                .. report.Entries.Select(entry =>
                    new HealthCheckDto(entry.Key, entry.Value.Status, DescriptionOf(texts, entry.Key, entry.Value))),
            ]);

        // Written straight to the body, outside MVC, so the controllers' JSON options don't apply;
        // StreamingJson.Options mirrors them (camelCase, enums as strings).
        return context.Response.WriteAsJsonAsync(response, StreamingJson.Options, context.RequestAborted);
    }

    // For a check that throws or times out, the health-check service makes the exception's message
    // the description. These endpoints are anonymous and that message can carry paths, hosts and SQL,
    // so it is replaced here, for every check present and future, instead of trusting each check to
    // catch its own exceptions. The service has already logged the exception at Error.
    private static string? DescriptionOf(IRequestTexts texts, string name, HealthReportEntry entry)
    {
        if (entry.Exception is not null)
            return texts.Render(Message.Of("common.health.check-threw", ("name", name)));
        return entry.Data.TryGetValue(MessageKey, out var value) && value is Message message
            ? texts.Render(message)
            : entry.Description;
    }
}
