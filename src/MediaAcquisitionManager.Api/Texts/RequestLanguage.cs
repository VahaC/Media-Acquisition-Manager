using Microsoft.Net.Http.Headers;

namespace MediaAcquisitionManager.Api.Texts;

/// <summary>
/// MAM-47 (ADR 0042) — the language a request asks for: its <c>Accept-Language</c>, reduced to <c>en</c> or <c>uk</c>,
/// else English. The web app sends its UI language (<c>lib/api.ts</c>); a token, an MCP client or a script gets English
/// unless it sends the header.
/// </summary>
/// <remarks>
/// No session, token or <c>Users.Language</c> is read (Vasyl's decision), so a 401, a sign-in failure, a token refusal or
/// a 429 depends only on the request's own header, and the anti-probing answers stay the same bytes for every cause
/// (ADR 0002, ADR 0040). Lazy, not a middleware: the 401 challenge, the token's 429 and 403 run inside authentication
/// and authorization, and the exception handler is outermost. No <c>CultureInfo.CurrentCulture</c> change and no
/// <c>RequestLocalization</c>: nothing else of the request formats by culture.
/// </remarks>
public static class RequestLanguage
{
    private const string English = "en";
    private const string Ukrainian = "uk";

    /// <summary>The primary subtag of the pseudo-language's tag, <c>qps-ploc</c>.</summary>
    private const string Pseudo = "qps";

    /// <summary>Where a request's language is kept once worked out.</summary>
    private static readonly object ItemsKey = new();

    /// <summary>
    /// The language <paramref name="context"/>'s request asks for: the <c>Accept-Language</c> ranges ordered by quality
    /// (stable; malformed ranges and <c>q=0</c> skipped), each reduced to its primary subtag (<c>uk-UA</c> is
    /// <c>uk</c>); the first of <c>en</c>, <c>uk</c> — and <c>qps</c> when <paramref name="pseudoAllowed"/> — wins; none
    /// is English. Worked out once per request.
    /// </summary>
    public static TextLanguage Of(HttpContext context, bool pseudoAllowed)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.Items.TryGetValue(ItemsKey, out var cached) && cached is TextLanguage known)
            return known;

        var language = Resolve(context.Request, pseudoAllowed);
        context.Items[ItemsKey] = language;
        return language;
    }

    private static TextLanguage Resolve(HttpRequest request, bool pseudoAllowed)
    {
        // The typed parser skips a malformed range instead of refusing the whole header.
        var ranges = request.GetTypedHeaders().AcceptLanguage
            .Where(range => (range.Quality ?? 1) > 0)
            .OrderByDescending(range => range.Quality ?? 1);
        foreach (var range in ranges)
        {
            var tag = range.Value.Value ?? "";
            var dash = tag.IndexOf('-');
            var primary = (dash < 0 ? tag : tag[..dash]).ToLowerInvariant();
            switch (primary)
            {
                case English:
                    return TextLanguage.English;
                case Ukrainian:
                    return TextLanguage.Ukrainian;
                case Pseudo when pseudoAllowed:
                    return TextLanguage.Pseudo;
            }
        }

        return TextLanguage.English;
    }
}
