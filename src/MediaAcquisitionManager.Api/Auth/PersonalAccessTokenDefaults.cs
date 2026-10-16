namespace MediaAcquisitionManager.Api.Auth;

/// <summary>MAM-5 — the names of the authentication schemes personal access tokens add (ADR 0040).</summary>
public static class PersonalAccessTokenDefaults
{
    /// <summary>The scheme of <see cref="PersonalAccessTokenAuthenticationHandler"/>, which checks a
    /// <c>Bearer mam_pat_…</c> header.</summary>
    public const string AuthenticationScheme = "PersonalAccessToken";

    /// <summary>
    /// The default scheme: a policy scheme that sends a request carrying a personal access token to
    /// <see cref="AuthenticationScheme"/> and every other request to JwtBearer (<c>"Bearer"</c>), for authenticating,
    /// challenging and forbidding alike. JwtBearer keeps its own name, so its named options still bind.
    /// </summary>
    public const string PolicyScheme = "BearerOrPersonalAccessToken";
}
