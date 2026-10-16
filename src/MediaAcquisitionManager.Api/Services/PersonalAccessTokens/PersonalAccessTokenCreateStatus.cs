namespace MediaAcquisitionManager.Api.Services.PersonalAccessTokens;

/// <summary>MAM-5 — how creating a personal access token ended.</summary>
public enum PersonalAccessTokenCreateStatus
{
    /// <summary>The token was created, with its audit row; the result carries its secret, once.</summary>
    Created = 1,

    /// <summary>A field is missing or too long, which the error names; nothing was stored.</summary>
    Invalid = 2,

    /// <summary>The caller's session ended — revoked or expired, its user disabled or deleted — after the request was let
    /// in on it, typically by an owner's reset or disable committing meanwhile; nothing was stored, and the caller is
    /// answered as any ended session is (ADR 0002).</summary>
    SessionEnded = 3,
}
