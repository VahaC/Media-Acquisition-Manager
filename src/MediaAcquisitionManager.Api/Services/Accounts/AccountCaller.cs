namespace MediaAcquisitionManager.Api.Services.Accounts;

/// <summary>The signed-in owner a user-management call comes from.</summary>
/// <param name="UserId">Their user id; the audit rows name them as the actor.</param>
/// <param name="SessionId">The session the call came through, which a reset of their own password
/// keeps.</param>
public sealed record AccountCaller(Guid UserId, Guid SessionId);
