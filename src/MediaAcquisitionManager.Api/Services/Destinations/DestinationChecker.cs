using MediaAcquisitionManager.Api.Services.Files;
using MediaAcquisitionManager.Api.Services.Integrations;
using MediaAcquisitionManager.Core.Abstractions;
using MediaAcquisitionManager.Core.Abstractions.Downloads;
using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Downloads;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Paths;
using MediaAcquisitionManager.Core.Texts;
using MediaAcquisitionManager.Infrastructure.Integrations;
using MediaAcquisitionManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Services.Destinations;

/// <inheritdoc cref="IDestinationChecker"/>
/// <remarks>
/// <para>
/// Whether a type is a download client is asked of the adapter registry (<c>Find&lt;IDownloadClient&gt;</c>,
/// ADR 0005), never decided by a product name; the stored secrets are opened by
/// <see cref="IIntegrationSettingsFactory"/>, the one place that does.
/// </para>
/// <para>
/// MAM-30 — and whether its destinations are folders is asked of its capabilities the same way
/// (<see cref="DownloadClientCapabilities.DestinationKind"/>). A folder client's proposals are made of the library folders,
/// so they are read before it is asked; a category client is asked first, as before, and needs no folders when it
/// can't be.
/// </para>
/// </remarks>
public sealed class DestinationChecker(
    ApplicationDbContext db,
    IIntegrationAdapterRegistry adapters,
    IIntegrationSettingsFactory settingsFactory,
    ILogger<DestinationChecker> logger) : IDestinationChecker
{
    /// <summary>
    /// MAM-11 — <see cref="DestinationPathCheck"/>'s "sync a library" would send the owner to a sync that
    /// changes nothing when folders are stored and none of them maps into the app: the media library
    /// integration's path mappings are what to fix then.
    /// </summary>
    private static readonly Message LibraryFoldersUnmappedError = Message.Of("destinations.libraries-unmapped");

    /// <summary>A destination whose client was removed after the destination was read (the database
    /// removes its destinations with it).</summary>
    private static readonly Message ClientGoneError = Message.Of("destinations.client.gone");

    public async Task<IReadOnlyDictionary<Guid, DestinationPathCheckResult>> CheckAsync(
        IReadOnlyCollection<DestinationMappingEntity> destinations, CancellationToken cancellationToken = default)
    {
        if (destinations.Count == 0)
            return new Dictionary<Guid, DestinationPathCheckResult>();

        // Every database read happens before the clients are asked: the context is not safe to share
        // between the concurrent calls below (ReleaseSources' rule).
        var clientIds = destinations.Select(destination => destination.DownloadClientConnectionId).Distinct().ToList();
        var clients = await db.IntegrationConnections
            .AsNoTracking()
            .Include(client => client.PathMappings)
            .Where(client => clientIds.Contains(client.Id))
            .ToListAsync(cancellationToken);
        var folders = await LibraryFolderReader.LoadAsync(db, cancellationToken);
        // MAM-30 — a folder client checks each of its destinations' folders as itself (D3).
        var storedKeys = destinations
            .GroupBy(destination => destination.DownloadClientConnectionId)
            .ToDictionary(group => group.Key, group => (IReadOnlyCollection<string>)[.. group.Select(destination => destination.ClientDestinationKey)]);

        // MAM-11 — each client once, however many destinations on the page it holds, and all of them at
        // once: the slowest client sets the wait, not their sum, and one that fails can't hold the others
        // back (the release search's rule).
        var answers = await Task.WhenAll(clients.Select(client => AskAsync(client, folders, storedKeys[client.Id], cancellationToken)));
        var asked = clients
            .Select((client, index) => (Client: client, Answer: answers[index]))
            .ToDictionary(pair => pair.Client.Id);

        var checks = new Dictionary<Guid, DestinationPathCheckResult>();
        foreach (var destination in destinations)
        {
            checks[destination.Id] = asked.TryGetValue(destination.DownloadClientConnectionId, out var client)
                ? Check(destination.ClientDestinationKey, client.Client, client.Answer, folders)
                : DestinationPathCheckResult.ClientUnavailable(ClientGoneError);
        }

        return checks;
    }

    public async Task<ClientDestinationsResult> ListAsync(
        IntegrationConnectionEntity client, IReadOnlyCollection<string> storedKeys, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(storedKeys);
        // MAM-30 — a folder client is proposed the library folders, so they are read before it is asked; a category
        // client's are read after it answered: one that couldn't be asked needs none.
        var kind = KindOf(client);
        var proposals = kind == DownloadDestinationKind.Folder ? await LibraryFolderReader.LoadAsync(db, cancellationToken) : null;
        var answer = await AskAsync(client, proposals, storedKeys, cancellationToken);
        if (answer.Status != ClientDestinationsStatus.Listed)
            return ClientDestinationsResult.NotListed(answer.Status, answer.Error!, kind);

        var folders = proposals ?? await LibraryFolderReader.LoadAsync(db, cancellationToken);
        var pairs = PairsOf(client);
        List<CheckedClientDestination> listed =
        [
            .. answer.Destinations.Select(destination =>
                new CheckedClientDestination(destination, Explain(DestinationPathCheck.Check(destination, pairs, folders.Mapped), folders))),
        ];
        if (kind == DownloadDestinationKind.Folder)
        {
            // MAM-30 — a stored folder the client didn't list — one that isn't a full path, never proposed — is listed
            // all the same, with why the app can't use it: the Edit dialog shows its own folder, never a missing one, and
            // a send finds why it can't go there (D3).
            foreach (var key in storedKeys.Where(key => !listed.Any(entry => ClientDestinationsResult.IsSameFolder(entry.Destination.Key, key))))
            {
                var check = DestinationPathCheck.Check(key, answer.Destinations, pairs, folders.Mapped, DownloadDestinationKind.Folder, client.Name);
                listed.Add(new CheckedClientDestination(new ClientDestination(key, key, check.ServicePath, Problem: check.Problem), check));
            }
        }

        return ClientDestinationsResult.Listed(kind, listed, answer.Unplaced);
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// What one client says about its destinations. Never throws but for the caller's cancellation, and
    /// never touches the database, so several run at once. MAM-30 — a folder client is proposed what
    /// <see cref="Propose"/> makes of <paramref name="folders"/>, read beforehand, and <paramref name="storedKeys"/>; a
    /// folder it lists that no destination could hold comes back with why (<see cref="Storable"/>).
    /// </summary>
    private async Task<ClientAnswer> AskAsync(
        IntegrationConnectionEntity client, LibraryFolders? folders, IReadOnlyCollection<string> storedKeys, CancellationToken cancellationToken)
    {
        // Every client starts on its own: one whose call runs synchronously up to its first wait would
        // otherwise hold the others back until it is done (ReleaseSources).
        await Task.Yield();

        // MAM-11 — the order a save refuses in (decision 3): a switched-off client is left alone, the way
        // a sync and a release search leave it (MAM-7's switch); then whether this build can use its type;
        // then whether its stored secrets can be read; then the call.
        if (!client.IsEnabled)
            return NotAsked(client, ClientDestinationsStatus.ClientDisabled, DisabledError(client));
        if (adapters.Find<IDownloadClient>(client.AdapterType) is not { } downloadClient)
            return NotAsked(client, ClientDestinationsStatus.TypeUnavailable, TypeUnavailableError(client));

        var settingsResult = settingsFactory.Create(client);
        if (settingsResult.Settings is not { } settings)
            return NotAsked(client, ClientDestinationsStatus.SecretUnreadable, settingsResult.Error!);

        var (proposed, unplaced) = downloadClient.Capabilities.DestinationKind == DownloadDestinationKind.Folder
            ? Propose(client, folders!, storedKeys)
            : ([], []);
        try
        {
            var listed = await downloadClient.ListDestinationsAsync(settings, proposed, cancellationToken);
            if (!listed.IsSuccess)
                return Failed(client, listed.Status, listed.Error!, settings);

            logger.LogInformation(
                "Destination listing of integration {IntegrationId} ({AdapterType}) found {Count} destinations.",
                client.Id,
                client.AdapterType,
                listed.Value!.Count);
            // MAM-30 — a folder no destination could hold comes back with why (D1).
            IReadOnlyList<ClientDestination> destinations = downloadClient.Capabilities.DestinationKind == DownloadDestinationKind.Folder
                ? [.. listed.Value.Select(entry => Storable(client, entry))]
                : listed.Value;
            return new ClientAnswer(ClientDestinationsStatus.Listed, destinations, null, unplaced);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // An adapter never throws on an expected failure; this is a bug meeting an answer nobody
            // foresaw. It costs this client its answer, never the others': escaping, it would fault the
            // Task.WhenAll above and answer 500 for the whole page. Error, with the type and the stack,
            // never the message: a parser's message can quote what the client sent (ReleaseSources).
            logger.LogError(
                "Destination listing of integration {IntegrationId} ({AdapterType}) threw {ExceptionType} {StackTrace}",
                client.Id,
                client.AdapterType,
                exception.GetType().FullName,
                exception.StackTrace);
            return Failed(client, IntegrationCallStatus.UnexpectedResponse, IntegrationAdapterMessages.UnreadableAnswer(client.Name), settings);
        }
    }

    private ClientAnswer NotAsked(IntegrationConnectionEntity client, ClientDestinationsStatus status, Message error)
    {
        logger.LogInformation(
            "Destination listing of integration {IntegrationId} ({AdapterType}) did not ask it: {Status}.", client.Id, client.AdapterType, status);
        return new ClientAnswer(status, [], error, []);
    }

    private ClientAnswer Failed(IntegrationConnectionEntity client, IntegrationCallStatus status, Message error, IntegrationConnectionSettings settings)
    {
        // Information: the status only — never the address or the sentence (the connection test's rule).
        logger.LogInformation(
            "Destination listing of integration {IntegrationId} ({AdapterType}) failed: {Status}.", client.Id, client.AdapterType, status);
        // Masked again after the adapter (ADR 0007's second pass), which also covers an adapter that isn't
        // HTTP.
        return new ClientAnswer(ClientDestinationsStatus.CallFailed, [], error.Redacted(settings.Secrets), []);
    }

    /// <summary>One stored destination's check against what its client answered — MAM-30, a folder by the folder.</summary>
    private DestinationPathCheckResult Check(string clientDestinationKey, IntegrationConnectionEntity client, ClientAnswer answer, LibraryFolders folders) =>
        answer.Status switch
        {
            ClientDestinationsStatus.Listed => Explain(
                DestinationPathCheck.Check(
                    clientDestinationKey,
                    answer.Destinations,
                    PairsOf(client),
                    folders.Mapped,
                    KindOf(client) ?? DownloadDestinationKind.Category,
                    client.Name),
                folders),
            ClientDestinationsStatus.ClientDisabled => DestinationPathCheckResult.ClientDisabled(answer.Error!),
            _ => DestinationPathCheckResult.ClientUnavailable(answer.Error!),
        };

    /// <summary>
    /// MAM-30 — what a folder client is proposed (ADR 0039): each library folder inside the app mapped back through
    /// <paramref name="client"/>'s path mappings, and each of <paramref name="storedKeys"/> that is a full path, each folder
    /// once however many libraries or destinations name it; and each library folder that gives the client no folder,
    /// with why. A stored key that isn't a full path is never proposed: the client would refuse the whole listing for it,
    /// and it is that destination's problem alone (D3).
    /// </summary>
    private static (IReadOnlyCollection<string> Proposed, IReadOnlyList<UnplacedLibraryFolder> Unplaced) Propose(
        IntegrationConnectionEntity client, LibraryFolders folders, IReadOnlyCollection<string> storedKeys)
    {
        var pairs = PairsOf(client);
        var proposed = new List<NormalizedPath>();
        var unplaced = new List<UnplacedLibraryFolder>();
        foreach (var folder in folders.Mapped)
        {
            var back = PathMapper.MapBack(folder.AppPath, pairs);
            if (!back.HasServicePath)
                unplaced.Add(new UnplacedLibraryFolder(folder.LibraryName, folder.AppPath, NoFolderProblem(client, folder, back, pairs)));
            else if (back.ServicePath!.Length > DestinationRules.ClientDestinationKeyMaximumLength)
                unplaced.Add(new UnplacedLibraryFolder(folder.LibraryName, folder.AppPath, TooLongProblem(client, folder.LibraryName, back.ServicePath)));
            else
                Add(proposed, back.ServicePath);
        }

        foreach (var folder in folders.Unmapped)
        {
            unplaced.Add(new UnplacedLibraryFolder(
                folder.LibraryName,
                folder.ServicePath,
                Message.Of(
                    "destinations.unplaced.not-in-app",
                    ("library", folder.LibraryName),
                    ("path", folder.ServicePath),
                    ("client", client.Name),
                    ("integration", folder.IntegrationName))));
        }

        foreach (var key in storedKeys)
            Add(proposed, key);

        // By library name, whichever way each went: the order the dialog lists the libraries in.
        return ([.. proposed.Select(path => path.ToString())], [.. unplaced.OrderBy(folder => folder.LibraryName, StringComparer.Ordinal)]);

        static void Add(List<NormalizedPath> proposed, string folder)
        {
            if (NormalizedPath.TryParse(folder, out var path, out _) && !proposed.Any(known => known.IsSameAs(path)))
                proposed.Add(path);
        }
    }

    /// <summary>MAM-30 — why the client's path mappings give the library folder no folder of the client's, and how to fix
    /// it.</summary>
    private static Message NoFolderProblem(
        IntegrationConnectionEntity client, LibraryFolder folder, ServicePathMapResult back, IReadOnlyList<PathMappingPair> pairs)
    {
        switch (back.Status)
        {
            case PathMapStatus.NotCovered:
                return Message.Of(
                    "destinations.unplaced.not-covered", ("client", client.Name), ("library", folder.LibraryName), ("folder", folder.AppPath));
            case PathMapStatus.Ambiguous:
                // The places each mapping alone leads back from, collapsed the way the mapper collapses agreeing ones.
                var places = new List<NormalizedPath>();
                foreach (var pair in pairs)
                {
                    if (PathMapper.MapBack(folder.AppPath, [pair]) is { HasServicePath: true, ServicePath: { } place }
                        && NormalizedPath.TryParse(place, out var path, out _)
                        && !places.Any(known => known.IsSameAs(path)))
                        places.Add(path);
                }

                return Message.Of(
                    "destinations.unplaced.ambiguous",
                    ("client", client.Name),
                    ("library", folder.LibraryName),
                    ("folder", folder.AppPath),
                    ("first", places[0].ToString()),
                    ("second", places[1].ToString()));
            default:
                // A folder name the client's style can't write: the mapper's own sentence says which.
                return Message.Of("destinations.unplaced.unwritable", ("client", client.Name), ("library", folder.LibraryName), ("reason", back.Reason!));
        }
    }

    /// <summary>MAM-30 — a library folder whose folder in the client is longer than a destination's key can hold
    /// (<see cref="DestinationRules.ClientDestinationKeyMaximumLength"/>).</summary>
    private static Message TooLongProblem(IntegrationConnectionEntity client, string libraryName, string folder) =>
        Message.Of(
            "destinations.unplaced.too-long",
            ("library", libraryName),
            ("client", client.Name),
            ("folder", folder),
            ("count", DestinationRules.ClientDestinationKeyMaximumLength));

    /// <summary>
    /// MAM-30 — a folder the client listed as <paramref name="entry"/>, or, when it is longer than a destination's key can
    /// hold, that folder with why (D1). Only the client's own download folder can be: the app never proposes a longer one,
    /// and no stored key is. Listed with its sentence and never offered, as a folder the client can't see is (D5): a save
    /// of it would be refused for its length.
    /// </summary>
    private static ClientDestination Storable(IntegrationConnectionEntity client, ClientDestination entry) =>
        entry.Key.Length > DestinationRules.ClientDestinationKeyMaximumLength
            ? entry with
            {
                Problem = Message.Of(
                    "destinations.folder-too-long",
                    ("folder", entry.Key),
                    ("client", client.Name),
                    ("count", DestinationRules.ClientDestinationKeyMaximumLength)),
            }
            : entry;

    /// <summary>MAM-30 — what <paramref name="client"/>'s destinations are, by its type; <c>null</c> for a type this build
    /// doesn't register.</summary>
    private DownloadDestinationKind? KindOf(IntegrationConnectionEntity client) =>
        adapters.Find<IDownloadClient>(client.AdapterType)?.Capabilities.DestinationKind;

    // The check's own sentence, except for "libraries unknown" while folders are stored that none of their
    // mappings bring into the app: syncing again wouldn't change that.
    private static DestinationPathCheckResult Explain(DestinationPathCheckResult result, LibraryFolders folders) =>
        result.Status == DestinationPathStatus.LibrariesUnknown && folders.StoredCount > 0
            ? result with { Problem = LibraryFoldersUnmappedError }
            : result;

    private static List<PathMappingPair> PairsOf(IntegrationConnectionEntity client) =>
        [.. client.PathMappings.OrderBy(mapping => mapping.Position).Select(mapping => new PathMappingPair(mapping.ServicePrefix, mapping.AppPrefix))];

    /// <summary>MAM-47 — shared: the Destinations page, a destination's save and a send that stores it on its job say it
    /// alike (<c>destinations.client.*</c>).</summary>
    private static Message DisabledError(IntegrationConnectionEntity client) =>
        Message.Of("destinations.client.switched-off", ("client", client.Name));

    private static Message TypeUnavailableError(IntegrationConnectionEntity client) =>
        Message.Of("destinations.client.type-unavailable", ("type", client.AdapterType));

    /// <summary>What one client said, before any destination is checked against it — MAM-30, with the library folders a
    /// folder client was proposed none for. Its <c>ToString</c> prints the lists as their type names; the error is
    /// masked.</summary>
    private sealed record ClientAnswer(
        ClientDestinationsStatus Status, IReadOnlyList<ClientDestination> Destinations, Message? Error, IReadOnlyList<UnplacedLibraryFolder> Unplaced);
}
