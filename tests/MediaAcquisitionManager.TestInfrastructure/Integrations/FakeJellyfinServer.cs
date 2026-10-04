using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using MediaAcquisitionManager.TestInfrastructure.Http;

namespace MediaAcquisitionManager.TestInfrastructure.Integrations;

/// <summary>
/// A Jellyfin server behind a <see cref="RecordingHttpHandler"/> (MAM-8): it answers the requests the
/// adapter makes — <c>/System/Info</c>, <c>/Library/VirtualFolders</c>, pages of <c>/Items</c> and
/// <c>/Videos/{id}/AdditionalParts</c> — from the real samples (<see cref="JellyfinSamples"/>) unless a
/// test changes them.
/// </summary>
/// <remarks>
/// <para>
/// Pages are cut from <see cref="Movies"/> in list order, honouring <c>startIndex</c> and <c>limit</c>
/// exactly as Jellyfin does, with <see cref="Movies"/>' count as the total. A library that changes
/// while it is read is a test changing <see cref="Movies"/> in <see cref="BeforeItemsPage"/>, or
/// <see cref="TotalRecordCountFor"/> reporting another total; any other failure is an answer of
/// <see cref="Intercept"/>'s.
/// </para>
/// <para>
/// MAM-32 — <c>includeItemTypes</c> picks the list: <see cref="Movies"/>, <see cref="Series"/> or
/// <see cref="Episodes"/>. The series and episodes are empty until a test fills them, so a test about
/// movies reads the library it always did; <see cref="BeforeTypedItemsPage"/> and
/// <see cref="TotalRecordCountOf"/> change any list while it is read.
/// </para>
/// <para>
/// One server per address: <see cref="Arm(RecordingHttpHandler, FakeJellyfinServer[])"/> gives one
/// handler several, each answering the requests to its own <see cref="BaseUrl"/>, so two integrations
/// synced side by side each read their own library. A request to no server's address answers 503, like
/// an unarmed handler. Not thread safe: a test reads one library at a time from each server.
/// </para>
/// </remarks>
public sealed class FakeJellyfinServer
{
    /// <summary>Where an integration's data factory points by default: port 9 on the loopback address,
    /// which refuses at once should anything reach the real network.</summary>
    public const string DefaultBaseUrl = "http://127.0.0.1:9";

    // Jellyfin sends its text as UTF-8, not as \u escapes.
    private static readonly JsonSerializerOptions Json = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private readonly Uri _base;

    /// <param name="baseUrl">The address the server answers at, as an integration stores it (no
    /// trailing slash); a path is Jellyfin's base path.</param>
    public FakeJellyfinServer(string baseUrl = DefaultBaseUrl)
    {
        BaseUrl = baseUrl;
        _base = new Uri(baseUrl.TrimEnd('/') + "/");
    }

    /// <summary>The address this server answers at.</summary>
    public string BaseUrl { get; }

    /// <summary>What <c>/System/Info</c> answers: the sample, Jellyfin 12.1.0. Set its
    /// <c>Version</c> for the version gate.</summary>
    public JsonObject SystemInfo { get; } = JellyfinSamples.SystemInfo();

    /// <summary>What <c>/Library/VirtualFolders</c> answers: the sample's five libraries.</summary>
    public JsonArray VirtualFolders { get; } = JellyfinSamples.VirtualFolders();

    /// <summary>The library's movies, paged in list order (the fake's stand-in for Jellyfin's sort
    /// order): the four samples until a test changes the list.</summary>
    public List<JsonObject> Movies { get; } = JellyfinSamples.Movies();

    /// <summary>The library's series (MAM-32): none until a test adds them.</summary>
    public List<JsonObject> Series { get; } = [];

    /// <summary>The library's episodes (MAM-32): none until a test adds them.</summary>
    public List<JsonObject> Episodes { get; } = [];

    /// <summary>The further parts of each stacked movie by its id, for
    /// <c>/Videos/{id}/AdditionalParts</c> (<see cref="JellyfinSamples.Part"/>); another id answers 404.</summary>
    public Dictionary<string, List<JsonObject>> AdditionalParts { get; } = new(StringComparer.Ordinal);

    /// <summary>When set, a request without <c>Authorization: MediaBrowser Token="{ApiKey}"</c> answers
    /// 401, as Jellyfin does for a key it doesn't know.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Answers a request before the server does — a failure, a redirect, a stalled body —
    /// or returns <c>null</c> to let the server answer.</summary>
    public Func<HttpRequestMessage, HttpResponseMessage?>? Intercept { get; set; }

    /// <summary>Runs before a page of <c>/Items</c> is cut, given its <c>startIndex</c>: where a test
    /// changes <see cref="Movies"/> between two pages, or removes the integration mid-sync.</summary>
    public Action<int>? BeforeItemsPage { get; set; }

    /// <summary>The <c>TotalRecordCount</c> a page reports, given its <c>startIndex</c>;
    /// <see cref="Movies"/>' count when unset.</summary>
    public Func<int, int>? TotalRecordCountFor { get; set; }

    /// <summary>MAM-32 — runs before a page of any list is cut, given the list's type
    /// (<c>Movie</c>, <c>Series</c>, <c>Episode</c>) and the page's <c>startIndex</c>.</summary>
    public Action<string, int>? BeforeTypedItemsPage { get; set; }

    /// <summary>MAM-32 — the <c>TotalRecordCount</c> a page of any list reports, given its type and
    /// <c>startIndex</c>; the list's count when unset or <c>null</c>.</summary>
    public Func<string, int, int?>? TotalRecordCountOf { get; set; }

    /// <summary>Makes <paramref name="handler"/> answer as this server.</summary>
    public void Arm(RecordingHttpHandler handler) => Arm(handler, this);

    /// <summary>Makes <paramref name="handler"/> answer as <paramref name="servers"/>, each at its own
    /// address; a request to none of them answers 503.</summary>
    public static void Arm(RecordingHttpHandler handler, params FakeJellyfinServer[] servers)
    {
        ArgumentNullException.ThrowIfNull(handler);
        handler.Handler = (request, _) =>
            Array.Find(servers, server => server.Serves(request.RequestUri!)) is { } server
                ? server.Answer(request)
                : new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
    }

    /// <summary>What this server answers <paramref name="request"/>.</summary>
    public HttpResponseMessage Answer(HttpRequestMessage request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (Intercept?.Invoke(request) is { } intercepted)
            return intercepted;
        if (ApiKey is not null && !HasKey(request, ApiKey))
            return new HttpResponseMessage(HttpStatusCode.Unauthorized);

        var uri = request.RequestUri!;
        var path = Uri.UnescapeDataString(uri.AbsolutePath[_base.AbsolutePath.Length..]);
        var segments = path.Split('/');
        return path switch
        {
            "System/Info" => JsonAnswer(SystemInfo),
            "Library/VirtualFolders" => JsonAnswer(VirtualFolders),
            "Items" => ItemsPage(uri),
            _ when segments is ["Videos", var itemId, "AdditionalParts"] => PartsOf(itemId),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        };
    }

    private bool Serves(Uri uri) =>
        Uri.Compare(uri, _base, UriComponents.SchemeAndServer, UriFormat.SafeUnescaped, StringComparison.OrdinalIgnoreCase) == 0
        && uri.AbsolutePath.StartsWith(_base.AbsolutePath, StringComparison.Ordinal);

    private HttpResponseMessage ItemsPage(Uri uri)
    {
        var query = Query(uri);
        var startIndex = query.TryGetValue("startIndex", out var start) ? int.Parse(start, CultureInfo.InvariantCulture) : 0;
        var limit = query.TryGetValue("limit", out var size) ? int.Parse(size, CultureInfo.InvariantCulture) : int.MaxValue;
        var type = query.TryGetValue("includeItemTypes", out var types) ? types : "Movie";

        BeforeTypedItemsPage?.Invoke(type, startIndex);
        List<JsonObject> list;
        int? total;
        switch (type)
        {
            case "Series":
                list = Series;
                total = TotalRecordCountOf?.Invoke(type, startIndex);
                break;
            case "Episode":
                list = Episodes;
                total = TotalRecordCountOf?.Invoke(type, startIndex);
                break;
            default:
                BeforeItemsPage?.Invoke(startIndex);
                list = Movies;
                total = TotalRecordCountOf?.Invoke(type, startIndex) ?? TotalRecordCountFor?.Invoke(startIndex);
                break;
        }

        var page = new JsonArray([.. list.Skip(startIndex).Take(limit).Select(item => item.DeepClone())]);
        return JsonAnswer(new JsonObject
        {
            ["Items"] = page,
            ["TotalRecordCount"] = total ?? list.Count,
            ["StartIndex"] = startIndex,
        });
    }

    private HttpResponseMessage PartsOf(string itemId)
    {
        if (!AdditionalParts.TryGetValue(itemId, out var parts))
            return new HttpResponseMessage(HttpStatusCode.NotFound);

        return JsonAnswer(new JsonObject
        {
            ["Items"] = new JsonArray([.. parts.Select(part => part.DeepClone())]),
            ["TotalRecordCount"] = parts.Count,
            ["StartIndex"] = 0,
        });
    }

    private static bool HasKey(HttpRequestMessage request, string apiKey) =>
        request.Headers.NonValidated.TryGetValues("Authorization", out var values)
        && values.Any(value => string.Equals(value, $"MediaBrowser Token=\"{apiKey}\"", StringComparison.Ordinal));

    private static Dictionary<string, string> Query(Uri uri) =>
        uri.Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('=', 2))
            .ToDictionary(
                pair => Uri.UnescapeDataString(pair[0]),
                pair => pair.Length > 1 ? Uri.UnescapeDataString(pair[1]) : "",
                StringComparer.OrdinalIgnoreCase);

    private static HttpResponseMessage JsonAnswer(JsonNode body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body.ToJsonString(Json), Encoding.UTF8, "application/json") };
}
