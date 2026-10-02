namespace MediaAcquisitionManager.Api.Services.Accounts;

/// <summary>The signed-in user a user-management call or a change to their own account comes
/// from.</summary>
/// <param name="UserId">Their user id; the audit rows name them as the actor.</param>
/// <param name="SessionId">The session the call came through, which a reset or a change of their own
/// password keeps.</param>
public sealed record AccountCaller(Guid UserId, Guid SessionId);
