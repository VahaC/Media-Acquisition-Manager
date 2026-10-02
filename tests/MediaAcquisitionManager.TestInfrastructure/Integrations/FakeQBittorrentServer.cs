using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using MediaAcquisitionManager.Core.Downloads;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.TestInfrastructure.Http;

namespace MediaAcquisitionManager.TestInfrastructure.Integrations;

/// <summary>
/// One qBittorrent behind a <see cref="RecordingHttpHandler"/> (MAM-11): it answers the sign-in and the
/// reads the adapter makes — <c>auth/login</c>, <c>app/version</c>, <c>torrents/categories</c> and
/// <c>app/defaultSavePath</c> — as qBittorrent 5.2.3 does in <c>VendorSamples/QBittorrent</c>, unless a test
/// changes them. MAM-13 adds the submission and the job lookup: <c>POST torrents/add</c> and
/// <c>GET torrents/info</c>, over the torrents the server has (<see cref="Torrents"/>).
/// </summary>
/// <remarks>
/// <para>
/// MAM-13 — <c>torrents/add</c> as qBittorrent's source answers it (VendorSamples README): it reads the
/// <c>multipart/form-data</c> the adapter sends and records each request's fields
/// (<see cref="AddRequests"/>); a torrent's hashes are worked out from the .torrent file or the magnet link
/// with the core's <see cref="TorrentInfoHashReader"/>. A torrent the server already has is refused —
/// <c>409 Conflict</c> from 5.2, <c>200 Fails.</c> before — and a .torrent that can't be read answers
/// <c>415</c>. An added one is answered with 5.2's JSON and its id, or <c>200 Ok.</c> before 5.2, as
/// <see cref="Version"/> says. A category the server doesn't have is created, with no folder of its own,
/// exactly as qBittorrent does it: silently. <c>torrents/info</c> lists the torrents, filtered by
/// <c>hashes</c> (TorrentIDs joined by <c>|</c>) when given; <see cref="AddedTorrentsUnlistedForLookups"/>
/// leaves a just-added torrent out of the first lookups, as qBittorrent's asynchronous add does.
/// </para>
/// <para>
/// Sessions as qBittorrent keeps them: a good sign-in answers 204 with a new <c>QBT_SID_8080</c> cookie, and
/// every other request needs a session it handed out and still knows — otherwise it answers what
/// qBittorrent answered a forgotten one (<c>expired-session-response.txt</c>, a recorded
/// <c>403 Forbidden</c>). <see cref="ExpireSessions"/> forgets them all, as an idle timeout or a restart
/// does. A user name or password other than <see cref="UserName"/> and <see cref="Password"/> answers 401,
/// as 5.2 does; a method qBittorrent doesn't take for a path answers 405, as 4.4.4 and later do.
/// </para>
/// <para>
/// One server per address: <see cref="Arm(RecordingHttpHandler, FakeQBittorrentServer[])"/> gives one
/// handler several, each answering the requests to its own <see cref="BaseUrl"/>. A request to none of them
/// answers 503, like an unarmed handler.
/// </para>
/// <para>
/// Thread safe: a destination list asks every client on the page at once, and two submissions of one
/// release may race (MAM-13: only one of them is added). Each answer is built fresh from the properties,
/// which a test sets before the calls; an <see cref="Intercept"/> it sets must be safe to call concurrently
/// itself.
/// </para>
/// </remarks>
public sealed class FakeQBittorrentServer
{
    /// <summary>Where a test's qBittorrent is unless it says otherwise; a fake name, never a real host.</summary>
    public const string DefaultBaseUrl = "http://qbittorrent.test:8080";

    /// <summary>The user name the server takes unless a test changes it.</summary>
    public const string DefaultUserName = "admin";

    /// <summary>The password the server takes unless a test changes it.</summary>
    public const string DefaultPassword = "correct-horse-battery";

    /// <summary>The session cookie's name: 5.2 appends the Web UI's port.</summary>
    public const string CookieName = "QBT_SID_8080";

    /// <summary>What <c>app/defaultSavePath</c> answers unless a test changes it: <c>save_path</c> of the
    /// sample's <c>preferences.json</c>.</summary>
    public const string SampleDefaultSavePath = "/downloads";

    /// <summary>The recorded answer to a session qBittorrent doesn't know.</summary>
    public const string ExpiredSessionResponseFile = "QBittorrent/expired-session-response.txt";

    // qBittorrent sends its text as UTF-8, not as \u escapes.
    private static readonly JsonSerializerOptions Json = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private static readonly Lazy<string> SampleVersion = new(() => Read("app-version.txt").Trim());
    private static readonly Lazy<string> SampleCategories = new(() => Read("categories.json"));

    private readonly Uri _base;
    private readonly ConcurrentDictionary<string, byte> _sessions = new(StringComparer.Ordinal);

    // JsonObject fills itself in lazily on first read, which is not safe to do from two threads at once.
    private readonly Lock _categoriesLock = new();

    // MAM-13 — the torrents, the lookups each new one stays unlisted for, and the add requests change
    // together: two submissions racing must never both add one torrent. Taken before _categoriesLock when
    // both are needed, never after it.
    private readonly Lock _torrentsLock = new();
    private readonly List<FakeQBittorrentTorrent> _torrents = [];
    private readonly Dictionary<string, int> _unlistedLookups = new(StringComparer.Ordinal);
    private readonly List<FakeQBittorrentAddRequest> _addRequests = [];

    /// <param name="baseUrl">The address the server answers at, as an integration stores it (no trailing
    /// slash); a path is the base path of a reverse proxy in front of it.</param>
    public FakeQBittorrentServer(string baseUrl = DefaultBaseUrl)
    {
        BaseUrl = baseUrl;
        _base = new Uri(baseUrl.TrimEnd('/') + "/");
    }

    /// <summary>The address this server answers at.</summary>
    public string BaseUrl { get; }

    /// <summary>The user name a sign-in must send.</summary>
    public string UserName { get; set; } = DefaultUserName;

    /// <summary>The password a sign-in must send; change it to refuse the one an integration stores.</summary>
    public string Password { get; set; } = DefaultPassword;

    /// <summary>What <c>app/version</c> answers: the sample's <c>v5.2.3</c>. Set an older one for the
    /// version gate.</summary>
    public string Version { get; set; } = SampleVersion.Value;

    /// <summary>What <c>torrents/categories</c> answers: the sample's four categories, keyed by name in
    /// qBittorrent's order. Remove one for a category the client no longer has, or add one with
    /// <see cref="AddCategory"/>.</summary>
    public JsonObject Categories { get; } = JsonNode.Parse(SampleCategories.Value)!.AsObject();

    /// <summary>What <c>app/defaultSavePath</c> answers, as plain text.</summary>
    public string DefaultSavePath { get; set; } = SampleDefaultSavePath;

    /// <summary>When set, every request fails as one to a port nothing listens on: the client throws
    /// before any answer.</summary>
    public bool Unreachable { get; set; }

    /// <summary>Answers a request before the server does — a failure, a slow answer — or returns
    /// <c>null</c> to let the server answer.</summary>
    public Func<HttpRequestMessage, HttpResponseMessage?>? Intercept { get; set; }

    /// <summary>
    /// MAM-13 — how many <c>torrents/info</c> answers leave out a torrent added through <c>torrents/add</c>,
    /// whatever they ask for: qBittorrent lists one only once libtorrent has taken it, a moment after the add
    /// was answered. 0, the default, lists it straight away.
    /// </summary>
    public int AddedTorrentsUnlistedForLookups { get; set; }

    /// <summary>MAM-13 — every torrent the server has, pre-seeded with <see cref="AddTorrent(FakeQBittorrentTorrent)"/>
    /// or added through <c>torrents/add</c>, in the order it got them; a snapshot.</summary>
    public IReadOnlyList<FakeQBittorrentTorrent> Torrents
    {
        get
        {
            lock (_torrentsLock)
            {
                return [.. _torrents];
            }
        }
    }

    /// <summary>MAM-13 — every <c>torrents/add</c> received with a session the server knows, oldest first,
    /// whatever it answered; a snapshot.</summary>
    public IReadOnlyList<FakeQBittorrentAddRequest> AddRequests
    {
        get
        {
            lock (_torrentsLock)
            {
                return [.. _addRequests];
            }
        }
    }

    /// <summary>Adds a category the way 5.2 lists one: its name, the save path as stored, and the
    /// defaults of everything else.</summary>
    public void AddCategory(string name, string savePath)
    {
        lock (_categoriesLock)
        {
            Categories[name] = Category(name, savePath);
        }
    }

    /// <summary>MAM-13 — gives the server <paramref name="torrent"/>, as one added in qBittorrent itself: listed
    /// straight away, and a submission of it is refused as a duplicate.</summary>
    public void AddTorrent(FakeQBittorrentTorrent torrent)
    {
        ArgumentNullException.ThrowIfNull(torrent);
        lock (_torrentsLock)
        {
            _torrents.Add(torrent);
        }
    }

    /// <summary>MAM-13 — gives the server a finished, seeding torrent named by <paramref name="hashes"/>
    /// (<see cref="FakeQBittorrentTorrent.Of"/>), as one added in qBittorrent itself, and returns it.</summary>
    public FakeQBittorrentTorrent AddTorrent(TorrentInfoHashes hashes, string name = "Added in qBittorrent", string category = "")
    {
        var torrent = FakeQBittorrentTorrent.Of(hashes, name, category);
        AddTorrent(torrent);
        return torrent;
    }

    /// <summary>Forgets every session handed out so far, as qBittorrent does when they expire or it
    /// restarts: the next request with one of them answers the recorded 403.</summary>
    public void ExpireSessions() => _sessions.Clear();

    /// <summary>Makes <paramref name="handler"/> answer as this server.</summary>
    public void Arm(RecordingHttpHandler handler) => Arm(handler, this);

    /// <summary>Makes <paramref name="handler"/> answer as <paramref name="servers"/>, each at its own
    /// address; a request to none of them answers 503.</summary>
    public static void Arm(RecordingHttpHandler handler, params FakeQBittorrentServer[] servers)
    {
        ArgumentNullException.ThrowIfNull(handler);
        handler.Handler = (request, body) =>
            Array.Find(servers, server => server.Serves(request.RequestUri!)) is { } server
                ? server.Answer(request, body)
                : new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
    }

    /// <summary>What this server answers <paramref name="request"/>, whose body was
    /// <paramref name="body"/>.</summary>
    public HttpResponseMessage Answer(HttpRequestMessage request, string? body)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (Unreachable)
            throw new HttpRequestException(HttpRequestError.ConnectionError, "No connection could be made because the target machine actively refused it.");
        if (Intercept?.Invoke(request) is { } intercepted)
            return intercepted;

        var path = request.RequestUri!.AbsolutePath[_base.AbsolutePath.Length..];
        if (path == "api/v2/auth/login")
            return request.Method == HttpMethod.Post ? SignIn(body) : new HttpResponseMessage(HttpStatusCode.MethodNotAllowed);
        // Only the sign-in is public; everything else needs a session (webapplication.cpp).
        if (!HasSession(request))
            return VendorResponses.Recorded(ExpiredSessionResponseFile);
        if (path == "api/v2/torrents/add")
            return request.Method == HttpMethod.Post ? AddTorrents(request, body) : new HttpResponseMessage(HttpStatusCode.MethodNotAllowed);
        if (request.Method != HttpMethod.Get)
            return new HttpResponseMessage(HttpStatusCode.MethodNotAllowed);

        return path switch
        {
            "api/v2/app/version" => Text(Version, "text/plain"),
            "api/v2/app/defaultSavePath" => Text(DefaultSavePath, "text/plain"),
            "api/v2/torrents/categories" => Text(CategoriesJson(), "application/json"),
            "api/v2/torrents/info" => TorrentsInfo(request.RequestUri!),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        };
    }

    private bool Serves(Uri uri) =>
        Uri.Compare(uri, _base, UriComponents.SchemeAndServer, UriFormat.SafeUnescaped, StringComparison.OrdinalIgnoreCase) == 0
        && uri.AbsolutePath.StartsWith(_base.AbsolutePath, StringComparison.Ordinal);

    // 5.2: 204 and a new session for the right credentials, 401 for anything else.
    private HttpResponseMessage SignIn(string? body)
    {
        var form = Form(body);
        if (!form.TryGetValue("username", out var userName) || !string.Equals(userName, UserName, StringComparison.Ordinal)
            || !form.TryGetValue("password", out var password) || !string.Equals(password, Password, StringComparison.Ordinal))
            return Text("Unauthorized", "text/plain", HttpStatusCode.Unauthorized);

        // 32 characters, like qBittorrent's own session ids.
        var session = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        _sessions[session] = 0;
        return VendorResponses.WithSetCookie(
            new HttpResponseMessage(HttpStatusCode.NoContent), $"{CookieName}={session}; HttpOnly; SameSite=Strict; path=/");
    }

    private bool HasSession(HttpRequestMessage request) =>
        request.Headers.NonValidated.TryGetValues("Cookie", out var values)
        && values
            .SelectMany(value => value.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            .Select(pair => pair.Split('=', 2))
            .Any(pair => pair.Length == 2
                && string.Equals(pair[0], CookieName, StringComparison.Ordinal)
                && _sessions.ContainsKey(pair[1]));

    private string CategoriesJson()
    {
        lock (_categoriesLock)
        {
            return Categories.ToJsonString(Json);
        }
    }

    /// <summary>
    /// MAM-13 — <c>torrents/add</c> as qBittorrent's <c>addAction</c> answers it: a .torrent that doesn't load
    /// fails the whole request with 415 before anything is added; each torrent it already has is skipped;
    /// then 5.2's JSON (409 when nothing was added), or <c>Ok.</c> / <c>Fails.</c> before 5.2.
    /// </summary>
    private HttpResponseMessage AddTorrents(HttpRequestMessage request, string? body)
    {
        var fields = FormFields(request, body);
        var files = fields.Where(field => field.FileName is not null).ToList();
        var urls = TextOf(fields, "urls");
        var category = TextOf(fields, "category");
        var autoTmm = TextOf(fields, "autoTMM");
        var hasFile = files.Count > 0;
        lock (_torrentsLock)
        {
            _addRequests.Add(new FakeQBittorrentAddRequest(
                [.. fields.Select(field => field.Name)],
                hasFile ? ReleasePayloadKind.TorrentFile : urls is not null ? ReleasePayloadKind.Magnet : null,
                category,
                autoTmm,
                urls,
                hasFile ? files[0].Bytes : null,
                hasFile ? files[0].FileName : null,
                hasFile ? files[0].ContentType : null));
        }

        var candidates = new List<(TorrentInfoHashes Hashes, string? Name, bool IsMagnet)>();
        foreach (var torrentFile in files)
        {
            var hashes = TorrentInfoHashReader.FromTorrentFile(torrentFile.Bytes);
            if (!hashes.IsKnown)
                return Text($"Error: '{torrentFile.FileName}' is not a valid torrent file.", "text/plain", HttpStatusCode.UnsupportedMediaType);
            candidates.Add((hashes, null, false));
        }

        // One link per line, as qBittorrent reads urls; one it can't read adds nothing.
        var links = (urls ?? "").Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        foreach (var link in links)
        {
            var hashes = TorrentInfoHashReader.FromMagnet(link);
            if (hashes.IsKnown)
                candidates.Add((hashes, MagnetName(link), true));
        }

        var added = new List<string>();
        lock (_torrentsLock)
        {
            foreach (var (hashes, name, isMagnet) in candidates)
            {
                // Session::addTorrent refuses a torrent it already has, by either hash.
                if (_torrents.Any(torrent => torrent.IsNamedBy(hashes)))
                    continue;

                var torrent = NewTorrent(hashes, name, isMagnet, category ?? "", autoTmm);
                _torrents.Add(torrent);
                if (AddedTorrentsUnlistedForLookups > 0)
                    _unlistedLookups[torrent.Hash] = AddedTorrentsUnlistedForLookups;
                added.Add(torrent.Hash);
            }
        }

        if (!AnswersAsOf52())
            return Text(added.Count > 0 ? "Ok." : "Fails.", "text/plain");
        if (added.Count == 0)
            return Text("Conflict", "text/plain", HttpStatusCode.Conflict);

        var answer = new JsonObject
        {
            ["success_count"] = added.Count,
            ["failure_count"] = files.Count + links.Length - added.Count,
            ["pending_count"] = 0,
            ["added_torrent_ids"] = new JsonArray([.. added.Select(id => (JsonNode)JsonValue.Create(id))]),
        };
        return Text(answer.ToJsonString(Json), "application/json");
    }

    /// <summary>
    /// A torrent just added: named by its magnet's <c>dn</c>, or by its id (the name a .torrent's info
    /// dictionary holds isn't read here), not finished, in its category's folder under automatic torrent
    /// management and in the default save path otherwise. A category the server doesn't have is created on
    /// the way, with no folder of its own, as qBittorrent does it.
    /// </summary>
    private FakeQBittorrentTorrent NewTorrent(TorrentInfoHashes hashes, string? name, bool isMagnet, string category, string? autoTmm)
    {
        var categoryFolder = category.Length > 0 ? EnsureCategory(category) : null;
        // Utils::String::parseBool: "true" in any letter case.
        var automatic = string.Equals(autoTmm, "true", StringComparison.OrdinalIgnoreCase);
        var savePath = automatic && categoryFolder is not null ? categoryFolder : DefaultSavePath;
        // A magnet's metadata is still being fetched; a .torrent starts downloading.
        return FakeQBittorrentTorrent.Of(
            hashes, name ?? FakeQBittorrentTorrent.IdOf(hashes), category, isMagnet ? "metaDL" : "downloading", 0, savePath, automatic);
    }

    /// <summary>The folder <paramref name="category"/> saves to, after creating it with no folder of its own
    /// when the server doesn't have it.</summary>
    private string EnsureCategory(string category)
    {
        lock (_categoriesLock)
        {
            if (!Categories.ContainsKey(category))
                Categories[category] = Category(category, "");

            var savePath = Categories[category]?["savePath"]?.GetValue<string>();
            return string.IsNullOrEmpty(savePath) ? $"{DefaultSavePath}/{category}" : savePath;
        }
    }

    /// <summary>
    /// MAM-13 — <c>torrents/info</c>: every torrent, or those whose id is one of <c>hashes</c> (TorrentIDs
    /// joined by <c>|</c>, any letter case — a 64-character v2 hash names none), leaving out the ones still
    /// unlisted after their add (<see cref="AddedTorrentsUnlistedForLookups"/>).
    /// </summary>
    private HttpResponseMessage TorrentsInfo(Uri uri)
    {
        var ids = QueryValue(uri, "hashes")?.Split('|', StringSplitOptions.RemoveEmptyEntries);
        var listed = new JsonArray();
        lock (_torrentsLock)
        {
            foreach (var torrent in _torrents)
            {
                if (_unlistedLookups.TryGetValue(torrent.Hash, out var unlisted) && unlisted > 0)
                {
                    _unlistedLookups[torrent.Hash] = unlisted - 1;
                    continue;
                }

                if (ids is null || ids.Contains(torrent.Hash, StringComparer.OrdinalIgnoreCase))
                    listed.Add(Describe(torrent));
            }
        }

        return Text(listed.ToJsonString(Json), "application/json");
    }

    /// <summary>A category as 5.2 lists it: its name, the save path as stored, and the defaults of everything
    /// else.</summary>
    private static JsonObject Category(string name, string savePath) => new()
    {
        ["download_path"] = null,
        ["inactive_seeding_time_limit"] = -2,
        ["name"] = name,
        ["ratio_limit"] = -2,
        ["savePath"] = savePath,
        ["seeding_time_limit"] = -2,
        ["share_limit_action"] = "Default",
    };

    /// <summary>A torrent as 5.2's <c>torrents/info</c> lists it, these properties only.</summary>
    private static JsonObject Describe(FakeQBittorrentTorrent torrent) => new()
    {
        ["auto_tmm"] = torrent.AutoTmm,
        ["category"] = torrent.Category,
        ["content_path"] = torrent.ContentPath,
        ["hash"] = torrent.Hash,
        ["infohash_v1"] = torrent.InfoHashV1,
        ["infohash_v2"] = torrent.InfoHashV2,
        ["name"] = torrent.Name,
        ["progress"] = torrent.Progress,
        ["save_path"] = torrent.SavePath,
        ["state"] = torrent.State,
    };

    /// <summary>Whether <see cref="Version"/> answers <c>torrents/add</c> the way 5.2 does.</summary>
    private bool AnswersAsOf52()
    {
        var parts = Version.TrimStart('v').Split('.');
        return parts.Length >= 2
            && int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var major)
            && int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var minor)
            && (major > 5 || (major == 5 && minor >= 2));
    }

    /// <summary>
    /// Every field of the form, in the order sent: the parts of the <c>multipart/form-data</c> the adapter
    /// sends, read back from the request itself, or — for anything else — the fields of a URL-encoded body.
    /// </summary>
    private static List<(string Name, string? FileName, string? ContentType, byte[] Bytes)> FormFields(HttpRequestMessage request, string? body)
    {
        if (request.Content is not MultipartFormDataContent multipart)
            return [.. Form(body).Select(field => (field.Key, (string?)null, (string?)null, Encoding.UTF8.GetBytes(field.Value)))];

        var fields = new List<(string Name, string? FileName, string? ContentType, byte[] Bytes)>();
        foreach (var part in multipart)
        {
            var disposition = part.Headers.ContentDisposition;
            using var stream = part.ReadAsStream();
            using var bytes = new MemoryStream();
            stream.CopyTo(bytes);
            fields.Add((
                Unquote(disposition?.Name) ?? "",
                Unquote(disposition?.FileName ?? disposition?.FileNameStar),
                part.Headers.ContentType?.MediaType,
                bytes.ToArray()));
        }

        return fields;
    }

    /// <summary>The last text field named <paramref name="name"/> as UTF-8, or <c>null</c> when none was
    /// sent: qBittorrent keeps the last of two with one name.</summary>
    private static string? TextOf(List<(string Name, string? FileName, string? ContentType, byte[] Bytes)> fields, string name)
    {
        string? value = null;
        foreach (var field in fields)
        {
            if (field.FileName is null && string.Equals(field.Name, name, StringComparison.Ordinal))
                value = Encoding.UTF8.GetString(field.Bytes);
        }

        return value;
    }

    private static string? Unquote(string? value) => value is null ? null : value.Trim('"');

    /// <summary>A magnet link's <c>dn</c>, the name it gives the torrent, or <c>null</c>.</summary>
    private static string? MagnetName(string magnet) => QueryValue(magnet, "dn");

    private static string? QueryValue(Uri uri, string name) => QueryValue(uri.Query, name);

    private static string? QueryValue(string text, string name)
    {
        var query = text.IndexOf('?', StringComparison.Ordinal);
        if (query < 0)
            return null;

        return text[(query + 1)..]
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('=', 2))
            .Where(pair => string.Equals(pair[0], name, StringComparison.Ordinal))
            .Select(pair => pair.Length > 1 ? Decode(pair[1]) : "")
            .FirstOrDefault();
    }

    // application/x-www-form-urlencoded, as the adapter sends the sign-in.
    private static Dictionary<string, string> Form(string? body) =>
        (body ?? "")
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('=', 2))
            .ToDictionary(
                pair => Decode(pair[0]),
                pair => pair.Length > 1 ? Decode(pair[1]) : "",
                StringComparer.Ordinal);

    private static string Decode(string value) => Uri.UnescapeDataString(value.Replace('+', ' '));

    private static HttpResponseMessage Text(string body, string mediaType, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, mediaType) };

    private static string Read(string fileName) =>
        File.ReadAllText(Path.Combine(RepositoryPaths.VendorSamples, "QBittorrent", fileName));
}
