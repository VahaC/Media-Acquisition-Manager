namespace MediaAcquisitionManager.Infrastructure.Security;

/// <summary>
/// Reads the configured <c>Encryption:Key</c> into the 32 bytes AES-256 needs (MAM-7, ADR 0007).
/// </summary>
/// <remarks>
/// A key that can't be read is an operator's mistake, not bad data, so it throws rather than letting
/// <see cref="AesGcmEncryptionService.TryDecrypt"/> answer <c>false</c>: a "secret unreadable" answer
/// would send the owner re-entering every credential when the fix is one line of configuration. Each
/// message names the variable and what to do, and never includes the configured value — it reaches
/// the error log, which ships off the box.
/// </remarks>
internal static class EncryptionKey
{
    /// <summary>The key size of AES-256; <see cref="FirstRunSecrets.EncryptionKey"/> generates this many.</summary>
    public const int KeyBytes = 32;

    /// <summary>The key bytes of <paramref name="configured"/>, base64 of exactly <see cref="KeyBytes"/> bytes.</summary>
    /// <exception cref="InvalidOperationException">The key is blank, not base64, or of another length.</exception>
    public static byte[] Parse(string? configured)
    {
        // Normally unreachable: the first start generates the key when nothing configures it. Worded
        // for the operator who set MAM_Encryption__Key by hand and got it wrong.
        if (string.IsNullOrWhiteSpace(configured))
            throw new InvalidOperationException(
                "Encryption:Key has no value, so integration credentials can't be encrypted or read. Leave MAM_Encryption__Key unset to let the app generate one, or set it to base64 of 32 random bytes (openssl rand -base64 32).");

        byte[] key;
        try
        {
            key = Convert.FromBase64String(configured);
        }
        catch (FormatException)
        {
            // Not chained as the inner exception: it adds nothing the sentence doesn't say, and the
            // rule "the key never reaches a log" stays easy to check with nothing nested to read.
            throw new InvalidOperationException(
                "Encryption:Key is not valid base64, so integration credentials can't be encrypted or read. Set MAM_Encryption__Key to base64 of exactly 32 random bytes (openssl rand -base64 32), or restore .secrets/encryption-key in the data directory from a backup.");
        }

        // Exactly 32, not "at least": AES only takes 16, 24 or 32 bytes, and a longer value silently
        // cut down would be a different key from the one the operator believes is in use.
        if (key.Length != KeyBytes)
            throw new InvalidOperationException(
                $"Encryption:Key is {key.Length} bytes long, but AES-256 needs exactly 32. Set MAM_Encryption__Key to base64 of exactly 32 random bytes (openssl rand -base64 32), or restore .secrets/encryption-key in the data directory from a backup.");

        return key;
    }
}
