namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>
/// A signed-in session: what setup, sign-in and refresh answer. The refresh token travels
/// separately, in the <c>mam_refresh</c> cookie the same response sets.
/// </summary>
/// <param name="AccessToken">The bearer token for every API call, short-lived.</param>
/// <param name="AccessTokenExpiresUtc">When <paramref name="AccessToken"/> stops working.</param>
/// <param name="User">Who is signed in.</param>
public sealed record SignInResponse(string AccessToken, DateTime AccessTokenExpiresUtc, CurrentUserResponse User);
