namespace MediaAcquisitionManager.Api.Auth;

/// <summary>
/// MAM-5 — the claims a request let in on a personal access token carries beside <c>sub</c> and <c>name</c>, the ones a
/// signed-in session's JWT carries too. Never <c>sid</c>: a token is no session, and whatever needs one is refused to
/// tokens (ADR 0040).
/// </summary>
public static class PersonalAccessTokenClaims
{
    /// <summary>The token's id: its presence marks the request as a token's.</summary>
    public const string TokenId = "pat";

    /// <summary>The token's access, a <c>PersonalAccessTokenAccess</c> name.</summary>
    public const string Access = "pat_access";
}
