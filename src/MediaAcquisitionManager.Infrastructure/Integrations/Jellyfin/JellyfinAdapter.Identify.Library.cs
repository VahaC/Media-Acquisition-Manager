using System.Net;
using System.Text.Json;
using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Abstractions.Library;
using MediaAcquisitionManager.Core.Texts;

namespace MediaAcquisitionManager.Infrastructure.Integrations.Jellyfin;

// MAM-34 — which library holds an item, and whether identifying in it could change files beside the media (decision 5 of
// ADR 0026: files and folders are never touched). Identify itself writes the item's metadata and images under Jellyfin's
// own data directory — recorded live: the folder listings of three identified items were identical before and after —, but
// a library set to save beside the media (an NFO file, artwork and metadata, trickplay, downloaded subtitles) would put
// files into the owner's media folders as soon as the item is refreshed. The options are read before any search, and
// anything the app can't read is taken as unsafe.
internal sealed partial class JellyfinAdapter
{
    // MAM-47 (ADR 0042) — what a library saves beside the media is a list of labels the template joins ("NFO files",
    // "artwork and metadata and trickplay images"), and why the app can't tell is a clause the template puts in its
    // parentheses: each a key of jellyfin.json, never English joined here.
    private static readonly Message LibraryNotFoundReason = Message.Of("jellyfin.identify.library.why.no-library-clause");

    private static readonly Message LibraryOptionsMissingReason = Message.Of("jellyfin.identify.library.why.options-missing-clause");

    /// <summary>
    /// <c>GET /Library/VirtualFolders</c> and the verdict for the libraries that hold <paramref name="itemPath"/>. The same
    /// answer <see cref="ListLocationsAsync"/> reads, with the same statuses for a refused key or a missing route.
    /// </summary>
    private async Task<IntegrationCallResult<LibraryVerdict>> ReadLibraryOptionsAsync(
        HttpClient client, IntegrationConnectionSettings settings, string? itemPath, ICollection<string> sessionSecrets, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(settings, "Library/VirtualFolders");
        using var response = await client.SendAsync(request, cancellationToken);
        if (response.StatusCode != HttpStatusCode.OK)
            return DescribeLibraryCallStatus<LibraryVerdict>(response, NotFoundError("/Library/VirtualFolders"), settings, sessionSecrets);

        using var document = await TryReadJsonAsync(response, cancellationToken);
        return document?.RootElement is { ValueKind: JsonValueKind.Array } folders
            ? IntegrationCallResult<LibraryVerdict>.Succeeded(Judge(folders, itemPath, CallSecrets(settings, sessionSecrets)))
            : NotJellyfin<LibraryVerdict>();
    }

    /// <summary>
    /// The verdict on every library whose location holds the item's path. A location holds the path when it equals it or is
    /// a folder above it (compared in any letter case, with <c>\</c> as <c>/</c> and no trailing slash): a false match only
    /// adds a library to check, which can only refuse more. The item's path is the only thing that says which library holds
    /// it — the item carries no library id —, so a path none of them holds, or none at all, is unknown. A path under
    /// several libraries needs all of them safe.
    /// </summary>
    /// <remarks>
    /// Per library, anything that would put a file beside the media makes it unsafe: <c>SaveLocalMetadata</c> true,
    /// <c>MetadataSavers</c> with the NFO saver, <c>SaveTrickplayWithMedia</c> true, and downloaded subtitles (some download
    /// languages and <c>SaveSubtitlesWithMedia</c> not false). A setting that must be a yes or a no and isn't one, or is
    /// missing, can't be told, and the library's options being missing altogether likewise. Unsafe beats unknown beats safe.
    /// Age ratings are not read: they don't matter to the owner (ADR 0026). On a safe verdict the library's metadata language
    /// and country come with it, for the search (see <see cref="SearchAsync"/>): they are not a safety fact, so a missing or
    /// odd one is simply none.
    /// </remarks>
    private static LibraryVerdict Judge(JsonElement folders, string? itemPath, string?[] secrets)
    {
        if (string.IsNullOrWhiteSpace(itemPath))
            return Unknown(LibraryNotFoundReason);

        var path = NormalisePath(itemPath);
        var matched = folders.EnumerateArray()
            .Where(folder => folder.ValueKind == JsonValueKind.Object && LocationsOf(folder).Any(location => Holds(NormalisePath(location), path)))
            .ToList();
        if (matched.Count == 0)
            return Unknown(LibraryNotFoundReason);

        Message? writes = null;
        Message? unknown = null;
        string? language = null;
        string? country = null;
        foreach (var folder in matched)
        {
            var name = VendorProperty(folder, "Name", RemoteValueMaximumLength, secrets);
            if (!folder.TryGetProperty("LibraryOptions", out var options) || options.ValueKind != JsonValueKind.Object)
            {
                unknown ??= LibraryOptionsMissingReason;
                continue;
            }

            var (whats, why) = SavesBesideMedia(options);
            if (whats.Count > 0)
            {
                var saved = new ListValue(whats);
                writes ??= name.Text.Length > 0
                    ? Message.Of("jellyfin.identify.library.writes-files", ("library", name), ("saved", saved))
                    : Message.Of("jellyfin.identify.library.writes-files-unnamed", ("saved", saved));
            }

            if (why is not null)
                unknown ??= why;

            if (language is null && StringValue(options, "PreferredMetadataLanguage") is { } preferred && !string.IsNullOrWhiteSpace(preferred))
            {
                language = preferred.Trim();
                country = StringValue(options, "MetadataCountryCode") is { } code && !string.IsNullOrWhiteSpace(code) ? code.Trim() : null;
            }
        }

        if (writes is not null)
            return new LibraryVerdict(LibraryIdentifyRefusal.LibraryWritesFiles, writes, null, null);
        if (unknown is not null)
            return Unknown(unknown);
        return new LibraryVerdict(null, null, language, country);
    }

    /// <summary>What one library's options save beside the media, and the reason (or none) the app can't tell something.</summary>
    private static (List<MessageValue> Whats, Message? Unknown) SavesBesideMedia(JsonElement options)
    {
        List<MessageValue> whats = [];
        Message? unknown = null;

        if (!options.TryGetProperty("SaveLocalMetadata", out var saveLocal) || saveLocal.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            unknown ??= Message.Of("jellyfin.identify.library.why.save-local-metadata-clause");
        else if (saveLocal.ValueKind == JsonValueKind.True)
            whats.Add(Message.Of("jellyfin.identify.library.saved.artwork-and-metadata-clause"));

        // A missing or null list is none; a list holding the NFO saver writes NFO files; anything else is no list.
        if (options.TryGetProperty("MetadataSavers", out var savers) && savers.ValueKind != JsonValueKind.Null)
        {
            if (savers.ValueKind != JsonValueKind.Array)
                unknown ??= Message.Of("jellyfin.identify.library.why.metadata-savers-clause");
            else if (savers.EnumerateArray().Any(saver => saver.ValueKind == JsonValueKind.String && string.Equals(saver.GetString(), "Nfo", StringComparison.OrdinalIgnoreCase)))
                whats.Add(Message.Of("jellyfin.identify.library.saved.nfo-files-clause"));
        }

        if (!options.TryGetProperty("SaveTrickplayWithMedia", out var trickplay) || trickplay.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            unknown ??= Message.Of("jellyfin.identify.library.why.save-trickplay-clause");
        else if (trickplay.ValueKind == JsonValueKind.True)
            whats.Add(Message.Of("jellyfin.identify.library.saved.trickplay-images-clause"));

        // Downloaded subtitles land beside the media unless the library keeps them in its own folder; with no language to
        // download there is nothing to save, whichever way SaveSubtitlesWithMedia is set.
        if (options.TryGetProperty("SubtitleDownloadLanguages", out var languages) && languages.ValueKind != JsonValueKind.Null)
        {
            if (languages.ValueKind != JsonValueKind.Array)
                unknown ??= Message.Of("jellyfin.identify.library.why.subtitle-languages-clause");
            else if (languages.GetArrayLength() > 0
                && !(options.TryGetProperty("SaveSubtitlesWithMedia", out var withMedia) && withMedia.ValueKind == JsonValueKind.False))
                whats.Add(Message.Of("jellyfin.identify.library.saved.downloaded-subtitles-clause"));
        }

        return (whats, unknown);
    }

    /// <summary>The folder's locations: its root folders as Jellyfin sees them, the non-blank ones.</summary>
    private static IEnumerable<string> LocationsOf(JsonElement folder) =>
        folder.TryGetProperty("Locations", out var locations) && locations.ValueKind == JsonValueKind.Array
            ? locations.EnumerateArray()
                .Where(location => location.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(location.GetString()))
                .Select(location => location.GetString()!)
            : [];

    private static string NormalisePath(string path) => path.Replace('\\', '/').Trim().TrimEnd('/');

    /// <summary>Whether <paramref name="location"/> is <paramref name="path"/> or a folder above it, both normalised.</summary>
    private static bool Holds(string location, string path) =>
        string.Equals(location, path, StringComparison.OrdinalIgnoreCase) || path.StartsWith(location + "/", StringComparison.OrdinalIgnoreCase);

    private static LibraryVerdict Unknown(Message reason) =>
        new(LibraryIdentifyRefusal.LibraryUnknown, Message.Of("jellyfin.identify.library.unknown", ("why", reason)), null, null);

    /// <summary>What the libraries holding an item say: <c>Refusal</c> null means safe, then with the language and country the
    /// search carries (each <c>null</c> when none); otherwise the refusal and its sentence.</summary>
    private sealed record LibraryVerdict(LibraryIdentifyRefusal? Refusal, Message? Sentence, string? Language, string? Country);
}
