namespace MediaAcquisitionManager.Api.Http;

/// <summary>
/// Adds a fixed set of hardening headers to every response — API, SPA shell and static assets
/// alike — each only when the response doesn't carry that header already.
/// </summary>
/// <remarks>
/// The headers are added in <see cref="HttpResponse.OnStarting(Func{Task})"/>, just before they are
/// sent, rather than on the way in: an endpoint that sets its own value for one of them keeps it
/// (it is present by then, so it is skipped), and a response the exception handler rewrites still
/// gets them (the handler clears the headers set so far, but not the callbacks).
/// </remarks>
public sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    /// <summary>
    /// SHA-256 of the inline theme script in <c>frontend/index.html</c> — the exact text between
    /// <c>&lt;script&gt;</c> and <c>&lt;/script&gt;</c>, UTF-8, base64. It is the SPA's only inline
    /// script (it applies the dark class before first paint, so a dark-mode visitor sees no white
    /// flash), and pinning its hash lets it run without opening <c>script-src</c> to
    /// <c>'unsafe-inline'</c>. Vite copies it into the built page byte for byte.
    /// <c>SecurityHeadersHttpTests</c> recomputes the hash from the file, so editing the script
    /// without updating this constant fails the build instead of silently breaking the theme.
    /// </summary>
    public const string ThemeScriptHash = "sha256-42OIMy9HknT1+DRP1m9jnCV7bYDMONkK9XKA3fx1qlM=";

    /// <summary>
    /// Same-origin everything: the API and the SPA share one origin, fonts are bundled rather than
    /// pulled from a CDN (air-gapped installs, no third party in the policy), images may be
    /// <c>data:</c> URIs. <c>style-src</c> keeps <c>'unsafe-inline'</c> because Radix Dialog's scroll
    /// lock (react-remove-scroll) injects a <c>&lt;style&gt;</c> element while a dialog is open;
    /// inline <em>script</em> stays forbidden, which is what the policy is really for.
    /// </summary>
    public const string ContentSecurityPolicy =
        "default-src 'self'; " +
        "script-src 'self' '" + ThemeScriptHash + "'; " +
        "style-src 'self' 'unsafe-inline'; " +
        "img-src 'self' data:; " +
        "font-src 'self'; " +
        "connect-src 'self'; " +
        "object-src 'none'; " +
        "base-uri 'self'; " +
        "form-action 'self'; " +
        "frame-ancestors 'none'";

    public Task InvokeAsync(HttpContext context)
    {
        context.Response.OnStarting(static state =>
        {
            var headers = ((HttpResponse)state).Headers;
            headers.TryAdd("X-Content-Type-Options", "nosniff");
            // frame-ancestors covers current browsers; X-Frame-Options covers the ones that
            // predate it.
            headers.TryAdd("X-Frame-Options", "DENY");
            headers.TryAdd("Referrer-Policy", "no-referrer");
            headers.TryAdd("Content-Security-Policy", ContentSecurityPolicy);
            return Task.CompletedTask;
        }, context.Response);

        return next(context);
    }
}
