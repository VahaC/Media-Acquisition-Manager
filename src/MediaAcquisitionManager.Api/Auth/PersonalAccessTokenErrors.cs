namespace MediaAcquisitionManager.Api.Auth;

/// <summary>
/// MAM-5 — the sentences a personal access token is refused with (403), each saying what to do instead and where the
/// README explains it (ADR 0040). English constants until MAM-47 localises the server's sentences.
/// </summary>
public static class PersonalAccessTokenErrors
{
    /// <summary>A Read only token on one of the three writes Read and download opens.</summary>
    public const string ReadOnly =
        "This access token is read only. To send downloads, cancel following or mark Not interested from a script, create a token with “Read and download” access on your Profile page; see “Scripts and the API” in the README.";

    /// <summary>Any token on an endpoint no token may call: every write but the three, and the account, token management
    /// and settings, reads included.</summary>
    public const string NotForTokens =
        "An access token can't do this. A token reads, and with “Read and download” access also sends downloads, cancels following and marks Not interested; everything else is done in the web app. See “Scripts and the API” in the README.";

    /// <summary>A token's cancel of a job whose replacement is still followed, which the cancel would end too.</summary>
    public const string CancelEndsReplacement =
        "This job also replaces a copy in the library, and an access token never touches a replacement. Cancel it on the Jobs page of the web app; see “Scripts and the API” in the README.";
}
