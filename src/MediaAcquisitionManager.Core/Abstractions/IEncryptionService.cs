using System.Diagnostics.CodeAnalysis;

namespace MediaAcquisitionManager.Core.Abstractions;

/// <summary>
/// Seals short secrets for storage: integration credentials since MAM-7, later a release's payload
/// locator (ADR 0007).
/// </summary>
/// <remarks>
/// <para>
/// The associated data names what the value is for — a row and a field
/// (<see cref="MediaAcquisitionManager.Core.Integrations.IntegrationSecretPurpose"/>). A sealed value copied to another row or into
/// the other column (a hand-restored or tampered database, or a bug that stores the password in the
/// API key's column) then fails to open instead of being opened and sent to that row's server.
/// </para>
/// <para>
/// What it is <b>not</b>: a defence against someone who can write the database. They can simply
/// change the row's address instead, and a kept secret goes wherever the address points.
/// </para>
/// </remarks>
public interface IEncryptionService
{
    /// <summary>
    /// Seals <paramref name="plaintext"/> with AES-256-GCM and a fresh random nonce per call, so two
    /// calls never give the same text. <paramref name="associatedData"/> is authenticated, not
    /// stored: the same value must be passed to <see cref="TryDecrypt"/>.
    /// </summary>
    string Encrypt(string plaintext, string associatedData);

    /// <summary>
    /// Opens what <see cref="Encrypt"/> made. <c>false</c> — never an exception — when the value was
    /// made with another key or other associated data, was altered or cut short, isn't this format,
    /// or has a format version this build doesn't read.
    /// </summary>
    /// <remarks>
    /// A Try method rather than a result record on purpose: a record holding the plaintext would
    /// print it from its generated <c>ToString</c> into the first log line that mentions it.
    /// </remarks>
    bool TryDecrypt(string protectedValue, string associatedData, [NotNullWhen(true)] out string? plaintext);
}
