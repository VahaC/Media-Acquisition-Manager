using MediaAcquisitionManager.Core.Accounts;

namespace MediaAcquisitionManager.Api.Auth;

/// <summary>
/// MAM-5 — tells a request carrying a personal access token from one carrying a session's JWT. Both arrive as
/// <c>Authorization: Bearer …</c>, so a script uses a token the way the web app uses its access token; the
/// <c>mam_pat_</c> prefix decides which handler checks it (ADR 0040).
/// </summary>
public static class PersonalAccessTokenBearer
{
    private const string BearerPrefix = "Bearer ";

    /// <summary>
    /// The value after <c>Bearer </c> when it starts with <c>mam_pat_</c>, in <paramref name="token"/>. The scheme word
    /// in any letter case and the value trimmed, as JwtBearer reads its header, so the two never disagree about which
    /// of them a header is for.
    /// </summary>
    public static bool TryRead(HttpRequest request, out string token)
    {
        var authorization = request.Headers.Authorization.ToString();
        if (authorization.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var value = authorization[BearerPrefix.Length..].Trim();
            if (value.StartsWith(PersonalAccessTokenRules.SecretPrefix, StringComparison.Ordinal))
            {
                token = value;
                return true;
            }
        }

        token = "";
        return false;
    }
}
