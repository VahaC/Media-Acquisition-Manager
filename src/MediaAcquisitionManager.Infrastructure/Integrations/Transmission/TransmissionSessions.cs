using System.Collections.Concurrent;
using System.Security.Cryptography;
using MediaAcquisitionManager.Core.Abstractions.Integrations;

namespace MediaAcquisitionManager.Infrastructure.Integrations.Transmission;

/// <summary>
/// MAM-30 — at most one Transmission session id per saved integration, shared by every call the singleton adapter
/// serves, so a call doesn't start with a 409 every time — and the refusals of the integration's credentials or address,
/// so no call sends what Transmission refused again (ADR 0010, D7).
/// </summary>
/// <remarks>
/// <para>
/// Kept by the integration's id: one integration's id is never sent to another, and one kept for an id whose address or
/// credentials have since changed is not used (<see cref="TransmissionSession.WasHandedTo"/>). An unsaved draft has no
/// id and keeps nothing.
/// </para>
/// <para>
/// A refusal is kept the same way, by id and by the address and credentials refused — and only Transmission's own HTTP
/// no: 401 for credentials it refused or asked for, 403 for an address off its whitelist or an RPC it locked. Every
/// request carries the credentials, and with Transmission's anti-brute-force setting on each 401 counts towards locking
/// its RPC for everyone, the owner's own web interface included, until it restarts — a count that a request without
/// credentials adds to too. Anything else Transmission refuses (its temporary folder, a host name it doesn't take, a
/// torrent it won't add) says nothing about the credentials, and the next call asks again.
/// </para>
/// <para>
/// One refusal per address and credentials, never one per integration: a connection test carries the integration's id
/// whether it tests the saved credentials or ones typed into the Edit dialog and not saved, and neither may push the
/// other out. A call's refusal of the saved ones stays when the owner tests others, and a failed test of credentials
/// he has just saved is honoured by the next call, though a refusal of the old ones is kept too. A refusal goes when a
/// connection test gets past Transmission's checks with the same address and credentials (<see cref="ForgetRefusal"/>),
/// which is the owner's way to try them again, and every refusal of the integration goes when a call gets a session id
/// (<see cref="Keep"/>): Transmission hands one out only past its credential check, so its RPC isn't locked and its
/// whitelist takes the app.
/// </para>
/// <para>
/// Held in memory only, like Transmission's own session id: a restart of either side starts with a 409 again.
/// </para>
/// </remarks>
internal sealed class TransmissionSessions
{
    private readonly ConcurrentDictionary<Guid, TransmissionSession> _sessions = new();

    // A few refusals per integration at most — one per address and credentials Transmission refused since a call last got
    // a session id — read and changed under one lock, so a refusal kept by one call is never lost to another's change.
    private readonly Dictionary<Guid, List<Refusal>> _refusals = [];
    private readonly Lock _refusalsLock = new();

    /// <summary>The session id kept for <paramref name="settings"/>' integration, when it was handed out for the same
    /// address and credentials; otherwise <c>null</c>.</summary>
    public TransmissionSession? Find(IntegrationConnectionSettings settings) =>
        settings.ConnectionId is { } connectionId
        && _sessions.TryGetValue(connectionId, out var session)
        && session.WasHandedTo(settings)
            ? session
            : null;

    /// <summary>Keeps <paramref name="session"/> for <paramref name="settings"/>' integration in place of any other;
    /// nothing for a draft. Every refusal kept for the integration goes: Transmission hands out an id only past its
    /// credential check, so these were taken, and the refusals kept were of others or from before.</summary>
    public void Keep(IntegrationConnectionSettings settings, TransmissionSession session)
    {
        if (settings.ConnectionId is not { } connectionId)
            return;

        _sessions[connectionId] = session;
        lock (_refusalsLock)
            _refusals.Remove(connectionId);
    }

    /// <summary>How Transmission refused <paramref name="settings"/>' address and credentials the last time a call or a
    /// test of the integration sent them — <see cref="IntegrationCallStatus.AuthenticationFailed"/> for a 401,
    /// <see cref="IntegrationCallStatus.Refused"/> for a 403; otherwise <c>null</c>.</summary>
    public IntegrationCallStatus? FindRefusal(IntegrationConnectionSettings settings)
    {
        if (settings.ConnectionId is not { } connectionId)
            return null;

        var fingerprint = TransmissionSession.Fingerprint(settings);
        lock (_refusalsLock)
            return _refusals.TryGetValue(connectionId, out var kept) && kept.Find(refusal => refusal.WasRefusedWith(fingerprint)) is { } refusal
                ? refusal.Status
                : null;
    }

    /// <summary>
    /// Remembers that Transmission refused <paramref name="settings"/> as <paramref name="status"/> says, in place of an
    /// earlier refusal of the same address and credentials only — whether a call or a connection test was refused, and
    /// whichever credentials the test carried: a refusal of others kept for the integration stays. Nothing for a draft.
    /// </summary>
    public void KeepRefusal(IntegrationConnectionSettings settings, IntegrationCallStatus status)
    {
        if (settings.ConnectionId is not { } connectionId)
            return;

        var fingerprint = TransmissionSession.Fingerprint(settings);
        lock (_refusalsLock)
        {
            if (!_refusals.TryGetValue(connectionId, out var kept))
                _refusals[connectionId] = kept = [];
            kept.RemoveAll(refusal => refusal.WasRefusedWith(fingerprint));
            kept.Add(new Refusal(fingerprint, status));
        }
    }

    /// <summary>
    /// Forgets the refusal kept for <paramref name="settings"/>' integration once Transmission has taken the same address
    /// and credentials — only theirs: a test of other ones, typed into the Edit dialog and not saved, says nothing about the
    /// ones the integration's calls still send.
    /// </summary>
    public void ForgetRefusal(IntegrationConnectionSettings settings)
    {
        if (settings.ConnectionId is not { } connectionId)
            return;

        var fingerprint = TransmissionSession.Fingerprint(settings);
        lock (_refusalsLock)
        {
            if (!_refusals.TryGetValue(connectionId, out var kept))
                return;
            kept.RemoveAll(refusal => refusal.WasRefusedWith(fingerprint));
            if (kept.Count == 0)
                _refusals.Remove(connectionId);
        }
    }

    /// <summary>A refusal: how, and the fingerprint of the address and credentials refused.</summary>
    private sealed class Refusal(byte[] fingerprint, IntegrationCallStatus status)
    {
        /// <summary><see cref="IntegrationCallStatus.AuthenticationFailed"/> for a 401,
        /// <see cref="IntegrationCallStatus.Refused"/> for a 403.</summary>
        public IntegrationCallStatus Status { get; } = status;

        /// <summary>Whether <paramref name="other"/> is the fingerprint of the address and credentials refused
        /// (<see cref="TransmissionSession.Fingerprint"/>).</summary>
        public bool WasRefusedWith(byte[] other) => CryptographicOperations.FixedTimeEquals(fingerprint, other);
    }
}
