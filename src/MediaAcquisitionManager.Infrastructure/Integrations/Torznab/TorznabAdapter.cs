using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Abstractions.Search;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Texts;

namespace MediaAcquisitionManager.Infrastructure.Integrations.Torznab;

/// <summary>
/// A Torznab feed behind the core's contracts (Adapter, ADR 0005): plain Torznab, whoever serves it —
/// Prowlarr, Jackett or an indexer of its own; nothing here knows Prowlarr or Toloka. MAM-7 builds the
/// registration and the connection test; MAM-10 implements the release search contract
/// (<see cref="IReleaseSearchProvider"/>): the capabilities and the search in
/// <c>TorznabAdapter.Search.cs</c>, the listed releases in <c>TorznabAdapter.Items.cs</c>, the payload
/// fetch in <c>TorznabAdapter.Payload.cs</c>, and the rules every link a feed lists is held to in
/// <see cref="TorznabLinks"/>.
/// </summary>
/// <remarks>
/// <para>
/// MAM-30 (D16) — a sentence that says how to fix a refused key or a wrong address names Jackett and
/// Prowlarr as examples ("such as …"), since the owner fixes it in one of them; what the adapter does stays
/// plain Torznab, the same for either.
/// </para>
/// <para>
/// MAM-47 (ADR 0042) — every sentence is a key of <c>torznab.json</c> and its values, never English: the feed's own
/// words (its server title, an error's description, an address or a scheme it redirects to) are vendor values, cleaned
/// and masked where they are read (<see cref="VendorText.Clean"/>).
/// </para>
/// </remarks>
internal sealed partial class TorznabAdapter(IHttpClientFactory httpClients)
    : HttpIntegrationAdapter(httpClients, HttpClientName), IReleaseSearchProvider
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

    /// <summary>
    /// How deep a feed's XML may nest (MAM-10): a Torznab answer is four levels deep
    /// (<c>rss/channel/item/torznab:attr</c>, <c>caps/categories/category/subcat</c>), so 32 is room to
    /// spare for any real one and nowhere near what overflows a stack (<see cref="TryReadXml"/>).
    /// </summary>
    private const int MaximumElementDepth = 32;

    private static readonly Message NotTorznabError = Message.Of("torznab.not-torznab");

    // MAM-30 (D16) — Jackett's copied address ends in /results/torznab/, not /api, so the hint names both
    // shapes the owner copies.
    private static readonly Message NotFoundError = Message.Of("torznab.not-found");

    // A bare HTTP 429 without Torznab's <error> body: a server or proxy in front of the feed that
    // limits requests. Prowlarr's own 429 carries an <error>, which FeedError reads.
    private static readonly Message TooManyRequestsError = Message.Of("torznab.too-many-requests");

    /// <summary>"title": the parameter every search takes, and the one a sentence names when the feed names none this
    /// app knows.</summary>
    private static readonly Message TitleParameter = Message.Of("torznab.parameter.title-clause");

    private static readonly Message ImdbIdParameter = Message.Of("torznab.parameter.imdb-id-clause");

    private static readonly Message TmdbIdParameter = Message.Of("torznab.parameter.tmdb-id-clause");

    /// <summary>
    /// The movie-search parameters a sentence names, in the order it names them; a parameter not
    /// listed (<c>genre</c>, say) is left out. MAM-47 — each name is a value-less key of its own, which the template
    /// joins (<c>list:and:message</c>), so a translation names and joins them in its own words.
    /// </summary>
    private static readonly (string Parameter, Message Name)[] MovieSearchParameters =
    [
        ("q", TitleParameter),
        ("imdbid", ImdbIdParameter),
        ("tmdbid", TmdbIdParameter),
        ("year", Message.Of("torznab.parameter.year-clause")),
    ];

    /// <summary>MAM-32 — the TV-search parameters a sentence names, in the order it names them.</summary>
    private static readonly (string Parameter, Message Name)[] TvSearchParameters =
    [
        ("q", TitleParameter),
        ("tvdbid", Message.Of("torznab.parameter.tvdb-id-clause")),
        ("imdbid", ImdbIdParameter),
        ("tmdbid", TmdbIdParameter),
        ("season", Message.Of("torznab.parameter.season-clause")),
        ("ep", Message.Of("torznab.parameter.episode-clause")),
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

    /// <summary>Which of the feed's requests an <c>&lt;error&gt;</c> answered: each words the same
    /// codes its own way (MAM-10).</summary>
    private enum FeedRequest
    {
        /// <summary><c>t=caps</c>: the connection test and the capabilities call.</summary>
        Capabilities = 1,

        /// <summary><c>t=search</c>, <c>t=movie</c> or <c>t=tvsearch</c>.</summary>
        Search = 2,

        /// <summary>The fetch of a release's download link.</summary>
        Download = 3,
    }

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
        using var request = CapabilitiesRequest(settings);
        using var response = await client.SendAsync(request, cancellationToken);
        var document = await TryReadXmlAsync(response, cancellationToken);
        var secrets = CallSecrets(settings, sessionSecrets);

        // A feed reports its own failures as <error code=… description=…/>, with whatever HTTP status
        // it likes (200 included), so the body speaks first.
        if (document?.Root is { Name.LocalName: "error" } error)
            return DescribeError(error, response.StatusCode, secrets);

        if (response.StatusCode == HttpStatusCode.OK)
            return document?.Root is { Name.LocalName: "caps" } caps ? DescribeCapabilities(caps, secrets) : NotTorznab();

        return KnownFailure(response) is { } failure
            ? ConnectionTestResult.Failed(failure.Status, failure.Message)
            : DescribeUnexpectedStatus(response, settings, sessionSecrets);
    }

    /// <summary>
    /// <c>GET {endpoint}?t=caps&amp;apikey=…</c>, for the connection test and the capabilities call
    /// alike (MAM-10): one request, so the test proves exactly what a search relies on.
    /// </summary>
    private static HttpRequestMessage CapabilitiesRequest(IntegrationConnectionSettings settings)
    {
        // The key goes in the query because that is where Torznab reads it. .NET masks a logged
        // URI's query unless an operator switches that off, so the client has no logging handlers at
        // all and the URI is never logged (ADR 0004, ADR 0007).
        var query = settings.ApiKey is null ? "?t=caps" : $"?t=caps&apikey={Uri.EscapeDataString(settings.ApiKey)}";
        var request = new HttpRequestMessage(HttpMethod.Get, new Uri(EndpointFor(settings.BaseUrl) + query));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/xml"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/xml"));
        return request;
    }

    /// <summary>The body as XML, or <c>null</c> when it isn't well-formed XML this parser accepts.</summary>
    private static async Task<XDocument?> TryReadXmlAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        // The client has already buffered the body within its limit, so this reads memory.
        await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
        return TryReadXml(body);
    }

    /// <summary>
    /// <paramref name="body"/> as XML, or <c>null</c> when it isn't well-formed XML this parser accepts or
    /// nests deeper than <see cref="MaximumElementDepth"/>.
    /// </summary>
    /// <remarks>
    /// MAM-10 — the depth is checked in a first pass, before anything is built: loading deeply nested
    /// XML takes time that grows faster than its size (a 560 KB body took 12 s), and reading an element's
    /// text recurses once per level, so a few hundred kilobytes of nesting inside one item overflow the
    /// stack and end the process — a StackOverflowException can't be caught. Both happen after the body is
    /// buffered, where the client's timeout no longer applies.
    /// </remarks>
    private static XDocument? TryReadXml(Stream body)
    {
        try
        {
            var seekable = body.CanSeek ? body : CopyToMemory(body);
            using (var scan = XmlReader.Create(seekable, SafeXml))
            {
                while (scan.Read())
                {
                    if (scan.Depth > MaximumElementDepth)
                        return null;
                }
            }

            seekable.Position = 0;
            using var reader = XmlReader.Create(seekable, SafeXml);
            return XDocument.Load(reader);
        }
        catch (XmlException)
        {
            return null;
        }
    }

    private static MemoryStream CopyToMemory(Stream body)
    {
        var copy = new MemoryStream();
        body.CopyTo(copy);
        copy.Position = 0;
        return copy;
    }

    /// <summary>
    /// The HTTP failures every request to the endpoint answers alike: a refused key, nothing at the
    /// address, a bare 429. <c>null</c> for any other status, which the base class words.
    /// </summary>
    private static (IntegrationCallStatus Status, Message Message)? KnownFailure(HttpResponseMessage response) =>
        response.StatusCode switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => (
                IntegrationCallStatus.AuthenticationFailed, RefusedKeyByStatus(response)),
            HttpStatusCode.NotFound => (IntegrationCallStatus.UnexpectedResponse, NotFoundError),
            HttpStatusCode.TooManyRequests => (IntegrationCallStatus.Refused, TooManyRequestsError),
            _ => null,
        };

    /// <summary><see cref="KnownFailure"/>, or the base class's sentence, as a kind-contract call's failure.</summary>
    private IntegrationCallResult<T> DescribeFailedCall<T>(
        HttpResponseMessage response, IntegrationConnectionSettings settings, ICollection<string> sessionSecrets) where T : class =>
        KnownFailure(response) is { } failure
            ? IntegrationCallResult<T>.Failed(failure.Status, failure.Message)
            : DescribeUnexpectedStatus<T>(response, settings, sessionSecrets);

    private static ConnectionTestResult DescribeError(XElement error, HttpStatusCode status, string?[] secrets)
    {
        var (callStatus, message) = FeedError(error, status, secrets, FeedRequest.Capabilities);
        return ConnectionTestResult.Failed(callStatus, message);
    }

    /// <summary><see cref="FeedError"/> as a kind-contract call's failure, so the test and a call answer
    /// one error alike.</summary>
    private static IntegrationCallResult<T> DescribeError<T>(XElement error, HttpStatusCode status, string?[] secrets, FeedRequest request)
        where T : class
    {
        var (callStatus, message) = FeedError(error, status, secrets, request);
        return IntegrationCallResult<T>.Failed(callStatus, message);
    }

    /// <summary>
    /// The sentence for a feed's own <c>&lt;error code=… description=…/&gt;</c>, worded for the request
    /// it answered — one mapping for the connection test and every call (MAM-10).
    /// </summary>
    /// <remarks>
    /// MAM-47 (ADR 0042) — the feed's error, "error 100: Incorrect user credentials" or "error 100" when it gave no
    /// description, is a message of its own nested in each sentence's parentheses (<c>torznab.error.*</c>): an aside
    /// that agrees with nothing around it in any language, so it is translated once instead of once per sentence. The
    /// description stays the feed's words (a vendor value) and the code a number.
    /// </remarks>
    private static (IntegrationCallStatus Status, Message Message) FeedError(
        XElement error, HttpStatusCode status, string?[] secrets, FeedRequest request)
    {
        // Allow surrounding whitespace, nothing else: a code that isn't a plain number isn't Torznab.
        if (!int.TryParse(
                (string?)error.Attribute("code"),
                NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite,
                CultureInfo.InvariantCulture,
                out var code))
            return (IntegrationCallStatus.UnexpectedResponse, NotTorznabSentence(request));

        // MAM-10 — every address cut to its scheme and host before the text is cleaned and cut: Jackett
        // puts a whole exception's text into error 900 and Prowlarr its exception's message into a
        // download's error 500, often naming the tracker URL with a passkey in its path or query —
        // a credential this app doesn't hold and so can't mask (VendorSamples README, "Cut to the host").
        var description = VendorText.Clean(TorznabLinks.WithoutAddresses((string?)error.Attribute("description")), DescriptionMaximumLength, secrets);
        // "(error 100: Incorrect user credentials)", or "(error 100)" when the feed gave no description.
        // MAM-47 — the code goes in as a number ({{code, number}}), not as text: a stored "error 900: Fetching … failed: …"
        // then reads back one way only, since the code ends at its digits; as text it could also take the description's
        // words up to its own ": " (Jackett's exceptions, Prowlarr's "Fetching … failed: …") and the row stayed English.
        // English prints the same digits; the spec's codes (100 to 910) stay below the 1,000 Ukrainian would group.
        var detail = description.Text.Length > 0
            ? Message.Of("torznab.error.described-clause", ("code", code), ("description", description))
            : Message.Of("torznab.error.code-clause", ("code", code));

        return code switch
        {
            // MAM-7, ADR 0004 — Prowlarr answers an indexer it paused after failures, or one at its
            // query limit, with HTTP 429 and <error code="429" description="Indexer is disabled till …
            // due to recent failures."/>. The HTTP 429 decides whatever code comes with it, so the
            // feed's own words say why and the status still says "not now".
            _ when code == 429 || status == HttpStatusCode.TooManyRequests => (
                IntegrationCallStatus.Refused, Message.Of("torznab.feed.refusing", ("error", detail))),
            // MAM-10 — Prowlarr answers 410 Gone with <error code="410"> for a release its indexer no
            // longer has, and for an indexer switched off in Prowlarr: either way, not this release.
            _ when request == FeedRequest.Download && (code == 410 || status == HttpStatusCode.Gone) => (
                IntegrationCallStatus.NotFound, Message.Of("torznab.download.gone", ("error", detail))),
            // MAM-30 (D16) — a refused key, whichever way the feed says it (errors 100 to 102 here, a bare 401 or 403
            // in RefusedKeyByStatus), with where the owner finds the right one: the gateway's own dashboard, which this
            // app can't name.
            100 or 101 or 102 => (IntegrationCallStatus.AuthenticationFailed, Message.Of("torznab.feed.key-refused", ("error", detail))),
            910 => (IntegrationCallStatus.Refused, Message.Of("torznab.feed.api-off", ("error", detail))),
            >= 200 and <= 299 => (IntegrationCallStatus.UnexpectedResponse, NotUnderstoodSentence(request, detail)),
            _ when request == FeedRequest.Download => (
                IntegrationCallStatus.ServiceError, Message.Of("torznab.download.failed", ("error", detail))),
            _ => (IntegrationCallStatus.ServiceError, Message.Of("torznab.feed.failed", ("error", detail))),
        };
    }

    /// <summary>
    /// MAM-30 (D16) — the key refused by a bare 401 or 403, named by its status ("401 Unauthorized"), with where the
    /// owner finds the right one — as <see cref="FeedError"/> words the feed's own errors 100 to 102.
    /// </summary>
    private static Message RefusedKeyByStatus(HttpResponseMessage response) =>
        Message.Of("torznab.feed.key-refused-status", ("status", StatusText(response)));

    /// <summary>Errors 200–299 say the request was malformed: from this app, the feed is not what it seems.</summary>
    /// <remarks>
    /// MAM-30 (D16) — the capabilities request sends nothing but <c>t=caps</c> and the key, so such an error
    /// there can only be about the address: Jackett answers an indexer it doesn't have or hasn't configured
    /// with HTTP 200 and <c>&lt;error code="201" description="Indexer is not supported" /&gt;</c> (its
    /// <c>ResultsController</c>; not recorded, MAM-31 confirms it live).
    /// </remarks>
    private static Message NotUnderstoodSentence(FeedRequest request, Message detail) => request switch
    {
        FeedRequest.Search => Message.Of("torznab.search.not-understood", ("error", detail)),
        FeedRequest.Download => Message.Of("torznab.download.not-understood", ("error", detail)),
        _ => Message.Of("torznab.feed.wrong-indexer", ("error", detail)),
    };

    private static Message NotTorznabSentence(FeedRequest request) => request switch
    {
        FeedRequest.Search => NotSearchResultsError,
        FeedRequest.Download => NotATorrentError,
        _ => NotTorznabError,
    };

    /// <remarks>
    /// MAM-47 (ADR 0042) — a whole sentence per shape: the server the feed names, or "the feed" when it names none
    /// (<c>torznab.connected-to-feed.*</c>), each with no search, the TV search alone, the movie search, or both.
    /// </remarks>
    private static ConnectionTestResult DescribeCapabilities(XElement caps, string?[] secrets)
    {
        var title = VendorText.Clean((string?)caps.Element("server")?.Attribute("title"), ServerTitleMaximumLength, secrets);
        (string, MessageValue)? server = title.Text.Length > 0 ? ("server", title) : null;

        var movieSearch = caps.Element("searching")?.Element("movie-search");
        var tvSearch = caps.Element("searching")?.Element("tv-search");
        // MAM-32 — the TV search is the owner's second question once series are searched for too.
        (string, MessageValue)? tv = IsAvailable(tvSearch) ? ("tv", ParameterNames(tvSearch!, TvSearchParameters)) : null;
        (string, MessageValue)? movie = IsAvailable(movieSearch) ? ("movie", ParameterNames(movieSearch!, MovieSearchParameters)) : null;

        return ConnectionTestResult.Succeeded((server, movie, tv) switch
        {
            ({ } named, null, null) => Message.Of("torznab.connected.no-search", named),
            ({ } named, null, { } tvOnly) => Message.Of("torznab.connected.tv-only", named, tvOnly),
            ({ } named, { } movieOnly, null) => Message.Of("torznab.connected.movie", named, movieOnly),
            ({ } named, { } both, { } andTv) => Message.Of("torznab.connected.movie-and-tv", named, both, andTv),
            (null, null, null) => Message.Of("torznab.connected-to-feed.no-search"),
            (null, null, { } tvOnly) => Message.Of("torznab.connected-to-feed.tv-only", tvOnly),
            (null, { } movieOnly, null) => Message.Of("torznab.connected-to-feed.movie", movieOnly),
            (null, { } both, { } andTv) => Message.Of("torznab.connected-to-feed.movie-and-tv", both, andTv),
        });
    }

    /// <summary>Whether a <c>searching</c> mode says <c>available="yes"</c>, in any letter case.</summary>
    private static bool IsAvailable(XElement? mode) =>
        string.Equals(((string?)mode?.Attribute("available"))?.Trim(), "yes", StringComparison.OrdinalIgnoreCase);

    /// <summary>The parameters a <c>searching</c> mode names in <c>supportedParams</c>.</summary>
    private static string[] SupportedParameters(XElement mode) =>
        ((string?)mode.Attribute("supportedParams") ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    /// <summary>"title", "title and year", "title, IMDb id and year"; "title" when the feed names none
    /// this app knows — a movie or TV search always takes a query. MAM-47 — as a list the template joins (its
    /// <c>list:and:message</c> joins English as this did: "a, b and c").</summary>
    private static ListValue ParameterNames(XElement mode, (string Parameter, Message Name)[] known)
    {
        var supported = SupportedParameters(mode);
        MessageValue[] names =
        [
            .. known
                .Where(parameter => supported.Contains(parameter.Parameter, StringComparer.OrdinalIgnoreCase))
                .Select(parameter => parameter.Name),
        ];

        return new ListValue(names.Length > 0 ? names : [TitleParameter]);
    }

    private static ConnectionTestResult NotTorznab() =>
        ConnectionTestResult.Failed(IntegrationCallStatus.UnexpectedResponse, NotTorznabError);

    /// <summary>A Torznab category id: digits only, as every gateway numbers them, short enough for an
    /// <see cref="int"/>. <c>\z</c>, not <c>$</c>, which would also accept a trailing line break.</summary>
    [GeneratedRegex(@"^[0-9]{1,9}\z")]
    private static partial Regex CategoryIdPattern();
}
