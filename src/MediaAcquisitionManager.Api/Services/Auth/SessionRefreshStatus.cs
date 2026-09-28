namespace MediaAcquisitionManager.Api.Services.Auth;

/// <summary>How a refresh ended.</summary>
public enum SessionRefreshStatus
{
    /// <summary>The refresh token was rotated and a new access token issued.</summary>
    Refreshed = 1,

    /// <summary>
    /// Another request refreshed this session a moment ago (two tabs at once): nothing changed, and
    /// the client should retry — by then its browser holds the new cookie.
    /// </summary>
    Conflict = 2,

    /// <summary>The token is unknown, the session is revoked or expired, or its user is disabled or
    /// gone. The client has to sign in again.</summary>
    Ended = 3,
}
