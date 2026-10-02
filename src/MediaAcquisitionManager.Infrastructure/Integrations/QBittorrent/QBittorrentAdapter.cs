using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using MediaAcquisitionManager.Core.Abstractions.Downloads;
using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Infrastructure.Integrations.QBittorrent;

/// <summary>
/// qBittorrent's Web API behind the core's contracts (Adapter, ADR 0005). MAM-7 builds the
/// registration and the connection test; MAM-11 adds the destinations of the download client contract
/// (<see cref="IDownloadClient"/>): the categories and the folders they save to in
/// <c>QBittorrentAdapter.Destinations.cs</c> and <see cref="QBittorrentCategoryPaths"/>, and the session
/// the calls share in <c>QBittorrentAdapter.Sessions.cs</c> and <see cref="QBittorrentSessions"/>. MAM-13
/// adds the submission of a release in <c>QBittorrentAdapter.Submissions.cs</c>, and the lookup of the
/// torrents that hold its info hashes in <c>QBittorrentAdapter.Jobs.cs</c>; MAM-14 adds following a job and
/// its files.
/// </summary>
/// <remarks>
/// The sign-in and GETs, plus — MAM-13 — one write: <c>torrents/add</c>, sent once on a session a read has
/// just confirmed and never sent again (house rule 8: a write sent twice may act twice). Nothing else that
/// changes anything in qBittorrent. Every answer is read as an allowlist, property by property by name
/// (house rule 8).
/// </remarks>
internal sealed partial class QBittorrentAdapter(IHttpClientFactory httpClients)
    : HttpIntegrationAdapter(httpClients, HttpClientName), IDownloadClient
{
    /// <summary>The stored type id — part of the on-disk contract (ADR 0005).</summary>
    public const string TypeId = "qbittorrent";

    /// <summary>The named client this adapter's requests go through, so a test can swap its handler.</summary>
    public const string HttpClientName = TypeId;

    /// <summary>Far longer than qBittorrent's 32-character session id; a longer name or value is not
    /// one this app sends back.</summary>
    private const int CookiePartMaximumLength = 256;

    /// <summary>Longer than any version qBittorrent reports (<c>v5.2.3</c>).</summary>
    private const int VersionMaximumLength = 32;

    private const string CredentialsRefusedError = "qBittorrent refused the user name or password.";

    private const string BannedError =
        "qBittorrent has banned this app's address after too many failed sign-ins. Wait for the ban to end (qBittorrent's Web UI options set how long), then try again.";

    private const string NotQBittorrentSignInError =
        "The service at this address answered the sign-in, but not the way qBittorrent does.";

    private const string NotFoundError =
        "qBittorrent's Web API wasn't found at this address (404 Not Found). Use the address of qBittorrent's Web UI, such as http://192.168.1.10:8080.";

    private const string NoCookieError = "qBittorrent accepted the sign-in but sent no session cookie.";

    private const string UnusableCookieError = "qBittorrent sent a session cookie this app can't use.";

    private const string NotAVersionError = "The service at this address answered, but not with a qBittorrent version.";

    private const string SignInNeededError =
        "qBittorrent asks for a user name and password. Enter them, or let this app's address skip authentication in qBittorrent's Web UI options.";

    private const string SessionRefusedError = "qBittorrent refused the session it had just opened.";

    /// <summary>
    /// MAM-11 — the oldest qBittorrent the app works with (the owner's choice). 4.4.0 lists each
    /// category's folder as <c>save_path</c> instead of the <c>savePath</c> every other version sends
    /// (qBittorrent issue #15969, fixed in 4.4.1), and older versions differ more. The adapter was
    /// sampled live against 5.2.3 only (VendorSamples); 4.4.1 to 5.1 are accepted from reading
    /// qBittorrent's source. An older one is refused with a sentence rather than read through answers
    /// nobody has checked.
    /// </summary>
    private static readonly Version MinimumVersion = new(4, 4, 1);

    /// <inheritdoc/>
    public override IntegrationAdapterDescriptor Descriptor { get; } = new(
        TypeId,
        "qBittorrent",
        IntegrationKind.DownloadClient,
        // Optional: qBittorrent can let whitelisted subnets in without signing in (decision 1).
        UserName: CredentialRequirement.Optional,
        Password: CredentialRequirement.Optional,
        ApiKey: CredentialRequirement.NotUsed,
        ReportsFilePaths: true);

    /// <inheritdoc/>
    /// <remarks>
    /// MAM-11 — <c>torrents/add</c> takes a .torrent file and a magnet link alike, and a category saves to a
    /// folder qBittorrent says (<see cref="QBittorrentCategoryPaths"/>). No job id promised from a
    /// submission: 4.4.1 to 5.1 answer <c>torrents/add</c> with <c>Ok.</c> only, so with 4.4.1 the oldest
    /// version taken the type can't promise one, and the info hash correlates instead. MAM-13 — 5.2 lists
    /// the ids it added (<c>added_torrent_ids</c>), and the adapter passes one on when it gets exactly one;
    /// any other answer leaves the receipt without an id.
    /// </remarks>
    public DownloadClientCapabilities Capabilities { get; } = new(
        PayloadKinds: [ReleasePayloadKind.TorrentFile, ReleasePayloadKind.Magnet],
        DestinationsHavePaths: true,
        SubmissionReturnsJobId: false);

    /// <summary>
    /// Signs in when a user name is set (the credential rules make the password come with it), then
    /// reads <c>GET /api/v2/app/version</c> — with the session cookie when there is one.
    /// </summary>
    protected override async Task<ConnectionTestResult> TestAsync(
        HttpClient client, IntegrationConnectionSettings settings, ICollection<string> sessionSecrets, CancellationToken cancellationToken)
    {
        string? sessionCookie = null;
        if (!string.IsNullOrEmpty(settings.UserName))
        {
            var signIn = await SignInAsync(client, settings, cancellationToken);
            if (signIn.Failure is { } failure)
            {
                // MAM-11 — a test qBittorrent refused counts towards its ban like any other sign-in, so the
                // download client calls don't send the same credentials again after it (ADR 0010).
                if (failure.Status is IntegrationCallStatus.AuthenticationFailed or IntegrationCallStatus.Refused)
                    _sessions.KeepTestRefusal(settings, failure.Status);
                return failure;
            }
            // MAM-11 — the test is the owner's way to try refused credentials again, so it never asks
            // whether they were refused; once qBittorrent takes them, the download client calls may too.
            _sessions.ForgetRefusal(settings);
            sessionCookie = signIn.Cookie;
            // The session is as good as the password until it expires: masked like one.
            sessionSecrets.Add(signIn.SessionValue!);
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, settings.Resolve("api/v2/app/version"));
        // The one cookie this call means to send; the client keeps no cookie container.
        if (sessionCookie is not null)
            request.Headers.TryAddWithoutValidation("Cookie", sessionCookie);

        using var response = await client.SendAsync(request, cancellationToken);
        var signedIn = sessionCookie is not null;
        return response.StatusCode switch
        {
            HttpStatusCode.OK => DescribeVersion(await ReadTextAsync(response, cancellationToken), signedIn),
            HttpStatusCode.Forbidden when signedIn => ConnectionTestResult.Failed(IntegrationCallStatus.UnexpectedResponse, SessionRefusedError),
            HttpStatusCode.Forbidden => ConnectionTestResult.Failed(IntegrationCallStatus.AuthenticationFailed, SignInNeededError),
            HttpStatusCode.NotFound => ConnectionTestResult.Failed(IntegrationCallStatus.UnexpectedResponse, NotFoundError),
            _ => DescribeUnexpectedStatus(response, settings, sessionSecrets),
        };
    }

    /// <summary>
    /// <c>POST /api/v2/auth/login</c>, exactly once: a failed sign-in counts towards qBittorrent's ban
    /// of the address, so it is never repeated.
    /// </summary>
    /// <remarks>
    /// The credentials travel form-encoded in the body, never in the URL. No <c>Referer</c> or
    /// <c>Origin</c>: by its source, qBittorrent compares them with <c>Host</c> only when present, and
    /// a proxy that rewrites <c>Host</c> would make a <c>Referer</c> fail the sign-in.
    /// </remarks>
    private async Task<SignInResult> SignInAsync(HttpClient client, IntegrationConnectionSettings settings, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, settings.Resolve("api/v2/auth/login"))
        {
            Content = new FormUrlEncodedContent(
            [
                new KeyValuePair<string, string>("username", settings.UserName!),
                new KeyValuePair<string, string>("password", settings.Password ?? ""),
            ]),
        };

        using var response = await client.SendAsync(request, cancellationToken);
        switch (response.StatusCode)
        {
            // 5.2 and later answer a good sign-in with 204.
            case HttpStatusCode.NoContent:
                return ReadSessionCookie(response);
            // Earlier versions answer 200 "Ok." or 200 "Fails.".
            case HttpStatusCode.OK:
                var body = (await ReadTextAsync(response, cancellationToken)).Trim();
                if (string.Equals(body, "Ok.", StringComparison.Ordinal))
                    return ReadSessionCookie(response);
                return SignInResult.Failed(string.Equals(body, "Fails.", StringComparison.Ordinal)
                    ? ConnectionTestResult.Failed(IntegrationCallStatus.AuthenticationFailed, CredentialsRefusedError)
                    : ConnectionTestResult.Failed(IntegrationCallStatus.UnexpectedResponse, NotQBittorrentSignInError));
            case HttpStatusCode.Unauthorized:
                return SignInResult.Failed(ConnectionTestResult.Failed(IntegrationCallStatus.AuthenticationFailed, CredentialsRefusedError));
            case HttpStatusCode.Forbidden:
                return SignInResult.Failed(ConnectionTestResult.Failed(IntegrationCallStatus.Refused, BannedError));
            case HttpStatusCode.NotFound:
                return SignInResult.Failed(ConnectionTestResult.Failed(IntegrationCallStatus.UnexpectedResponse, NotFoundError));
            default:
                // No session yet: this is the call that would open one.
                return SignInResult.Failed(DescribeUnexpectedStatus(response, settings, sessionSecrets: []));
        }
    }

    /// <summary>
    /// The first <c>Set-Cookie</c>'s <c>name=value</c>, whatever its name: <c>QBT_SID_&lt;port&gt;</c>
    /// since 5.2, <c>SID</c> before (VendorSamples README). Its attributes are dropped; the cookie is
    /// sent back once, to the same address, straight away.
    /// </summary>
    private static SignInResult ReadSessionCookie(HttpResponseMessage response)
    {
        // Raw values: a header value that doesn't parse is still something the service sent, and
        // it is judged below instead of silently skipped.
        if (!response.Headers.NonValidated.TryGetValues("Set-Cookie", out var values) || values.Count == 0)
            return SignInResult.Failed(ConnectionTestResult.Failed(IntegrationCallStatus.UnexpectedResponse, NoCookieError));

        var setCookie = values.First();
        var end = setCookie.IndexOf(';', StringComparison.Ordinal);
        var pair = (end < 0 ? setCookie : setCookie[..end]).Trim();
        var separator = pair.IndexOf('=', StringComparison.Ordinal);
        if (separator <= 0)
            return Unusable();

        var name = pair[..separator].Trim();
        var value = pair[(separator + 1)..].Trim();
        // A control character (CR and LF above all) would split the Cookie header this value is sent
        // back in; an oversized part is not a session id.
        if (name.Length == 0 || value.Length == 0
            || name.Length > CookiePartMaximumLength || value.Length > CookiePartMaximumLength
            || name.Any(char.IsControl) || value.Any(char.IsControl))
            return Unusable();

        return new SignInResult(null, $"{name}={value}", value);

        static SignInResult Unusable() =>
            SignInResult.Failed(ConnectionTestResult.Failed(IntegrationCallStatus.UnexpectedResponse, UnusableCookieError));
    }

    /// <summary>The version as qBittorrent reports it (<c>v5.2.3</c>), when the body is one this app works
    /// with.</summary>
    private static ConnectionTestResult DescribeVersion(string body, bool signedIn)
    {
        if (ReportedVersion(body) is not { } version)
            return ConnectionTestResult.Failed(IntegrationCallStatus.UnexpectedResponse, NotAVersionError);

        // MAM-11 — the version gate, as in every download client call: an owner testing an old
        // qBittorrent hears it here, before a destination is saved on it.
        if (UnsupportedVersionError(version) is { } unsupported)
            return ConnectionTestResult.Failed(IntegrationCallStatus.UnsupportedVersion, unsupported);

        return ConnectionTestResult.Succeeded(signedIn
            ? $"Connected to qBittorrent {version}."
            : $"Connected to qBittorrent {version} without signing in: it lets this app's address skip authentication.");
    }

    /// <summary>The version in <paramref name="body"/> as qBittorrent reports it (<c>v5.2.3</c>), or
    /// <c>null</c> when the body is not one.</summary>
    private static string? ReportedVersion(string body)
    {
        var version = body.Trim();
        return version.Length > VersionMaximumLength || !VersionPattern().IsMatch(version) ? null : version;
    }

    /// <summary>
    /// The major, minor and build numbers of a version <see cref="ReportedVersion"/> accepted (a missing
    /// build counts as 0), or <c>null</c> when one of them doesn't fit an <see cref="int"/>. A fourth part,
    /// as in 4.1.9.1, is a rebuild of the same release and isn't compared.
    /// </summary>
    private static Version? ComparableVersion(string reported)
    {
        var parts = (reported.StartsWith('v') ? reported[1..] : reported).Split('.');
        var numbers = new int[3];
        for (var index = 0; index < numbers.Length && index < parts.Length; index++)
        {
            if (!int.TryParse(parts[index], NumberStyles.None, CultureInfo.InvariantCulture, out numbers[index]))
                return null;
        }

        return new Version(numbers[0], numbers[1], numbers[2]);
    }

    /// <summary>
    /// The sentence refusing <paramref name="reported"/>, or <c>null</c> when it is
    /// <see cref="MinimumVersion"/> or later. A version that can't be compared is refused, never guessed
    /// at.
    /// </summary>
    private static string? UnsupportedVersionError(string reported) =>
        ComparableVersion(reported) switch
        {
            null => $"qBittorrent reported the version “{reported}”, which the app can't compare with {MinimumVersion}, the oldest version it works with.",
            var version when version < MinimumVersion =>
                $"This is qBittorrent {reported}; the app works with qBittorrent {MinimumVersion} or later. Update qBittorrent, then test again.",
            _ => null,
        };

    /// <summary>
    /// <c>v5.2.3</c> or <c>4.6.7</c>: ASCII digits only (<c>\d</c> would take any script's), and
    /// <c>\z</c> rather than <c>$</c>, which would also accept a trailing line break.
    /// </summary>
    [GeneratedRegex(@"^v?[0-9]+(\.[0-9]+){1,3}\z")]
    private static partial Regex VersionPattern();

    /// <summary>How a sign-in went: a failure to answer with, or the cookie to send and the session
    /// value to mask. Private: nothing outside this adapter's calls needs it.</summary>
    private sealed record SignInResult(ConnectionTestResult? Failure, string? Cookie, string? SessionValue)
    {
        public static SignInResult Failed(ConnectionTestResult failure) => new(failure, null, null);

        // A record's generated ToString prints every property, the live session included.
        public override string ToString() => $"SignInResult {{ Failure = {Failure} }}";
    }
}
