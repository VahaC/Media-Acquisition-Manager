using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using MediaAcquisitionManager.Core.Abstractions;
using MediaAcquisitionManager.Core.Abstractions.Integrations;

namespace MediaAcquisitionManager.Infrastructure.Integrations.QBittorrent;

/// <summary>
/// A session qBittorrent handed out at sign-in (MAM-11): the cookie a call sends back, and the address and
/// credentials it was opened with, as a fingerprint, so an edited address, user name or password never
/// reuses it.
/// </summary>
/// <remarks>
/// The session is as good as the password until it expires, so nothing prints it: not
/// <see cref="ToString"/>, and no log line (the adapter logs nothing). A class rather than a record, whose
/// generated <c>ToString</c> would print every property, and whose value equality would let
/// <see cref="QBittorrentSessions.Drop"/> remove a different session that happens to carry the same
/// cookie.
/// </remarks>
internal sealed class QBittorrentSession
{
    private readonly byte[] _fingerprint;

    private QBittorrentSession(byte[] fingerprint, string cookie, string value)
    {
        _fingerprint = fingerprint;
        Cookie = cookie;
        Value = value;
    }

    /// <summary>The <c>name=value</c> pair a request sends back in its <c>Cookie</c> header.</summary>
    public string Cookie { get; }

    /// <summary>The session id alone, which every message of a call that sent it masks.</summary>
    public string Value { get; }

    /// <summary>The session <paramref name="settings"/> signed in for: <paramref name="cookie"/> to send back,
    /// <paramref name="value"/> to mask.</summary>
    public static QBittorrentSession OpenedWith(IntegrationConnectionSettings settings, string cookie, string value) =>
        new(Fingerprint(settings), cookie, value);

    /// <summary>Whether <paramref name="settings"/> carry the address and credentials this session was
    /// opened with.</summary>
    public bool WasOpenedWith(IntegrationConnectionSettings settings) =>
        CryptographicOperations.FixedTimeEquals(_fingerprint, Fingerprint(settings));

    /// <inheritdoc/>
    public override string ToString() => $"QBittorrentSession {{ Cookie = {SecretRedaction.Mask} }}";

    /// <summary>
    /// SHA-256 of the address, the user name and the password, each prefixed with its length so no two
    /// different triples run together into one text. A hash rather than the values: the password has no
    /// business living on in a cache for as long as the app runs. MAM-11 — also what a refused sign-in is
    /// remembered by (<see cref="QBittorrentSessions.KeepRefusal"/>), so a session and a refusal are told
    /// apart from other credentials alike.
    /// </summary>
    internal static byte[] Fingerprint(IntegrationConnectionSettings settings)
    {
        // A missing password signs in as an empty one (QBittorrentAdapter.SignInAsync), so the two are one.
        var userName = settings.UserName ?? "";
        var password = settings.Password ?? "";
        var text = string.Create(
            CultureInfo.InvariantCulture,
            $"{settings.BaseUrl.Length}:{settings.BaseUrl}{userName.Length}:{userName}{password.Length}:{password}");
        return SHA256.HashData(Encoding.UTF8.GetBytes(text));
    }
}
