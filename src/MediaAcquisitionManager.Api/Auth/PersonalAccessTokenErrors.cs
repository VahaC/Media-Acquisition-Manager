using MediaAcquisitionManager.Core.Texts;

namespace MediaAcquisitionManager.Api.Auth;

/// <summary>
/// MAM-5 — the sentences a personal access token is refused with (403), each saying what to do instead and where the
/// README explains it (ADR 0040). MAM-47 (ADR 0042) — messages of <c>account.token.*</c>, written in the language the
/// request asks for; the body's <c>code</c> is the key, the same in every language, for a script to match on.
/// </summary>
public static class PersonalAccessTokenErrors
{
    /// <summary>A Read only token on one of the three writes Read and download opens.</summary>
    public static readonly Message ReadOnly = Message.Of("account.token.read-only");

    /// <summary>Any token on an endpoint no token may call: every write but the three, and the account, token management
    /// and settings, reads included.</summary>
    public static readonly Message NotForTokens = Message.Of("account.token.not-for-tokens");

    /// <summary>A token's cancel of a job whose replacement is still followed, which the cancel would end too.</summary>
    public static readonly Message CancelEndsReplacement = Message.Of("account.token.cancel-ends-replacement");

    /// <summary>MAM-45 — a signed-in session's access token on the MCP endpoint, which takes personal access tokens
    /// alone.</summary>
    public static readonly Message SessionNotAccepted = Message.Of("account.token.session-not-accepted");

    /// <summary>The 403's message for <paramref name="reason"/>: the HTTP API's refusal, the MCP access gate's and the
    /// session's on <c>/mcp</c> alike.</summary>
    public static Message MessageOf(PersonalAccessTokenRefusalReason reason) => reason switch
    {
        PersonalAccessTokenRefusalReason.ReadOnly => ReadOnly,
        PersonalAccessTokenRefusalReason.CancelEndsReplacement => CancelEndsReplacement,
        PersonalAccessTokenRefusalReason.SessionNotAccepted => SessionNotAccepted,
        _ => NotForTokens,
    };
}
