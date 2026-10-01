using System.Net;
using MediaAcquisitionManager.Core.Abstractions.Integrations;

namespace MediaAcquisitionManager.Infrastructure.Integrations.QBittorrent;

// MAM-11 — the session the download client calls send: kept between calls (QBittorrentSessions) while the
// integration's address and credentials stay the same, and renewed once when qBittorrent has forgotten
// it. A sign-in qBittorrent refused is remembered the same way and not sent again. The connection test
// signs in afresh every time, because proving the credentials is its job, and leaves the kept session
// alone; when its sign-in works, the refusal of those credentials is forgotten.
internal sealed partial class QBittorrentAdapter
{
    private const string RememberedCredentialsRefusedError =
        "qBittorrent refused the user name or password the last time the app signed in, so the app won't try them again: each refused sign-in counts towards qBittorrent's ban of this app's address. Correct them under Integrations, or test the connection there once qBittorrent accepts them.";

    private const string RememberedBanError =
        "qBittorrent had banned this app's address the last time the app signed in, so the app won't sign in again on its own. Once the ban has ended (qBittorrent's Web UI options set how long), test the connection under Integrations.";

    /// <summary>The sessions this singleton keeps, one per saved integration, and the sign-ins qBittorrent
    /// refused.</summary>
    private readonly QBittorrentSessions _sessions = new();

    /// <summary>
    /// The session a call starts with: none when no user name is set (qBittorrent lets this address in
    /// without signing in), the kept one when it was opened with these settings, and otherwise a fresh
    /// sign-in — or the refusal of these settings remembered from the last one. Every session value used
    /// goes into <paramref name="sessionSecrets"/>.
    /// </summary>
    private async Task<(CallSession? Session, ConnectionTestResult? Failure)> OpenSessionAsync(
        HttpClient client, IntegrationConnectionSettings settings, ICollection<string> sessionSecrets, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(settings.UserName))
            return (new CallSession(null, signedIn: false), null);

        if (_sessions.Find(settings) is { } kept)
        {
            sessionSecrets.Add(kept.Value);
            return (new CallSession(kept, signedIn: false), null);
        }

        var (session, failure) = await SignInForCallAsync(client, settings, sessionSecrets, cancellationToken);
        return failure is null ? (new CallSession(session, signedIn: true), null) : (null, failure);
    }

    /// <summary>
    /// One sign-in (<see cref="SignInAsync"/>, never repeated after a failure): its session is masked in
    /// this call's messages and kept for the next call — unless the settings are an unsaved draft, which
    /// keeps nothing. None at all when qBittorrent refused these very address and credentials last time.
    /// </summary>
    /// <remarks>
    /// MAM-11 — only qBittorrent's own no is remembered: refused credentials, or a banned address.
    /// Refused credentials sent again only add to the failures its ban counts, and a ban lasts for a time
    /// the app can't see (ADR 0010). Anything else — nothing listening, no answer in time, a 5xx, an
    /// answer that isn't qBittorrent's — never reached qBittorrent's count, and the next call tries again.
    /// </remarks>
    private async Task<(QBittorrentSession? Session, ConnectionTestResult? Failure)> SignInForCallAsync(
        HttpClient client, IntegrationConnectionSettings settings, ICollection<string> sessionSecrets, CancellationToken cancellationToken)
    {
        if (_sessions.FindRefusal(settings) is { } refused)
        {
            return (null, ConnectionTestResult.Failed(
                refused, refused == IntegrationCallStatus.Refused ? RememberedBanError : RememberedCredentialsRefusedError));
        }

        var signIn = await SignInAsync(client, settings, cancellationToken);
        if (signIn.Failure is { } failure)
        {
            if (failure.Status is IntegrationCallStatus.AuthenticationFailed or IntegrationCallStatus.Refused)
                _sessions.KeepRefusal(settings, failure.Status);
            return (null, failure);
        }

        var session = QBittorrentSession.OpenedWith(settings, signIn.Cookie!, signIn.SessionValue!);
        sessionSecrets.Add(session.Value);
        _sessions.Keep(settings, session);
        return (session, null);
    }

    /// <summary>
    /// <c>GET</c> of <paramref name="relativePath"/> with the call's session: qBittorrent's 200 answer, for
    /// the caller to read and dispose, or the failure.
    /// </summary>
    /// <remarks>
    /// <para>
    /// qBittorrent answers a session it no longer knows — expired after its idle timeout, or lost when it
    /// restarted — with <c>403 Forbidden</c> (VendorSamples/QBittorrent/expired-session-response.txt; it
    /// never answers 401 for it). So a 403 to a kept session drops it, signs in once more and sends this
    /// one GET again, once.
    /// </para>
    /// <para>
    /// Only reads are sent again (house rule 8: a write sent twice may act twice), and MAM-11 sends nothing
    /// else. A 403 to a session this call signed in for is not: qBittorrent refused what it had just
    /// handed out, and asking again would only repeat it. A failed sign-in is never repeated either, since
    /// each one counts towards qBittorrent's ban of the address — and a refused one not even by a later
    /// call, while the address and credentials stay the same (<see cref="SignInForCallAsync"/>).
    /// </para>
    /// </remarks>
    private async Task<(HttpResponseMessage? Answer, ConnectionTestResult? Failure)> GetAsync(
        HttpClient client,
        IntegrationConnectionSettings settings,
        CallSession call,
        string relativePath,
        ICollection<string> sessionSecrets,
        CancellationToken cancellationToken)
    {
        var response = await SendGetAsync(client, settings, call.Session, relativePath, cancellationToken);
        if (response.StatusCode == HttpStatusCode.Forbidden && call.Session is { } forgotten && !call.SignedIn)
        {
            response.Dispose();
            _sessions.Drop(settings, forgotten);
            var (renewed, failure) = await SignInForCallAsync(client, settings, sessionSecrets, cancellationToken);
            if (failure is not null)
                return (null, failure);

            call.Renew(renewed!);
            response = await SendGetAsync(client, settings, call.Session, relativePath, cancellationToken);
        }

        if (response.StatusCode == HttpStatusCode.OK)
            return (response, null);

        using (response)
        {
            if (response.StatusCode != HttpStatusCode.Forbidden)
            {
                return (null, response.StatusCode == HttpStatusCode.NotFound
                    ? ConnectionTestResult.Failed(IntegrationCallStatus.UnexpectedResponse, NotFoundError)
                    : DescribeUnexpectedStatus(response, settings, sessionSecrets));
            }

            if (call.Session is not { } refused)
                return (null, ConnectionTestResult.Failed(IntegrationCallStatus.AuthenticationFailed, SignInNeededError));

            // Never kept once refused: the next call signs in rather than sending it again.
            _sessions.Drop(settings, refused);
            return (null, ConnectionTestResult.Failed(IntegrationCallStatus.UnexpectedResponse, SessionRefusedError));
        }
    }

    private static async Task<HttpResponseMessage> SendGetAsync(
        HttpClient client, IntegrationConnectionSettings settings, QBittorrentSession? session, string relativePath, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, settings.Resolve(relativePath));
        // The one cookie this call means to send; the client keeps no cookie container.
        if (session is not null)
            request.Headers.TryAddWithoutValidation("Cookie", session.Cookie);

        return await client.SendAsync(request, cancellationToken);
    }

    /// <summary>
    /// The session one call sends, and whether that call signed in for it: a kept session qBittorrent
    /// refuses is renewed once, one it has just handed out is not. Lives for one call; its
    /// <c>ToString</c> is the type's name, never the session.
    /// </summary>
    private sealed class CallSession(QBittorrentSession? session, bool signedIn)
    {
        /// <summary>The session sent; <c>null</c> when the call doesn't sign in.</summary>
        public QBittorrentSession? Session { get; private set; } = session;

        /// <summary>Whether this call signed in for <see cref="Session"/>.</summary>
        public bool SignedIn { get; private set; } = signedIn;

        /// <summary>Sends <paramref name="renewed"/>, signed in for by this call, from now on.</summary>
        public void Renew(QBittorrentSession renewed)
        {
            Session = renewed;
            SignedIn = true;
        }
    }
}
