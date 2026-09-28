using System.Diagnostics;
using Microsoft.AspNetCore.Diagnostics;

namespace MediaAcquisitionManager.Api.Http;

/// <summary>
/// The answer to anything that escapes a controller or a middleware: 500 with the same
/// <c>{ "error": "…" }</c> body every other failure uses, quoting the trace id. A client fault that
/// Kestrel or MVC reports as <see cref="BadHttpRequestException"/> keeps its own 4xx instead.
/// </summary>
/// <remarks>
/// The default is a bare 500 with an empty body, which the frontend can only show as "something
/// went wrong" and which leaves nothing to match a report against the log. The trace id closes that
/// gap: the user quotes it, the Error log line below carries it. The exception text itself never
/// goes to the client — it can hold file paths, SQL and configuration values, and this answer
/// reaches anonymous callers.
/// </remarks>
public sealed class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        // Kestrel and MVC throw this for the client's own faults — a body over the size limit (413),
        // a truncated or malformed one — and it carries the right 4xx. Answering 500 would blame the
        // server, and an Error per oversized upload would ship noise off the box. Information keeps a
        // trace for someone debugging a client without raising an alarm.
        if (exception is BadHttpRequestException badRequest)
        {
            logger.LogInformation(
                "Refused {Method} {Path} with {StatusCode}: {Reason}",
                httpContext.Request.Method,
                httpContext.Request.Path,
                badRequest.StatusCode,
                badRequest.Message);

            httpContext.Response.StatusCode = badRequest.StatusCode;
            await httpContext.Response.WriteAsJsonAsync(
                new { error = DescribeClientFault(badRequest.StatusCode) }, cancellationToken);
            return true;
        }

        // The W3C trace id when the request has an activity (it does whenever logging is on), so the
        // quoted id is the one log sinks index; the connection-level id otherwise.
        var traceId = Activity.Current?.TraceId.ToString() ?? httpContext.TraceIdentifier;

        logger.LogError(
            exception,
            "Unhandled exception on {Method} {Path}, trace id {TraceId}.",
            httpContext.Request.Method,
            httpContext.Request.Path,
            traceId);

        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;
        await httpContext.Response.WriteAsJsonAsync(
            new { error = $"The server failed to process this request. Quote trace id {traceId} when you report it." },
            cancellationToken);
        return true;
    }

    // Fixed sentences rather than the exception's message, like the 500 path: the message is
    // server-side wording (limits in bytes, parser internals), not something to show a user.
    private static string DescribeClientFault(int statusCode) => statusCode switch
    {
        StatusCodes.Status413PayloadTooLarge => "The request body is larger than this server accepts.",
        _ => "The server could not read this request.",
    };
}
