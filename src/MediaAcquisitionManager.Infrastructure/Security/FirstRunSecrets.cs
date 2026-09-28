namespace MediaAcquisitionManager.Infrastructure.Security;

/// <summary>
/// The secrets an installation can run without configuring: generated once into the secrets
/// directory, then reused on every start. The JWT secret signs the access tokens (MAM-4); the
/// encryption key's consumer arrives with the integration credentials (MAM-7, AES-256-GCM) — but
/// provisioning it from the first release means an installation never has to be reconfigured when
/// that lands.
/// </summary>
internal static class FirstRunSecrets
{
    /// <summary>32 bytes: the key size of AES-256.</summary>
    public static FirstRunSecret EncryptionKey { get; } = new(
        "Encryption:Key", "encryption-key", 32,
        "makes every credential stored with the old key unreadable");

    /// <summary>64 bytes: the block size of HMAC-SHA256, the longest key it uses without hashing it
    /// down first.</summary>
    /// <remarks>
    /// MAM-4 — losing it costs little, and the message must not promise more: the refresh cookie is a
    /// random token checked against the session table, not signed with this key, so a new key ends no
    /// session. An operator who wants someone out disables the user or resets their password.
    /// </remarks>
    public static FirstRunSecret JwtSecret { get; } = new(
        "Jwt:Secret", "jwt-secret", 64,
        "makes every access token already issued stop working; open tabs quietly get a new one from their refresh cookie, and nobody is signed out");

    public static IReadOnlyList<FirstRunSecret> All { get; } = [EncryptionKey, JwtSecret];
}
