using System.ComponentModel.DataAnnotations;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Integrations;

namespace MediaAcquisitionManager.Core.Entities;

/// <summary>
/// One service the app talks to — a media library, a release search source or a download client —
/// through a registered adapter type (ADR 0005). The owner manages these on the Integrations settings
/// page (MAM-7).
/// </summary>
/// <remarks>
/// Three fixed credential slots rather than a key/value bag: every product the specification and the
/// roadmap name needs a subset of {user name, password, API key}, and a bag would be a passthrough
/// type on the contract whose entries couldn't get <c>Has*</c> flags.
/// </remarks>
public class IntegrationConnectionEntity : AuditableEntity
{
    /// <summary>The display name, trimmed; lists, provenance and audit rows show it.</summary>
    [Required, MaxLength(IntegrationConnectionRules.NameMaximumLength)]
    public string Name { get; set; } = default!;

    /// <summary>
    /// <see cref="IntegrationConnectionRules.NormalizeName"/> of <see cref="Name"/>. Its unique index
    /// makes "Jellyfin" and "jellyfin" one name — SQLite compares text case-sensitively, so a unique
    /// index on <see cref="Name"/> would let both exist.
    /// </summary>
    [Required, MaxLength(IntegrationConnectionRules.NameMaximumLength)]
    public string NormalizedName { get; set; } = default!;

    /// <summary>
    /// The registered adapter type id (<c>jellyfin</c>, <c>torznab</c>, <c>qbittorrent</c>). A string,
    /// not an enum: an enum would put product names into the core and need a core change for every
    /// adapter (ADR 0005). Never changes after the row is created. The registry's ids are at most 32
    /// characters; 64 leaves room without a migration.
    /// </summary>
    [Required, MaxLength(64)]
    public string AdapterType { get; set; } = default!;

    /// <summary>
    /// Copied from the adapter's descriptor at creation, so the list filters by kind in SQL and a row
    /// whose type a later build no longer registers still says what it was.
    /// </summary>
    public IntegrationKind Kind { get; set; }

    /// <summary>
    /// Whether the app uses the integration. Stored and shown since MAM-7; MAM-8, 10 and 11 skip a
    /// disabled one. A connection test still runs on a disabled integration.
    /// </summary>
    public bool IsEnabled { get; set; }

    /// <summary>
    /// <see cref="IntegrationConnectionRules.NormalizeBaseUrl"/> output: an absolute http(s) address
    /// with no user info, query, fragment or trailing slash, so it can't carry a secret.
    /// </summary>
    [Required, MaxLength(IntegrationConnectionRules.BaseUrlMaximumLength)]
    public string BaseUrl { get; set; } = default!;

    /// <summary>The user name, trimmed; <c>null</c> when none. Not a secret, so the API returns it.</summary>
    [MaxLength(IntegrationConnectionRules.UserNameMaximumLength)]
    public string? UserName { get; set; }

    /// <summary>
    /// The password sealed by <see cref="Abstractions.IEncryptionService"/> for this row and field
    /// (<see cref="IntegrationSecretPurpose"/>); <c>null</c> when none is stored. Never returned.
    /// 256 characters are at most 768 UTF-8 bytes, so the sealed text is at most 1 067 characters.
    /// </summary>
    [MaxLength(2048)]
    public string? PasswordEncrypted { get; set; }

    /// <summary>The API key, sealed like <see cref="PasswordEncrypted"/>; <c>null</c> when none is
    /// stored. Never returned.</summary>
    [MaxLength(2048)]
    public string? ApiKeyEncrypted { get; set; }

    /// <summary>
    /// How the service's file paths map onto this app's mounts (ADR 0003). The one navigation
    /// property of the model: the mappings are part of the connection and replaced by its one Save.
    /// </summary>
    public List<PathMappingEntity> PathMappings { get; set; } = [];
}
