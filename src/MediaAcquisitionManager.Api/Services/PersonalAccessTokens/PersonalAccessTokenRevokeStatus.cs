namespace MediaAcquisitionManager.Api.Services.PersonalAccessTokens;

/// <summary>MAM-5 — how a user's revoke of one of their personal access tokens ended.</summary>
public enum PersonalAccessTokenRevokeStatus
{
    /// <summary>The token is revoked now, with its audit row.</summary>
    Revoked = 1,

    /// <summary>The caller has no such token not yet revoked: there is none, it is another user's, or it was revoked
    /// already — by an earlier click, or one racing this. Nothing changed.</summary>
    NotFound = 2,
}
