using MediaAcquisitionManager.Api.Services.Files;
using MediaAcquisitionManager.Api.Services.Integrations;
using MediaAcquisitionManager.Api.Services.LibraryLinks;
using MediaAcquisitionManager.Api.Services.Recommendations;
using MediaAcquisitionManager.Core.Abstractions;
using MediaAcquisitionManager.Core.Abstractions.Files;
using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Abstractions.Library;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Texts;
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
    IFileInventory inventory,
    IFileInspectionService inspections,
    IRecommendationRunService recommendations,
    ILibraryLinkSuggestionRunService linkSuggestions,
    TimeProvider time,
    ILogger<LibrarySyncRunner> logger) : ILibrarySyncRunner
{
    /// <summary>
    /// Items per request. A page of 50 stays far inside the client's 4 MiB buffer and its 15 s timeout
    /// (the heaviest sample movie is about 12 KB as Jellyfin sends it, and the heaviest episode, the
    /// Tom &amp; Jerry Kids item with 65 media sources, about 149 KB; <c>IntegrationsInfrastructureExtensions</c>),
    /// while the reference library of 870 movies still takes only 18 requests with the one-item overlap.
    /// </summary>
    public const int LibraryPageSize = 50;

    /// <summary>
    /// MAM-32 — the library's lists, read one after the other: its movies, its series, then its
    /// episodes, each with its own total and its own checks (ADR 0008, ADR 0019).
    /// </summary>
    private static readonly MediaWorkKind[] Lists = [MediaWorkKind.Movie, MediaWorkKind.Series, MediaWorkKind.Episode];

    private static readonly Message RemovedBeforeStartError = Message.Of("library.sync.removed-before-start");
    private static readonly Message RemovedWhileSyncingError = Message.Of("library.sync.removed-while-syncing");

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
            await FailAsync(runId, integration.Id, "SwitchedOff", Message.Of("library.sync.switched-off-before-start", ("integration", integration.Name)), settings: null);
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

        // MAM-43 — a disc's clips, which the library names by their folder only, become its version's files.
        var items = await LibraryDiscClips.FillAsync(db, inventory, integration.Id, read.Items, cancellationToken);
        var snapshot = await snapshots.ApplyAsync(runId, integration.Id, locations.Value!, items, cancellationToken);
        switch (snapshot.Status)
        {
            case LibrarySnapshotStatus.Applied:
                // Information: counts only — never an address, a path or a title.
                logger.LogInformation(
                    "Library sync {RunId} of integration {IntegrationId} finished: {Works} works, {Series} series, {Versions} versions, {Files} files.",
                    runId,
                    integration.Id,
                    snapshot.WorksCount,
                    snapshot.SeriesCount,
                    snapshot.VersionsCount,
                    snapshot.FilesCount);
                await QueueInspectionAsync(runId, cancellationToken);
                await QueueRecommendationsAsync(runId, cancellationToken);
                await QueueLinkSuggestionsAsync(runId, cancellationToken);
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
    /// Every item of the library, its movies, series and episodes, each list read in pages that overlap
    /// by one (ADR 0008): each page after the first starts at the previous page's last item, which must
    /// come back first again. Jellyfin applies <c>startIndex</c> and <c>limit</c> exactly, so a total that
    /// changes, a short or empty page before the end, a boundary item that moved, or fewer different items
    /// than the total all mean the library changed while it was read — and a sync that read it anyway
    /// could store a movie twice or miss one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// MAM-32 — the first page of every list is read before the rest of any, so the progress knows the
    /// whole library's total from the third request on, instead of a total that grows list by list.
    /// </para>
    /// <para>
    /// Not every change shows. A removal and an addition that both sort before a boundary already read,
    /// between two page requests, leave the total, every boundary and the count of different items as
    /// they were: the run succeeds and stores a stale row for the removed movie and none for the new
    /// one, which the next sync corrects. Nothing real is deleted — a sync only ever writes this app's
    /// rows. A second, id-only pass over the whole list would narrow the gap at the price of a second
    /// round of requests on every sync; ADR 0008 accepts the gap instead.
    /// </para>
    /// </remarks>
    private async Task<ReadOutcome> ReadItemsAsync(
        Guid runId,
        IMediaLibraryProvider provider,
        IntegrationConnectionSettings settings,
        string displayName,
        CancellationToken cancellationToken)
    {
        var lists = Lists.Select(kind => new ListRead(kind)).ToList();
        int ItemsRead() => lists.Sum(list => list.Items.Count);

        // The first page of each list, for the whole library's total.
        foreach (var list in lists)
        {
            if (await ReadPageAsync(list, provider, settings, displayName, cancellationToken) is { } failed)
                return failed;
        }

        var itemsTotal = lists.Sum(list => list.FirstTotal!.Value);
        await ReportProgressAsync(runId, ItemsRead(), itemsTotal, cancellationToken);

        foreach (var list in lists)
        {
            while (!list.IsDone)
            {
                if (await ReadPageAsync(list, provider, settings, displayName, cancellationToken) is { } failed)
                    return failed;
                await ReportProgressAsync(runId, ItemsRead(), itemsTotal, cancellationToken);
            }

            if (list.Items.Count != list.FirstTotal)
                return ReadOutcome.Failed("LibraryChanged", list.DistinctCountError(displayName));
        }

        return new ReadOutcome([.. lists.SelectMany(list => list.Items.Values)], null, null);
    }

    /// <summary>
    /// The next page of <paramref name="list"/>, added to it; the failure that ends the read when the
    /// call failed or the page shows the list changed, else <c>null</c>.
    /// </summary>
    private static async Task<ReadOutcome?> ReadPageAsync(
        ListRead list,
        IMediaLibraryProvider provider,
        IntegrationConnectionSettings settings,
        string displayName,
        CancellationToken cancellationToken)
    {
        var result = await provider.ListItemsAsync(settings, new LibraryPageRequest(list.Kind, list.StartIndex, LibraryPageSize), cancellationToken);
        if (!result.IsSuccess)
            return ReadOutcome.Failed(result.Status.ToString(), result.Error!);

        var page = result.Value!;
        list.FirstTotal ??= page.TotalCount;
        if (page.TotalCount != list.FirstTotal)
            return ReadOutcome.Failed("LibraryChanged", list.TotalChangedError(displayName, page.TotalCount));
        if (list.BoundaryId is not null
            && (page.Items.Count == 0 || !string.Equals(page.Items[0].SourceItemId, list.BoundaryId, StringComparison.Ordinal)))
            return ReadOutcome.Failed("LibraryChanged", list.PagesShiftedError(displayName));

        foreach (var item in page.Items)
            list.Items[item.SourceItemId] = item;

        if (list.StartIndex + page.Items.Count >= page.TotalCount)
        {
            list.IsDone = true;
            return null;
        }

        // Short before the end: items went missing from under the read.
        if (page.Items.Count < LibraryPageSize)
            return ReadOutcome.Failed("LibraryChanged", list.PagesShiftedError(displayName));

        list.BoundaryId = page.Items[^1].SourceItemId;
        list.StartIndex += LibraryPageSize - 1;
        return null;
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
    /// MAM-33 — what the library holds may have changed, so a successful sync queues a computation of the
    /// recommendations (or joins the one waiting); quietly nothing without a metadata catalog. After the inspection's,
    /// on its own: the sync has succeeded and stays so, and a failure here is logged and costs the inspection nothing —
    /// the next sync or the owner's Recompute now queues one again.
    /// </summary>
    private async Task QueueRecommendationsAsync(Guid runId, CancellationToken cancellationToken)
    {
        try
        {
            await recommendations.EnsureQueuedAsync(RecommendationRunTrigger.LibrarySynced, cancellationToken: cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Library sync {RunId} succeeded, but no recommendation run could be queued after it.", runId);
        }
    }

    /// <summary>
    /// MAM-34 — works without a TMDb id may have come or changed, so a successful sync queues a preparation of their TMDb
    /// suggestions (or joins the one waiting); quietly nothing without a metadata catalog. Last and on its own, like the
    /// recommendations': the sync has succeeded and stays so, and a failure here is logged and costs the others nothing —
    /// the next sync or the owner's "Prepare again" queues one again.
    /// </summary>
    private async Task QueueLinkSuggestionsAsync(Guid runId, CancellationToken cancellationToken)
    {
        try
        {
            await linkSuggestions.EnsureQueuedAsync(LibraryLinkSuggestionRunTrigger.LibrarySynced, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Library sync {RunId} succeeded, but no TMDb suggestion run could be queued after it.", runId);
        }
    }

    /// <summary>
    /// Ends the run as failed with <paramref name="error"/>, masked again with the call's secrets — a
    /// second pass after the adapter's own (the MAM-7 rule): one line here covers every adapter, a future
    /// one that isn't HTTP included. Logged as a Warning with <paramref name="status"/> only, never the
    /// sentence, which can carry the address or what a service said back about a credential.
    /// </summary>
    /// <remarks>MAM-47 (ADR 0042) — the pass masks every value of the message, nested ones too: the template holds no
    /// secret, so what reached a sentence through a value is what it masks.</remarks>
    private async Task FailAsync(Guid runId, Guid? integrationId, string status, Message error, IntegrationConnectionSettings? settings)
    {
        var masked = settings is null ? error : error.Redacted(settings.Secrets);
        await db.LibrarySyncRuns
            .Where(run => run.Id == runId && run.Status == LibrarySyncStatus.Running)
            .MarkFailedAsync(masked, time.GetUtcNow().UtcDateTime);
        logger.LogWarning("Library sync {RunId} of integration {IntegrationId} failed ({Status}).", runId, integrationId, status);
    }

    /// <summary>What reading the items gave: every item, or the failure's status and reason.</summary>
    private sealed record ReadOutcome(IReadOnlyList<LibraryItem> Items, string? FailureStatus, Message? Error)
    {
        public static ReadOutcome Failed(string status, Message error) => new([], status, error);
    }

    /// <summary>How far one of the library's lists has been read (MAM-32), and the reasons a read of it gives when the
    /// library changed under it.</summary>
    /// <remarks>
    /// ADR 0008 — the three ways a read shows the library changed under it, each ending by saying nothing was stored and
    /// what to do. MAM-47 (ADR 0042) — one key per list, never the list's noun as a value: the noun agrees with the
    /// count, and in Ukrainian with the words around it, so it belongs to the template.
    /// </remarks>
    private sealed class ListRead(MediaWorkKind kind)
    {
        public MediaWorkKind Kind { get; } = kind;

        /// <summary>By id, keeping the last listing of an id: the overlapping item is read twice by
        /// design.</summary>
        public Dictionary<string, LibraryItem> Items { get; } = new(StringComparer.Ordinal);

        public int? FirstTotal { get; set; }

        public string? BoundaryId { get; set; }

        public int StartIndex { get; set; }

        public bool IsDone { get; set; }

        /// <summary>The total changed between the first page and <paramref name="lastTotal"/>'s; counted on the
        /// first total, which the noun follows.</summary>
        public Message TotalChangedError(string service, int lastTotal) => Message.Of(
            Kind switch
            {
                MediaWorkKind.Movie => "library.sync.total-changed-movies",
                MediaWorkKind.Series => "library.sync.total-changed-series",
                _ => "library.sync.total-changed-episodes",
            },
            ("service", service),
            ("count", FirstTotal!.Value),
            ("last", lastTotal));

        /// <summary>A page started at an item other than the previous page's last one, or ended short.</summary>
        public Message PagesShiftedError(string service) => Message.Of(
            Kind switch
            {
                MediaWorkKind.Movie => "library.sync.pages-shifted-movies",
                MediaWorkKind.Series => "library.sync.pages-shifted-series",
                _ => "library.sync.pages-shifted-episodes",
            },
            ("service", service));

        /// <summary>Fewer different items were read than the list's total; counted on the items read, which the noun
        /// follows.</summary>
        public Message DistinctCountError(string service) => Message.Of(
            Kind switch
            {
                MediaWorkKind.Movie => "library.sync.distinct-count-movies",
                MediaWorkKind.Series => "library.sync.distinct-count-series",
                _ => "library.sync.distinct-count-episodes",
            },
            ("service", service),
            ("count", Items.Count),
            ("listed", FirstTotal!.Value));
    }
}
