using System.Globalization;
using System.Net;
using System.Text.Json;
using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Media;

namespace MediaAcquisitionManager.Infrastructure.Integrations.Jellyfin;

// MAM-34 — what an identification reads: the administrator it reads as, the item, and the item's facts. Every read is an
// allowlist of named properties (house rule 8); nothing of an answer goes further than this class but the strings and
// numbers the identification compares.
internal sealed partial class JellyfinAdapter
{
    /// <summary>The name Jellyfin gives an item's registry ids, in every item it sends and takes.</summary>
    private const string ProviderIdsProperty = "ProviderIds";

    /// <summary>
    /// <c>GET /Users</c>: the id of the first user, in Jellyfin's order, who is an administrator and not disabled —
    /// Jellyfin reads one item only as a user (<c>GET /Items/{id}</c> with the key and no user is 400, recorded live), and an
    /// administrator sees every library. Each user is read for its id and its two policy flags only.
    /// </summary>
    private async Task<IntegrationCallResult<string>> ReadAdministratorAsync(
        HttpClient client, IntegrationConnectionSettings settings, ICollection<string> sessionSecrets, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(settings, "Users");
        using var response = await client.SendAsync(request, cancellationToken);
        if (response.StatusCode != HttpStatusCode.OK)
            return DescribeLibraryCallStatus<string>(response, NotFoundError("/Users"), settings, sessionSecrets);

        using var document = await TryReadJsonAsync(response, cancellationToken);
        if (document?.RootElement is not { ValueKind: JsonValueKind.Array } users)
            return NotJellyfin<string>();

        foreach (var user in users.EnumerateArray())
        {
            if (user.ValueKind == JsonValueKind.Object
                && user.TryGetProperty("Policy", out var policy)
                && policy.ValueKind == JsonValueKind.Object
                && policy.TryGetProperty("IsAdministrator", out var isAdministrator)
                && isAdministrator.ValueKind == JsonValueKind.True
                && !(policy.TryGetProperty("IsDisabled", out var isDisabled) && isDisabled.ValueKind == JsonValueKind.True)
                // The id goes into the item's query, so only one that is an id Jellyfin gives (MAM-8's rule for paths).
                && IdProperty(user, "Id") is { } id
                && Guid.TryParse(id, out var userId))
                return IntegrationCallResult<string>.Succeeded(userId.ToString("N"));
        }

        return IntegrationCallResult<string>.Failed(IntegrationCallStatus.Refused, NoAdministratorError);
    }

    /// <summary>
    /// <c>GET /Items/{id}?userId=</c>: the whole item, as a document the caller disposes, once it is an object with a type and
    /// <c>ProviderIds</c> that are an object, null or absent. Which type it must be is the caller's to judge: an item of
    /// another type is a decision (refused), not a failed read.
    /// </summary>
    private async Task<IntegrationCallResult<JsonDocument>> ReadItemAsync(
        HttpClient client, IntegrationConnectionSettings settings, Guid itemId, string userId, ICollection<string> sessionSecrets, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(settings, string.Create(CultureInfo.InvariantCulture, $"{ItemPath(itemId)}?userId={userId}"));
        using var response = await client.SendAsync(request, cancellationToken);
        switch (response.StatusCode)
        {
            case HttpStatusCode.OK:
                break;
            case HttpStatusCode.NotFound:
                return await ItemMissingAsync(client, settings, itemId, sessionSecrets, cancellationToken);
            // 401 and 403 as any library read; a 400 — the live answer to a read without a user — is the shared
            // "doesn't send this" sentence: the app always names one.
            default:
                return DescribeLibraryCallStatus<JsonDocument>(response, ItemGoneError, settings, sessionSecrets);
        }

        var document = await TryReadJsonAsync(response, cancellationToken);
        if (document?.RootElement is { ValueKind: JsonValueKind.Object } item && IsItem(item))
            return IntegrationCallResult<JsonDocument>.Succeeded(document);

        document?.Dispose();
        return IntegrationCallResult<JsonDocument>.Failed(IntegrationCallStatus.UnexpectedResponse, NotJellyfinError);
    }

    /// <summary>
    /// Why the item read as the administrator answered 404. Jellyfin answers 404 for an item that user may not see as for
    /// one that is gone (<c>LibraryManager.GetItemById</c> with a user: a parental limit, a blocked tag, a library the user
    /// has no access to — administrators included), while the sync's user-less list still holds it. So the same user-less
    /// list is asked for that one id (<c>GET /Items?ids=</c>, read in Jellyfin's source, not tried live): listed, the item
    /// is there but hidden, and the call is refused with its own sentence; not listed, it is gone.
    /// </summary>
    private async Task<IntegrationCallResult<JsonDocument>> ItemMissingAsync(
        HttpClient client, IntegrationConnectionSettings settings, Guid itemId, ICollection<string> sessionSecrets, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(
            settings,
            string.Create(CultureInfo.InvariantCulture, $"Items?ids={itemId:N}&recursive=true&enableImages=false&enableUserData=false&enableTotalRecordCount=true"));
        using var response = await client.SendAsync(request, cancellationToken);
        if (response.StatusCode != HttpStatusCode.OK)
            return DescribeLibraryCallStatus<JsonDocument>(response, ItemGoneError, settings, sessionSecrets);

        using var document = await TryReadJsonAsync(response, cancellationToken);
        if (document?.RootElement is not { ValueKind: JsonValueKind.Object } root
            || !root.TryGetProperty("Items", out var items)
            || items.ValueKind != JsonValueKind.Array)
            return NotJellyfin<JsonDocument>();

        var listed = items.EnumerateArray().Any(item =>
            item.ValueKind == JsonValueKind.Object && IdProperty(item, "Id") is { } id && Guid.TryParse(id, out var listedId) && listedId == itemId);
        return listed
            ? IntegrationCallResult<JsonDocument>.Failed(IntegrationCallStatus.Refused, HiddenFromAdministratorError)
            : IntegrationCallResult<JsonDocument>.Failed(IntegrationCallStatus.NotFound, ItemGoneError);
    }

    /// <summary>Whether an object is an item as Jellyfin sends one: a string <c>Type</c>, and <c>ProviderIds</c> an object,
    /// null or absent.</summary>
    private static bool IsItem(JsonElement item) =>
        item.TryGetProperty("Type", out var type)
        && type.ValueKind == JsonValueKind.String
        && !(item.TryGetProperty(ProviderIdsProperty, out var providerIds) && providerIds.ValueKind is not (JsonValueKind.Object or JsonValueKind.Null));

    // ── the item's facts ─────────────────────────────────────────────────────

    /// <summary>
    /// The facts an identification compares, read by name from the item (absent <c>LockData</c> and <c>LockedFields</c> mean
    /// unlocked, absent <c>ImageTags</c>, <c>Etag</c> and <c>Path</c> mean none): its type, name, overview, year, whether it
    /// is locked as a whole or in its name or overview, its path (which says which library holds it), its Etag and primary
    /// image tag (what tells Apply changed it) and its provider ids.
    /// </summary>
    private static ItemFacts ItemFactsOf(JsonElement item)
    {
        var lockedFields = item.TryGetProperty("LockedFields", out var fields) && fields.ValueKind == JsonValueKind.Array
            ? fields.EnumerateArray().Where(field => field.ValueKind == JsonValueKind.String).Select(field => field.GetString()!).ToList()
            : [];
        var primaryImageTag = item.TryGetProperty("ImageTags", out var imageTags)
            && imageTags.ValueKind == JsonValueKind.Object
            && imageTags.TryGetProperty("Primary", out var primary)
            && primary.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(primary.GetString())
                ? primary.GetString()
                : null;
        return new ItemFacts(
            StringValue(item, "Type") ?? "",
            StringValue(item, "Name"),
            StringValue(item, "Overview"),
            Number(item, "ProductionYear"),
            item.TryGetProperty("LockData", out var lockData) && lockData.ValueKind == JsonValueKind.True,
            lockedFields.Contains("Name", StringComparer.OrdinalIgnoreCase),
            lockedFields.Contains("Overview", StringComparer.OrdinalIgnoreCase),
            StringValue(item, "Path"),
            StringValue(item, "Etag"),
            primaryImageTag,
            ProviderIdPairs(item));
    }

    /// <summary>A string property as it came, <c>null</c> when it is absent or not a string.</summary>
    private static string? StringValue(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    // ── provider ids ─────────────────────────────────────────────────────────

    /// <summary>The item's provider ids in Jellyfin's order: each key with its text, <c>null</c> for a value that isn't
    /// a string. None when the item has no <c>ProviderIds</c> object.</summary>
    private static List<(string Key, string? Value)> ProviderIdPairs(JsonElement item) =>
        item.TryGetProperty(ProviderIdsProperty, out var providerIds) && providerIds.ValueKind == JsonValueKind.Object
            ?
            [
                .. providerIds.EnumerateObject().Select(property =>
                    (property.Name, property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() : null)),
            ]
            : [];

    /// <summary>
    /// The item's well-formed id under <paramref name="key"/> (<c>Imdb</c>, or <c>Tmdb</c> for a film's or a show's),
    /// the first in any letter case, trimmed; <c>null</c> when it holds none — a blank or malformed value is not held, as
    /// a sync doesn't read it either (<see cref="Identifiers"/>).
    /// </summary>
    private static string? HeldId(List<(string Key, string? Value)> providerIds, string key) =>
        providerIds
            .Where(pair => string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase) && pair.Value is not null)
            .Select(pair => pair.Value!.Trim())
            .FirstOrDefault(value => string.Equals(key, "Imdb", StringComparison.Ordinal) ? MediaIdentifierRules.IsImdbId(value) : MediaIdentifierRules.IsTmdbId(value));

    /// <summary>Whether the item holds <paramref name="value"/> under <paramref name="key"/> in any letter case, compared
    /// trimmed and without regard to case — whatever else it holds under the key's other spellings.</summary>
    private static bool HoldsKeyValue(List<(string Key, string? Value)> providerIds, string key, string value) =>
        providerIds.Any(pair => string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase) && SameValue(pair.Value, value));

    /// <summary>Two values as the same id: both none, or equal once trimmed, in any letter case.</summary>
    private static bool SameValue(string? current, string? wanted) =>
        current is null || wanted is null
            ? current is null && wanted is null
            : string.Equals(current.Trim(), wanted.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Whether a transport failure of Apply may have come after the request reached Jellyfin. Only the failures of making
    /// the connection — the name, the connection, its TLS, a proxy tunnel — are certain to have sent nothing; the rest (the
    /// answer cut off, malformed, too large) came once the request was on its way.
    /// </summary>
    private static bool MayHaveArrived(HttpRequestException exception) =>
        exception.HttpRequestError is not (HttpRequestError.NameResolutionError
            or HttpRequestError.ConnectionError
            or HttpRequestError.SecureConnectionError
            or HttpRequestError.ProxyTunnelError);

    /// <summary>The item's path, in Jellyfin's own 32-hex-digit form.</summary>
    private static string ItemPath(Guid itemId) => string.Create(CultureInfo.InvariantCulture, $"Items/{itemId:N}");

    /// <summary>The facts of an item read for an identification (see <see cref="ItemFactsOf"/>).</summary>
    private sealed record ItemFacts(
        string Type,
        string? Name,
        string? Overview,
        int? Year,
        bool LockData,
        bool NameLocked,
        bool OverviewLocked,
        string? Path,
        string? Etag,
        string? PrimaryImageTag,
        List<(string Key, string? Value)> ProviderIds);
}
