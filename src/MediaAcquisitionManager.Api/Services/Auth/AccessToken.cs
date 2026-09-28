namespace MediaAcquisitionManager.Api.Services.Auth;

/// <summary>A signed access token.</summary>
/// <param name="Token">The compact JWT, sent as <c>Authorization: Bearer …</c>.</param>
/// <param name="ExpiresUtc">When it stops working.</param>
public sealed record AccessToken(string Token, DateTime ExpiresUtc);
