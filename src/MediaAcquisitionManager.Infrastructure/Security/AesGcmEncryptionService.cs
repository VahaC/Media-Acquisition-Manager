using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using MediaAcquisitionManager.Core.Abstractions;
using MediaAcquisitionManager.Core.Options;
using Microsoft.Extensions.Options;

namespace MediaAcquisitionManager.Infrastructure.Security;

/// <inheritdoc cref="IEncryptionService"/>
/// <remarks>
/// <para>
/// Format v1: <c>v1:</c> + base64(nonce[12] ‖ ciphertext ‖ tag[16]), AES-256-GCM with the UTF-8 of the
/// associated data authenticated alongside (MAM-7, ADR 0007). The prefix lets a later format — a key
/// id for rotation, say — live next to this one; a value without it is simply not readable here.
/// </para>
/// <para>
/// The key is parsed on first use, like the JWT signing key (<c>AccessTokenService</c>): <c>dotnet
/// ef</c> and a container build never need one, the constructor never throws, and a broken key fails
/// the first save or test of a secret with a sentence naming <c>MAM_Encryption__Key</c> instead of
/// stopping the whole app, sign-in included.
/// </para>
/// </remarks>
internal sealed class AesGcmEncryptionService(IOptions<EncryptionOptions> options) : IEncryptionService
{
    private const string FormatPrefix = "v1:";

    /// <summary>
    /// 96 bits, the size GCM is specified for. Random per call: NIST bounds random 96-bit nonces at
    /// 2³² encryptions per key, and this app seals a handful of secrets per key.
    /// </summary>
    private const int NonceBytes = 12;

    /// <summary>The full 128-bit tag; a shorter one only makes forging a value easier.</summary>
    private const int TagBytes = 16;

    /// <summary>
    /// Throws on invalid input in both directions instead of substituting U+FFFD: a secret that
    /// decrypts to other characters than were sealed, or is sealed as other characters than were
    /// given, would be sent to a service as a different credential without anyone noticing.
    /// </summary>
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private readonly Lazy<byte[]> _key = new(() => EncryptionKey.Parse(options.Value.Key));

    public string Encrypt(string plaintext, string associatedData)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        ArgumentNullException.ThrowIfNull(associatedData);

        var key = _key.Value;
        var associatedBytes = StrictUtf8.GetBytes(associatedData);
        var plainBytes = StrictUtf8.GetBytes(plaintext);
        try
        {
            var sealedBytes = new byte[NonceBytes + plainBytes.Length + TagBytes];
            var nonce = sealedBytes.AsSpan(0, NonceBytes);
            RandomNumberGenerator.Fill(nonce);

            // An instance per call: AesGcm isn't documented as thread-safe, this service is a singleton,
            // and constructing one is cheap next to the database write that follows.
            using var aes = new AesGcm(key, TagBytes);
            aes.Encrypt(
                nonce,
                plainBytes,
                sealedBytes.AsSpan(NonceBytes, plainBytes.Length),
                sealedBytes.AsSpan(NonceBytes + plainBytes.Length, TagBytes),
                associatedBytes);

            return FormatPrefix + Convert.ToBase64String(sealedBytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plainBytes);
        }
    }

    public bool TryDecrypt(string protectedValue, string associatedData, [NotNullWhen(true)] out string? plaintext)
    {
        ArgumentNullException.ThrowIfNull(protectedValue);
        ArgumentNullException.ThrowIfNull(associatedData);
        plaintext = null;

        // First, so a broken key configuration always surfaces as the operator's sentence, whatever
        // the value: answering "false" for it would tell the owner to re-enter every credential.
        var key = _key.Value;
        var associatedBytes = StrictUtf8.GetBytes(associatedData);

        if (!protectedValue.StartsWith(FormatPrefix, StringComparison.Ordinal))
            return false;

        var payload = protectedValue.AsSpan(FormatPrefix.Length);
        var sealedBytes = new byte[(payload.Length + 3) / 4 * 3];
        if (!Convert.TryFromBase64Chars(payload, sealedBytes, out var length) || length < NonceBytes + TagBytes)
            return false;

        var nonce = sealedBytes.AsSpan(0, NonceBytes);
        var cipher = sealedBytes.AsSpan(NonceBytes, length - NonceBytes - TagBytes);
        var tag = sealedBytes.AsSpan(length - TagBytes, TagBytes);
        var plainBytes = new byte[cipher.Length];
        try
        {
            using var aes = new AesGcm(key, TagBytes);
            aes.Decrypt(nonce, cipher, tag, plainBytes, associatedBytes);
            plaintext = StrictUtf8.GetString(plainBytes);
            return true;
        }
        // The tag check failed (another key, other associated data, an altered or cut value); the
        // mismatch exception derives from CryptographicException.
        catch (CryptographicException)
        {
            return false;
        }
        // Authentic but not UTF-8: only a value sealed by something other than Encrypt.
        catch (DecoderFallbackException)
        {
            return false;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plainBytes);
        }
    }
}
