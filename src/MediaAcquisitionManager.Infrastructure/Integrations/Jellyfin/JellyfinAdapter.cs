using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Abstractions.Library;
using MediaAcquisitionManager.Core.Abstractions.Media;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Media;

namespace MediaAcquisitionManager.Infrastructure.Integrations.Jellyfin;

/// <summary>
/// Jellyfin behind the core's contracts (Adapter, ADR 0005): the registration and the connection test
/// (MAM-7), the media library contract (MAM-8): its movies, and since MAM-32 its series and episodes;
/// and since MAM-34 the optional item identifier (<c>JellyfinAdapter.Identify*.cs</c>), which identifies one
/// movie or series item as the owner's TMDb link says, exactly as Jellyfin's own "Identify" button does; and since MAM-25
/// the optional file lookup (<c>JellyfinAdapter.Files.cs</c>), which asks Jellyfin to look at a download's new files and
/// reads the item it made of each. Identify is the only thing that changes an item in Jellyfin: a search for the id, which
/// changes nothing although it is a POST, and Apply, which replaces the item's title, overview, images and ids with the
/// chosen result's. MAM-25's request to look at paths is the one other POST, and changes no item itself: Jellyfin looks at
/// them as its own scan would. Every other call is a GET, and a sync only reads.
/// </summary>
/// <remarks>
/// Every answer is read as an allowlist, property by property by name (house rule 8): a movie's answer
/// also carries its streams, people, images and the server's paths, none of which goes further than
/// this class. MAM-34 — the one body this adapter sends back is the chosen search result, exactly as Jellyfin gave it.
/// MAM-35 — of the images, the sync reads one name: the tag of the item's own primary image (<c>ImageTags.Primary</c>),
/// and the poster it names is fetched as a JPEG by one more GET (<c>JellyfinAdapter.Posters.cs</c>).
/// </remarks>
internal sealed partial class JellyfinAdapter(IHttpClientFactory httpClients)
    : HttpIntegrationAdapter(httpClients, HttpClientName), IMediaLibraryProvider, IMediaLibraryItemIdentifier, IMediaLibraryFileLookup
{
    /// <summary>The stored type id — part of the on-disk contract (ADR 0005).</summary>
    public const string TypeId = "jellyfin";

    /// <summary>The named client this adapter's requests go through, so a test can swap its handler.</summary>
    public const string HttpClientName = TypeId;

    /// <summary>
    /// MAM-34 — the second named client, for Apply alone: the shared client's 15 seconds is far too short for a call that
    /// takes a second per episode of a series, and an <see cref="HttpClient"/>'s timeout can't change once it has been used
    /// (<see cref="IntegrationsInfrastructureExtensions.IdentifyTimeout"/>).
    /// </summary>
    public const string IdentifyHttpClientName = "jellyfin-identify";

    /// <summary>
    /// The most items one page may ask for. The heaviest sample movie is about 12 KB as Jellyfin sends
    /// it (VendorSamples, MAM-6), so 200 stay far inside the client's 4 MiB buffer
    /// (<see cref="IntegrationsInfrastructureExtensions.MaximumResponseBytes"/>); a bigger page could
    /// outgrow it and fail as "far larger than anything Jellyfin sends".
    /// </summary>
    /// <remarks>
    /// MAM-32 — an episode item can weigh more: the Tom &amp; Jerry Kids item, 65 media sources in one, is
    /// about 149 KB. The sync asks for 50 items a page, which holds a handful of such items among ordinary
    /// ones; a library of nothing but 65-source items would need smaller pages.
    /// </remarks>
    public const int MaximumPageSize = 200;

    /// <summary>The longest server name or version a sentence repeats.</summary>
    private const int RemoteValueMaximumLength = 100;

    /// <summary>What a title column holds (<see cref="MediaWorkTitleEntity.Text"/>).</summary>
    private const int TitleMaximumLength = MediaWorkTitleEntity.TextMaximumLength;

    /// <summary>
    /// Jellyfin's ids are 32 hexadecimal characters. The most is what a version id column holds
    /// (<see cref="MediaVersionEntity.SourceVersionId"/>), the narrowest column an id read here goes to:
    /// anything longer is not an id this app can keep.
    /// </summary>
    private const int IdMaximumLength = MediaVersionEntity.SourceVersionIdMaximumLength;

    /// <summary>Linux's <c>PATH_MAX</c>, what a file path column holds (<see cref="PhysicalFileEntity.ServicePath"/>).</summary>
    private const int PathMaximumLength = PhysicalFileEntity.ServicePathMaximumLength;

    private const string KeyRefusedError =
        "Jellyfin refused the API key. Create one in Jellyfin under Dashboard → API Keys and enter it here.";

    private const string ForbiddenError =
        "Jellyfin accepted the key but won't show it the server's details (403 Forbidden). Use a key from Dashboard → API Keys, not a user's access token.";

    private const string LibraryForbiddenError =
        "Jellyfin accepted the key but won't let it read the library (403 Forbidden). Use a key from Dashboard → API Keys, not a user's access token.";

    // {0} is what the list holds, one of it ("a movie", "an episode", "a series", MAM-32).
    private const string PartsNotFoundError =
        "Jellyfin no longer finds the other parts of {0} it has just listed (404 Not Found); its library is changing. Try again once Jellyfin's library scan has finished.";

    private const string NotJellyfinError =
        "The service at this address answered, but not the way Jellyfin does. Check the address.";

    private const string ItemWithoutIdError =
        "The service at this address listed {0} without an id, which Jellyfin never does. Check the address.";

    private const string PartsOfOddIdError =
        "The service at this address listed {0} in several parts under an id Jellyfin never gives, so the app won't ask for its other parts. Check the address.";

    /// <summary>
    /// MAM-8 — the oldest Jellyfin the app reads libraries from: the owner's choice. The adapter was
    /// sampled live against 12.1.0 only (VendorSamples); 10.9 to 10.11 are accepted from reading
    /// Jellyfin's source (<c>ItemsController.cs</c> of v10.9.0: a user-less <c>/Items</c> with an API
    /// key works since 10.9; <c>AuthorizationContext.cs</c>: <c>Authorization: MediaBrowser Token=</c>
    /// is read by 10.9 and later). Their merged versions come back as separate items that each list
    /// every source, which <c>LibraryVersionOwnership</c> resolves. An older server is refused with a
    /// sentence rather than read through answers nobody has seen.
    /// </summary>
    private static readonly Version MinimumVersion = new(10, 9);

    /// <inheritdoc/>
    public override IntegrationAdapterDescriptor Descriptor { get; } = new(
        TypeId,
        "Jellyfin",
        IntegrationKind.MediaLibrary,
        UserName: CredentialRequirement.NotUsed,
        Password: CredentialRequirement.NotUsed,
        ApiKey: CredentialRequirement.Required,
        ReportsFilePaths: true);

    /// <inheritdoc/>
    /// <remarks>Jellyfin can keep several versions of a movie as media sources of one item (its version
    /// picker), each with its own id.</remarks>
    public MediaLibraryCapabilities Capabilities { get; } = new(GroupsVersionsUnderOneItem: true);

    /// <summary>
    /// <c>GET /System/Info</c> with the key. Not <c>/System/Info/Public</c>: it answers without a key and
    /// so proves nothing about it.
    /// </summary>
    protected override async Task<ConnectionTestResult> TestAsync(
        HttpClient client, IntegrationConnectionSettings settings, ICollection<string> sessionSecrets, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(settings, "System/Info");
        using var response = await client.SendAsync(request, cancellationToken);
        return response.StatusCode switch
        {
            HttpStatusCode.OK => await ReadSystemInfoAsync(response, CallSecrets(settings, sessionSecrets), cancellationToken),
            HttpStatusCode.Unauthorized => ConnectionTestResult.Failed(IntegrationCallStatus.AuthenticationFailed, KeyRefusedError),
            HttpStatusCode.Forbidden => ConnectionTestResult.Failed(IntegrationCallStatus.AuthenticationFailed, ForbiddenError),
            HttpStatusCode.NotFound => ConnectionTestResult.Failed(IntegrationCallStatus.UnexpectedResponse, NotFoundError("/System/Info")),
            _ => DescribeUnexpectedStatus(response, settings, sessionSecrets),
        };
    }

    /// <summary><c>GET /Library/VirtualFolders</c>: every library, its id, its name and its root folders.</summary>
    public Task<IntegrationCallResult<IReadOnlyList<LibraryLocation>>> ListLocationsAsync(
        IntegrationConnectionSettings settings, CancellationToken cancellationToken = default) =>
        CallAsync<IReadOnlyList<LibraryLocation>>(
            settings,
            async (client, sessionSecrets, token) =>
            {
                using var request = CreateRequest(settings, "Library/VirtualFolders");
                using var response = await client.SendAsync(request, token);
                if (response.StatusCode != HttpStatusCode.OK)
                    return DescribeLibraryCallStatus<IReadOnlyList<LibraryLocation>>(response, NotFoundError("/Library/VirtualFolders"), settings, sessionSecrets);

                using var document = await TryReadJsonAsync(response, token);
                return document is null
                    ? NotJellyfin<IReadOnlyList<LibraryLocation>>()
                    : ReadLocations(document.RootElement, CallSecrets(settings, sessionSecrets));
            },
            cancellationToken);

    /// <summary>
    /// <c>GET /Items</c> for one page of every movie, series or episode (MAM-32), with its paths, media
    /// sources, provider ids and original title, and <c>GET /Videos/{id}/AdditionalParts</c> for each
    /// movie or episode stacked from several files.
    /// </summary>
    public Task<IntegrationCallResult<LibraryItemPage>> ListItemsAsync(
        IntegrationConnectionSettings settings, LibraryPageRequest page, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(page);
        // A caller's mistake, not the service's answer: nothing is sent.
        if (!Enum.IsDefined(page.Kind))
            throw new ArgumentOutOfRangeException(nameof(page), page.Kind, "The list is not one Jellyfin has.");
        ArgumentOutOfRangeException.ThrowIfNegative(page.StartIndex);
        ArgumentOutOfRangeException.ThrowIfLessThan(page.Limit, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(page.Limit, MaximumPageSize);

        return CallAsync<LibraryItemPage>(
            settings, (client, sessionSecrets, token) => ReadItemsPageAsync(client, settings, page, sessionSecrets, token), cancellationToken);
    }

    // ── requests ─────────────────────────────────────────────────────────────

    /// <summary>A GET of <paramref name="relativePath"/> (which may carry a query) with the key and
    /// JSON asked for.</summary>
    private static HttpRequestMessage CreateRequest(IntegrationConnectionSettings settings, string relativePath)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, settings.Resolve(relativePath));
        // The Authorization scheme Jellyfin 12 reads by default. X-Emby-Token and api_key= work only
        // while legacy authorization is switched on, and 12 switches it off (VendorSamples README);
        // api_key= would also put the key into every URI a proxy logs. The key rules keep the value
        // free of quotes and backslashes, so it can't break out of the quoted string.
        request.Headers.TryAddWithoutValidation("Authorization", $"MediaBrowser Token=\"{settings.ApiKey}\"");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return request;
    }

    private async Task<IntegrationCallResult<LibraryItemPage>> ReadItemsPageAsync(
        HttpClient client,
        IntegrationConnectionSettings settings,
        LibraryPageRequest page,
        ICollection<string> sessionSecrets,
        CancellationToken cancellationToken)
    {
        // collapseBoxSetItems=false: asked with an API key there is no user, and Jellyfin then replaces
        // the movies of a collection with one box-set item each whatever the server's setting — 670
        // items instead of 870 in the reference library (VendorSamples README). SortName, then
        // DateCreated: a stable order is what makes overlapping pages mean something, and the two
        // "Чужі" items share a sort name, as the two halves of a Mickey Mouse Clubhouse episode do. No
        // user data: nothing here reads it. MAM-32 — a series has no files of its own, so its list asks
        // for its ids and titles only; an episode's SeriesId, ParentIndexNumber, IndexNumber and
        // LocationType come without being asked for (VendorSamples README). MAM-35 — the primary image's
        // tag and no other image's (enableImageTypes=Primary, imageTypeLimit=1): it names the poster a
        // card shows (PosterTag). An episode's answer still carries its series' and season's tags
        // (SeriesPrimaryImageTag, ParentPrimaryImageTag, recorded 2026-10-07), which are never read.
        var (type, fields) = page.Kind switch
        {
            MediaWorkKind.Series => ("Series", "ProviderIds,OriginalTitle"),
            MediaWorkKind.Episode => ("Episode", "Path,MediaSources,ProviderIds,OriginalTitle"),
            _ => ("Movie", "Path,MediaSources,ProviderIds,OriginalTitle"),
        };
        var query = string.Create(
            CultureInfo.InvariantCulture,
            $"Items?recursive=true&includeItemTypes={type}&collapseBoxSetItems=false&fields={fields}&sortBy=SortName,DateCreated&sortOrder=Ascending&startIndex={page.StartIndex}&limit={page.Limit}&enableImages=true&enableImageTypes=Primary&imageTypeLimit=1&enableUserData=false&enableTotalRecordCount=true");
        using var request = CreateRequest(settings, query);
        using var response = await client.SendAsync(request, cancellationToken);
        if (response.StatusCode != HttpStatusCode.OK)
            return DescribeLibraryCallStatus<LibraryItemPage>(response, NotFoundError("/Items"), settings, sessionSecrets);

        var secrets = CallSecrets(settings, sessionSecrets);
        var items = new List<LibraryItem>();
        var stacked = new List<int>();
        int totalCount;
        using (var document = await TryReadJsonAsync(response, cancellationToken))
        {
            if (document?.RootElement is not { ValueKind: JsonValueKind.Object } root
                || !TryGetCount(root, "TotalRecordCount", out totalCount)
                || !root.TryGetProperty("Items", out var itemsElement)
                || itemsElement.ValueKind != JsonValueKind.Array)
                return NotJellyfin<LibraryItemPage>();

            foreach (var element in itemsElement.EnumerateArray())
            {
                var (item, isStacked, error) = ReadItem(element, page.Kind, secrets);
                if (error is not null)
                    return IntegrationCallResult<LibraryItemPage>.Failed(IntegrationCallStatus.UnexpectedResponse, error);
                if (isStacked)
                    stacked.Add(items.Count);
                items.Add(item!);
            }
        }

        foreach (var index in stacked)
        {
            var parts = await ReadAdditionalPartsAsync(client, settings, items[index].SourceItemId, page.Kind, sessionSecrets, cancellationToken);
            if (!parts.IsSuccess)
                return IntegrationCallResult<LibraryItemPage>.Failed(parts.Status, parts.Error!);
            items[index] = WithParts(items[index], parts.Value!);
        }

        return IntegrationCallResult<LibraryItemPage>.Succeeded(new LibraryItemPage(items.AsReadOnly(), totalCount));
    }

    /// <summary>
    /// The files after the first of a movie or an episode stacked from several (<c>part1</c>, <c>cd2</c>,
    /// …): Jellyfin lists only the first in the item's media source, and each further part as an item of
    /// its own that <c>/Items</c> doesn't return.
    /// </summary>
    private async Task<IntegrationCallResult<IReadOnlyList<LibraryFile>>> ReadAdditionalPartsAsync(
        HttpClient client, IntegrationConnectionSettings settings, string itemId, MediaWorkKind kind, ICollection<string> sessionSecrets, CancellationToken cancellationToken)
    {
        // MAM-8 — the id goes into the path, and escaping leaves "." and ".." as they are, which the URI
        // then resolves: a server listing a movie with the id ".." would have the keyed GET sent to
        // {base}/AdditionalParts instead. Jellyfin's item ids are Guids, so only one that parses as a
        // Guid is sent, in Jellyfin's own 32-hex-digit form; any other fails the page.
        if (!Guid.TryParse(itemId, out var id))
            return IntegrationCallResult<IReadOnlyList<LibraryFile>>.Failed(IntegrationCallStatus.UnexpectedResponse, Sentence(PartsOfOddIdError, kind));

        using var request = CreateRequest(settings, string.Create(CultureInfo.InvariantCulture, $"Videos/{id:N}/AdditionalParts"));
        using var response = await client.SendAsync(request, cancellationToken);
        if (response.StatusCode != HttpStatusCode.OK)
            return DescribeLibraryCallStatus<IReadOnlyList<LibraryFile>>(response, Sentence(PartsNotFoundError, kind), settings, sessionSecrets);

        using var document = await TryReadJsonAsync(response, cancellationToken);
        if (document?.RootElement is not { ValueKind: JsonValueKind.Object } root
            || !root.TryGetProperty("Items", out var partsElement)
            || partsElement.ValueKind != JsonValueKind.Array)
            return NotJellyfin<IReadOnlyList<LibraryFile>>();

        var files = new List<LibraryFile>();
        foreach (var part in partsElement.EnumerateArray())
        {
            if (part.ValueKind == JsonValueKind.Object && FilePath(part) is { } path)
                files.Add(new LibraryFile(path, FirstSourceSize(part)));
        }

        return IntegrationCallResult<IReadOnlyList<LibraryFile>>.Succeeded(files.AsReadOnly());
    }

    /// <summary>
    /// A library call's own statuses: the key refused or not allowed, and a 404, whose sentence depends
    /// on the call. Anything else is the shared sentence a connection test would get.
    /// </summary>
    private IntegrationCallResult<T> DescribeLibraryCallStatus<T>(
        HttpResponseMessage response, string notFoundError, IntegrationConnectionSettings settings, ICollection<string> sessionSecrets)
        where T : class =>
        response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => IntegrationCallResult<T>.Failed(IntegrationCallStatus.AuthenticationFailed, KeyRefusedError),
            HttpStatusCode.Forbidden => IntegrationCallResult<T>.Failed(IntegrationCallStatus.AuthenticationFailed, LibraryForbiddenError),
            HttpStatusCode.NotFound => IntegrationCallResult<T>.Failed(IntegrationCallStatus.UnexpectedResponse, notFoundError),
            _ => DescribeUnexpectedStatus<T>(response, settings, sessionSecrets),
        };

    // ── the connection test's answer ─────────────────────────────────────────

    /// <summary>
    /// Reads three named properties and nothing else (an allowlist): the body also carries the
    /// server's local address and its file-system paths, which have no business in a message. Each is
    /// masked of <paramref name="secrets"/> before it is cut to length.
    /// </summary>
    private static async Task<ConnectionTestResult> ReadSystemInfoAsync(
        HttpResponseMessage response, string?[] secrets, CancellationToken cancellationToken)
    {
        using var document = await TryReadJsonAsync(response, cancellationToken);
        if (document?.RootElement is not { ValueKind: JsonValueKind.Object } root)
            return NotJellyfin();

        // Checked before the version: another product that answers /System/Info (Emby does) is worth
        // naming, whatever else its answer lacks.
        if (root.TryGetProperty("ProductName", out var productElement) && productElement.ValueKind != JsonValueKind.Null)
        {
            if (productElement.ValueKind != JsonValueKind.String)
                return NotJellyfin();
            var product = RemoteText.Clean(productElement.GetString(), RemoteValueMaximumLength, secrets);
            if (product.Length == 0)
                return NotJellyfin();
            if (!product.StartsWith("Jellyfin", StringComparison.OrdinalIgnoreCase))
                return ConnectionTestResult.Failed(
                    IntegrationCallStatus.UnexpectedResponse, $"The service at this address is {product}, not Jellyfin.");
        }

        var version = TextProperty(root, "Version", RemoteValueMaximumLength, secrets);
        if (version.Length == 0)
            return NotJellyfin();

        // MAM-8 — the version gate: the sync runs this test first, so an old server is refused before
        // anything of its library is read.
        if (DescribeUnsupportedVersion(version) is { } unsupported)
            return ConnectionTestResult.Failed(IntegrationCallStatus.UnsupportedVersion, unsupported);

        var serverName = TextProperty(root, "ServerName", RemoteValueMaximumLength, secrets);
        return ConnectionTestResult.Succeeded(serverName.Length > 0
            ? $"Connected to Jellyfin {version} ({serverName})."
            : $"Connected to Jellyfin {version}.");
    }

    /// <summary>
    /// The sentence refusing <paramref name="version"/> (already cleaned), or <c>null</c> when it is
    /// <see cref="MinimumVersion"/> or later. Only the leading <c>major.minor</c> counts, so
    /// <c>10.9.11</c> and <c>12.1.0-rc1</c> compare; a version without one can't be compared, and is
    /// refused rather than guessed at.
    /// </summary>
    private static string? DescribeUnsupportedVersion(string version)
    {
        var match = VersionPattern().Match(version);
        if (!match.Success
            || !int.TryParse(match.Groups[1].ValueSpan, NumberStyles.None, CultureInfo.InvariantCulture, out var major)
            || !int.TryParse(match.Groups[2].ValueSpan, NumberStyles.None, CultureInfo.InvariantCulture, out var minor))
            return $"Jellyfin reported the version “{version}”, which the app can't compare with {MinimumVersion}, the oldest version it reads libraries from.";

        return new Version(major, minor) < MinimumVersion
            ? $"This is Jellyfin {version}; the app reads libraries from Jellyfin {MinimumVersion} or later. Update Jellyfin, then test again."
            : null;
    }

    // ── the libraries ────────────────────────────────────────────────────────

    /// <summary>
    /// Each library with an id, in Jellyfin's order: its name (the id when it has none) and its root
    /// folders exactly as Jellyfin sees them — they are compared with file paths, never shown. A second
    /// library with an id already read is dropped.
    /// </summary>
    private static IntegrationCallResult<IReadOnlyList<LibraryLocation>> ReadLocations(JsonElement root, string?[] secrets)
    {
        if (root.ValueKind != JsonValueKind.Array)
            return NotJellyfin<IReadOnlyList<LibraryLocation>>();

        var locations = new List<LibraryLocation>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var folder in root.EnumerateArray())
        {
            if (folder.ValueKind != JsonValueKind.Object || IdProperty(folder, "ItemId") is not { } id || !seen.Add(id))
                continue;

            var name = TextProperty(folder, "Name", LibraryLocationEntity.NameMaximumLength, secrets);
            List<string> paths = folder.TryGetProperty("Locations", out var pathsElement) && pathsElement.ValueKind == JsonValueKind.Array
                ?
                [
                    .. pathsElement.EnumerateArray()
                        .Where(path => path.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(path.GetString()))
                        .Select(path => path.GetString()!),
                ]
                : [];
            locations.Add(new LibraryLocation(id, name.Length > 0 ? name : id, paths.AsReadOnly()));
        }

        return IntegrationCallResult<IReadOnlyList<LibraryLocation>>.Succeeded(locations.AsReadOnly());
    }

    // ── the movies, series and episodes ──────────────────────────────────────

    /// <summary>
    /// One item of the page, or the sentence that fails the whole page: an item without an id can't be
    /// told apart from any other, and an item of another type than the list's means the list isn't what
    /// was asked for — neither is skipped, because a sync that silently lost an item would delete its
    /// facts.
    /// </summary>
    /// <remarks>
    /// MAM-32 — the type Jellyfin gives is the item's kind, whatever its file is called (ADR 0017): an
    /// episode the movie list holds stays a movie. A series has no files: its media sources, should it
    /// list any, are not read. A virtual episode (<c>LocationType</c> <c>Virtual</c>, a missing episode
    /// Jellyfin lists from the show's metadata) is read like any other, so the pages still add up, and
    /// marked missing; its placeholder source has no path, so it gives no file either way.
    /// </remarks>
    private static (LibraryItem? Item, bool IsStacked, string? Error) ReadItem(JsonElement element, MediaWorkKind kind, string?[] secrets)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return (null, false, NotJellyfinError);
        if (IdProperty(element, "Id") is not { } id)
            return (null, false, Sentence(ItemWithoutIdError, kind));
        if (element.TryGetProperty("Type", out var type) && type.ValueKind != JsonValueKind.Null)
        {
            if (type.ValueKind != JsonValueKind.String)
                return (null, false, NotJellyfinError);
            if (!string.Equals(type.GetString(), TypeName(kind), StringComparison.Ordinal))
                return (null, false, $"Jellyfin listed an item of type “{RemoteText.Clean(type.GetString(), RemoteValueMaximumLength, secrets)}” when asked for {Plural(kind)} only, so the app can't rely on the list. Check the address.");
        }

        if (element.TryGetProperty("MediaSources", out var sources)
            && sources.ValueKind is not (JsonValueKind.Array or JsonValueKind.Null))
            return (null, false, NotJellyfinError);

        var hasFiles = kind != MediaWorkKind.Series;
        var item = new LibraryItem(
            id,
            kind,
            Titles(element, secrets),
            Identifiers(element, kind),
            hasFiles && sources.ValueKind == JsonValueKind.Array ? Versions(sources, secrets) : [],
            kind == MediaWorkKind.Episode ? Episode(element) : null,
            kind == MediaWorkKind.Episode && IsVirtual(element),
            PosterTag(element, id));
        var partCount = element.TryGetProperty("PartCount", out var parts) && parts.ValueKind == JsonValueKind.Number && parts.TryGetInt32(out var count)
            ? count
            : 1;
        // Parts belong to a version; a movie that reports none has nowhere to put them.
        return (item, partCount > 1 && item.Versions.Count > 0, null);
    }

    /// <summary>The displayed title and the original one, each only when Jellyfin gives it, with the
    /// production year when it is a plausible one.</summary>
    private static List<ReportedTitle> Titles(JsonElement element, string?[] secrets)
    {
        int? year = element.TryGetProperty("ProductionYear", out var yearElement)
            && yearElement.ValueKind == JsonValueKind.Number
            && yearElement.TryGetInt32(out var value)
            && value is >= 1 and <= 9999
                ? value
                : null;

        var titles = new List<ReportedTitle>();
        if (TextProperty(element, "Name", TitleMaximumLength, secrets) is { Length: > 0 } name)
            titles.Add(new ReportedTitle(name, MediaTitleKind.Display, year));
        if (TextProperty(element, "OriginalTitle", TitleMaximumLength, secrets) is { Length: > 0 } original)
            titles.Add(new ReportedTitle(original, MediaTitleKind.Original, year));
        return titles;
    }

    /// <summary>
    /// The registry ids among the provider ids, keys in any letter case, values trimmed, the first
    /// well-formed value of each scheme: IMDb and TMDb for a movie; MAM-32 — IMDb, TMDb and TVDB for a
    /// series, its TMDb id a show's (<see cref="MediaIdentifierScheme.TmdbTv"/>: TMDb numbers shows apart
    /// from movies, ADR 0017); IMDb and TVDB for an episode, which carries no TMDb id of a work. Nothing
    /// else: <c>TmdbCollection</c> names a box set, not a work, and as a work id it would make every movie
    /// of a collection one work (ADR 0006).
    /// </summary>
    private static List<MediaIdentifier> Identifiers(JsonElement element, MediaWorkKind kind)
    {
        if (!element.TryGetProperty("ProviderIds", out var providerIds) || providerIds.ValueKind != JsonValueKind.Object)
            return [];

        string? imdb = null;
        string? tmdb = null;
        string? tvdb = null;
        foreach (var property in providerIds.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.String)
                continue;
            var value = property.Value.GetString()!.Trim();
            if (imdb is null && string.Equals(property.Name, "Imdb", StringComparison.OrdinalIgnoreCase) && MediaIdentifierRules.IsImdbId(value))
                imdb = value;
            else if (tmdb is null && kind != MediaWorkKind.Episode
                && string.Equals(property.Name, "Tmdb", StringComparison.OrdinalIgnoreCase) && MediaIdentifierRules.IsTmdbId(value))
                tmdb = value;
            else if (tvdb is null && kind != MediaWorkKind.Movie
                && string.Equals(property.Name, "Tvdb", StringComparison.OrdinalIgnoreCase) && MediaIdentifierRules.IsTvdbId(value))
                tvdb = value;
        }

        var identifiers = new List<MediaIdentifier>();
        if (imdb is not null)
            identifiers.Add(new MediaIdentifier(MediaIdentifierScheme.Imdb, imdb));
        if (tmdb is not null)
            identifiers.Add(new MediaIdentifier(kind == MediaWorkKind.Series ? MediaIdentifierScheme.TmdbTv : MediaIdentifierScheme.Tmdb, tmdb));
        if (tvdb is not null)
            identifiers.Add(new MediaIdentifier(MediaIdentifierScheme.Tvdb, tvdb));
        return identifiers;
    }

    /// <summary>
    /// MAM-32 — where an episode sits: its series item's id and its season and episode numbers, each only
    /// when Jellyfin gives one (the Tom &amp; Jerry Kids item has numbers and no series). The series item
    /// says which show's ids the core reads; it is never the show itself (ADR 0017).
    /// </summary>
    private static LibraryEpisode Episode(JsonElement element) =>
        new(IdProperty(element, "SeriesId"), Number(element, "ParentIndexNumber"), Number(element, "IndexNumber"));

    /// <summary>A whole number of zero or more; <c>null</c> when absent or anything else.</summary>
    private static int? Number(JsonElement element, string name) =>
        element.TryGetProperty(name, out var numberElement)
        && numberElement.ValueKind == JsonValueKind.Number
        && numberElement.TryGetInt32(out var number)
        && number >= 0
            ? number
            : null;

    /// <summary>MAM-32 — Jellyfin's mark of an item it lists without a file: <c>LocationType</c>
    /// <c>Virtual</c>.</summary>
    private static bool IsVirtual(JsonElement element) =>
        element.TryGetProperty("LocationType", out var location)
        && location.ValueKind == JsonValueKind.String
        && string.Equals(location.GetString(), "Virtual", StringComparison.Ordinal);

    /// <summary>
    /// MAM-35 — the tag of the item's own primary image (<c>ImageTags.Primary</c>), the one its card shows; <c>null</c> when
    /// it has none (an empty <c>ImageTags</c>, as a virtual special has), or when the value is not a tag the app can keep
    /// and name a file by (<see cref="PosterTagPattern"/>). Nor when the item's id is not a Guid:
    /// <see cref="GetPosterAsync"/> sends only a Guid (MAM-8's rule), so no stored row names a poster the fetch would refuse
    /// — the poster endpoint would otherwise throw on every view of the card instead of answering 404. Never the
    /// <c>SeriesPrimaryImageTag</c> or <c>ParentPrimaryImageTag</c> an episode also carries: those are its series' and
    /// season's pictures, and a card shows the item's own or none. Identify reads the same property more leniently for its own purpose, to see whether Apply
    /// changed the item (<see cref="ItemFactsOf"/>); this reading is the sync's alone.
    /// </summary>
    private static string? PosterTag(JsonElement element, string itemId) =>
        Guid.TryParse(itemId, out _)
        && element.TryGetProperty("ImageTags", out var imageTags)
        && imageTags.ValueKind == JsonValueKind.Object
        && imageTags.TryGetProperty("Primary", out var primary)
        && primary.ValueKind == JsonValueKind.String
        && PosterTagPattern().IsMatch(primary.GetString()!)
            ? primary.GetString()
            : null;

    /// <summary>The type Jellyfin gives the items of <paramref name="kind"/>'s list.</summary>
    private static string TypeName(MediaWorkKind kind) => kind switch
    {
        MediaWorkKind.Series => "Series",
        MediaWorkKind.Episode => "Episode",
        _ => "Movie",
    };

    /// <summary>What a sentence calls the items of <paramref name="kind"/>'s list.</summary>
    private static string Plural(MediaWorkKind kind) => kind switch
    {
        MediaWorkKind.Series => "series",
        MediaWorkKind.Episode => "episodes",
        _ => "movies",
    };

    /// <summary><paramref name="format"/> with one item of <paramref name="kind"/>'s list as its
    /// <c>{0}</c>.</summary>
    private static string Sentence(string format, MediaWorkKind kind) =>
        string.Format(CultureInfo.InvariantCulture, format, kind switch
        {
            MediaWorkKind.Series => "a series",
            MediaWorkKind.Episode => "an episode",
            _ => "a movie",
        });

    /// <summary>
    /// One version per media source, the first of any two with one id. The source's name labels it only
    /// when the movie has two or more: then it is what Jellyfin's version picker shows; alone it is just
    /// the file name.
    /// </summary>
    private static List<LibraryVersion> Versions(JsonElement sources, string?[] secrets)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var kept = new List<(string? Id, JsonElement Source)>();
        foreach (var source in sources.EnumerateArray())
        {
            if (source.ValueKind != JsonValueKind.Object)
                continue;
            var sourceId = IdProperty(source, "Id");
            if (sourceId is null || seen.Add(sourceId))
                kept.Add((sourceId, source));
        }

        var labelled = kept.Count >= 2;
        return
        [
            .. kept.Select(entry => new LibraryVersion(
                entry.Id,
                labelled && TextProperty(entry.Source, "Name", MediaVersionEntity.EditionLabelMaximumLength, secrets) is { Length: > 0 } label ? label : null,
                FileOf(entry.Source) is { } file ? [file] : [])),
        ];
    }

    /// <summary>
    /// The one file of a media source when it is a local video file: not streamed over another protocol
    /// or from a remote address, not a DVD or Blu-ray folder, and not a <c>.strm</c> pointer (10.9
    /// reports one as remote, 12.1 as the .strm file itself). A row is one name of a file (ADR 0006),
    /// so anything else gives its version no file rather than a folder or a link.
    /// </summary>
    private static LibraryFile? FileOf(JsonElement source)
    {
        if (source.TryGetProperty("Protocol", out var protocol)
            && protocol.ValueKind != JsonValueKind.Null
            && !(protocol.ValueKind == JsonValueKind.String && string.Equals(protocol.GetString(), "File", StringComparison.Ordinal)))
            return null;
        if (source.TryGetProperty("IsRemote", out var isRemote) && isRemote.ValueKind == JsonValueKind.True)
            return null;
        if (source.TryGetProperty("VideoType", out var videoType)
            && videoType.ValueKind != JsonValueKind.Null
            && !(videoType.ValueKind == JsonValueKind.String && videoType.GetString() is "VideoFile" or "Iso"))
            return null;

        return FilePath(source) is { } path ? new LibraryFile(path, Size(source)) : null;
    }

    /// <summary>
    /// The <c>Path</c> exactly as Jellyfin sent it — never cleaned or trimmed, because it names the file
    /// — when it is one: not blank, not longer than a path column holds, without control characters,
    /// and not a <c>.strm</c>.
    /// </summary>
    private static string? FilePath(JsonElement element)
    {
        if (!element.TryGetProperty("Path", out var pathElement) || pathElement.ValueKind != JsonValueKind.String)
            return null;
        var path = pathElement.GetString()!;
        return string.IsNullOrWhiteSpace(path)
            || path.Length > PathMaximumLength
            || path.Any(char.IsControl)
            || path.EndsWith(".strm", StringComparison.OrdinalIgnoreCase)
                ? null
                : path;
    }

    /// <summary>The <c>Size</c> in bytes when it is a whole number of zero or more; otherwise unknown.</summary>
    private static long? Size(JsonElement element) =>
        element.TryGetProperty("Size", out var size) && size.ValueKind == JsonValueKind.Number && size.TryGetInt64(out var bytes) && bytes >= 0
            ? bytes
            : null;

    /// <summary>A part's size, which Jellyfin gives on its first media source.</summary>
    private static long? FirstSourceSize(JsonElement part) =>
        part.TryGetProperty("MediaSources", out var sources) && sources.ValueKind == JsonValueKind.Array
            && sources.EnumerateArray().FirstOrDefault() is { ValueKind: JsonValueKind.Object } first
            ? Size(first)
            : null;

    /// <summary>
    /// <paramref name="item"/> with <paramref name="parts"/> after the files of its own media source (the
    /// one whose id is the item's), or of its first when none is; a path it already has is not added
    /// twice.
    /// </summary>
    private static LibraryItem WithParts(LibraryItem item, IReadOnlyList<LibraryFile> parts)
    {
        var versions = item.Versions.ToList();
        var index = versions.FindIndex(version => string.Equals(version.SourceVersionId, item.SourceItemId, StringComparison.Ordinal));
        if (index < 0)
            index = 0;

        var files = versions[index].Files.ToList();
        foreach (var part in parts)
        {
            if (!files.Any(file => string.Equals(file.ServicePath, part.ServicePath, StringComparison.Ordinal)))
                files.Add(part);
        }

        versions[index] = versions[index] with { Files = files.AsReadOnly() };
        return item with { Versions = versions.AsReadOnly() };
    }

    // ── reading helpers ──────────────────────────────────────────────────────

    /// <summary>A string property as an id: verbatim, and only when it is not blank, not longer than
    /// <see cref="IdMaximumLength"/> and free of control characters.</summary>
    private static string? IdProperty(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var idElement) || idElement.ValueKind != JsonValueKind.String)
            return null;
        var id = idElement.GetString()!;
        return string.IsNullOrWhiteSpace(id) || id.Length > IdMaximumLength || id.Any(char.IsControl) ? null : id;
    }

    /// <summary>A string property as text for a person, cleaned and cut (<see cref="RemoteText.Clean"/>);
    /// empty when it is absent or not a string.</summary>
    private static string TextProperty(JsonElement element, string name, int maximumLength, string?[] secrets) =>
        element.TryGetProperty(name, out var textElement) && textElement.ValueKind == JsonValueKind.String
            ? RemoteText.Clean(textElement.GetString(), maximumLength, secrets)
            : "";

    private static bool TryGetCount(JsonElement element, string name, out int count)
    {
        count = 0;
        return element.TryGetProperty(name, out var countElement)
            && countElement.ValueKind == JsonValueKind.Number
            && countElement.TryGetInt32(out count)
            && count >= 0;
    }

    /// <summary>Where Jellyfin wasn't found, and what to do about a base path.</summary>
    private static string NotFoundError(string path) =>
        $"Jellyfin wasn't found at this address (404 Not Found for {path}). If Jellyfin runs under a base path, include it, for example http://192.168.1.10:8096/jellyfin.";

    private static ConnectionTestResult NotJellyfin() =>
        ConnectionTestResult.Failed(IntegrationCallStatus.UnexpectedResponse, NotJellyfinError);

    private static IntegrationCallResult<T> NotJellyfin<T>() where T : class =>
        IntegrationCallResult<T>.Failed(IntegrationCallStatus.UnexpectedResponse, NotJellyfinError);

    /// <summary>
    /// A version's leading <c>major.minor</c>, ASCII digits only (<c>\d</c> would take any script's).
    /// </summary>
    [GeneratedRegex(@"^([0-9]+)\.([0-9]+)")]
    private static partial Regex VersionPattern();

    /// <summary>
    /// MAM-35 — an image tag as the app takes one: 1 to 64 ASCII letters and digits (Jellyfin's are 32 hexadecimal digits,
    /// and a poster row holds 64, <see cref="MediaWorkPosterEntity.TagMaximumLength"/>), so it goes into a query and a file
    /// name as it is. <c>\z</c>, not <c>$</c>, which would also accept a trailing line break.
    /// </summary>
    [GeneratedRegex(@"^[A-Za-z0-9]{1,64}\z")]
    private static partial Regex PosterTagPattern();
}
