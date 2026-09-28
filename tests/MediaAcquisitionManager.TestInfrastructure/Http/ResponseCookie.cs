using Microsoft.Net.Http.Headers;

namespace MediaAcquisitionManager.TestInfrastructure.Http;

/// <summary>A cookie a response sets, as its <c>Set-Cookie</c> header describes it.</summary>
/// <param name="Name">The cookie's name.</param>
/// <param name="Value">Its value; empty when the header deletes the cookie.</param>
/// <param name="Path">The <c>Path</c> attribute, or <c>null</c>.</param>
/// <param name="Expires">The <c>Expires</c> attribute (whole seconds), or <c>null</c>.</param>
/// <param name="HttpOnly">Whether scripts are kept away from it.</param>
/// <param name="Secure">Whether the browser sends it over HTTPS only.</param>
/// <param name="SameSite">The <c>SameSite</c> attribute; <see cref="SameSiteMode.Unspecified"/> when absent.</param>
public sealed record ResponseCookie(
    string Name,
    string Value,
    string? Path,
    DateTimeOffset? Expires,
    bool HttpOnly,
    bool Secure,
    SameSiteMode SameSite)
{
    /// <summary>The <c>Cookie</c> request header that sends this cookie back, for the factory's
    /// <c>cookie</c> argument.</summary>
    public string RequestHeader => $"{Name}={Value}";
}
