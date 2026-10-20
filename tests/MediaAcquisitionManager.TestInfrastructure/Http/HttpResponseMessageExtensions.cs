using System.Text.Json;
using MediaAcquisitionManager.TestInfrastructure.Texts;
using Microsoft.Net.Http.Headers;

namespace MediaAcquisitionManager.TestInfrastructure.Http;

public static class HttpResponseMessageExtensions
{
    /// <summary>
    /// The cookie named <paramref name="name"/> that the response sets, with its attributes, or
    /// <c>null</c> when it sets none. Parsed with ASP.NET Core's own <c>Set-Cookie</c> parser, the
    /// way a browser reads the header rather than the way the app meant to write it.
    /// </summary>
    /// <exception cref="InvalidOperationException">The response sets the cookie more than once — a
    /// bug a browser would resolve silently in favour of the last header.</exception>
    public static ResponseCookie? ReadSetCookie(this HttpResponseMessage response, string name)
    {
        if (!response.Headers.TryGetValues(HeaderNames.SetCookie, out var headers))
            return null;

        var matches = headers
            .Select(header => SetCookieHeaderValue.Parse(header))
            .Where(cookie => cookie.Name.Equals(name, StringComparison.Ordinal))
            .ToList();

        return matches switch
        {
            [] => null,
            [var cookie] => new ResponseCookie(
                cookie.Name.ToString(),
                cookie.Value.ToString(),
                cookie.Path.HasValue ? cookie.Path.ToString() : null,
                cookie.Expires,
                cookie.HttpOnly,
                cookie.Secure,
                cookie.SameSite),
            _ => throw new InvalidOperationException($"The response sets the {name} cookie {matches.Count} times."),
        };
    }

    /// <summary>
    /// The <c>error</c> sentence of a <c>{ "error": "…" }</c> body. Read by exact property name, so a
    /// body that drifts to <c>Error</c> or <c>message</c> fails instead of deserializing to null.
    /// </summary>
    /// <remarks>
    /// MAM-47 (ADR 0042) — when the request asked for the pseudo-language (the pseudo pass's default), the sentence must
    /// be one wrapped message and is returned as its English, so every English assert on an error body proves the
    /// sentence came from the locale files. A request that asked for <c>uk</c> or <c>en</c> reads its sentence as it is.
    /// </remarks>
    public static async Task<string?> ReadErrorAsync(this HttpResponseMessage response, CancellationToken cancellationToken)
    {
        using var document = await ReadBodyAsync(response, cancellationToken);
        return ErrorOf(response, document.RootElement);
    }

    /// <summary>
    /// MAM-47 (ADR 0042) — the <c>code</c> of an error body: the sentence's key in the server's locale files, the same in
    /// every language. Read by exact property name, like <see cref="ReadErrorAsync"/>.
    /// </summary>
    public static async Task<string?> ReadErrorCodeAsync(this HttpResponseMessage response, CancellationToken cancellationToken)
    {
        using var document = await ReadBodyAsync(response, cancellationToken);
        return document.RootElement.GetProperty("code").GetString();
    }

    /// <summary>
    /// MAM-47 (ADR 0042) — both parts of a <c>{ "error": "…", "code": "…" }</c> body: the sentence (its English in the
    /// pseudo pass, as <see cref="ReadErrorAsync"/> reads it) and its key.
    /// </summary>
    public static async Task<(string Error, string Code)> ReadErrorBodyAsync(
        this HttpResponseMessage response, CancellationToken cancellationToken)
    {
        using var document = await ReadBodyAsync(response, cancellationToken);
        var error = ErrorOf(response, document.RootElement)
            ?? throw new InvalidOperationException("The error body's \"error\" is null.");
        var code = document.RootElement.GetProperty("code").GetString()
            ?? throw new InvalidOperationException("The error body's \"code\" is null.");
        return (error, code);
    }

    private static async Task<JsonDocument> ReadBodyAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(response);
        await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonDocument.ParseAsync(body, cancellationToken: cancellationToken);
    }

    private static string? ErrorOf(HttpResponseMessage response, JsonElement root)
    {
        var error = root.GetProperty("error").GetString();
        return error is not null && AcceptLanguageHandler.AskedForPseudo(response.RequestMessage)
            ? PseudoText.Unwrap(error, "the error body")
            : error;
    }
}
