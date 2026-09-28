namespace MediaAcquisitionManager.Api.Services.Auth;

/// <summary>How a sign-in with a user name and password ended.</summary>
public enum PasswordSignInStatus
{
    /// <summary>The credentials matched an enabled user; a session was started.</summary>
    SignedIn = 1,

    /// <summary>Anything else. Deliberately one status: the caller can't answer differently for an
    /// unknown user, a wrong password or a disabled account if it isn't told which.</summary>
    Refused = 2,
}
