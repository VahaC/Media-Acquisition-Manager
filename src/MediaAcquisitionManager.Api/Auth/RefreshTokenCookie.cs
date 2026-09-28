namespace MediaAcquisitionManager.Api.Auth;

/// <summary>
/// The cookie that carries the refresh token: <c>HttpOnly</c>, <c>SameSite=Strict</c>, and sent only
/// to <c>/api/auth</c>.
/// </summary>
/// <remarks>
/// <para>
/// MAM-4 — HttpOnly keeps the long-lived token out of reach of any script on the page, so an XSS
/// bug can at worst use the 15-minute access token it finds in memory, not take the session with
/// it. SameSite=Strict means a cross-site page can't make the browser refresh or sign out on the
/// user's behalf. The path keeps it off every other request: only refresh and sign-out need it.
/// </para>
/// <para>
/// <c>Secure</c> is set only when the request came over HTTPS. The default install is plain HTTP on
/// a LAN, and a browser refuses to store a <c>Secure</c> cookie from an <c>http://</c> origin — the
/// user would sign in and be signed out again at the first refresh. Behind a TLS-terminating proxy
/// the app sees plain HTTP as well, so the flag stays off there until the app is told to trust the
/// proxy's forwarded headers; HttpOnly and SameSite apply either way.
/// </para>
/// </remarks>
public static class RefreshTokenCookie
{
    public const string Name = "mam_refresh";

    public const string Path = "/api/auth";

    /// <summary>The refresh token the request carries, or <c>null</c>.</summary>
    public static string? Read(HttpRequest request) => request.Cookies[Name];

    /// <summary>Hands the browser <paramref name="refreshToken"/>, kept until the session would
    /// expire.</summary>
    public static void Append(HttpContext context, string refreshToken, DateTime expiresUtc) =>
        context.Response.Cookies.Append(
            Name, refreshToken, OptionsFor(context.Request, new DateTimeOffset(DateTime.SpecifyKind(expiresUtc, DateTimeKind.Utc))));

    /// <summary>Tells the browser to drop the cookie: the same name and path with a date in the past.</summary>
    public static void Delete(HttpContext context) =>
        context.Response.Cookies.Delete(Name, OptionsFor(context.Request, expires: null));

    private static CookieOptions OptionsFor(HttpRequest request, DateTimeOffset? expires) => new()
    {
        HttpOnly = true,
        SameSite = SameSiteMode.Strict,
        Path = Path,
        Secure = request.IsHttps,
        Expires = expires,
        // Signing in is the user's own request, not tracking; a consent policy added later must not
        // be able to drop the cookie that keeps them signed in.
        IsEssential = true,
    };
}
