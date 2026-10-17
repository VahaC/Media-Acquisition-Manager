namespace MediaAcquisitionManager.Api.Services.Accounts;

/// <summary>MAM-46 — how saving a user's language ended.</summary>
public enum UserLanguageSaveStatus
{
    /// <summary>The language was saved.</summary>
    Saved = 1,

    /// <summary>The value is no language of the app, which the error names; nothing was saved.</summary>
    Invalid = 2,

    /// <summary>The user was deleted between the token's check and the save: to the caller, a session that has
    /// ended.</summary>
    UserGone = 3,
}
