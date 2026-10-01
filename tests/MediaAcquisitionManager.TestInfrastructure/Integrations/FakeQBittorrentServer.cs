using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using MediaAcquisitionManager.TestInfrastructure.Http;

namespace MediaAcquisitionManager.TestInfrastructure.Integrations;

/// <summary>
/// One qBittorrent behind a <see cref="RecordingHttpHandler"/> (MAM-11): it answers the sign-in and the
/// reads the adapter makes — <c>auth/login</c>, <c>app/version</c>, <c>torrents/categories</c> and
/// <c>app/defaultSavePath</c> — as qBittorrent 5.2.3 does in <c>VendorSamples/QBittorrent</c>, unless a test
/// changes them.
/// </summary>
/// <remarks>
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
/// Thread safe: a destination list asks every client on the page at once. Each answer is built fresh from
/// the properties, which a test sets before the calls; an <see cref="Intercept"/> it sets must be safe to
/// call concurrently itself.
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

    /// <summary>Adds a category the way 5.2 lists one: its name, the save path as stored, and the
    /// defaults of everything else.</summary>
    public void AddCategory(string name, string savePath)
    {
        lock (_categoriesLock)
        {
            Categories[name] = new JsonObject
            {
                ["download_path"] = null,
                ["inactive_seeding_time_limit"] = -2,
                ["name"] = name,
                ["ratio_limit"] = -2,
                ["savePath"] = savePath,
                ["seeding_time_limit"] = -2,
                ["share_limit_action"] = "Default",
            };
        }
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
        if (request.Method != HttpMethod.Get)
            return new HttpResponseMessage(HttpStatusCode.MethodNotAllowed);

        return path switch
        {
            "api/v2/app/version" => Text(Version, "text/plain"),
            "api/v2/app/defaultSavePath" => Text(DefaultSavePath, "text/plain"),
            "api/v2/torrents/categories" => Text(CategoriesJson(), "application/json"),
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
