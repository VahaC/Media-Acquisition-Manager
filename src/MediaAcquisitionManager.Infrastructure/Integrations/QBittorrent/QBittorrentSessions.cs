using System.Collections.Concurrent;
using System.Security.Cryptography;
using MediaAcquisitionManager.Core.Abstractions.Integrations;

namespace MediaAcquisitionManager.Infrastructure.Integrations.QBittorrent;

/// <summary>
/// At most one qBittorrent session per saved integration, shared by every call the singleton adapter
/// serves (MAM-11), so a page that lists destinations doesn't sign in on every load — and at most one
/// sign-in qBittorrent refused, so a page doesn't keep sending what it refused either.
/// </summary>
/// <remarks>
/// <para>
/// Each sign-in is a request qBittorrent counts towards its ban of the address when it fails, and a
/// session left unused expires on its own (an hour by default), so keeping one is both kinder and cheaper
/// than signing in per call. Kept by the integration's id: one integration's session is never sent to
/// another, and a session kept for an id whose address or credentials have since changed is not used
/// (<see cref="QBittorrentSession.WasOpenedWith"/>). An unsaved draft has no id and keeps nothing.
/// </para>
/// <para>
/// MAM-11 — a refusal is kept the same way, by id and by the address and credentials it was refused
/// (<see cref="QBittorrentSession.Fingerprint"/>). Without it, a wrong password would be sent again by
/// every page load, dialog and save until qBittorrent banned the app's address (five failures in a row
/// by default), and from then on even a corrected password would be refused until the ban ended. It
/// goes when the address or credentials change, when a sign-in with other ones works, and when a
/// connection test signs in with the same ones (<see cref="ForgetRefusal"/>), which is the owner's way
/// to try them again.
/// </para>
/// <para>
/// Held in memory only, like qBittorrent's own sessions: a restart of either side signs in again.
/// </para>
/// </remarks>
internal sealed class QBittorrentSessions
{
    private readonly ConcurrentDictionary<Guid, QBittorrentSession> _sessions = new();
    private readonly ConcurrentDictionary<Guid, Refusal> _refusals = new();

    /// <summary>The session kept for <paramref name="settings"/>' integration, when it was opened with the
    /// same address and credentials; otherwise <c>null</c>.</summary>
    public QBittorrentSession? Find(IntegrationConnectionSettings settings) =>
        settings.ConnectionId is { } connectionId
        && _sessions.TryGetValue(connectionId, out var session)
        && session.WasOpenedWith(settings)
            ? session
            : null;

    /// <summary>Keeps <paramref name="session"/> for <paramref name="settings"/>' integration in place of any
    /// other; nothing for a draft. A refusal kept for the integration goes: it was of other credentials,
    /// or qBittorrent takes them again.</summary>
    public void Keep(IntegrationConnectionSettings settings, QBittorrentSession session)
    {
        if (settings.ConnectionId is { } connectionId)
        {
            _sessions[connectionId] = session;
            _refusals.TryRemove(connectionId, out _);
        }
    }

    /// <summary>
    /// Forgets <paramref name="session"/> once qBittorrent has refused it — only while it is still the one
    /// kept for <paramref name="settings"/>' integration: a call running alongside may already have signed in
    /// again, and its fresh session must stay.
    /// </summary>
    public void Drop(IntegrationConnectionSettings settings, QBittorrentSession session)
    {
        if (settings.ConnectionId is { } connectionId)
            _sessions.TryRemove(new KeyValuePair<Guid, QBittorrentSession>(connectionId, session));
    }

    /// <summary>How qBittorrent refused the last sign-in of <paramref name="settings"/>' integration, when it
    /// was with the same address and credentials; otherwise <c>null</c>.</summary>
    public IntegrationCallStatus? FindRefusal(IntegrationConnectionSettings settings) =>
        settings.ConnectionId is { } connectionId
        && _refusals.TryGetValue(connectionId, out var refusal)
        && refusal.WasRefusedWith(settings)
            ? refusal.Status
            : null;

    /// <summary>Remembers that qBittorrent refused <paramref name="settings"/>' sign-in as
    /// <paramref name="status"/> says, in place of any other refusal of the integration; nothing for a
    /// draft.</summary>
    public void KeepRefusal(IntegrationConnectionSettings settings, IntegrationCallStatus status)
    {
        if (settings.ConnectionId is { } connectionId)
            _refusals[connectionId] = new Refusal(QBittorrentSession.Fingerprint(settings), status);
    }

    /// <summary>
    /// Remembers that qBittorrent refused a connection test's sign-in, unless a refusal of other address or
    /// credentials is kept for the integration: a test of ones typed into the Edit dialog and not saved
    /// carries the integration's id, and must not stand in for the refusal of the ones it still signs in
    /// with. A refusal of the same ones is replaced, so a ban that has ended and left a wrong password
    /// behind is answered as the wrong password. Nothing for a draft.
    /// </summary>
    public void KeepTestRefusal(IntegrationConnectionSettings settings, IntegrationCallStatus status)
    {
        if (settings.ConnectionId is not { } connectionId)
            return;

        var refusal = new Refusal(QBittorrentSession.Fingerprint(settings), status);
        _refusals.AddOrUpdate(connectionId, refusal, (_, kept) => kept.WasRefusedWith(settings) ? refusal : kept);
    }

    /// <summary>
    /// Forgets the refusal kept for <paramref name="settings"/>' integration once qBittorrent has taken the
    /// same address and credentials — only those: a test of other ones, typed into the Edit dialog and not
    /// saved, says nothing about the ones the integration still signs in with.
    /// </summary>
    public void ForgetRefusal(IntegrationConnectionSettings settings)
    {
        if (settings.ConnectionId is { } connectionId
            && _refusals.TryGetValue(connectionId, out var refusal)
            && refusal.WasRefusedWith(settings))
            _refusals.TryRemove(new KeyValuePair<Guid, Refusal>(connectionId, refusal));
    }

    /// <summary>
    /// A sign-in qBittorrent refused: how, and the fingerprint of the address and credentials it refused.
    /// A class, so <see cref="ForgetRefusal"/> removes only the very refusal it read, never a newer one a
    /// call alongside has just kept.
    /// </summary>
    private sealed class Refusal(byte[] fingerprint, IntegrationCallStatus status)
    {
        /// <summary><see cref="IntegrationCallStatus.AuthenticationFailed"/> for refused credentials,
        /// <see cref="IntegrationCallStatus.Refused"/> for a banned address.</summary>
        public IntegrationCallStatus Status { get; } = status;

        /// <summary>Whether <paramref name="settings"/> carry the address and credentials refused.</summary>
        public bool WasRefusedWith(IntegrationConnectionSettings settings) =>
            CryptographicOperations.FixedTimeEquals(fingerprint, QBittorrentSession.Fingerprint(settings));
    }
}
