using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Api.Texts;
using MediaAcquisitionManager.Core.Texts;

namespace MediaAcquisitionManager.Api.Http;

/// <summary>
/// MAM-47 (ADR 0042) — the one writer of an error body, <c>{ "error": "…", "code": "…" }</c>, for controllers
/// (<see cref="ApiErrorExtensions"/>) and for the writers outside MVC alike (the 401 challenge, the exception handler,
/// the rate limiter), so every failure answers in one shape and one language rule.
/// </summary>
public static class ApiErrorBody
{
    /// <summary><paramref name="message"/> as an error body, written in the language <paramref name="context"/>'s request
    /// asks for; the code is the message's key. Never throws in production: the lenient renderer falls back instead.</summary>
    public static ApiErrorResponse For(HttpContext context, Message message)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(message);
        var texts = context.RequestServices.GetRequiredService<IRequestTexts>();
        return new ApiErrorResponse(texts.Render(message), message.Key);
    }

    /// <summary>Writes <paramref name="message"/> as the error body of a <paramref name="statusCode"/> answer.</summary>
    public static Task WriteAsync(HttpContext context, int statusCode, Message message)
    {
        // Rendered before the status is set, so nothing about the answer has started if rendering fails in a test host.
        var body = For(context, message);
        context.Response.StatusCode = statusCode;
        return context.Response.WriteAsJsonAsync(body, context.RequestAborted);
    }
}
