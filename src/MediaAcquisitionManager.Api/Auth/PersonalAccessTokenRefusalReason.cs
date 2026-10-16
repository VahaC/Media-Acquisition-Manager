namespace MediaAcquisitionManager.Api.Auth;

/// <summary>MAM-5 — why a personal access token was refused (a 403), for the log line and the sentence.</summary>
public enum PersonalAccessTokenRefusalReason
{
    /// <summary>A Read only token on a write Read and download opens: <see cref="PersonalAccessTokenErrors.ReadOnly"/>.</summary>
    ReadOnly = 1,

    /// <summary>An endpoint no token may call: <see cref="PersonalAccessTokenErrors.NotForTokens"/>.</summary>
    NotForTokens = 2,

    /// <summary>A cancel that would end the job's replacement too:
    /// <see cref="PersonalAccessTokenErrors.CancelEndsReplacement"/>.</summary>
    CancelEndsReplacement = 3,
}
