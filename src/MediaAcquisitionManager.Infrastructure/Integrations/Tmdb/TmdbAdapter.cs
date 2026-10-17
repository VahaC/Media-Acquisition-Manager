using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Abstractions.Metadata;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Media;

namespace MediaAcquisitionManager.Infrastructure.Integrations.Tmdb;

/// <summary>
/// TMDb behind the core's contracts (Adapter, ADR 0005), MAM-33: the registration and the connection
/// test here, with what every call shares — the request, the pacing, the answer to a failure and the
/// reading of a listed work; the catalog's works, collections and people — MAM-34, and a show's episode — in
/// <c>TmdbAdapter.Works.cs</c>,
/// its searches, discovery and genres in <c>TmdbAdapter.Lists.cs</c>, the lookup a match runs
/// (<see cref="IMetadataResolver"/>) in <c>TmdbAdapter.Lookup.cs</c>, and the posters in
/// <c>TmdbAdapter.Posters.cs</c>. Read-only: every call is a GET.
/// </summary>
/// <remarks>
/// <para>
/// Every answer is read as an allowlist, property by property by name (house rule 8): a film's details
/// also carry its cast, budget and companies, none of which goes further than this class. MAM-34 — a work's
/// overview, runtime and alternative titles, and a show's IMDb id from its external ids, are read too, for the
/// owner linking a library work to it; the other registries' ids, the titles' countries and every other field
/// still stay here. A list entry that can't be read — no id, no title — is left out, never the whole answer:
/// one odd recommendation must not cost a work its details.
/// </para>
/// <para>
/// The key is TMDb's v4 "API Read Access Token" and travels only as <c>Authorization: Bearer</c>, never
/// as v3's <c>api_key=</c>: a URI is what a proxy logs, and the integration clients log none themselves
/// (<see cref="IntegrationsInfrastructureExtensions"/>). The posters come from TMDb's image host, which
/// takes no key, and get none (ADR 0007).
/// </para>
/// </remarks>
internal sealed partial class TmdbAdapter(IHttpClientFactory httpClients, TimeProvider time)
    : HttpIntegrationAdapter(httpClients, HttpClientName), IMetadataResolver, IMetadataCatalog
{
    /// <summary>The stored type id — part of the on-disk contract (ADR 0005).</summary>
    public const string TypeId = "tmdb";

    /// <summary>The named client this adapter's requests go through — the posters' included — so a test
    /// can swap its handler.</summary>
    public const string HttpClientName = TypeId;

    /// <summary>
    /// MAM-46 — the language a catalog call asks in when the settings name none (a draft, or a row stored without one):
    /// TMDb's own default, which it answers in when no <c>language</c> is sent. Every call of a saved integration asks in
    /// its <see cref="IntegrationConnectionSettings.CatalogLanguage"/> instead — <c>uk-UA</c> on an installation from
    /// before MAM-46, whose every call asked in Ukrainian —, so titles, genre names and collection names come in that
    /// language where TMDb has them, and in the original otherwise (TMDb's own fallback). The original title always comes
    /// beside it.
    /// </summary>
    public const string DefaultLanguage = "en-US";

    /// <summary>
    /// MAM-46 — every language TMDb keeps translations in: <c>GET /3/configuration/primary_translations</c>, recorded on
    /// 2026-10-17 (VendorSamples/Tmdb/configuration-primary-translations.json), 144 tags in TMDb's order. Vendor data
    /// kept as a constant, like the image host: the list is offered when the owner adds TMDb, before any key could
    /// read it, and it changes as rarely as TMDb's languages do.
    /// </summary>
    internal static readonly IReadOnlyList<string> PrimaryTranslations =
    [
        "af-ZA", "ar-AE", "ar-BH", "ar-EG", "ar-IQ", "ar-JO", "ar-LY", "ar-MA", "ar-QA", "ar-SA", "ar-TD", "ar-YE",
        "be-BY", "bg-BG", "bn-BD", "bn-IN", "br-FR", "ca-AD", "ca-ES", "ch-GU", "cs-CZ", "cy-GB", "da-DK", "de-AT",
        "de-CH", "de-DE", "el-CY", "el-GR", "en-AG", "en-AU", "en-BB", "en-BZ", "en-CA", "en-CM", "en-GB", "en-GG",
        "en-GH", "en-GI", "en-GY", "en-IE", "en-JM", "en-KE", "en-LC", "en-MW", "en-NZ", "en-PG", "en-TC", "en-US",
        "en-ZM", "en-ZW", "eo-EO", "es-AR", "es-CL", "es-DO", "es-EC", "es-ES", "es-GQ", "es-GT", "es-HN", "es-MX",
        "es-NI", "es-PA", "es-PE", "es-PY", "es-SV", "es-UY", "et-EE", "eu-ES", "fa-IR", "fi-FI", "fr-BF", "fr-CA",
        "fr-CD", "fr-CI", "fr-FR", "fr-GF", "fr-GP", "fr-MC", "fr-ML", "fr-MU", "fr-PF", "ga-IE", "gd-GB", "gl-ES",
        "he-IL", "hi-IN", "hr-HR", "hu-HU", "hy-AM", "id-ID", "it-IT", "it-VA", "ja-JP", "ka-GE", "kk-KZ", "kn-IN",
        "ko-KR", "ku-TR", "ky-KG", "lt-LT", "lv-LV", "ml-IN", "mr-IN", "ms-MY", "ms-SG", "nb-NO", "ne-NP", "nl-BE",
        "nl-NL", "no-NO", "oc-FR", "pa-IN", "pl-PL", "pt-AO", "pt-BR", "pt-MZ", "pt-PT", "ro-MD", "ro-RO", "ru-RU",
        "si-LK", "sk-SK", "sl-SI", "so-SO", "sq-AL", "sq-XK", "sr-ME", "sr-RS", "sv-SE", "sw-TZ", "ta-IN", "te-IN",
        "th-TH", "tl-PH", "tr-TR", "uk-UA", "ur-PK", "uz-UZ", "vi-VN", "zh-CN", "zh-HK", "zh-SG", "zh-TW", "zu-ZA",
    ];

    /// <summary>
    /// TMDb's documented secure image base (<c>images.secure_base_url</c> of <c>/3/configuration</c>,
    /// VendorSamples). A constant, never read from an answer: the poster fetch goes to TMDb's own image
    /// host and nowhere an answer could point it.
    /// </summary>
    public const string PosterBaseUrl = "https://image.tmdb.org/t/p/";

    /// <summary>
    /// The poster size asked for, one of the configuration's <c>poster_sizes</c>: 342 pixels wide is
    /// sharp on a card about 170 pixels wide on a high-density screen, at a few dozen kilobytes a poster.
    /// </summary>
    public const string PosterSize = "w342";

    /// <summary>
    /// How often a GET TMDb answered 429 is sent again before the call is refused: three waits of at most
    /// 10 seconds each (<see cref="TmdbRequestPacer.MaximumRetryDelay"/>). A read changes nothing, so
    /// sending it again is safe (house rule 8 forbids retrying a write; there is none here).
    /// </summary>
    public const int MaximumRetries = 3;

    /// <summary>
    /// The path segment of TMDb's API version. The owner enters <c>https://api.themoviedb.org</c>; an
    /// address that already ends in it is taken as is (<see cref="EndpointFor"/>).
    /// </summary>
    private const string ApiVersionSegment = "3";

    /// <summary>What a title column holds (<see cref="MediaWorkTitleEntity.Text"/>).</summary>
    private const int TitleMaximumLength = MediaWorkTitleEntity.TextMaximumLength;

    /// <summary>The longest name of a collection, a person or a genre kept: far past any real one.</summary>
    private const int NameMaximumLength = 200;

    private const string ConnectedMessage = "Connected to TMDb.";

    private const string KeyRefusedError =
        "TMDb refused the key (401 Unauthorized). Enter the API Read Access Token from themoviedb.org → Settings → API, not the shorter API key.";

    private const string NotTmdbError =
        "The service at this address answered, but not the way TMDb's API does. Check the address: TMDb's is https://api.themoviedb.org.";

    private const string ApiNotFoundError =
        "TMDb's API wasn't found at this address (404 Not Found). Use https://api.themoviedb.org.";

    private const string TooManyRequestsError =
        "TMDb is asking the app to slow down (429 Too Many Requests), and still was after three retries. Try again in a minute.";

    /// <summary>TMDb's own code for "The resource you requested could not be found." in a 404's body.</summary>
    private const int ResourceNotFoundCode = 34;

    /// <summary>The one gate every request passes, shared by every caller of this singleton.</summary>
    private readonly TmdbRequestPacer _pacer = new(time);

    /// <inheritdoc/>
    public override IntegrationAdapterDescriptor Descriptor { get; } = new(
        TypeId,
        "TMDb",
        IntegrationKind.Metadata,
        UserName: CredentialRequirement.NotUsed,
        Password: CredentialRequirement.NotUsed,
        // The API Read Access Token: TMDb answers nothing without one.
        ApiKey: CredentialRequirement.Required,
        // A catalog lists works, never files.
        ReportsFilePaths: false);

    /// <inheritdoc/>
    public IReadOnlyList<string> Languages => PrimaryTranslations;

    /// <summary>
    /// TMDb's API root for an address: the address itself when its last path segment is <c>3</c> (an
    /// address copied with the version), and otherwise the address with <c>/3</c> appended — a fixed rule
    /// about the address, like <c>TorznabAdapter.EndpointFor</c>'s <c>/api</c>.
    /// </summary>
    internal static string EndpointFor(string baseUrl)
    {
        var path = new Uri(baseUrl).AbsolutePath.TrimEnd('/');
        var lastSegment = path[(path.LastIndexOf('/') + 1)..];
        return string.Equals(lastSegment, ApiVersionSegment, StringComparison.Ordinal)
            ? baseUrl.TrimEnd('/')
            : $"{baseUrl.TrimEnd('/')}/{ApiVersionSegment}";
    }

    /// <summary>
    /// <c>GET /3/authentication</c> with the token: TMDb answers <c>{"success":true}</c> for a token it
    /// takes and 401 for any other, and reads nothing of the catalog to do it.
    /// </summary>
    protected override async Task<ConnectionTestResult> TestAsync(
        HttpClient client, IntegrationConnectionSettings settings, ICollection<string> sessionSecrets, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(client, () => ApiRequest(settings, "authentication"), cancellationToken);
        if (response.StatusCode != HttpStatusCode.OK)
        {
            return await KnownFailureAsync(response, notFoundError: null, cancellationToken) is { } failure
                ? ConnectionTestResult.Failed(failure.Status, failure.Message)
                : DescribeUnexpectedStatus(response, settings, sessionSecrets);
        }

        using var document = await TryReadJsonAsync(response, cancellationToken);
        return document?.RootElement is { ValueKind: JsonValueKind.Object } root
            && root.TryGetProperty("success", out var success)
            && success.ValueKind == JsonValueKind.True
                ? ConnectionTestResult.Succeeded(ConnectedMessage)
                : ConnectionTestResult.Failed(IntegrationCallStatus.UnexpectedResponse, NotTmdbError);
    }

    // ── requests ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Sends the request <paramref name="createRequest"/> makes once the pacer lets it start, and again,
    /// made anew, each time TMDb answers 429 — after the wait it asked for, at most
    /// <see cref="MaximumRetries"/> times. The last answer is returned whatever it is.
    /// </summary>
    private async Task<HttpResponseMessage> SendAsync(
        HttpClient client, Func<HttpRequestMessage> createRequest, CancellationToken cancellationToken)
    {
        for (var retries = 0; ; retries++)
        {
            await _pacer.WaitTurnAsync(cancellationToken);
            // A request can be sent once; a retry sends a new one to the same URI with the same headers.
            using var request = createRequest();
            var response = await client.SendAsync(request, cancellationToken);
            if (response.StatusCode != HttpStatusCode.TooManyRequests)
                return response;

            // Every caller waits it out, not only this one: TMDb counts requests per address. MAM-33 — the last 429 too,
            // which this call gives up on: the next request from anyone would be over the limit as well.
            _pacer.HoldOff(response.Headers.RetryAfter);
            if (retries == MaximumRetries)
                return response;
            response.Dispose();
        }
    }

    /// <summary>
    /// MAM-46 — the <c>language</c> value a catalog call sends: the integration's catalog language, or
    /// <see cref="DefaultLanguage"/> when the settings name none. Escaped, as it goes into the query: the integration
    /// rules only ever store a tag of <see cref="PrimaryTranslations"/>, but a row edited by hand must not add a
    /// parameter of its own.
    /// </summary>
    private static string LanguageAskedBy(IntegrationConnectionSettings settings) =>
        Uri.EscapeDataString(settings.CatalogLanguage ?? DefaultLanguage);

    /// <summary>A GET of <paramref name="pathAndQuery"/> under the API root, with the token and JSON
    /// asked for.</summary>
    private static HttpRequestMessage ApiRequest(IntegrationConnectionSettings settings, string pathAndQuery)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, new Uri($"{EndpointFor(settings.BaseUrl)}/{pathAndQuery}"));
        // The header, never v3's api_key= in the query, which a proxy would log with the URI. The key rules
        // keep the value to visible ASCII without spaces, so it can't break the header.
        if (settings.ApiKey is not null)
            request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {settings.ApiKey}");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return request;
    }

    /// <summary>
    /// One API call read whole: the GET of <paramref name="pathAndQuery"/>, a failure answered by its
    /// sentence, and an OK answer read by <paramref name="read"/> from its root object — whose
    /// <c>null</c>, like a body that isn't a JSON object, means the answer isn't TMDb's.
    /// </summary>
    /// <param name="notFoundError">The sentence for TMDb's own 404 when the call names one resource (a
    /// work, a collection, a person); <c>null</c> when a 404 can only mean the address is wrong.</param>
    private async Task<IntegrationCallResult<T>> GetJsonAsync<T>(
        HttpClient client,
        IntegrationConnectionSettings settings,
        ICollection<string> sessionSecrets,
        string pathAndQuery,
        string? notFoundError,
        Func<JsonElement, string?[], T?> read,
        CancellationToken cancellationToken) where T : class
    {
        using var response = await SendAsync(client, () => ApiRequest(settings, pathAndQuery), cancellationToken);
        if (response.StatusCode != HttpStatusCode.OK)
        {
            return await KnownFailureAsync(response, notFoundError, cancellationToken) is { } failure
                ? IntegrationCallResult<T>.Failed(failure.Status, failure.Message)
                : DescribeUnexpectedStatus<T>(response, settings, sessionSecrets);
        }

        using var document = await TryReadJsonAsync(response, cancellationToken);
        return document?.RootElement is { ValueKind: JsonValueKind.Object } root
            && read(root, CallSecrets(settings, sessionSecrets)) is { } value
                ? IntegrationCallResult<T>.Succeeded(value)
                : NotTmdb<T>();
    }

    /// <summary>
    /// The failures every API call answers alike, or <c>null</c> for a status the base class words: a
    /// refused token, a 429 still there after the retries, and a 404 — TMDb's own (its JSON with
    /// <see cref="ResourceNotFoundCode"/>) for a call naming one resource, otherwise an address that isn't
    /// TMDb's API, which is no reason to say a work doesn't exist.
    /// </summary>
    private static async Task<(IntegrationCallStatus Status, string Message)?> KnownFailureAsync(
        HttpResponseMessage response, string? notFoundError, CancellationToken cancellationToken)
    {
        switch (response.StatusCode)
        {
            case HttpStatusCode.Unauthorized:
                return (IntegrationCallStatus.AuthenticationFailed, KeyRefusedError);
            case HttpStatusCode.TooManyRequests:
                return (IntegrationCallStatus.Refused, TooManyRequestsError);
            case HttpStatusCode.NotFound:
                return notFoundError is not null && await IsResourceNotFoundAsync(response, cancellationToken)
                    ? (IntegrationCallStatus.NotFound, notFoundError)
                    : (IntegrationCallStatus.UnexpectedResponse, ApiNotFoundError);
            default:
                return null;
        }
    }

    /// <summary>Whether a 404's body is TMDb's own: a JSON object whose <c>status_code</c> is
    /// <see cref="ResourceNotFoundCode"/>.</summary>
    private static async Task<bool> IsResourceNotFoundAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        using var document = await TryReadJsonAsync(response, cancellationToken);
        return document?.RootElement is { ValueKind: JsonValueKind.Object } root
            && root.TryGetProperty("status_code", out var code)
            && code.ValueKind == JsonValueKind.Number
            && code.TryGetInt32(out var value)
            && value == ResourceNotFoundCode;
    }

    private static IntegrationCallResult<T> NotTmdb<T>() where T : class =>
        IntegrationCallResult<T>.Failed(IntegrationCallStatus.UnexpectedResponse, NotTmdbError);

    // ── a caller's mistakes ──────────────────────────────────────────────────

    /// <summary>
    /// TMDb's path segment for <paramref name="kind"/>: <c>movie</c> or <c>tv</c>. An episode, or a kind
    /// this adapter doesn't know, is the caller's mistake: TMDb catalogs films and shows as works, and an
    /// episode never stands alone in it (ADR 0017). Nothing is sent.
    /// </summary>
    private static string KindSegment(MediaWorkKind kind) => kind switch
    {
        MediaWorkKind.Movie => "movie",
        MediaWorkKind.Series => "tv",
        _ => throw new ArgumentException("A catalog work is a film or a show, never an episode.", nameof(kind)),
    };

    /// <summary>
    /// Throws unless <paramref name="id"/> is a TMDb id (digits only, <see cref="MediaIdentifierRules.IsTmdbId"/>):
    /// it goes into the request's path, where a <c>..</c> or a <c>?</c> would change what is asked.
    /// </summary>
    private static void RequireTmdbId(string id, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(id, parameterName);
        if (!MediaIdentifierRules.IsTmdbId(id))
            throw new ArgumentException("A TMDb id is 1 to 12 digits.", parameterName);
    }

    // ── reading what TMDb lists ──────────────────────────────────────────────

    /// <summary>
    /// One film or show as TMDb lists it — in details, a list, a collection or a person's credits — or
    /// <c>null</c> when it has no usable id or no title at all. A show's fields are <c>name</c>,
    /// <c>original_name</c> and <c>first_air_date</c>, a film's <c>title</c>, <c>original_title</c> and
    /// <c>release_date</c>; genres come as <c>genres</c> in details and as <c>genre_ids</c> in a list.
    /// </summary>
    private static CatalogWorkSummary? ReadSummary(JsonElement element, MediaWorkKind kind, string?[] secrets)
    {
        if (element.ValueKind != JsonValueKind.Object || IdOf(element) is not { } id)
            return null;

        var isShow = kind == MediaWorkKind.Series;
        var original = TextProperty(element, isShow ? "original_name" : "original_title", TitleMaximumLength, secrets);
        var title = TextProperty(element, isShow ? "name" : "title", TitleMaximumLength, secrets);
        // TMDb already falls back to the original title for a work without one in the language asked; a list that
        // gives no title at all still has the original to show.
        if (title.Length == 0)
            title = original;
        if (title.Length == 0)
            return null;

        var voteCount = CountProperty(element, "vote_count");
        return new CatalogWorkSummary(
            kind,
            id,
            title,
            original.Length > 0 ? original : null,
            LanguageOf(element),
            DateProperty(element, isShow ? "first_air_date" : "release_date"),
            GenreIdsOf(element),
            RatingOf(element, voteCount),
            voteCount,
            PosterPathOf(element));
    }

    /// <summary>
    /// The entries of <paramref name="element"/>'s array <paramref name="name"/> that are works of
    /// <paramref name="kind"/>: those whose <c>media_type</c> is <paramref name="kind"/>'s, or that have
    /// none (an answer of one kind only). An entry of the other kind, or one that can't be read, is left
    /// out; <c>null</c> when there is no such array, which TMDb always sends.
    /// </summary>
    private static List<CatalogWorkSummary>? ReadSummaries(JsonElement element, string name, MediaWorkKind kind, string?[] secrets)
    {
        if (!element.TryGetProperty(name, out var array) || array.ValueKind != JsonValueKind.Array)
            return null;

        var mediaType = kind == MediaWorkKind.Series ? "tv" : "movie";
        var summaries = new List<CatalogWorkSummary>();
        foreach (var entry in array.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object)
                continue;
            if (entry.TryGetProperty("media_type", out var type)
                && !(type.ValueKind == JsonValueKind.String && string.Equals(type.GetString(), mediaType, StringComparison.Ordinal)))
                continue;
            if (ReadSummary(entry, kind, secrets) is { } summary)
                summaries.Add(summary);
        }

        return summaries;
    }

    /// <summary>
    /// The <c>id</c> as text when it is a whole number TMDb could give (1 to 12 digits, like
    /// <see cref="MediaIdentifierRules.IsTmdbId"/>); otherwise <c>null</c>.
    /// </summary>
    private static string? IdOf(JsonElement element) =>
        element.TryGetProperty("id", out var idElement)
        && idElement.ValueKind == JsonValueKind.Number
        && idElement.TryGetInt64(out var id)
        && id is >= 1 and <= 999_999_999_999
            ? id.ToString(CultureInfo.InvariantCulture)
            : null;

    /// <summary>A reference by id and name — a collection, a director, a creator — or <c>null</c> when
    /// either is missing.</summary>
    private static CatalogReference? ReferenceOf(JsonElement element, string?[] secrets)
    {
        if (element.ValueKind != JsonValueKind.Object || IdOf(element) is not { } id)
            return null;
        var name = TextProperty(element, "name", NameMaximumLength, secrets);
        return name.Length > 0 ? new CatalogReference(id, name) : null;
    }

    /// <summary>The genre ids, each once and in TMDb's order, from <c>genre_ids</c> (a list) or
    /// <c>genres</c> (details); a value that isn't a positive whole number is left out.</summary>
    private static List<int> GenreIdsOf(JsonElement element)
    {
        var ids = new List<int>();
        if (element.TryGetProperty("genre_ids", out var genreIds) && genreIds.ValueKind == JsonValueKind.Array)
        {
            foreach (var value in genreIds.EnumerateArray())
            {
                if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var id) && id > 0 && !ids.Contains(id))
                    ids.Add(id);
            }
        }
        else if (element.TryGetProperty("genres", out var genres) && genres.ValueKind == JsonValueKind.Array)
        {
            foreach (var genre in genres.EnumerateArray())
            {
                if (genre.ValueKind == JsonValueKind.Object
                    && genre.TryGetProperty("id", out var value)
                    && value.ValueKind == JsonValueKind.Number
                    && value.TryGetInt32(out var id)
                    && id > 0
                    && !ids.Contains(id))
                    ids.Add(id);
            }
        }

        return ids;
    }

    /// <summary>
    /// <c>vote_average</c> when somebody rated the work and it is a rating, 0 to 10; <c>null</c> for a
    /// work with no votes. TMDb sends 0 then, which as a rating would put an unrated work beside the
    /// worst-rated ones.
    /// </summary>
    private static double? RatingOf(JsonElement element, int voteCount) =>
        voteCount > 0
        && element.TryGetProperty("vote_average", out var average)
        && average.ValueKind == JsonValueKind.Number
        && average.TryGetDouble(out var rating)
        && rating is >= 0 and <= 10
            ? rating
            : null;

    /// <summary>
    /// <c>original_language</c> when it is a language code as TMDb writes one: two or three lower-case
    /// ASCII letters (ISO 639-1, and TMDb's own <c>xx</c> for "no language").
    /// </summary>
    private static string? LanguageOf(JsonElement element) =>
        element.TryGetProperty("original_language", out var language)
        && language.ValueKind == JsonValueKind.String
        && language.GetString() is { Length: 2 or 3 } code
        && code.All(character => character is >= 'a' and <= 'z')
            ? code
            : null;

    /// <summary>
    /// <c>poster_path</c> when it is a path <see cref="GetPosterAsync"/> would fetch; any other value is
    /// no poster, so an answer can never hand the poster fetch a path it would refuse.
    /// </summary>
    private static string? PosterPathOf(JsonElement element) =>
        element.TryGetProperty("poster_path", out var path)
        && path.ValueKind == JsonValueKind.String
        && path.GetString() is { } value
        && PosterPathPattern().IsMatch(value)
            ? value
            : null;

    /// <summary>A <c>yyyy-MM-dd</c> date; <c>null</c> when absent, empty (TMDb's "not known yet") or
    /// anything else.</summary>
    private static DateOnly? DateProperty(JsonElement element, string name) =>
        element.TryGetProperty(name, out var dateElement)
        && dateElement.ValueKind == JsonValueKind.String
        && DateOnly.TryParseExact(dateElement.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : null;

    /// <summary>A whole number of zero or more; 0 when absent or anything else.</summary>
    private static int CountProperty(JsonElement element, string name) =>
        element.TryGetProperty(name, out var countElement)
        && countElement.ValueKind == JsonValueKind.Number
        && countElement.TryGetInt32(out var count)
        && count >= 0
            ? count
            : 0;

    /// <summary>A string property as text for a person, cleaned and cut (<see cref="RemoteText.Clean"/>);
    /// empty when it is absent or not a string.</summary>
    private static string TextProperty(JsonElement element, string name, int maximumLength, string?[] secrets) =>
        element.TryGetProperty(name, out var textElement) && textElement.ValueKind == JsonValueKind.String
            ? RemoteText.Clean(textElement.GetString(), maximumLength, secrets)
            : "";

    /// <summary>
    /// A poster path as TMDb gives one — <c>/</c>, a file name of letters, digits, <c>_</c> and
    /// <c>-</c>, and a JPEG or PNG extension — and nothing a URI would read as more path, a query or
    /// another host. <c>\z</c>, not <c>$</c>, which would also accept a trailing line break.
    /// </summary>
    [GeneratedRegex(@"^/[A-Za-z0-9_-]{1,100}\.(jpg|jpeg|png)\z")]
    private static partial Regex PosterPathPattern();
}
