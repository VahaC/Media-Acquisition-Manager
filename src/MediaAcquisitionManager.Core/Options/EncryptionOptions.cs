namespace MediaAcquisitionManager.Core.Options;

/// <summary>
/// The key integration credentials are sealed with (MAM-7). Bound from the <c>Encryption</c> section
/// (<c>MAM_Encryption__Key</c> in the environment); when nothing configures it, the app generates one
/// on its first start and keeps it in the data directory's <c>.secrets/encryption-key</c>.
/// </summary>
public sealed class EncryptionOptions
{
    public const string SectionName = "Encryption";

    /// <summary>
    /// Base64 of 32 random bytes (AES-256). Unlike the JWT secret, changing or losing it has a
    /// lasting cost: every credential stored under the old key becomes unreadable, and each
    /// integration's secrets have to be entered again. Back it up with the database.
    /// </summary>
    public string? Key { get; set; }
}
