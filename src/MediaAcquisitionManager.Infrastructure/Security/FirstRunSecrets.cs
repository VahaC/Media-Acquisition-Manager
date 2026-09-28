namespace MediaAcquisitionManager.Infrastructure.Security;

/// <summary>
/// The secrets an installation can run without configuring: generated once into the secrets
/// directory, then reused on every start. Their consumers arrive with later tickets — the
/// encryption key with the integration credentials (MAM-7, AES-256-GCM), the JWT secret with
/// sign-in (MAM-4) — but provisioning them from the first release means an installation never has
/// to be reconfigured when those land.
/// </summary>
internal static class FirstRunSecrets
{
    /// <summary>32 bytes: the key size of AES-256.</summary>
    public static FirstRunSecret EncryptionKey { get; } = new(
        "Encryption:Key", "encryption-key", 32,
        "makes every credential stored with the old key unreadable");

    /// <summary>64 bytes: the block size of HMAC-SHA256, the longest key it uses without hashing it
    /// down first.</summary>
    public static FirstRunSecret JwtSecret { get; } = new(
        "Jwt:Secret", "jwt-secret", 64,
        "signs everyone out");

    public static IReadOnlyList<FirstRunSecret> All { get; } = [EncryptionKey, JwtSecret];
}
