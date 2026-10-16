using System.ComponentModel.DataAnnotations;
using MediaAcquisitionManager.Core.Accounts;
using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Core.Entities;

/// <summary>
/// MAM-5 — a personal access token: a long-lived secret a user creates on their Profile page for a script or an AI
/// assistant, which then acts as that user with the token's <see cref="Access"/> (ADR 0040). A request is let in only
/// while the token is active (<see cref="PersonalAccessTokenRules.IsActive"/>) and its user is not disabled.
/// </summary>
/// <remarks>
/// Only a hash of the token is stored. A token is 256 random bits, so a plain SHA-256 is enough — there is nothing to
/// brute-force, unlike a password — and whoever reads a copy of the database still can't use the tokens in it. The
/// secret itself is shown once, in the create response, and never again.
/// </remarks>
public class PersonalAccessTokenEntity : AuditableEntity
{
    /// <summary>The user the token acts as.</summary>
    public Guid UserId { get; set; }

    /// <summary>What the user called it ("Claude on the laptop"), trimmed.</summary>
    [Required, MaxLength(PersonalAccessTokenRules.NameMaximumLength)]
    public string Name { get; set; } = default!;

    /// <summary>The token's first characters (<c>mam_pat_</c> and six more), shown in lists so a user can tell their
    /// tokens apart and find the one a script holds; far too short to use.</summary>
    [Required, MaxLength(16)]
    public string Prefix { get; set; } = default!;

    /// <summary>SHA-256 of the token's UTF-8 text, lowercase hex.</summary>
    [Required, MaxLength(64)]
    public string TokenHash { get; set; } = default!;

    /// <summary>What the token may do as its user.</summary>
    public PersonalAccessTokenAccess Access { get; set; }

    /// <summary>When the token stops working; <c>null</c> when it never expires.</summary>
    public DateTime? ExpiresUtc { get; set; }

    /// <summary>When a request last came in on the token, to the minute: written at most once a minute.</summary>
    public DateTime? LastUsedUtc { get; set; }

    /// <summary>When a refusal of the token was last recorded in the audit trail: at most one row a minute.</summary>
    public DateTime? LastRefusedUtc { get; set; }

    /// <summary>When the token was revoked: by its user, or by the owner (revoke all, a password reset, a disable).
    /// A revoked token never works again.</summary>
    public DateTime? RevokedUtc { get; set; }
}
