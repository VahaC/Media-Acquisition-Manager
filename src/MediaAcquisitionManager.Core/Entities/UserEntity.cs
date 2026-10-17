using System.ComponentModel.DataAnnotations;
using MediaAcquisitionManager.Core.Accounts;

namespace MediaAcquisitionManager.Core.Entities;

/// <summary>An account that signs in with a user name and a password.</summary>
public class UserEntity : AuditableEntity
{
    /// <summary>The name as the user typed it, surrounding whitespace removed; what the UI shows.</summary>
    [Required, MaxLength(UserNameRules.MaximumLength)]
    public string UserName { get; set; } = default!;

    /// <summary>
    /// <see cref="UserNameRules.Normalize"/> of <see cref="UserName"/>. Sign-in looks users up by it,
    /// and its unique index makes "Alice" and "alice" one account — SQLite compares text
    /// case-sensitively, so a unique index on <see cref="UserName"/> would let both exist.
    /// </summary>
    [Required, MaxLength(UserNameRules.MaximumLength)]
    public string NormalizedUserName { get; set; } = default!;

    /// <summary>
    /// The password hash in the hasher's self-describing format (algorithm, iteration count, salt,
    /// hash). 256 leaves room for a stronger algorithm's longer format without a migration.
    /// </summary>
    [Required, MaxLength(256)]
    public string PasswordHash { get; set; } = default!;

    /// <summary>A disabled user can't sign in, and every session they hold stops working.</summary>
    public bool IsDisabled { get; set; }

    /// <summary>The last successful sign-in; <c>null</c> until the first one.</summary>
    public DateTime? LastSignInUtc { get; set; }

    /// <summary>
    /// MAM-46 — the language the web app speaks to the user, one of <see cref="UiLanguages.Supported"/>, saved on their
    /// Profile so it follows them to every browser and device; <c>null</c> until they choose one, which means "as in the
    /// browser".
    /// </summary>
    [MaxLength(UiLanguages.MaximumLength)]
    public string? Language { get; set; }
}
