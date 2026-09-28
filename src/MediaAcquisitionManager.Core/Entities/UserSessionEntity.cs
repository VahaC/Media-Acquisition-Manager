using System.ComponentModel.DataAnnotations;

namespace MediaAcquisitionManager.Core.Entities;

/// <summary>
/// One signed-in browser or device: the server side of a refresh token. Every access token names
/// its session, and a request is let in only while that session is active, so revoking this row
/// signs the device out at once.
/// </summary>
/// <remarks>
/// Only hashes of refresh tokens are stored. A token is 256 random bits, so a plain SHA-256 is
/// enough — there is nothing to brute-force, unlike a password — and whoever reads a copy of the
/// database still can't use the tokens in it.
/// </remarks>
public class UserSessionEntity : AuditableEntity
{
    /// <summary>The signed-in user.</summary>
    public Guid UserId { get; set; }

    /// <summary>SHA-256 of the refresh token the browser holds now, lowercase hex.</summary>
    [Required, MaxLength(64)]
    public string RefreshTokenHash { get; set; } = default!;

    /// <summary>
    /// SHA-256 of the token the last refresh replaced. A browser presenting it again is either a
    /// second tab that refreshed at the same moment (inside a short grace) or someone replaying a
    /// stolen token (after it).
    /// </summary>
    [MaxLength(64)]
    public string? PreviousRefreshTokenHash { get; set; }

    /// <summary>When the refresh token was last rotated; <c>null</c> until the first refresh.</summary>
    public DateTime? RotatedUtc { get; set; }

    /// <summary>When the session was last created or refreshed.</summary>
    public DateTime LastUsedUtc { get; set; }

    /// <summary>When the session ends unless it is refreshed before.</summary>
    public DateTime ExpiresUtc { get; set; }

    /// <summary>When the session was ended early: sign-out, a replayed token, an owner's action.</summary>
    public DateTime? RevokedUtc { get; set; }
}
