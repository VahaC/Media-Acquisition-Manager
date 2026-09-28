namespace MediaAcquisitionManager.Api.Services.Auth;

/// <summary>The outcome of <see cref="ISessionService.SignInAsync"/>.</summary>
/// <param name="Status">How it ended.</param>
/// <param name="Grant">The new session when <see cref="PasswordSignInStatus.SignedIn"/>.</param>
public sealed record PasswordSignInResult(PasswordSignInStatus Status, SessionGrant? Grant)
{
    public static PasswordSignInResult Refused { get; } = new(PasswordSignInStatus.Refused, null);

    public static PasswordSignInResult SignedIn(SessionGrant grant) => new(PasswordSignInStatus.SignedIn, grant);
}
