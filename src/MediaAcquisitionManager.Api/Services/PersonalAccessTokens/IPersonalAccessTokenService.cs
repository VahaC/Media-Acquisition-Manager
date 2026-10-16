using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Api.Services.Audit;

namespace MediaAcquisitionManager.Api.Services.PersonalAccessTokens;

/// <summary>
/// MAM-5 — a user's personal access tokens: listing their own, creating one, revoking one, and revoking all of a user's
/// at the owner's hand (ADR 0040). Expected failures come back as statuses, never as exceptions, and every change
/// records its audit row in the same save as the change.
/// </summary>
/// <remarks>
/// It checks nothing about who calls: that a session, not a token, manages tokens is the endpoint's business
/// (<c>[DenyPersonalAccessToken]</c>), and so is that only the owner revokes another user's. The one exception is
/// <see cref="CreateAsync"/>'s second look at the caller's session, which only a check inside its own transaction can
/// make race-free.
/// </remarks>
public interface IPersonalAccessTokenService
{
    /// <summary>
    /// The tokens of <paramref name="userId"/> not revoked, expired ones included and flagged, newest first. Unpaged on
    /// purpose: one person's own tokens are a handful.
    /// </summary>
    Task<PersonalAccessTokenListResponse> ListAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a token for <paramref name="userId"/> from <paramref name="request"/> and records it, answering with the
    /// token and its secret — the one time the secret is ever given out.
    /// <see cref="PersonalAccessTokenCreateStatus.Invalid"/>, with nothing stored, for a blank or too long name, or a
    /// missing access or lifetime. <see cref="PersonalAccessTokenCreateStatus.SessionEnded"/>, with nothing stored, when
    /// the caller's session <paramref name="sessionId"/> is no longer active for an enabled <paramref name="userId"/> by
    /// the time the token would be stored: an owner's reset, disable or delete that committed since the request was let
    /// in wins, so no token outlives the revocation it raced.
    /// </summary>
    Task<PersonalAccessTokenCreateResult> CreateAsync(
        Guid userId, Guid sessionId, CreatePersonalAccessTokenRequest? request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Revokes the token <paramref name="tokenId"/> of <paramref name="userId"/>, who is the audit row's actor; it stops
    /// working with the next request. <see cref="PersonalAccessTokenRevokeStatus.NotFound"/> for another user's token,
    /// one that doesn't exist, and one already revoked — so of two clicks at once, one revokes and records.
    /// </summary>
    Task<PersonalAccessTokenRevokeStatus> RevokeAsync(Guid userId, Guid tokenId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Revokes every token of <paramref name="userId"/> not revoked yet, one audit row each with
    /// <paramref name="actor"/> — the owner revoking all of a user's tokens, resetting their password or disabling them.
    /// Inside the caller's transaction when it has one, otherwise in one of its own, so the rows commit with the
    /// revocation. How many it revoked; 0, recording nothing, when there were none.
    /// </summary>
    Task<int> RevokeAllForUserAsync(Guid userId, AuditActor actor, CancellationToken cancellationToken = default);
}
