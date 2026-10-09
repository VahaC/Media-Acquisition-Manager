using MediaAcquisitionManager.Api.Services.Files;
using MediaAcquisitionManager.Api.Services.Integrations;
using MediaAcquisitionManager.Core.Abstractions;
using MediaAcquisitionManager.Core.Abstractions.Downloads;
using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Downloads;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Paths;
using MediaAcquisitionManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Services.Destinations;

/// <inheritdoc cref="IDestinationChecker"/>
/// <remarks>
/// Whether a type is a download client is asked of the adapter registry (<c>Find&lt;IDownloadClient&gt;</c>,
/// ADR 0005), never decided by a product name; the stored secrets are opened by
/// <see cref="IIntegrationSettingsFactory"/>, the one place that does.
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
    private const string LibraryFoldersUnmappedError =
        "None of the media library folders maps to a folder of the app through its integration's path mappings. Check those mappings under Integrations, then look again.";

    /// <summary>A destination whose client was removed after the destination was read (the database
    /// removes its destinations with it).</summary>
    private const string ClientGoneError = "The download client of this destination no longer exists.";

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

        // MAM-11 — each client once, however many destinations on the page it holds, and all of them at
        // once: the slowest client sets the wait, not their sum, and one that fails can't hold the others
        // back (the release search's rule).
        var answers = await Task.WhenAll(clients.Select(client => AskAsync(client, cancellationToken)));
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

    public async Task<ClientDestinationsResult> ListAsync(IntegrationConnectionEntity client, CancellationToken cancellationToken = default)
    {
        var answer = await AskAsync(client, cancellationToken);
        if (answer.Status != ClientDestinationsStatus.Listed)
            return ClientDestinationsResult.NotListed(answer.Status, answer.Error!);

        // Read after the client answered: one that couldn't be asked needs no folders.
        var folders = await LibraryFolderReader.LoadAsync(db, cancellationToken);
        var pairs = PairsOf(client);
        return ClientDestinationsResult.Listed(
        [
            .. answer.Destinations.Select(destination =>
                new CheckedClientDestination(destination, Explain(DestinationPathCheck.Check(destination, pairs, folders.Mapped), folders))),
        ]);
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// What one client says about its destinations. Never throws but for the caller's cancellation, and
    /// never touches the database, so several run at once.
    /// </summary>
    private async Task<ClientAnswer> AskAsync(IntegrationConnectionEntity client, CancellationToken cancellationToken)
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

        try
        {
            var listed = await downloadClient.ListDestinationsAsync(settings, cancellationToken);
            if (!listed.IsSuccess)
                return Failed(client, listed.Status, listed.Error!, settings);

            logger.LogInformation(
                "Destination listing of integration {IntegrationId} ({AdapterType}) found {Count} destinations.",
                client.Id,
                client.AdapterType,
                listed.Value!.Count);
            return new ClientAnswer(ClientDestinationsStatus.Listed, listed.Value, null);
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
            return Failed(client, IntegrationCallStatus.UnexpectedResponse, $"The app couldn't read what “{client.Name}” answered.", settings);
        }
    }

    private ClientAnswer NotAsked(IntegrationConnectionEntity client, ClientDestinationsStatus status, string error)
    {
        logger.LogInformation(
            "Destination listing of integration {IntegrationId} ({AdapterType}) did not ask it: {Status}.", client.Id, client.AdapterType, status);
        return new ClientAnswer(status, [], error);
    }

    private ClientAnswer Failed(IntegrationConnectionEntity client, IntegrationCallStatus status, string error, IntegrationConnectionSettings settings)
    {
        // Information: the status only — never the address or the sentence (the connection test's rule).
        logger.LogInformation(
            "Destination listing of integration {IntegrationId} ({AdapterType}) failed: {Status}.", client.Id, client.AdapterType, status);
        // Masked again after the adapter (ADR 0007's second pass), which also covers an adapter that isn't
        // HTTP.
        return new ClientAnswer(ClientDestinationsStatus.CallFailed, [], SecretRedaction.Redact(error, settings.Secrets));
    }

    /// <summary>One stored destination's check against what its client answered.</summary>
    private static DestinationPathCheckResult Check(string clientDestinationKey, IntegrationConnectionEntity client, ClientAnswer answer, LibraryFolders folders) =>
        answer.Status switch
        {
            ClientDestinationsStatus.Listed =>
                Explain(DestinationPathCheck.Check(clientDestinationKey, answer.Destinations, PairsOf(client), folders.Mapped), folders),
            ClientDestinationsStatus.ClientDisabled => DestinationPathCheckResult.ClientDisabled(answer.Error!),
            _ => DestinationPathCheckResult.ClientUnavailable(answer.Error!),
        };

    // The check's own sentence, except for "libraries unknown" while folders are stored that none of their
    // mappings bring into the app: syncing again wouldn't change that.
    private static DestinationPathCheckResult Explain(DestinationPathCheckResult result, LibraryFolders folders) =>
        result.Status == DestinationPathStatus.LibrariesUnknown && folders.StoredCount > 0
            ? result with { Problem = LibraryFoldersUnmappedError }
            : result;

    private static List<PathMappingPair> PairsOf(IntegrationConnectionEntity client) =>
        [.. client.PathMappings.OrderBy(mapping => mapping.Position).Select(mapping => new PathMappingPair(mapping.ServicePrefix, mapping.AppPrefix))];

    private static string DisabledError(IntegrationConnectionEntity client) =>
        $"“{client.Name}” is switched off. Switch it on under Integrations to use it for destinations.";

    private static string TypeUnavailableError(IntegrationConnectionEntity client) =>
        $"This version of the app can't send downloads to “{client.AdapterType}”.";

    /// <summary>What one client said, before any destination is checked against it. Its
    /// <c>ToString</c> prints the list as its type name; the error is masked.</summary>
    private sealed record ClientAnswer(ClientDestinationsStatus Status, IReadOnlyList<ClientDestination> Destinations, string? Error);
}
