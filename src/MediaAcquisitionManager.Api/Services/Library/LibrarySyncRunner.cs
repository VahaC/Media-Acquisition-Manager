using System.Globalization;
using MediaAcquisitionManager.Api.Services.Files;
using MediaAcquisitionManager.Api.Services.Integrations;
using MediaAcquisitionManager.Core.Abstractions;
using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Abstractions.Library;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Services.Library;

/// <inheritdoc cref="ILibrarySyncRunner"/>
/// <remarks>
/// ADR 0008 — the whole library is read before anything is written: a sync that fails half-way (the
/// server down, the key refused, the library rescanned under it) changes nothing stored. The adapter is
/// found through the registry by the stored type (Strategy, ADR 0005), and this class never names a
/// product: its sentences use the type's display name.
/// </remarks>
public sealed class LibrarySyncRunner(
    ApplicationDbContext db,
    IIntegrationAdapterRegistry adapters,
    IIntegrationSettingsFactory settingsFactory,
    ILibrarySnapshotStore snapshots,
    IFileInspectionService inspections,
    TimeProvider time,
    ILogger<LibrarySyncRunner> logger) : ILibrarySyncRunner
{
    /// <summary>
    /// Movies per request. A page of 50 stays far inside the client's 4 MiB buffer and its 15 s timeout
    /// (the heaviest sample movie is about 12 KB as Jellyfin sends it; <c>IntegrationsInfrastructureExtensions</c>),
    /// while the reference library of 870 movies still takes only 18 requests with the one-item overlap.
    /// </summary>
    public const int LibraryPageSize = 50;

    private const string RemovedBeforeStartError = "The integration was removed before the sync started.";
    private const string RemovedWhileSyncingError = "The integration was removed while it was being synced; nothing was stored.";
    private const string SwitchedOffBeforeStartError = "“{0}” was switched off before the sync started.";

    // ADR 0008 — the three ways a read shows the library changed under it. {0} is the type's display
    // name; each ends by saying nothing was stored and what to do.
    private const string TotalChangedError =
        "{0} listed {1} movies when the sync began and {2} by its end, so its library changed while it was being read. Nothing was changed here; sync again once {0}'s library scan has finished.";

    private const string PagesShiftedError =
        "{0}'s list of movies shifted between two pages, so its library changed while it was being read. Nothing was changed here; sync again once {0}'s library scan has finished.";

    private const string DistinctCountError =
        "The sync read {1} different movies of the {2} {0} listed, so its library changed while it was being read. Nothing was changed here; sync again once {0}'s library scan has finished.";

    public async Task RunAsync(Guid runId, CancellationToken cancellationToken = default)
    {
        // The atomic claim: only a queued run turns running, and only once, whoever else holds its id (a
        // second worker, the id queued twice). Nothing else is done for a run that isn't claimed.
        var startedUtc = time.GetUtcNow().UtcDateTime;
        var claimed = await db.LibrarySyncRuns
            .Where(run => run.Id == runId && run.Status == LibrarySyncStatus.Queued)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(run => run.Status, LibrarySyncStatus.Running)
                    .SetProperty(run => run.StartedUtc, startedUtc),
                cancellationToken) == 1;
        if (!claimed)
            return;

        // Not tracked (the context tracks by default): nothing here is saved through the tracker, and
        // the snapshot store's own tracked rows must not meet stale copies of these.
        var integrationId = await db.LibrarySyncRuns
            .AsNoTracking()
            .Where(run => run.Id == runId)
            .Select(run => run.IntegrationConnectionId)
            .SingleAsync(cancellationToken);
        var integration = integrationId is null
            ? null
            : await db.IntegrationConnections.AsNoTracking().SingleOrDefaultAsync(connection => connection.Id == integrationId, cancellationToken);
        if (integration is null)
        {
            await FailAsync(runId, integrationId, "IntegrationRemoved", RemovedBeforeStartError, settings: null);
            return;
        }

        // Checked again, not trusted from the start: the build or the switch may have changed since.
        if (adapters.Find(integration.AdapterType) is not { } adapter || adapter is not IMediaLibraryProvider provider)
        {
            await FailAsync(runId, integration.Id, "AdapterUnavailable", LibrarySyncService.AdapterUnavailableError(integration.AdapterType), settings: null);
            return;
        }

        if (!integration.IsEnabled)
        {
            await FailAsync(runId, integration.Id, "SwitchedOff", Format(SwitchedOffBeforeStartError, integration.Name), settings: null);
            return;
        }

        var settingsResult = settingsFactory.Create(integration);
        if (settingsResult.Settings is not { } settings)
        {
            await FailAsync(runId, integration.Id, "SecretUnreadable", settingsResult.Error!, settings: null);
            return;
        }

        // MAM-8 — the version gate is the connection test, exactly what the owner's Test button runs: a
        // server this app can't read is refused before a single item is asked for.
        var test = await adapter.TestConnectionAsync(settings, cancellationToken);
        if (!test.IsSuccess)
        {
            await FailAsync(runId, integration.Id, test.Status.ToString(), test.Message, settings);
            return;
        }

        var locations = await provider.ListLocationsAsync(settings, cancellationToken);
        if (!locations.IsSuccess)
        {
            await FailAsync(runId, integration.Id, locations.Status.ToString(), locations.Error!, settings);
            return;
        }

        var read = await ReadItemsAsync(runId, provider, settings, adapter.Descriptor.DisplayName, cancellationToken);
        if (read.Error is { } readError)
        {
            await FailAsync(runId, integration.Id, read.FailureStatus!, readError, settings);
            return;
        }

        var snapshot = await snapshots.ApplyAsync(runId, integration.Id, locations.Value!, read.Items, cancellationToken);
        switch (snapshot.Status)
        {
            case LibrarySnapshotStatus.Applied:
                // Information: counts only — never an address, a path or a title.
                logger.LogInformation(
                    "Library sync {RunId} of integration {IntegrationId} finished: {Works} works, {Versions} versions, {Files} files.",
                    runId,
                    integration.Id,
                    snapshot.WorksCount,
                    snapshot.VersionsCount,
                    snapshot.FilesCount);
                await QueueInspectionAsync(runId, cancellationToken);
                break;
            case LibrarySnapshotStatus.SourceRemoved:
                await FailAsync(runId, integration.Id, "IntegrationRemoved", RemovedWhileSyncingError, settings);
                break;
            default:
                // Something else finished the run first; its outcome stands and nothing was stored.
                logger.LogWarning(
                    "Library sync {RunId} of integration {IntegrationId} was finished elsewhere before its snapshot was stored; nothing was stored.",
                    runId,
                    integration.Id);
                break;
        }
    }

    /// <summary>
    /// Every item of the library, read in pages that overlap by one (ADR 0008): each page after the
    /// first starts at the previous page's last item, which must come back first again. Jellyfin applies
    /// <c>startIndex</c> and <c>limit</c> exactly, so a total that changes, a short or empty page before
    /// the end, a boundary item that moved, or fewer different items than the total all mean the library
    /// changed while it was read — and a sync that read it anyway could store a movie twice or miss one.
    /// </summary>
    /// <remarks>
    /// Not every change shows. A removal and an addition that both sort before a boundary already read,
    /// between two page requests, leave the total, every boundary and the count of different items as
    /// they were: the run succeeds and stores a stale row for the removed movie and none for the new
    /// one, which the next sync corrects. Nothing real is deleted — a sync only ever writes this app's
    /// rows. A second, id-only pass over the whole list would narrow the gap at the price of a second
    /// round of requests on every sync; ADR 0008 accepts the gap instead.
    /// </remarks>
    private async Task<ReadOutcome> ReadItemsAsync(
        Guid runId,
        IMediaLibraryProvider provider,
        IntegrationConnectionSettings settings,
        string displayName,
        CancellationToken cancellationToken)
    {
        // By id, keeping the last listing of an id: the overlapping item is read twice by design.
        var items = new Dictionary<string, LibraryItem>(StringComparer.Ordinal);
        int? firstTotal = null;
        string? boundaryId = null;
        var startIndex = 0;
        while (true)
        {
            var result = await provider.ListItemsAsync(settings, new LibraryPageRequest(startIndex, LibraryPageSize), cancellationToken);
            if (!result.IsSuccess)
                return ReadOutcome.Failed(result.Status.ToString(), result.Error!);

            var page = result.Value!;
            firstTotal ??= page.TotalCount;
            if (page.TotalCount != firstTotal)
                return ReadOutcome.Failed("LibraryChanged", Format(TotalChangedError, displayName, firstTotal, page.TotalCount));
            if (boundaryId is not null
                && (page.Items.Count == 0 || !string.Equals(page.Items[0].SourceItemId, boundaryId, StringComparison.Ordinal)))
                return ReadOutcome.Failed("LibraryChanged", Format(PagesShiftedError, displayName));

            foreach (var item in page.Items)
                items[item.SourceItemId] = item;
            await ReportProgressAsync(runId, items.Count, page.TotalCount, cancellationToken);

            if (startIndex + page.Items.Count >= page.TotalCount)
                break;
            // Short before the end: items went missing from under the read.
            if (page.Items.Count < LibraryPageSize)
                return ReadOutcome.Failed("LibraryChanged", Format(PagesShiftedError, displayName));

            boundaryId = page.Items[^1].SourceItemId;
            startIndex += LibraryPageSize - 1;
        }

        if (items.Count != firstTotal)
            return ReadOutcome.Failed("LibraryChanged", Format(DistinctCountError, displayName, items.Count, firstTotal));
        return new ReadOutcome([.. items.Values], null, null);
    }

    // The "Syncing… X of Y" the page polls for, after each page. Not terminal, so it is cancelled with
    // the sync; conditional, so a run something else finished is never written again.
    private Task ReportProgressAsync(Guid runId, int itemsRead, int itemsTotal, CancellationToken cancellationToken) =>
        db.LibrarySyncRuns
            .Where(run => run.Id == runId && run.Status == LibrarySyncStatus.Running)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(run => run.ItemsRead, itemsRead)
                    .SetProperty(run => run.ItemsTotal, itemsTotal),
                cancellationToken);

    /// <summary>
    /// MAM-9 — a new snapshot may name files the app hasn't read, so a successful sync queues an
    /// inspection (or joins the one waiting). The sync has succeeded by now and stays so: a failure here
    /// is logged, and the next sync, the app's start or the owner's Inspect files queues one again.
    /// </summary>
    private async Task QueueInspectionAsync(Guid runId, CancellationToken cancellationToken)
    {
        try
        {
            await inspections.EnsureQueuedAsync(FileInspectionTrigger.LibrarySynced, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Library sync {RunId} succeeded, but no file inspection could be queued after it.", runId);
        }
    }

    /// <summary>
    /// Ends the run as failed with <paramref name="error"/>, masked again with the call's secrets — a
    /// second pass after the adapter's own (the MAM-7 rule): one line here covers every adapter, a future
    /// one that isn't HTTP included. Logged as a Warning with <paramref name="status"/> only, never the
    /// sentence, which can carry the address or what a service said back about a credential.
    /// </summary>
    private async Task FailAsync(Guid runId, Guid? integrationId, string status, string error, IntegrationConnectionSettings? settings)
    {
        var masked = settings is null ? error : SecretRedaction.Redact(error, settings.Secrets)!;
        await db.LibrarySyncRuns
            .Where(run => run.Id == runId && run.Status == LibrarySyncStatus.Running)
            .MarkFailedAsync(masked, time.GetUtcNow().UtcDateTime);
        logger.LogWarning("Library sync {RunId} of integration {IntegrationId} failed ({Status}).", runId, integrationId, status);
    }

    private static string Format(string format, params object?[] values) => string.Format(CultureInfo.InvariantCulture, format, values);

    /// <summary>What reading the items gave: every item, or the failure's status and sentence.</summary>
    private sealed record ReadOutcome(IReadOnlyList<LibraryItem> Items, string? FailureStatus, string? Error)
    {
        public static ReadOutcome Failed(string status, string error) => new([], status, error);
    }
}
