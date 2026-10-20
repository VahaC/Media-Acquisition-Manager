using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Abstractions.Library;
using MediaAcquisitionManager.Core.Abstractions.Media;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Paths;
using MediaAcquisitionManager.Core.Texts;

namespace MediaAcquisitionManager.Infrastructure.Integrations.Jellyfin;

// MAM-25 — the check of a replacement's new files (IMediaLibraryFileLookup; specification §3.4, check 4; ADR 0035): Jellyfin
// is asked to look at the new paths, then read for the item it made of each file.
//
// Asking is POST /Library/Media/Updated with {"Updates":[{"Path":…,"UpdateType":"Created"}]}, read in Jellyfin 12.2's source
// (LibraryController.PostUpdatedMedia): each path goes to the library monitor as its own folder watcher would report it,
// whether real-time monitoring is on or off, and the answer is 204 at once, before anything is looked at, whatever the path
// is. So the answer proves only that Jellyfin took the request. It changes no item itself — Jellyfin makes and refreshes the
// items of what it then finds, as its own scan would — and asking again is harmless.
//
// MAM-26 — the same request tells Jellyfin that a replacement's old copies went into the quarantine ("Deleted") or came back
// ("Created"), at their old paths (ADR 0030). Jellyfin 12.2 never reads UpdateType — PostUpdatedMedia hands the monitor the
// path alone, which refreshes the nearest item still on the disk: that drops a file that went and finds one that appeared.
// The type still says what happened, as its own watcher would say it, so a Jellyfin that ever reads it hears the truth.
//
// Reading is by computed item id, never by a listing. Jellyfin gives an item the id new Guid(MD5(UTF-16 of its type's full
// name + its path)) (LibraryManager.GetNewItemId, with case-sensitive ids, the default; ADR 0030 already relies on it to say
// a file put back is the same item again), and GET /Items/{id}?userId= of an administrator answers any item, a hidden
// alternate version included. /Items can't do that: it has no path filter, and it leaves alternate versions out even when
// asked for their ids, so a new file Jellyfin made a version of an existing film would never be found there. A read-only
// live check of Jellyfin 12.2.0 on 2026-10-10 found the computed id right for 880 of 880 movies and 440 of 440 episode files
// (VendorSamples/Jellyfin/episode-alternate-version-item.json is one hidden alternate read this way). Each answer's Path must
// equal the asked path exactly, so a formula Jellyfin changes, a server with case-insensitive ids or one that rewrites the
// paths it reports (its path substitution) can only miss a file — it is not listed, and the check stops for review —, never
// take another file for it.
internal sealed partial class JellyfinAdapter
{
    private static readonly Message ScanForbiddenError = Message.Of("jellyfin.forbidden.scan");

    private const string LookupPathError =
        "Each path is an absolute path of at most 4096 characters without control characters, as the library reports one.";

    /// <summary>
    /// The types whose ids a file's own item may have, in the order they are tried: a film, an episode, Jellyfin's plain
    /// video — what a file becomes that is neither, a home video or an extra other than a trailer —, and a trailer, which
    /// Jellyfin 12.2's source resolves as a type of its own (ExtraResolver: a <c>trailers</c> folder, or a <c>-trailer</c>
    /// name). An alternate version has its primary's type, so these four cover every version too.
    /// </summary>
    private static readonly string[] FileItemTypeNames =
    [
        "MediaBrowser.Controller.Entities.Movies.Movie",
        "MediaBrowser.Controller.Entities.TV.Episode",
        "MediaBrowser.Controller.Entities.Video",
        "MediaBrowser.Controller.Entities.Trailer",
    ];

    /// <inheritdoc/>
    /// <remarks>
    /// <c>POST /Library/Media/Updated</c> with every path in one body, each with the change's update type, on the shared
    /// client: Jellyfin answers it at once. 204 is the one answer that took the request; 401 and 403 are the key's; anything
    /// else is an answer Jellyfin doesn't send for it. Sent once by this call, whatever becomes of it. Unlike Identify's Apply
    /// it doesn't ask for its connection to close: were .NET to send it again on a pooled connection the other end had closed,
    /// Jellyfin would only be asked to look twice.
    /// </remarks>
    public Task<IntegrationCallResult<LibraryScanRequest>> RequestScanAsync(
        IntegrationConnectionSettings settings, IReadOnlyList<string> servicePaths, LibraryFileChange change, CancellationToken cancellationToken = default)
    {
        // A caller's mistake, thrown before anything is sent.
        ArgumentNullException.ThrowIfNull(settings);
        ValidateLookupPaths(servicePaths);
        var body = ScanBody(servicePaths, UpdateTypeOf(change));
        var pathCount = servicePaths.Count;

        return CallAsync<LibraryScanRequest>(
            settings,
            async (client, sessionSecrets, token) =>
            {
                using var request = CreateRequest(settings, "Library/Media/Updated");
                // The same key and Accept as every read; only the method and the body differ.
                request.Method = HttpMethod.Post;
                request.Content = new ByteArrayContent(body) { Headers = { ContentType = new MediaTypeHeaderValue("application/json") } };
                using var response = await client.SendAsync(request, token);
                return response.StatusCode switch
                {
                    HttpStatusCode.NoContent => IntegrationCallResult<LibraryScanRequest>.Succeeded(new LibraryScanRequest(pathCount)),
                    HttpStatusCode.Forbidden => IntegrationCallResult<LibraryScanRequest>.Failed(IntegrationCallStatus.AuthenticationFailed, ScanForbiddenError),
                    _ => DescribeLibraryCallStatus<LibraryScanRequest>(response, NotFoundError("/Library/Media/Updated"), settings, sessionSecrets),
                };
            },
            cancellationToken);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// Reads, as the first enabled administrator (Jellyfin reads one item only as a user): <c>GET /Users</c>; for each path,
    /// <c>GET /Items/{id}?userId=</c> of the id the path would have as a film, an episode, a plain video and a trailer, in
    /// that order, until one answers an item whose <c>Path</c> is exactly the path asked — a 404, or an item of another
    /// path, is the next type; and for each distinct series of the episodes found, one user-less
    /// <c>GET /Items?ids={series}&amp;fields=ProviderIds</c>, which the live check saw answer a series' ids to the key alone.
    /// An item the administrator may not see answers 404 like one that doesn't exist, so it is not listed either.
    /// </para>
    /// <para>
    /// Refreshed is read from what Jellyfin's own refresh leaves, read in its source (MetadataService): a new item is saved
    /// first with no metadata at all, and the final save of its first refresh brings the running time it measured, the
    /// provider ids and an episode's numbers (the end number included) together. So an item with a <c>RunTimeTicks</c> above
    /// zero — and, for an episode, an <c>IndexNumber</c> — has been read; its ids are whatever that refresh found, none
    /// included. Every item is read as an allowlist, its ids and numbers by the sync's own readers.
    /// </para>
    /// </remarks>
    public Task<IntegrationCallResult<IReadOnlyList<LibraryFileReading>>> ReadFilesAsync(
        IntegrationConnectionSettings settings, IReadOnlyList<string> servicePaths, CancellationToken cancellationToken = default)
    {
        // A caller's mistake, thrown before anything is sent.
        ArgumentNullException.ThrowIfNull(settings);
        ValidateLookupPaths(servicePaths);
        string[] paths = [.. servicePaths];

        return CallAsync<IReadOnlyList<LibraryFileReading>>(
            settings, (client, sessionSecrets, token) => ReadFilesCoreAsync(client, settings, paths, sessionSecrets, token), cancellationToken);
    }

    private async Task<IntegrationCallResult<IReadOnlyList<LibraryFileReading>>> ReadFilesCoreAsync(
        HttpClient client, IntegrationConnectionSettings settings, string[] paths, ICollection<string> sessionSecrets, CancellationToken cancellationToken)
    {
        var user = await ReadAdministratorAsync(client, settings, sessionSecrets, cancellationToken);
        if (!user.IsSuccess)
            return IntegrationCallResult<IReadOnlyList<LibraryFileReading>>.Failed(user.Status, user.Error!);

        var readings = new List<LibraryFileReading>();
        // One read of each series, however many of its episodes the paths hold: a season pack is one series.
        var seriesIdentifiers = new Dictionary<Guid, IReadOnlyList<MediaIdentifier>>();
        foreach (var path in paths)
        {
            var read = await ReadFileItemAsync(client, settings, path, user.Value!, sessionSecrets, cancellationToken);
            if (!read.IsSuccess)
                return IntegrationCallResult<IReadOnlyList<LibraryFileReading>>.Failed(read.Status, read.Error!);

            var reading = read.Value!;
            if (reading.Episode?.SeriesItemId is { } seriesItemId)
            {
                var seriesId = Guid.Parse(seriesItemId);
                if (!seriesIdentifiers.TryGetValue(seriesId, out var identifiers))
                {
                    var series = await ReadSeriesIdentifiersAsync(client, settings, seriesId, sessionSecrets, cancellationToken);
                    if (!series.IsSuccess)
                        return IntegrationCallResult<IReadOnlyList<LibraryFileReading>>.Failed(series.Status, series.Error!);
                    identifiers = series.Value!;
                    seriesIdentifiers.Add(seriesId, identifiers);
                }

                reading = reading with { SeriesIdentifiers = identifiers };
            }

            readings.Add(reading);
        }

        return IntegrationCallResult<IReadOnlyList<LibraryFileReading>>.Succeeded(readings.AsReadOnly());
    }

    /// <summary>
    /// The item Jellyfin holds for exactly <paramref name="path"/>, looked up under the id it would have as each of
    /// <see cref="FileItemTypeNames"/>; a reading that it lists none when no id answers an item at that path. A 404 means no
    /// item has that id; any other failure fails the read.
    /// </summary>
    private async Task<IntegrationCallResult<LibraryFileReading>> ReadFileItemAsync(
        HttpClient client, IntegrationConnectionSettings settings, string path, string userId, ICollection<string> sessionSecrets, CancellationToken cancellationToken)
    {
        foreach (var typeName in FileItemTypeNames)
        {
            var itemId = ComputedItemId(typeName, path);
            using var request = CreateRequest(settings, string.Create(CultureInfo.InvariantCulture, $"{ItemPath(itemId)}?userId={userId}"));
            using var response = await client.SendAsync(request, cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotFound)
                continue;
            if (response.StatusCode != HttpStatusCode.OK)
                return DescribeLibraryCallStatus<LibraryFileReading>(response, NotFoundError("/Items"), settings, sessionSecrets);

            using var document = await TryReadJsonAsync(response, cancellationToken);
            if (document?.RootElement is not { ValueKind: JsonValueKind.Object } item || !IsItem(item))
                return NotJellyfin<LibraryFileReading>();
            // Another file's item under this id: nothing is taken for this file on the strength of a formula.
            if (!string.Equals(StringValue(item, "Path"), path, StringComparison.Ordinal))
                continue;

            return IntegrationCallResult<LibraryFileReading>.Succeeded(ReadingOf(path, itemId, item));
        }

        return IntegrationCallResult<LibraryFileReading>.Succeeded(
            new LibraryFileReading(path, Listed: false, ItemId: null, Kind: null, Refreshed: false, [], Episode: null, []));
    }

    /// <summary>
    /// The reading of a file's own item: its kind by its <c>Type</c> alone (ADR 0017), its ids by the sync's rules for that
    /// kind (<see cref="Identifiers"/>) — none for a plain video or a trailer —, an episode's place with its end number, and
    /// whether Jellyfin has finished reading it. The series' ids are the caller's to add.
    /// </summary>
    private static LibraryFileReading ReadingOf(string path, Guid itemId, JsonElement item)
    {
        var kind = StringValue(item, "Type") switch
        {
            "Movie" => LibraryFileKind.Movie,
            "Episode" => LibraryFileKind.Episode,
            _ => LibraryFileKind.Other,
        };
        List<MediaIdentifier> identifiers = kind switch
        {
            LibraryFileKind.Movie => Identifiers(item, MediaWorkKind.Movie),
            LibraryFileKind.Episode => Identifiers(item, MediaWorkKind.Episode),
            _ => [],
        };
        var place = kind == LibraryFileKind.Episode
            ? new LibraryEpisodePlace(SeriesItemId(item), Number(item, "ParentIndexNumber"), Number(item, "IndexNumber"), Number(item, "IndexNumberEnd"))
            : null;
        var refreshed = RunTimeTicks(item) > 0 && (place is null || place.EpisodeNumber is not null);
        return new LibraryFileReading(path, Listed: true, itemId.ToString("N"), kind, refreshed, identifiers.AsReadOnly(), place, []);
    }

    /// <summary>
    /// The registry ids of the series item <paramref name="seriesId"/>, read with the series' rules (its TMDb id a show's):
    /// a user-less <c>GET /Items?ids=</c> that asks for its provider ids. None when Jellyfin no longer lists it, or lists
    /// under that id an item that is not a series, whose ids would be another kind's (ADR 0017).
    /// </summary>
    private async Task<IntegrationCallResult<IReadOnlyList<MediaIdentifier>>> ReadSeriesIdentifiersAsync(
        HttpClient client, IntegrationConnectionSettings settings, Guid seriesId, ICollection<string> sessionSecrets, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(
            settings,
            string.Create(CultureInfo.InvariantCulture, $"Items?ids={seriesId:N}&fields=ProviderIds&enableImages=false&enableUserData=false"));
        using var response = await client.SendAsync(request, cancellationToken);
        if (response.StatusCode != HttpStatusCode.OK)
            return DescribeLibraryCallStatus<IReadOnlyList<MediaIdentifier>>(response, NotFoundError("/Items"), settings, sessionSecrets);

        using var document = await TryReadJsonAsync(response, cancellationToken);
        if (document?.RootElement is not { ValueKind: JsonValueKind.Object } root
            || !root.TryGetProperty("Items", out var items)
            || items.ValueKind != JsonValueKind.Array)
            return NotJellyfin<IReadOnlyList<MediaIdentifier>>();

        foreach (var series in items.EnumerateArray())
        {
            if (series.ValueKind == JsonValueKind.Object
                && IdProperty(series, "Id") is { } id
                && Guid.TryParse(id, out var listedId)
                && listedId == seriesId
                && string.Equals(StringValue(series, "Type"), "Series", StringComparison.Ordinal))
                return IntegrationCallResult<IReadOnlyList<MediaIdentifier>>.Succeeded(Identifiers(series, MediaWorkKind.Series).AsReadOnly());
        }

        return IntegrationCallResult<IReadOnlyList<MediaIdentifier>>.Succeeded([]);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// A caller's mistake, thrown before anything is sent: no list, an empty one, more than
    /// <see cref="IMediaLibraryFileLookup.MaximumPaths"/>, or a path Jellyfin could never report — blank, not absolute,
    /// holding a control character or a <c>.</c> or <c>..</c> folder (<see cref="NormalizedPath.TryParse"/>), or longer than
    /// a path column holds.
    /// </summary>
    private static void ValidateLookupPaths(IReadOnlyList<string> servicePaths)
    {
        ArgumentNullException.ThrowIfNull(servicePaths);
        if (servicePaths.Count == 0)
            throw new ArgumentException("Name at least one path.", nameof(servicePaths));
        if (servicePaths.Count > IMediaLibraryFileLookup.MaximumPaths)
            throw new ArgumentException(
                string.Create(CultureInfo.InvariantCulture, $"At most {IMediaLibraryFileLookup.MaximumPaths} paths are asked about at once."), nameof(servicePaths));
        foreach (var path in servicePaths)
        {
            if (path is null || path.Length > PathMaximumLength || !NormalizedPath.TryParse(path, out _, out _))
                throw new ArgumentException(LookupPathError, nameof(servicePaths));
        }
    }

    /// <summary>
    /// MAM-26 — Jellyfin's update type for <paramref name="change"/>, as its own folder watcher names a file that appeared or
    /// went (its <c>MediaUpdateInfoPathDto</c>); a change that is no member is a caller's mistake, thrown before anything is
    /// sent. Never <c>Modified</c>: the app tells only of a file that appeared or went.
    /// </summary>
    private static string UpdateTypeOf(LibraryFileChange change) => change switch
    {
        LibraryFileChange.Created => "Created",
        LibraryFileChange.Deleted => "Deleted",
        _ => throw new ArgumentOutOfRangeException(nameof(change), change, "Name a change: a file created or deleted."),
    };

    /// <summary>The request's body, compact, every path exactly as given, in the order given, each of
    /// <paramref name="updateType"/>.</summary>
    private static byte[] ScanBody(IReadOnlyList<string> servicePaths, string updateType)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteStartArray("Updates");
            foreach (var path in servicePaths)
            {
                writer.WriteStartObject();
                writer.WriteString("Path", path);
                writer.WriteString("UpdateType", updateType);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return stream.ToArray();
    }

    /// <summary>The id Jellyfin gives the item of the type <paramref name="typeName"/> at <paramref name="path"/> (see the
    /// head of this file). MD5 is Jellyfin's formula here, not a protection of anything.</summary>
    private static Guid ComputedItemId(string typeName, string path) =>
        new(MD5.HashData(Encoding.Unicode.GetBytes(typeName + path)));

    /// <summary>An episode's series item id, only when it is one Jellyfin gives — a Guid, since it goes into the series'
    /// query (MAM-8's rule) —; <c>null</c> otherwise, as when Jellyfin gives none (an episode of no series).</summary>
    private static string? SeriesItemId(JsonElement item) =>
        IdProperty(item, "SeriesId") is { } seriesId && Guid.TryParse(seriesId, out _) ? seriesId : null;

    /// <summary>The item's own running time in ticks; 0 when absent, as on an item Jellyfin hasn't read yet.</summary>
    private static long RunTimeTicks(JsonElement item) =>
        item.TryGetProperty("RunTimeTicks", out var ticks) && ticks.ValueKind == JsonValueKind.Number && ticks.TryGetInt64(out var value)
            ? value
            : 0;
}
