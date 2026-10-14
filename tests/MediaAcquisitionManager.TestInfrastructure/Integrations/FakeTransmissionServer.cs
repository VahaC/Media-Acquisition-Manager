using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using MediaAcquisitionManager.Core.Downloads;
using MediaAcquisitionManager.TestInfrastructure.Http;

namespace MediaAcquisitionManager.TestInfrastructure.Integrations;

/// <summary>
/// MAM-30 — one Transmission behind a <see cref="RecordingHttpHandler"/>: its legacy RPC as 4.0.6 and 4.1.3 answer it
/// (<see cref="Version"/>), in the shapes MAM-29 recorded in <c>VendorSamples/Transmission</c> and with the rules
/// Transmission's sources at those two tags show where nothing was recorded. It serves the four methods the app may
/// send — <c>session-get</c>, <c>free-space</c>, <c>torrent-get</c> and <c>torrent-add</c> — over the torrents it has
/// (<see cref="Torrents"/>) and the folders it can see (<see cref="AddFolder"/>), and lets a test move a torrent on as
/// Transmission would (<see cref="UpdateTorrent"/>, <see cref="GetMetadata"/>, <see cref="Complete"/>,
/// <see cref="RemoveTorrent"/>).
/// </summary>
/// <remarks>
/// <para>
/// Every request goes to one address, <c>POST {base}/transmission/rpc</c>, and is checked in the order Transmission's
/// <c>rpc-server.cc</c> <c>handle_request</c> checks it, each check answering a page of its own before the body is read:
/// its RPC locked by anti-brute-force (403) — the client's address not on its whitelist (403, the same page) —
/// <c>OPTIONS</c> (200) — the Basic credentials, only when it has a password (401) — a path outside
/// <c>/transmission/</c>, or <c>/transmission/web</c>, moved to the web interface (301) — the web interface — the host
/// name, only without a password (421) — the session id (409, the id in <c>X-Transmission-Session-Id</c>) — another path
/// (404) — a method other than <c>POST</c> (405). Only then is the body read: so a request any check refused did
/// nothing, and is no call (<see cref="Calls"/>).
/// </para>
/// <para>
/// Every RPC read is recorded (<see cref="Calls"/>, each <c>torrent-add</c> also in <see cref="AddRequests"/>), and
/// every one but the three reads lands in <see cref="Writes"/>. A method other than the four is answered with the
/// version's sentence for a method it doesn't know — 4.0.6's "method name not recognized", 4.1.3's "no method name" —
/// and never acted on: Transmission would stop or remove a torrent for <c>torrent-stop</c> or <c>torrent-remove</c>,
/// which the app must never send (ADR 0014), so a test asserts that <see cref="Writes"/> holds nothing but the add.
/// </para>
/// <para>
/// Two simplifications, on purpose: a torrent a <c>torrent-add</c> adds is listed downloading (or stopped, when paused)
/// at once, without the verify Transmission runs first, which 4.0.6 showed straight after its add (a test plants that
/// state); and <c>torrent-get</c> knows only the fields MAM-29 recorded, every field the app reads among them, leaving
/// out any other name as Transmission leaves out one it doesn't know.
/// </para>
/// <para>
/// Like <c>FakeQBittorrentServer</c>, each answer is built fresh from the properties, which a test sets before the
/// calls, and one lock covers the torrents, the folders, the session and what is recorded, whoever changes them — a
/// test or a request — so two adds racing add one torrent, and a test may change one while the poller reads. An
/// <see cref="Intercept"/> or <see cref="RpcIntercept"/> a test sets must be safe to call concurrently itself.
/// </para>
/// <para>
/// It is a Linux Transmission, as in the Docker images: a folder is absolute when it starts with <c>/</c>, and 4.1 keeps
/// a name as Linux takes it. A Windows Transmission isn't modelled.
/// </para>
/// </remarks>
public sealed partial class FakeTransmissionServer
{
    /// <summary>Where a test's Transmission is unless it says otherwise; a fake name, never a real host.</summary>
    public const string DefaultBaseUrl = "http://transmission.test:9091";

    /// <summary>Transmission's <c>rpc-url</c>, its default: every address it serves is under it.</summary>
    public const string RpcUrl = "/transmission/";

    /// <summary>The RPC's address below the base: every call goes there.</summary>
    public const string RpcPath = RpcUrl + "rpc";

    /// <summary>The web interface's address below the base, where a wrong path is moved to.</summary>
    public const string WebPath = RpcUrl + "web/";

    /// <summary>The user name the server takes unless a test changes it.</summary>
    public const string DefaultUserName = "admin";

    /// <summary>The password the server takes unless a test changes it.</summary>
    public const string DefaultPassword = "correct-horse-battery";

    /// <summary>The header the session id travels in, both ways.</summary>
    public const string SessionIdHeader = "X-Transmission-Session-Id";

    /// <summary>The header 4.1's 409 names its RPC's semantic version in.</summary>
    public const string RpcVersionHeader = "X-Transmission-Rpc-Version";

    /// <summary>
    /// What <c>session-get</c>'s <c>download-dir</c> answers unless a test changes it: the linuxserver image's, which the
    /// Api tests' path mapping <c>/downloads → /media</c> leads to. The samples' <c>/scratch</c> was MAM-29's own.
    /// </summary>
    public const string DefaultDownloadDir = "/downloads";

    /// <summary>What <c>incomplete-dir</c> answers unless a test changes it: the linuxserver image's.</summary>
    public const string DefaultIncompleteDir = "/downloads/incomplete";

    /// <summary>The free bytes <c>free-space</c> answers for a folder the server sees: the sample's.</summary>
    public const long DefaultFreeBytes = 52_361_691_136;

    /// <summary>The total bytes <c>free-space</c> answers for a folder the server sees: the sample's.</summary>
    public const long DefaultTotalBytes = 67_049_664_512;

    /// <summary>What the web interface answers: a stand-in for its <c>index.html</c>, which wasn't recorded.</summary>
    public const string WebInterfacePage = "<!DOCTYPE html><html><head><title>Transmission Web Interface</title></head><body></body></html>";

    // session-id.cc: 48 characters of [0-9a-zA-Z].
    private const string SessionIdCharacters = "0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ";
    private const int SessionIdLength = 48;

    // libevent's own type for the pages Transmission sends without one.
    private const string PageMediaType = "text/html; charset=ISO-8859-1";

    private readonly Uri _base;
    private readonly TimeProvider _time;

    // The torrents, the folders, the session, the login attempts and what is recorded change together: two adds racing
    // must never both add one torrent.
    private readonly Lock _lock = new();
    private readonly List<FakeTransmissionTorrent> _torrents = [];
    private readonly Dictionary<string, (long Free, long Total)> _folders = new(StringComparer.Ordinal);
    private readonly List<FakeTransmissionCall> _calls = [];
    private readonly List<FakeTransmissionAddRequest> _addRequests = [];
    private string _sessionId = NewSessionId();
    private int _nextId = 1;
    private int _loginAttempts;

    /// <param name="baseUrl">The address the server answers at, as an integration stores it (no trailing slash); a path
    /// is the base path of a reverse proxy in front of it, which Transmission itself never sees.</param>
    /// <param name="timeProvider">The clock a torrent's <c>addedDate</c> and <c>doneDate</c> are read from; the system's
    /// unless given.</param>
    public FakeTransmissionServer(string baseUrl = DefaultBaseUrl, TimeProvider? timeProvider = null)
    {
        BaseUrl = baseUrl;
        _base = new Uri(baseUrl.TrimEnd('/') + "/");
        _time = timeProvider ?? TimeProvider.System;
        _folders[DefaultDownloadDir] = (DefaultFreeBytes, DefaultTotalBytes);
    }

    /// <summary>The address this server answers at.</summary>
    public string BaseUrl { get; }

    /// <summary>The user name the Basic credentials must carry.</summary>
    public string UserName { get; set; } = DefaultUserName;

    /// <summary>
    /// The password the Basic credentials must carry; <c>null</c> for a Transmission without one, which takes any
    /// credentials or none (as MAM-29 saw) and checks the host name instead (<see cref="HostWhitelist"/>).
    /// </summary>
    public string? Password { get; set; } = DefaultPassword;

    /// <summary>Which Transmission this is: 4.1.3 unless a test says otherwise.</summary>
    public FakeTransmissionVersion Version { get; set; } = FakeTransmissionVersion.V4_1_3;

    /// <summary>
    /// <c>session-get</c>'s <c>download-dir</c>, where a <c>torrent-add</c> without <c>download-dir</c> lands. Changing it
    /// doesn't make <c>free-space</c> see it: <see cref="AddFolder"/> does.
    /// </summary>
    public string DownloadDir { get; set; } = DefaultDownloadDir;

    /// <summary><c>session-get</c>'s <c>incomplete-dir</c>.</summary>
    public string IncompleteDir { get; set; } = DefaultIncompleteDir;

    /// <summary><c>session-get</c>'s <c>incomplete-dir-enabled</c>: off, as the samples have it — the linuxserver image
    /// ships it on.</summary>
    public bool IncompleteDirEnabled { get; set; }

    /// <summary><c>session-get</c>'s <c>rename-partial-files</c>: on, as the samples have it. Only the disk would show
    /// it, which the server has none of.</summary>
    public bool RenamePartialFiles { get; set; } = true;

    /// <summary><c>session-get</c>'s <c>start-added-torrents</c>: whether an add that doesn't say <c>paused</c>
    /// starts.</summary>
    public bool StartAddedTorrents { get; set; } = true;

    /// <summary>When set, every request fails as one to a port nothing listens on: the client throws before any
    /// answer.</summary>
    public bool Unreachable { get; set; }

    /// <summary>Whether the client's address is on Transmission's <c>rpc-whitelist</c>; when not, every request answers
    /// <c>403 Forbidden</c> before anything else is looked at.</summary>
    public bool AddressAllowed { get; set; } = true;

    /// <summary>
    /// The host names Transmission takes while it has no password — <c>rpc-host-whitelist</c>, with its <c>*</c> and
    /// <c>?</c> wildcards —, besides an IP address and <c>localhost</c>, which it always takes; any other answers
    /// <c>421 Misdirected Request</c>. <c>null</c>, the default, is <c>rpc-host-whitelist-enabled</c> off; Transmission's
    /// own default is on with no name, so a Transmission without a password reached by a name refuses it.
    /// </summary>
    public IReadOnlyList<string>? HostWhitelist { get; set; }

    /// <summary>
    /// Transmission's <c>anti-brute-force-threshold</c> with <c>anti-brute-force-enabled</c> on: once this many wrong
    /// credentials came in a row (<see cref="LoginAttempts"/>), every request answers <c>403 Forbidden</c>, the right
    /// password too, until Transmission restarts. <c>null</c>, the default, is anti-brute-force off, Transmission's
    /// default; 0 is a Transmission already locked.
    /// </summary>
    public int? AntiBruteForceThreshold { get; set; }

    /// <summary>The wrong credentials in a row counted towards <see cref="AntiBruteForceThreshold"/>; right ones set it
    /// back to 0, and nothing is counted while anti-brute-force is off.</summary>
    public int LoginAttempts
    {
        get
        {
            lock (_lock)
            {
                return _loginAttempts;
            }
        }
    }

    /// <summary>
    /// Answers a request before the server does — a failure, a slow answer, a page no Transmission sends — or returns
    /// <c>null</c> to let the server answer. It sees the request and, when its body is a JSON object, the call it holds
    /// (not recorded: that happens only when the server reads it).
    /// </summary>
    public Func<HttpRequestMessage, FakeTransmissionCall?, HttpResponseMessage?>? Intercept { get; set; }

    /// <summary>
    /// Answers a call the server read — every check passed and the call recorded — before the server does: the whole
    /// answer, such as <c>{"arguments":{},"result":"No space left on device"}</c>, whose <c>tag</c> the server adds; or
    /// <c>null</c> to let the server answer. Nothing it answers changes the server.
    /// </summary>
    public Func<FakeTransmissionCall, JsonObject?>? RpcIntercept { get; set; }

    /// <summary>The session id the next request must carry: 48 random characters, until <see cref="RotateSession"/>.</summary>
    public string SessionId
    {
        get
        {
            lock (_lock)
            {
                return _sessionId;
            }
        }
    }

    /// <summary>Every torrent the server has, given by a test or added through <c>torrent-add</c>, in the order it got
    /// them; a snapshot.</summary>
    public IReadOnlyList<FakeTransmissionTorrent> Torrents
    {
        get
        {
            lock (_lock)
            {
                return [.. _torrents];
            }
        }
    }

    /// <summary>Every RPC the server read, oldest first, whatever it answered; a snapshot.</summary>
    public IReadOnlyList<FakeTransmissionCall> Calls
    {
        get
        {
            lock (_lock)
            {
                return [.. _calls];
            }
        }
    }

    /// <summary>
    /// Every call the server read but <c>session-get</c>, <c>free-space</c> and <c>torrent-get</c>, oldest first: each
    /// <c>torrent-add</c>, and anything else, which could change Transmission or that the app has no business sending.
    /// A snapshot.
    /// </summary>
    public IReadOnlyList<FakeTransmissionCall> Writes
    {
        get
        {
            lock (_lock)
            {
                return [.. _calls.Where(call => !IsRead(call.Method))];
            }
        }
    }

    /// <summary>Every <c>torrent-add</c> the server read, oldest first, whatever it answered; a snapshot.</summary>
    public IReadOnlyList<FakeTransmissionAddRequest> AddRequests
    {
        get
        {
            lock (_lock)
            {
                return [.. _addRequests];
            }
        }
    }

    /// <summary>
    /// Lets <c>free-space</c> see <paramref name="path"/> (a trailing <c>/</c> or none, as statvfs reads it) with these
    /// bytes. A folder it doesn't see answers as a missing one does, which is also what a folder not mounted into
    /// Transmission answers (MAM-29). <see cref="DefaultDownloadDir"/> is seen from the start.
    /// </summary>
    public void AddFolder(string path, long freeBytes = DefaultFreeBytes, long totalBytes = DefaultTotalBytes)
    {
        ArgumentNullException.ThrowIfNull(path);
        lock (_lock)
        {
            _folders[FolderKey(path)] = (freeBytes, totalBytes);
        }
    }

    /// <summary>Stops <c>free-space</c> seeing <paramref name="path"/>, as unmounting it does; whether it saw it.</summary>
    public bool RemoveFolder(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        lock (_lock)
        {
            return _folders.Remove(FolderKey(path));
        }
    }

    /// <summary>
    /// Gives the server <paramref name="torrent"/>, as one added in Transmission itself, under the next id, and returns
    /// it as kept: listed straight away, and an add of it answered as a duplicate. Throws when the server already has a
    /// torrent of that hash.
    /// </summary>
    public FakeTransmissionTorrent AddTorrent(FakeTransmissionTorrent torrent)
    {
        ArgumentNullException.ThrowIfNull(torrent);
        lock (_lock)
        {
            if (_torrents.Exists(held => string.Equals(held.HashString, torrent.HashString, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException($"The server already has the torrent {torrent.HashString}.");

            var kept = torrent with { Id = _nextId++ };
            _torrents.Add(kept);
            return kept;
        }
    }

    /// <summary>
    /// Gives the server a torrent named by <paramref name="hashes"/>' v1 hash (<see cref="FakeTransmissionTorrent.Of"/>):
    /// in <paramref name="downloadDir"/> (<see cref="DownloadDir"/> unless given), with <paramref name="files"/> — seeding
    /// and complete when every wanted byte is there, which without files it is —, added now and done now when complete.
    /// </summary>
    public FakeTransmissionTorrent AddTorrent(
        TorrentInfoHashes hashes,
        string name = "Added in Transmission",
        string? downloadDir = null,
        IReadOnlyList<string>? labels = null,
        IReadOnlyList<FakeTransmissionFile>? files = null)
    {
        var torrent = FakeTransmissionTorrent.Of(hashes, name, downloadDir ?? DownloadDir, files, labels);
        var now = Now();
        return AddTorrent(torrent with { AddedDate = now, DoneDate = torrent.LeftUntilDone == 0 ? now : 0 });
    }

    /// <summary>
    /// Replaces the torrent of <paramref name="hashString"/> (any letter case) with what <paramref name="update"/> makes
    /// of it, as a download moving on in Transmission does — its status, its progress, an error — and returns it.
    /// Throws when the server has no such torrent.
    /// </summary>
    public FakeTransmissionTorrent UpdateTorrent(string hashString, Func<FakeTransmissionTorrent, FakeTransmissionTorrent> update)
    {
        ArgumentNullException.ThrowIfNull(hashString);
        ArgumentNullException.ThrowIfNull(update);
        lock (_lock)
        {
            var index = _torrents.FindIndex(torrent => string.Equals(torrent.HashString, hashString, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
                throw new InvalidOperationException($"The server has no torrent {hashString}.");

            var updated = update(_torrents[index]) ?? throw new InvalidOperationException("An update must give the torrent back.");
            _torrents[index] = updated;
            return updated;
        }
    }

    /// <summary>
    /// What Transmission does to a magnet's torrent once its metadata arrives: it has <paramref name="files"/> from then
    /// on, and its sizes and progress follow from them (<see cref="FakeTransmissionTorrent.WithFiles"/>) — named
    /// <paramref name="name"/> when given, as the metadata may name it otherwise than the link did.
    /// </summary>
    public FakeTransmissionTorrent GetMetadata(string hashString, IReadOnlyList<FakeTransmissionFile> files, string? name = null)
    {
        ArgumentNullException.ThrowIfNull(files);
        return UpdateTorrent(hashString, torrent => torrent.WithFiles(files) with { Name = name ?? torrent.Name });
    }

    /// <summary>
    /// What Transmission shows once a download is complete: every wanted file whole, nothing left, seeding, done now —
    /// and <c>isFinished</c> still false, as it stays until a seeding limit. Throws for a torrent without its metadata,
    /// which can't be complete (<see cref="GetMetadata"/> first).
    /// </summary>
    public FakeTransmissionTorrent Complete(string hashString)
    {
        var now = Now();
        return UpdateTorrent(hashString, torrent =>
        {
            if (!torrent.HasMetadata)
                throw new InvalidOperationException($"The torrent {torrent.HashString} has no metadata yet, so it can't be complete.");

            var files = torrent.Files.Select(file => file.Wanted ? file with { BytesCompleted = file.Length } : file).ToList();
            return torrent.WithFiles(files) with { Status = 6, DoneDate = now, IsStalled = false };
        });
    }

    /// <summary>
    /// Takes away the torrent of <paramref name="hashString"/> (any letter case), as someone removing it in Transmission's
    /// web interface does: <c>torrent-get</c> no longer lists it. Whether the server had it.
    /// </summary>
    public bool RemoveTorrent(string hashString)
    {
        ArgumentNullException.ThrowIfNull(hashString);
        lock (_lock)
        {
            return _torrents.RemoveAll(torrent => string.Equals(torrent.HashString, hashString, StringComparison.OrdinalIgnoreCase)) > 0;
        }
    }

    /// <summary>
    /// Gives the server a new session id, as Transmission does every hour and on a restart: a request with the old one
    /// answers 409 with the new one from then on, and isn't read.
    /// </summary>
    public void RotateSession()
    {
        lock (_lock)
        {
            _sessionId = NewSessionId();
        }
    }

    /// <summary>Makes <paramref name="handler"/> answer as this server.</summary>
    public void Arm(RecordingHttpHandler handler) => Arm(handler, this);

    /// <summary>Makes <paramref name="handler"/> answer as <paramref name="servers"/>, each at its own address; a request
    /// to none of them answers 503.</summary>
    public static void Arm(RecordingHttpHandler handler, params FakeTransmissionServer[] servers)
    {
        ArgumentNullException.ThrowIfNull(handler);
        handler.Handler = (request, body) =>
            Array.Find(servers, server => server.Serves(request.RequestUri!)) is { } server
                ? server.Answer(request, body)
                : new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
    }

    /// <summary>
    /// For wire asserts: the RPC method a recorded request's body names (<c>session-get</c>, <c>torrent-add</c>…), or its
    /// HTTP method and path when the body is no RPC. A request a check refused shows like any other: its headers tell
    /// which (<see cref="SessionIdHeader"/>, <c>Authorization</c>).
    /// </summary>
    public static string Describe(RecordedHttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Envelope(request.Body) is { } envelope && TextOf(envelope, "method") is { } method
            ? method
            : $"{request.Method} {request.Uri.AbsolutePath}";
    }

    /// <summary>What this server answers <paramref name="request"/>, whose body was <paramref name="body"/>.</summary>
    public HttpResponseMessage Answer(HttpRequestMessage request, string? body)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (Unreachable)
            throw new HttpRequestException(HttpRequestError.ConnectionError, "No connection could be made because the target machine actively refused it.");

        var envelope = Envelope(body);
        if (Intercept?.Invoke(request, envelope is null ? null : CallOf(request, envelope)) is { } intercepted)
            return intercepted;

        return Check(request) ?? Rpc(request, envelope);
    }

    private bool Serves(Uri uri) =>
        Uri.Compare(uri, _base, UriComponents.SchemeAndServer, UriFormat.SafeUnescaped, StringComparison.OrdinalIgnoreCase) == 0
        && uri.AbsolutePath.StartsWith(_base.AbsolutePath, StringComparison.Ordinal);

    /// <summary>
    /// The page the first check that refuses <paramref name="request"/> answers, in <c>handle_request</c>'s order (4.0.6
    /// <c>rpc-server.cc:420-532</c>, 4.1.3 <c>:511-651</c>); <c>null</c> when it passes every one and goes to the RPC.
    /// </summary>
    private HttpResponseMessage? Check(HttpRequestMessage request)
    {
        lock (_lock)
        {
            if (AntiBruteForceThreshold is { } threshold && _loginAttempts >= threshold)
                return Page(HttpStatusCode.Forbidden, pastAddressCheck: false);
        }

        if (!AddressAllowed)
            return Page(HttpStatusCode.Forbidden, pastAddressCheck: false);
        if (request.Method == HttpMethod.Options)
            return Page(HttpStatusCode.OK);
        if (!IsAuthorized(request))
        {
            lock (_lock)
            {
                if (AntiBruteForceThreshold is not null)
                    _loginAttempts++;
            }

            var unauthorized = Page(HttpStatusCode.Unauthorized);
            unauthorized.Headers.TryAddWithoutValidation("WWW-Authenticate", "Basic realm=\"Transmission\"");
            return unauthorized;
        }

        lock (_lock)
        {
            _loginAttempts = 0;
        }

        // Transmission compares the request's own path and query with its rpc-url; behind a reverse proxy it never sees
        // the proxy's base path.
        var uri = TransmissionUri(request.RequestUri!);
        var location = uri.StartsWith(RpcUrl, StringComparison.Ordinal) ? uri[RpcUrl.Length..] : "";
        if (location is "" or "web")
        {
            var moved = Page(HttpStatusCode.MovedPermanently);
            moved.Headers.TryAddWithoutValidation("Location", WebPath);
            return moved;
        }

        if (location.StartsWith("web/", StringComparison.Ordinal))
            return WebInterface();
        if (!IsHostAllowed(request))
            return Page(HttpStatusCode.MisdirectedRequest, HostNotAllowedText());
        if (!HasSession(request))
            return Conflict();
        if (!location.StartsWith("rpc", StringComparison.Ordinal))
            return Page(HttpStatusCode.NotFound, uri);
        return request.Method == HttpMethod.Post ? null : Page(HttpStatusCode.MethodNotAllowed);
    }

    /// <summary>
    /// <c>is_authorized</c>: without a password anything passes; with one, <c>Basic </c> exactly, then base64 of the
    /// user name, a colon and the password — the user name ends at the first colon, the password may hold more.
    /// </summary>
    private bool IsAuthorized(HttpRequestMessage request)
    {
        if (Password is null)
            return true;
        if (HeaderOf(request, "Authorization") is not { } header || !header.StartsWith("Basic ", StringComparison.Ordinal))
            return false;

        string credentials;
        try
        {
            credentials = Encoding.UTF8.GetString(Convert.FromBase64String(header["Basic ".Length..]));
        }
        catch (FormatException)
        {
            return false;
        }

        var colon = credentials.IndexOf(':', StringComparison.Ordinal);
        var userName = colon < 0 ? credentials : credentials[..colon];
        var password = colon < 0 ? "" : credentials[(colon + 1)..];
        return string.Equals(userName, UserName, StringComparison.Ordinal) && string.Equals(password, Password, StringComparison.Ordinal);
    }

    /// <summary>
    /// <c>isHostnameAllowed</c>: any host while Transmission has a password or no host whitelist; otherwise an IP address
    /// (a port or none), <c>localhost</c>, or a name a whitelist entry matches. The Host header is the address's unless
    /// the request sets its own.
    /// </summary>
    private bool IsHostAllowed(HttpRequestMessage request)
    {
        if (Password is not null || HostWhitelist is not { } whitelist)
            return true;

        var host = request.Headers.Host ?? request.RequestUri!.Authority;
        if (string.IsNullOrEmpty(host))
            return false;
        if (IPEndPoint.TryParse(host, out _))
            return true;

        var colon = host.IndexOf(':', StringComparison.Ordinal);
        var name = colon < 0 ? host : host[..colon];
        return name is "localhost" or "localhost." || whitelist.Any(pattern => Wildmat(name, pattern));
    }

    /// <summary>tr_wildmat: <c>*</c> any run of characters, <c>?</c> any one, the rest itself, letter case kept.</summary>
    private static bool Wildmat(string name, string pattern) =>
        Regex.IsMatch(name, "^" + Regex.Escape(pattern).Replace("\\*", ".*", StringComparison.Ordinal).Replace("\\?", ".", StringComparison.Ordinal) + "$");

    private bool HasSession(HttpRequestMessage request) =>
        string.Equals(HeaderOf(request, SessionIdHeader), SessionId, StringComparison.Ordinal);

    /// <summary>The request's path and query as Transmission sees them: below the base path a proxy would strip.</summary>
    private string TransmissionUri(Uri uri)
    {
        var pathAndQuery = uri.PathAndQuery;
        return pathAndQuery.StartsWith(_base.AbsolutePath, StringComparison.Ordinal)
            ? "/" + pathAndQuery[_base.AbsolutePath.Length..]
            : pathAndQuery;
    }

    /// <summary>
    /// The 409 with the session id in its header and again on its page, as each version words it: 4.1 "session_id" and
    /// its RPC's semantic version in <see cref="RpcVersionHeader"/>, 4.0 "session-id" and the header exposed to scripts
    /// of other origins (<c>handshake-response.txt</c>, <c>4.0.6-handshake-response.txt</c>).
    /// </summary>
    private HttpResponseMessage Conflict()
    {
        var sessionId = SessionId;
        var spelling = Version.IsAtLeast41 ? "session_id" : "session-id";
        var conflict = Page(
            HttpStatusCode.Conflict,
            $"<p>Your request had an invalid {spelling} header.</p><p>To fix this, follow these steps:<ol><li> When reading a response, get its "
            + $"{SessionIdHeader} header and remember it<li> Add the updated header to your outgoing requests<li> When you get this 409 error message, "
            + "resend your request with the updated header</ol></p><p>This requirement has been added to help prevent "
            + "<a href=\"https://en.wikipedia.org/wiki/Cross-site_request_forgery\">CSRF</a> attacks.</p>"
            + $"<p><code>{SessionIdHeader}: {sessionId}</code></p>");
        conflict.Headers.TryAddWithoutValidation(SessionIdHeader, sessionId);
        if (!Version.IsAtLeast41)
            conflict.Headers.TryAddWithoutValidation("Access-Control-Expose-Headers", SessionIdHeader);
        else if (Version.RpcVersionSemver is { } semver)
            conflict.Headers.TryAddWithoutValidation(RpcVersionHeader, semver);
        return conflict;
    }

    /// <summary>The 421's explanation: its settings named in 4.1's snake case or 4.0's kebab case.</summary>
    private string HostNotAllowedText()
    {
        var setting = Version.IsAtLeast41 ? "rpc_host_whitelist" : "rpc-host-whitelist";
        var separator = Version.IsAtLeast41 ? "_" : "-";
        return "<p>Transmission received your request, but the hostname was unrecognized.</p><p>To fix this, choose one of the following options:"
            + "<ul><li>Enable password authentication, then any hostname is allowed.</li><li>Add the hostname you want to use to the whitelist in settings.</li></ul></p>"
            + $"<p>If you're editing settings.json, see the '{setting}' and '{setting}{separator}enabled' entries.</p>"
            + "<p>This requirement has been added to help prevent <a href=\"https://en.wikipedia.org/wiki/DNS_rebinding\">DNS Rebinding</a> attacks.</p>";
    }

    private HttpResponseMessage WebInterface()
    {
        var response = Stamp(new HttpResponseMessage(HttpStatusCode.OK), pastAddressCheck: true);
        response.Content = new StringContent(WebInterfacePage, Encoding.UTF8, "text/html");
        return response;
    }

    /// <summary>
    /// <c>send_simple_response</c>: <c>&lt;h1&gt;{code}: {reason}&lt;/h1&gt;</c> and <paramref name="text"/> after it, in
    /// libevent's own type, as every page Transmission sends is.
    /// </summary>
    private HttpResponseMessage Page(HttpStatusCode status, string? text = null, bool pastAddressCheck = true)
    {
        var reason = ReasonOf(status);
        var page = Stamp(new HttpResponseMessage(status) { ReasonPhrase = reason }, pastAddressCheck);
        page.Content = new ByteArrayContent(Encoding.Latin1.GetBytes($"<h1>{(int)status}: {reason}</h1>{text}"));
        page.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(PageMediaType);
        return page;
    }

    /// <summary>
    /// The headers every answer carries: <c>Server</c>; on 4.1 the clickjacking headers, from the first check on; on 4.0
    /// <c>Access-Control-Allow-Origin: *</c>, once the address check passed.
    /// </summary>
    private HttpResponseMessage Stamp(HttpResponseMessage response, bool pastAddressCheck)
    {
        response.Headers.TryAddWithoutValidation("Server", "Transmission");
        if (Version.IsAtLeast41)
        {
            response.Headers.TryAddWithoutValidation("X-Frame-Options", "SAMEORIGIN");
            response.Headers.TryAddWithoutValidation("Content-Security-Policy", "frame-ancestors 'self'");
        }
        else if (pastAddressCheck)
        {
            response.Headers.TryAddWithoutValidation("Access-Control-Allow-Origin", "*");
        }

        return response;
    }

    /// <summary>web-utils.cc <c>tr_webGetResponseStr</c>, for the statuses Transmission answers itself.</summary>
    private static string ReasonOf(HttpStatusCode status) => status switch
    {
        HttpStatusCode.OK => "OK",
        HttpStatusCode.MovedPermanently => "Moved Permanently",
        HttpStatusCode.Unauthorized => "Unauthorized",
        HttpStatusCode.Forbidden => "Forbidden",
        HttpStatusCode.NotFound => "Not Found",
        HttpStatusCode.MethodNotAllowed => "Method Not Allowed",
        HttpStatusCode.Conflict => "Conflict",
        HttpStatusCode.MisdirectedRequest => "Misdirected Request",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Transmission doesn't answer this status itself."),
    };

    /// <summary>The body as an RPC's envelope, when it is a JSON object.</summary>
    private static JsonObject? Envelope(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return null;
        try
        {
            return JsonNode.Parse(body) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static FakeTransmissionCall CallOf(HttpRequestMessage request, JsonObject envelope)
    {
        var arguments = envelope["arguments"] is JsonObject given ? (JsonObject)given.DeepClone() : new JsonObject();
        return new FakeTransmissionCall(
            TextOf(envelope, "method") ?? "",
            [.. arguments.Select(argument => argument.Key)],
            arguments,
            HeaderOf(request, SessionIdHeader),
            HeaderOf(request, "Authorization"),
            // Only an integer tag is echoed (4.0.6 tr_variantDictFindInt, 4.1.3 api-compat).
            envelope["tag"] is JsonValue tag && tag.TryGetValue<long>(out var value) ? value : null);
    }

    /// <summary>The first value of the request's header <paramref name="name"/>, as libevent's
    /// <c>evhttp_find_header</c> finds it.</summary>
    private static string? HeaderOf(HttpRequestMessage request, string name) =>
        request.Headers.NonValidated.TryGetValues(name, out var values) ? values.FirstOrDefault() : null;

    /// <summary>The text under <paramref name="name"/>, when it is a JSON string.</summary>
    private static string? TextOf(JsonObject node, string name) =>
        node[name] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    private static string FolderKey(string path)
    {
        var trimmed = path.TrimEnd('/');
        return trimmed.Length == 0 && path.Length > 0 ? "/" : trimmed;
    }

    private static bool IsRead(string method) => method is "session-get" or "free-space" or "torrent-get";

    private static string NewSessionId() => RandomNumberGenerator.GetString(SessionIdCharacters, SessionIdLength);

    private long Now() => _time.GetUtcNow().ToUnixTimeSeconds();
}
