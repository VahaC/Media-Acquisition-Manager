using System.Globalization;
using System.Security.Cryptography;
using MediaAcquisitionManager.Core.Abstractions;

namespace MediaAcquisitionManager.Infrastructure.Security;

/// <summary>
/// PBKDF2-HMAC-SHA256 with a 16-byte random salt and a 32-byte hash, stored as
/// <c>pbkdf2-sha256$&lt;iterations&gt;$&lt;salt base64&gt;$&lt;hash base64&gt;</c>.
/// </summary>
/// <remarks>
/// PBKDF2 because it ships in the BCL: no native dependency to build into the image, no package to
/// keep patched. Each hash carries its algorithm and iteration count, and <see cref="Verify"/> uses
/// the count stored in the hash rather than the one it was constructed with, so raising the count later
/// only affects new hashes — every stored password keeps working. The count is a constructor
/// parameter so the test host can register a cheap hasher: at the production count every seeded user
/// would cost a few hundred milliseconds.
/// </remarks>
internal sealed class Pbkdf2PasswordHasher(int iterations) : IPasswordHasher
{
    /// <summary>OWASP's 2023 recommendation for PBKDF2-HMAC-SHA256: a few hundred milliseconds per
    /// hash on a small server — nothing for one sign-in, ruinous for an offline guessing run.</summary>
    public const int DefaultIterations = 600_000;

    private const string Algorithm = "pbkdf2-sha256";
    private const char Separator = '$';

    /// <summary>128 bits: enough that no two hashes ever share a salt, which defeats precomputed tables.</summary>
    private const int SaltBytes = 16;

    /// <summary>The output size of SHA-256; a longer PBKDF2 output costs the defender more than the attacker.</summary>
    private const int HashBytes = 32;

    public string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, HashBytes);
        return string.Join(
            Separator,
            Algorithm,
            iterations.ToString(CultureInfo.InvariantCulture),
            Convert.ToBase64String(salt),
            Convert.ToBase64String(hash));
    }

    public bool Verify(string password, string hash)
    {
        // Everything is parsed defensively and a bad part answers false: a hash is data, and a row
        // someone edited by hand must fail a sign-in, not turn it into a 500.
        if (string.IsNullOrEmpty(hash) || hash.Split(Separator) is not [Algorithm, var iterationText, var saltText, var expectedText])
            return false;

        if (!int.TryParse(iterationText, NumberStyles.None, CultureInfo.InvariantCulture, out var storedIterations) || storedIterations < 1)
            return false;

        if (FromBase64(saltText) is not { } salt || FromBase64(expectedText) is not { } expected)
            return false;

        var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, storedIterations, HashAlgorithmName.SHA256, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    // TryFromBase64String rather than FromBase64String, so malformed input is an answer, not an exception.
    private static byte[]? FromBase64(string text)
    {
        var buffer = new byte[text.Length * 3 / 4];
        return Convert.TryFromBase64String(text, buffer, out var written) && written > 0 ? buffer[..written] : null;
    }
}
