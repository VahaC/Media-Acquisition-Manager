using System.Linq.Expressions;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Core.Accounts;

/// <summary>
/// MAM-5 — what a personal access token looks like and when one counts as active, in one place, so the request
/// handler that lets a token in, the Users page's count and the Profile page's list never disagree about it (ADR 0040).
/// </summary>
public static class PersonalAccessTokenRules
{
    /// <summary>Long enough for "Claude on the laptop in the living room"; short enough for a table cell.</summary>
    public const int NameMaximumLength = 100;

    /// <summary>
    /// What every token starts with. It tells a reader of a script or a log what the value is, lets secret scanners
    /// recognise a leaked one, and lets the server send a token to its own handler instead of reading it as a JWT.
    /// </summary>
    public const string SecretPrefix = "mam_pat_";

    /// <summary>How many of a token's characters a list shows and the row keeps: the prefix and six random ones, 36
    /// bits — enough to tell one user's tokens apart, and the other 220 bits are still unguessable.</summary>
    public const int DisplayPrefixLength = 14;

    /// <summary>
    /// Active: not revoked, and not expired at <paramref name="now"/>. An expression, so the database evaluates it inside
    /// each query that needs it. Whether the token's user is disabled is not the token's state and is checked beside it.
    /// </summary>
    public static Expression<Func<PersonalAccessTokenEntity, bool>> IsActive(DateTime now) =>
        token => token.RevokedUtc == null && (token.ExpiresUtc == null || token.ExpiresUtc > now);

    /// <summary>When a token created at <paramref name="now"/> with <paramref name="lifetime"/> expires; <c>null</c>
    /// for one that never does.</summary>
    /// <exception cref="ArgumentOutOfRangeException">A lifetime that isn't one of the four.</exception>
    public static DateTime? ExpiresUtc(PersonalAccessTokenLifetime lifetime, DateTime now) => lifetime switch
    {
        PersonalAccessTokenLifetime.Days30 => now.AddDays(30),
        PersonalAccessTokenLifetime.Days90 => now.AddDays(90),
        PersonalAccessTokenLifetime.OneYear => now.AddDays(365),
        PersonalAccessTokenLifetime.Never => null,
        _ => throw new ArgumentOutOfRangeException(nameof(lifetime), lifetime, "Not a token lifetime."),
    };
}
