using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using MediaAcquisitionManager.Core.Abstractions;
using MediaAcquisitionManager.Core.Abstractions.Integrations;

namespace MediaAcquisitionManager.Infrastructure.Integrations.Transmission;

/// <summary>
/// MAM-30 — a session id Transmission handed out in a 409 (its CSRF guard): the <c>X-Transmission-Session-Id</c> a call
/// sends, and the address and credentials it was handed out for, as a fingerprint, so an edited address, user name or
/// password never sends it.
/// </summary>
/// <remarks>
/// Transmission answers a request without its current id with 409 and the id, before it reads the body, and hands out a
/// new one every hour and on a restart. The id is not a credential on its own — every request carries the Basic
/// credentials too — but it is what keeps another page from making the owner's browser call Transmission, so nothing
/// prints it: not <see cref="ToString"/>, and no log line (the adapter logs nothing). A class rather than a record, whose
/// generated <c>ToString</c> would print every property.
/// </remarks>
internal sealed class TransmissionSession
{
    private readonly byte[] _fingerprint;

    private TransmissionSession(byte[] fingerprint, string id)
    {
        _fingerprint = fingerprint;
        Id = id;
    }

    /// <summary>The id a request sends in <c>X-Transmission-Session-Id</c>, which every message of a call that sent it
    /// masks.</summary>
    public string Id { get; }

    /// <summary>The session <paramref name="settings"/> were handed: <paramref name="id"/> to send and to mask.</summary>
    public static TransmissionSession HandedTo(IntegrationConnectionSettings settings, string id) => new(Fingerprint(settings), id);

    /// <summary>Whether <paramref name="settings"/> carry the address and credentials this id was handed out for.</summary>
    public bool WasHandedTo(IntegrationConnectionSettings settings) =>
        CryptographicOperations.FixedTimeEquals(_fingerprint, Fingerprint(settings));

    /// <inheritdoc/>
    public override string ToString() => $"TransmissionSession {{ Id = {SecretRedaction.Mask} }}";

    /// <summary>
    /// SHA-256 of the address, the user name and the password, each prefixed with its length so no two different triples
    /// run together into one text — as <c>QBittorrentSession.Fingerprint</c> builds it, and for the same reason: the
    /// password has no business living on in a cache for as long as the app runs. Also what a refusal is remembered by
    /// (<see cref="TransmissionSessions.KeepRefusal"/>).
    /// </summary>
    internal static byte[] Fingerprint(IntegrationConnectionSettings settings)
    {
        // A missing password is sent as an empty one (the Basic credentials), so the two are one.
        var userName = settings.UserName ?? "";
        var password = settings.Password ?? "";
        var text = string.Create(
            CultureInfo.InvariantCulture,
            $"{settings.BaseUrl.Length}:{settings.BaseUrl}{userName.Length}:{userName}{password.Length}:{password}");
        return SHA256.HashData(Encoding.UTF8.GetBytes(text));
    }
}
