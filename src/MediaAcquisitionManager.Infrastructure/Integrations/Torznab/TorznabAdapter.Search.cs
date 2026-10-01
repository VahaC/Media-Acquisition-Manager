using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Xml.Linq;
using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Abstractions.Media;
using MediaAcquisitionManager.Core.Abstractions.Search;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Media;

namespace MediaAcquisitionManager.Infrastructure.Integrations.Torznab;

// MAM-10 — the capabilities and the search: what a feed's caps say it can search by, and the request a
// query becomes. The releases the answer lists are read in TorznabAdapter.Items.cs.
internal sealed partial class TorznabAdapter
{
    /// <summary>The longest category name kept, like a server title.</summary>
    private const int CategoryNameMaximumLength = 100;

    private const string NotSearchResultsError =
        "The service at this address answered, but not with Torznab search results. Check that the address is the feed's Torznab URL.";

    /// <inheritdoc/>
    public Task<IntegrationCallResult<ReleaseSearchCapabilities>> GetCapabilitiesAsync(
        IntegrationConnectionSettings settings, CancellationToken cancellationToken = default) =>
        CallAsync<ReleaseSearchCapabilities>(
            settings,
            async (client, sessionSecrets, token) =>
            {
                using var request = CapabilitiesRequest(settings);
                using var response = await client.SendAsync(request, token);
                var document = await TryReadXmlAsync(response, token);
                var secrets = CallSecrets(settings, sessionSecrets);

                if (document?.Root is { Name.LocalName: "error" } error)
                    return DescribeError<ReleaseSearchCapabilities>(error, response.StatusCode, secrets, FeedRequest.Capabilities);

                if (response.StatusCode == HttpStatusCode.OK)
                {
                    return document?.Root is { Name.LocalName: "caps" } caps
                        ? IntegrationCallResult<ReleaseSearchCapabilities>.Succeeded(ReadCapabilities(caps, secrets))
                        : IntegrationCallResult<ReleaseSearchCapabilities>.Failed(IntegrationCallStatus.UnexpectedResponse, NotTorznabError);
                }

                return DescribeFailedCall<ReleaseSearchCapabilities>(response, settings, sessionSecrets);
            },
            cancellationToken);

    /// <inheritdoc/>
    public Task<IntegrationCallResult<IReadOnlyList<ReleaseSearchHit>>> SearchAsync(
        IntegrationConnectionSettings settings, ReleaseSearchQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        // A caller's mistake, not the feed's answer: nothing is sent.
        var uri = SearchUri(settings, query);
        return CallAsync<IReadOnlyList<ReleaseSearchHit>>(
            settings, (client, sessionSecrets, token) => ReadSearchAsync(client, settings, uri, query, sessionSecrets, token), cancellationToken);
    }

    private static ReleaseSearchCapabilities ReadCapabilities(XElement caps, string?[] secrets)
    {
        var searching = caps.Element("searching");
        var movieSearch = searching?.Element("movie-search");
        var hasMovieSearch = IsAvailable(movieSearch);
        // Newznab's default when a mode names no parameters is q alone; the connection test already
        // says "title" for it.
        var parameters = hasMovieSearch ? SupportedParameters(movieSearch!) : [];
        if (hasMovieSearch && parameters.Length == 0)
            parameters = ["q"];

        bool Takes(string parameter) => parameters.Contains(parameter, StringComparer.OrdinalIgnoreCase);
        List<MediaIdentifierScheme> schemes = [];
        if (Takes("imdbid"))
            schemes.Add(MediaIdentifierScheme.Imdb);
        if (Takes("tmdbid"))
            schemes.Add(MediaIdentifierScheme.Tmdb);

        var limits = caps.Element("limits");
        return new ReleaseSearchCapabilities(
            IsAvailable(searching?.Element("search")),
            hasMovieSearch,
            Takes("q"),
            schemes,
            Takes("year"),
            PositiveCount((string?)limits?.Attribute("max")) ?? PositiveCount((string?)limits?.Attribute("default")),
            ReadCategories(caps.Element("categories"), secrets));
    }

    /// <summary>
    /// Every category and subcategory with a well-formed id, parents before their children; an id the
    /// feed repeats (Toloka's caps list 100137 twice) keeps its first entry.
    /// </summary>
    private static List<ReleaseCategory> ReadCategories(XElement? categories, string?[] secrets)
    {
        var read = new List<ReleaseCategory>();
        if (categories is null)
            return read;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var category in categories.Elements("category"))
        {
            // A category whose id can't be one is skipped with its subcategories, which would name a
            // parent that isn't listed.
            if (Category(category, null, secrets) is not { } parent)
                continue;
            if (seen.Add(parent.Id))
                read.Add(parent);

            foreach (var subcategory in category.Elements("subcat"))
            {
                if (Category(subcategory, parent.Id, secrets) is { } child && seen.Add(child.Id))
                    read.Add(child);
            }
        }

        return read;
    }

    private static ReleaseCategory? Category(XElement element, string? parentId, string?[] secrets)
    {
        var id = ((string?)element.Attribute("id"))?.Trim();
        if (id is null || !CategoryIdPattern().IsMatch(id))
            return null;

        var name = RemoteText.Clean((string?)element.Attribute("name"), CategoryNameMaximumLength, secrets);
        return new ReleaseCategory(id, name.Length > 0 ? name : id, parentId, HoldsMovies(id));
    }

    /// <summary>
    /// Torznab's standard numbering files movies under 2000 to 2999 (Movies and its subcategories); a
    /// gateway's own ids (Toloka's 100xxx) say nothing the core could rely on. The core doesn't know the
    /// numbering, so the adapter tells it (MAM-10, ADR 0004).
    /// </summary>
    private static bool HoldsMovies(string id) =>
        int.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number is >= 2000 and <= 2999;

    /// <summary>
    /// <c>GET {endpoint}?t=movie|search&amp;apikey=…&amp;q=…&amp;imdbid=…&amp;tmdbid=…&amp;year=…&amp;cat=…&amp;limit=…</c>,
    /// only the parameters the query has, every value escaped so none can end early or add a parameter
    /// of its own. Throws for a query no core code builds.
    /// </summary>
    private static Uri SearchUri(IntegrationConnectionSettings settings, ReleaseSearchQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(query.Identifiers);
        ArgumentNullException.ThrowIfNull(query.CategoryIds);
        if (!Enum.IsDefined(query.Mode))
            throw new ArgumentOutOfRangeException(nameof(query), query.Mode, "The search mode is not one this adapter knows.");
        ArgumentOutOfRangeException.ThrowIfLessThan(query.Limit, 1, nameof(query));
        if (query.Text is not null && string.IsNullOrWhiteSpace(query.Text))
            throw new ArgumentException("The search text is blank; leave it null to search by an identifier alone.", nameof(query));
        if (query.Text is null && query.Identifiers.Count == 0)
            throw new ArgumentException("A search needs text or an identifier.", nameof(query));
        if (query.Identifiers.Count > 0 && query.Mode != ReleaseSearchMode.Movie)
            throw new ArgumentException("Only the movie search takes identifiers.", nameof(query));
        if (query.CategoryIds.Any(id => id is null || !CategoryIdPattern().IsMatch(id)))
            throw new ArgumentException("A category id is not one a Torznab feed lists.", nameof(query));

        var imdb = Identifier(query.Identifiers, MediaIdentifierScheme.Imdb);
        var tmdb = Identifier(query.Identifiers, MediaIdentifierScheme.Tmdb);

        var parameters = new List<string> { query.Mode == ReleaseSearchMode.Movie ? "t=movie" : "t=search" };
        if (settings.ApiKey is not null)
            parameters.Add($"apikey={Uri.EscapeDataString(settings.ApiKey)}");
        if (query.Text is not null)
            parameters.Add($"q={Uri.EscapeDataString(query.Text)}");
        // Newznab's imdbid is the number without "tt", leading zeros kept: Prowlarr adds the prefix back
        // for an indexer that wants it, and reads "tt0816692" as no id at all (Prowlarr issue #1631).
        if (imdb is not null)
            parameters.Add($"imdbid={Uri.EscapeDataString(imdb[2..])}");
        if (tmdb is not null)
            parameters.Add($"tmdbid={Uri.EscapeDataString(tmdb)}");
        if (query.Year is { } year)
            parameters.Add($"year={Uri.EscapeDataString(year.ToString(CultureInfo.InvariantCulture))}");
        if (query.CategoryIds.Count > 0)
            parameters.Add($"cat={string.Join(',', query.CategoryIds.Select(Uri.EscapeDataString))}");
        parameters.Add($"limit={Uri.EscapeDataString(query.Limit.ToString(CultureInfo.InvariantCulture))}");

        return new Uri($"{EndpointFor(settings.BaseUrl)}?{string.Join('&', parameters)}");
    }

    /// <summary>The query's one value of <paramref name="scheme"/>, or <c>null</c>; throws for a value a
    /// library would never hold, or two of one scheme.</summary>
    private static string? Identifier(IReadOnlyList<MediaIdentifier> identifiers, MediaIdentifierScheme scheme)
    {
        string? found = null;
        foreach (var identifier in identifiers)
        {
            ArgumentNullException.ThrowIfNull(identifier);
            if (!Enum.IsDefined(identifier.Scheme))
                throw new ArgumentOutOfRangeException(nameof(identifiers), identifier.Scheme, "The identifier scheme is not one this adapter knows.");
            if (identifier.Scheme != scheme)
                continue;

            var valid = identifier.Value is not null
                && (scheme == MediaIdentifierScheme.Imdb ? MediaIdentifierRules.IsImdbId(identifier.Value) : MediaIdentifierRules.IsTmdbId(identifier.Value));
            if (!valid)
                throw new ArgumentException($"An identifier is not a well-formed {scheme} id.", nameof(identifiers));
            if (found is not null)
                throw new ArgumentException($"A search takes one {scheme} id.", nameof(identifiers));
            found = identifier.Value;
        }

        return found;
    }

    private async Task<IntegrationCallResult<IReadOnlyList<ReleaseSearchHit>>> ReadSearchAsync(
        HttpClient client,
        IntegrationConnectionSettings settings,
        Uri uri,
        ReleaseSearchQuery query,
        ICollection<string> sessionSecrets,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/rss+xml"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/xml"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/xml"));

        using var response = await client.SendAsync(request, cancellationToken);
        var document = await TryReadXmlAsync(response, cancellationToken);
        var secrets = CallSecrets(settings, sessionSecrets);

        if (document?.Root is { Name.LocalName: "error" } error)
            return DescribeError<IReadOnlyList<ReleaseSearchHit>>(error, response.StatusCode, secrets, FeedRequest.Search);

        if (response.StatusCode == HttpStatusCode.OK)
        {
            return document?.Root is { Name.LocalName: "rss" } rss && rss.Element("channel") is { } channel
                ? ReadHits(channel, settings, query, secrets)
                : IntegrationCallResult<IReadOnlyList<ReleaseSearchHit>>.Failed(IntegrationCallStatus.UnexpectedResponse, NotSearchResultsError);
        }

        return DescribeFailedCall<IReadOnlyList<ReleaseSearchHit>>(response, settings, sessionSecrets);
    }
}
