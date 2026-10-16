using Microsoft.AspNetCore.Authorization;

namespace MediaAcquisitionManager.Api.Auth;

/// <summary>
/// MAM-5 — a failed <see cref="PersonalAccessTokenAccessRequirement"/>: why, and the sentence the 403 carries, which
/// <see cref="PersonalAccessTokenRefusalResultHandler"/> writes. A plain forbid would answer an empty 403.
/// </summary>
/// <param name="handler">The handler that refused.</param>
/// <param name="reason">Why.</param>
public sealed class PersonalAccessTokenFailureReason(IAuthorizationHandler handler, PersonalAccessTokenRefusalReason reason)
    : AuthorizationFailureReason(handler, SentenceOf(reason))
{
    /// <summary>Why the token was refused.</summary>
    public PersonalAccessTokenRefusalReason Reason { get; } = reason;

    /// <summary>The 403's sentence for <paramref name="reason"/>.</summary>
    public static string SentenceOf(PersonalAccessTokenRefusalReason reason) => reason switch
    {
        PersonalAccessTokenRefusalReason.ReadOnly => PersonalAccessTokenErrors.ReadOnly,
        PersonalAccessTokenRefusalReason.CancelEndsReplacement => PersonalAccessTokenErrors.CancelEndsReplacement,
        _ => PersonalAccessTokenErrors.NotForTokens,
    };
}
