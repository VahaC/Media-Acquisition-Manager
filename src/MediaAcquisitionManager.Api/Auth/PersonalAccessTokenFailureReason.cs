using Microsoft.AspNetCore.Authorization;

namespace MediaAcquisitionManager.Api.Auth;

/// <summary>
/// MAM-5 — a failed <see cref="PersonalAccessTokenAccessRequirement"/>: why, which
/// <see cref="PersonalAccessTokenRefusalResultHandler"/> answers with its sentence. A plain forbid would answer an empty
/// 403.
/// </summary>
/// <remarks>
/// MAM-47 (ADR 0042) — it carries the reason alone: the sentence is written when the 403 is, in the language that
/// request asks for (<see cref="PersonalAccessTokenErrors.MessageOf"/>). The base class's message, which nothing shows a
/// person, is the reason's name.
/// </remarks>
/// <param name="handler">The handler that refused.</param>
/// <param name="reason">Why.</param>
public sealed class PersonalAccessTokenFailureReason(IAuthorizationHandler handler, PersonalAccessTokenRefusalReason reason)
    : AuthorizationFailureReason(handler, reason.ToString())
{
    /// <summary>Why the token was refused.</summary>
    public PersonalAccessTokenRefusalReason Reason { get; } = reason;
}
