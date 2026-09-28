using System.Text.Json;
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
    public static async Task<string?> ReadErrorAsync(this HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(body, cancellationToken: cancellationToken);
        return document.RootElement.GetProperty("error").GetString();
    }
}
