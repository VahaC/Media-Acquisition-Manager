using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Xml;
using System.Xml.Linq;
using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Infrastructure.Integrations.Torznab;

/// <summary>
/// A Torznab feed behind the core's contracts (Adapter, ADR 0005): plain Torznab, whoever serves it —
/// Prowlarr, Jackett or an indexer of its own; nothing here knows Prowlarr or Toloka. MAM-7 builds the
/// registration and the connection test; MAM-10 adds the release search contract to this class.
/// </summary>
internal sealed class TorznabAdapter(IHttpClientFactory httpClients) : HttpIntegrationAdapter(httpClients, HttpClientName)
{
    /// <summary>The stored type id — part of the on-disk contract (ADR 0005).</summary>
    public const string TypeId = "torznab";

    /// <summary>The named client this adapter's requests go through, so a test can swap its handler.</summary>
    public const string HttpClientName = TypeId;

    /// <summary>The longest server title a sentence repeats.</summary>
    private const int ServerTitleMaximumLength = 100;

    /// <summary>The longest error description a sentence repeats: enough for any real one.</summary>
    private const int DescriptionMaximumLength = 200;

    /// <summary>
    /// Four million characters bound what the parser reads, in line with the client's 4 MiB buffer
    /// limit (Toloka's caps are about 25 KB).
    /// </summary>
    private const long MaximumCharactersInDocument = 4_000_000;

    private const string NotTorznabError =
        "The service at this address answered, but not with Torznab capabilities. Check that the address is the feed's Torznab URL.";

    private const string NotFoundError =
        "Nothing answers Torznab at this address (404 Not Found). Use the feed's Torznab address, which usually ends in /api.";

    // A bare HTTP 429 without Torznab's <error> body: a server or proxy in front of the feed that
    // limits requests. Prowlarr's own 429 carries an <error>, which DescribeError reads.
    private const string TooManyRequestsError =
        "The Torznab feed is refusing requests for now (429 Too Many Requests). Try again in a few minutes.";

    /// <summary>
    /// The movie-search parameters a sentence names, in the order it names them; a parameter not
    /// listed (<c>genre</c>, say) is left out.
    /// </summary>
    private static readonly (string Parameter, string Name)[] MovieSearchParameters =
    [
        ("q", "title"),
        ("imdbid", "IMDb id"),
        ("tmdbid", "TMDb id"),
        ("year", "year"),
    ];

    /// <summary>
    /// A hostile or wrong server can't make the parser expand entities or fetch files: a DTD is
    /// refused outright and nothing is ever resolved.
    /// </summary>
    private static readonly XmlReaderSettings SafeXml = new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
        MaxCharactersInDocument = MaximumCharactersInDocument,
    };

    /// <inheritdoc/>
    public override IntegrationAdapterDescriptor Descriptor { get; } = new(
        TypeId,
        "Torznab",
        IntegrationKind.ReleaseSearch,
        UserName: CredentialRequirement.NotUsed,
        Password: CredentialRequirement.NotUsed,
        // Required: every gateway the specification names (Prowlarr, Jackett) needs one (L.3).
        ApiKey: CredentialRequirement.Required,
        // An indexer's paths are never trusted (specification §4).
        ReportsFilePaths: false);

    /// <summary>
    /// The Torznab endpoint of a feed address: the address itself when its last path segment is
    /// <c>api</c>, in any letter case — Prowlarr's per-indexer <c>http://192.168.1.195:9696/1/api</c>
    /// (ADR 0004) — and otherwise the address with <c>/api</c> appended, as Jackett's copied
    /// <c>…/results/torznab/</c> needs. A fixed rule about the address, not a guess about the service
    /// (MAM-7, L.3).
    /// </summary>
    internal static string EndpointFor(string baseUrl)
    {
        var path = new Uri(baseUrl).AbsolutePath.TrimEnd('/');
        var lastSegment = path[(path.LastIndexOf('/') + 1)..];
        return string.Equals(lastSegment, "api", StringComparison.OrdinalIgnoreCase)
            ? baseUrl
            : $"{baseUrl.TrimEnd('/')}/api";
    }

    /// <summary><c>GET {endpoint}?t=caps&amp;apikey=…</c>: read-only, and a feed checks the key on it.</summary>
    protected override async Task<ConnectionTestResult> TestAsync(
        HttpClient client, IntegrationConnectionSettings settings, ICollection<string> sessionSecrets, CancellationToken cancellationToken)
    {
        // The key goes in the query because that is where Torznab reads it. .NET masks a logged
        // URI's query unless an operator switches that off, so the client has no logging handlers at
        // all and the URI is never logged (ADR 0004, ADR 0007).
        var query = settings.ApiKey is null ? "?t=caps" : $"?t=caps&apikey={Uri.EscapeDataString(settings.ApiKey)}";
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(EndpointFor(settings.BaseUrl) + query));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/xml"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/xml"));

        using var response = await client.SendAsync(request, cancellationToken);
        var document = await TryReadXmlAsync(response, cancellationToken);
        var secrets = CallSecrets(settings, sessionSecrets);

        // A feed reports its own failures as <error code=… description=…/>, with whatever HTTP status
        // it likes (200 included), so the body speaks first.
        if (document?.Root is { Name.LocalName: "error" } error)
            return DescribeError(error, response.StatusCode, secrets);

        return response.StatusCode switch
        {
            HttpStatusCode.OK => document?.Root is { Name.LocalName: "caps" } caps ? DescribeCapabilities(caps, secrets) : NotTorznab(),
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => ConnectionTestResult.Failed(
                IntegrationCallStatus.AuthenticationFailed, $"The Torznab feed refused the API key ({StatusText(response)})."),
            HttpStatusCode.NotFound => ConnectionTestResult.Failed(IntegrationCallStatus.UnexpectedResponse, NotFoundError),
            HttpStatusCode.TooManyRequests => ConnectionTestResult.Failed(IntegrationCallStatus.Refused, TooManyRequestsError),
            _ => DescribeUnexpectedStatus(response, settings, sessionSecrets),
        };
    }

    /// <summary>The body as XML, or <c>null</c> when it isn't well-formed XML this parser accepts.</summary>
    private static async Task<XDocument?> TryReadXmlAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        // The client has already buffered the body within its limit, so this reads memory.
        await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
        try
        {
            using var reader = XmlReader.Create(body, SafeXml);
            return XDocument.Load(reader);
        }
        catch (XmlException)
        {
            return null;
        }
    }

    private static ConnectionTestResult DescribeError(XElement error, HttpStatusCode status, string?[] secrets)
    {
        // Allow surrounding whitespace, nothing else: a code that isn't a plain number isn't Torznab.
        if (!int.TryParse(
                (string?)error.Attribute("code"),
                NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite,
                CultureInfo.InvariantCulture,
                out var code))
            return NotTorznab();

        var description = RemoteText.Clean((string?)error.Attribute("description"), DescriptionMaximumLength, secrets);
        // "(error 100: Incorrect user credentials)", or "(error 100)" when the feed gave no description.
        var detail = description.Length > 0
            ? string.Create(CultureInfo.InvariantCulture, $"error {code}: {description}")
            : string.Create(CultureInfo.InvariantCulture, $"error {code}");

        return code switch
        {
            // MAM-7, ADR 0004 — Prowlarr answers an indexer it paused after failures, or one at its
            // query limit, with HTTP 429 and <error code="429" description="Indexer is disabled till …
            // due to recent failures."/>. The HTTP 429 decides whatever code comes with it, so the
            // feed's own words say why and the status still says "not now".
            _ when code == 429 || status == HttpStatusCode.TooManyRequests => ConnectionTestResult.Failed(
                IntegrationCallStatus.Refused, $"The Torznab feed is refusing requests for now ({detail}). Try again in a few minutes."),
            100 or 101 or 102 => ConnectionTestResult.Failed(
                IntegrationCallStatus.AuthenticationFailed, $"The Torznab feed refused the API key ({detail})."),
            910 => ConnectionTestResult.Failed(
                IntegrationCallStatus.Refused, $"The Torznab feed has its API switched off ({detail})."),
            >= 200 and <= 299 => ConnectionTestResult.Failed(
                IntegrationCallStatus.UnexpectedResponse,
                $"The Torznab feed didn't understand the capabilities request ({detail}). Check that the address is a Torznab feed."),
            _ => ConnectionTestResult.Failed(IntegrationCallStatus.ServiceError, $"The Torznab feed failed ({detail})."),
        };
    }

    private static ConnectionTestResult DescribeCapabilities(XElement caps, string?[] secrets)
    {
        var title = RemoteText.Clean((string?)caps.Element("server")?.Attribute("title"), ServerTitleMaximumLength, secrets);
        var server = title.Length > 0 ? title : "the feed";

        var movieSearch = caps.Element("searching")?.Element("movie-search");
        var available = string.Equals(((string?)movieSearch?.Attribute("available"))?.Trim(), "yes", StringComparison.OrdinalIgnoreCase);
        if (!available)
            return ConnectionTestResult.Succeeded($"Connected to {server} over Torznab, but it offers no movie search.");

        return ConnectionTestResult.Succeeded($"Connected to {server} over Torznab; movie search by {MovieSearchParameterNames(movieSearch!)}.");
    }

    /// <summary>"title", "title and year", "title, IMDb id and year"; "title" when the feed names none
    /// this app knows — a movie search always takes a query.</summary>
    private static string MovieSearchParameterNames(XElement movieSearch)
    {
        var supported = ((string?)movieSearch.Attribute("supportedParams") ?? "")
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var names = MovieSearchParameters
            .Where(known => supported.Contains(known.Parameter, StringComparer.OrdinalIgnoreCase))
            .Select(known => known.Name)
            .ToArray();

        return names.Length switch
        {
            0 => "title",
            1 => names[0],
            _ => $"{string.Join(", ", names[..^1])} and {names[^1]}",
        };
    }

    private static ConnectionTestResult NotTorznab() =>
        ConnectionTestResult.Failed(IntegrationCallStatus.UnexpectedResponse, NotTorznabError);
}
