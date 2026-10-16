namespace MediaAcquisitionManager.Core.Enums;

/// <summary>
/// MAM-5 — how long a new personal access token works, picked when it is created. Never stored: the token keeps the
/// instant it expires (<c>ExpiresUtc</c>), which <see cref="Accounts.PersonalAccessTokenRules.ExpiresUtc"/> works out.
/// </summary>
public enum PersonalAccessTokenLifetime
{
    /// <summary>Thirty days.</summary>
    Days30 = 1,

    /// <summary>Ninety days: the Create token dialog's default.</summary>
    Days90 = 2,

    /// <summary>One year, 365 days.</summary>
    OneYear = 3,

    /// <summary>Until it is revoked.</summary>
    Never = 4,
}
