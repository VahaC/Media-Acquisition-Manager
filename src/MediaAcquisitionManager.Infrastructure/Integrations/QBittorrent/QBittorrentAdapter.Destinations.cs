using System.Text.Json;
using MediaAcquisitionManager.Core.Abstractions.Downloads;
using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Downloads;

namespace MediaAcquisitionManager.Infrastructure.Integrations.QBittorrent;

// MAM-11 — the download client contract's destinations: qBittorrent's categories, each with the folder
// it saves to (QBittorrentCategoryPaths). Every request is the sign-in or a GET.
internal sealed partial class QBittorrentAdapter
{
    private const string NotCategoriesError = "The service at this address answered, but not with qBittorrent's categories.";

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// <c>GET /api/v2/app/version</c> first, because the version gate and the folder rules depend on it;
    /// then <c>GET /api/v2/torrents/categories</c>; and <c>GET /api/v2/app/defaultSavePath</c> only when a
    /// category's folder sits under the default save path.
    /// </para>
    /// <para>
    /// Never <c>app/preferences</c>, although it holds the default save path too: it also carries the Web
    /// UI's API key (5.2) and the proxy and mail passwords, none of which this app should ever receive.
    /// </para>
    /// </remarks>
    public Task<IntegrationCallResult<IReadOnlyList<ClientDestination>>> ListDestinationsAsync(
        IntegrationConnectionSettings settings, CancellationToken cancellationToken = default) =>
        CallAsync<IReadOnlyList<ClientDestination>>(
            settings, (client, sessionSecrets, token) => ReadDestinationsAsync(client, settings, sessionSecrets, token), cancellationToken);

    private async Task<IntegrationCallResult<IReadOnlyList<ClientDestination>>> ReadDestinationsAsync(
        HttpClient client, IntegrationConnectionSettings settings, ICollection<string> sessionSecrets, CancellationToken cancellationToken)
    {
        var (call, signInFailure) = await OpenSessionAsync(client, settings, sessionSecrets, cancellationToken);
        if (signInFailure is not null)
            return Failed<IReadOnlyList<ClientDestination>>(signInFailure);

        // Read here as the connection test reads it, so an old qBittorrent is refused before its
        // categories are read (MinimumVersion).
        var (versionAnswer, versionFailure) = await GetAsync(client, settings, call!, "api/v2/app/version", sessionSecrets, cancellationToken);
        if (versionFailure is not null)
            return Failed<IReadOnlyList<ClientDestination>>(versionFailure);

        string versionBody;
        using (versionAnswer)
        {
            versionBody = await ReadTextAsync(versionAnswer!, cancellationToken);
        }

        if (ReportedVersion(versionBody) is not { } reported)
            return IntegrationCallResult<IReadOnlyList<ClientDestination>>.Failed(IntegrationCallStatus.UnexpectedResponse, NotAVersionError);
        if (UnsupportedVersionError(reported) is { } unsupported)
            return IntegrationCallResult<IReadOnlyList<ClientDestination>>.Failed(IntegrationCallStatus.UnsupportedVersion, unsupported);
        var version = ComparableVersion(reported)!;

        var (categoriesAnswer, categoriesFailure) = await GetAsync(client, settings, call!, "api/v2/torrents/categories", sessionSecrets, cancellationToken);
        if (categoriesFailure is not null)
            return Failed<IReadOnlyList<ClientDestination>>(categoriesFailure);

        (List<string> Names, Dictionary<string, string?> SavePaths)? categories;
        using (categoriesAnswer)
        using (var document = await TryReadJsonAsync(categoriesAnswer!, cancellationToken))
        {
            categories = document is null ? null : ReadCategories(document.RootElement);
        }

        if (categories is not { } read)
            return IntegrationCallResult<IReadOnlyList<ClientDestination>>.Failed(IntegrationCallStatus.UnexpectedResponse, NotCategoriesError);

        var secrets = CallSecrets(settings, sessionSecrets);
        var kept = new List<(string Key, string DisplayName)>();
        foreach (var name in read.Names)
        {
            // MAM-11 — a category whose name the app can't keep is left out rather than failing the list,
            // so every other category stays usable: blank, longer than a destination's key holds, or with
            // a control character, it could be neither stored nor sent back intact (DestinationRules); and
            // one that is nothing but invisible characters has nothing to show.
            if (DestinationRules.ValidateClientDestinationKey(name) is not null)
                continue;
            var displayName = RemoteText.Clean(name, DestinationRules.ClientDestinationKeyMaximumLength, secrets);
            if (displayName.Length > 0)
                kept.Add((name, displayName));
        }

        string? defaultSavePath = null;
        if (QBittorrentCategoryPaths.NeedsDefaultSavePath(version, kept.Select(category => category.Key), read.SavePaths))
        {
            var (answer, failure) = await GetAsync(client, settings, call!, "api/v2/app/defaultSavePath", sessionSecrets, cancellationToken);
            if (failure is not null)
                return Failed<IReadOnlyList<ClientDestination>>(failure);

            // Plain text, taken as qBittorrent sent it: a folder name may end in a space.
            using (answer)
            {
                defaultSavePath = await ReadTextAsync(answer!, cancellationToken);
            }
        }

        return IntegrationCallResult<IReadOnlyList<ClientDestination>>.Succeeded(
        [
            .. kept.Select(category => new ClientDestination(
                category.Key,
                category.DisplayName,
                QBittorrentCategoryPaths.FolderOf(version, category.Key, read.SavePaths, defaultSavePath))),
        ]);
    }

    /// <summary>
    /// Every category's name in qBittorrent's order, and each one's stored <c>savePath</c> (<c>null</c> when
    /// its entry gives none as text) — or <c>null</c> when the body is not a JSON object of objects, which is
    /// how every version from 4.1.4 on answers.
    /// </summary>
    /// <remarks>
    /// The name is the entry's key: qBittorrent writes each category under its name and repeats the name
    /// inside (<c>categoriesAction</c>), and it is the key a submission sends. Read as an allowlist: the
    /// folder for unfinished downloads (<c>download_path</c>) and the seeding limits go no further — a
    /// finished download is moved to the save path, which is what lands in a library. A second entry under
    /// a name already read is dropped.
    /// </remarks>
    private static (List<string> Names, Dictionary<string, string?> SavePaths)? ReadCategories(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
            return null;

        var names = new List<string>();
        var savePaths = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var entry in root.EnumerateObject())
        {
            // qBittorrent never lists a category as anything but an object; an answer that does is not one
            // to read the rest of by.
            if (entry.Value.ValueKind != JsonValueKind.Object)
                return null;
            if (!savePaths.TryAdd(entry.Name, SavePath(entry.Value)))
                continue;
            names.Add(entry.Name);
        }

        return (names, savePaths);
    }

    /// <summary>The stored <c>savePath</c> exactly as sent — never trimmed, because it names a folder — or
    /// <c>null</c> when it isn't text.</summary>
    private static string? SavePath(JsonElement category) =>
        category.TryGetProperty("savePath", out var savePath) && savePath.ValueKind == JsonValueKind.String
            ? savePath.GetString()
            : null;

    /// <summary>A failure a sign-in or a read answered, as a kind-contract call's failure.</summary>
    private static IntegrationCallResult<T> Failed<T>(ConnectionTestResult failure) where T : class =>
        IntegrationCallResult<T>.Failed(failure.Status, failure.Message);
}
