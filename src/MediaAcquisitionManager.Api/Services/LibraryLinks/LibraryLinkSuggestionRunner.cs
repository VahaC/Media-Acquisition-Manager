using MediaAcquisitionManager.Api.Services.Catalog;
using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Abstractions.Metadata;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Texts;
using MediaAcquisitionManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Services.LibraryLinks;

/// <inheritdoc cref="ILibraryLinkSuggestionRunner"/>
/// <remarks>
/// <para>
/// Split by facet: this file claims the run, passes its gates and ends it; <c>LibraryLinkSuggestionRunner.Searches.cs</c>
/// warms the libraries' profiles and searches and stores each work; <c>LibraryLinkSuggestionRunner.Progress.cs</c> tells
/// the TMDb page how far it is. The <c>RecommendationRunner</c> shape.
/// </para>
/// <para>
/// Every catalog answer goes through <see cref="ICatalogStore"/>, one call at a time — the catalog's adapter paces
/// itself, and a cached answer costs no call at all. Logs carry ids and counts only, never a title or an address.
/// </para>
/// </remarks>
public sealed partial class LibraryLinkSuggestionRunner(
    ApplicationDbContext db,
    ICatalogConnector catalogs,
    ICatalogStore store,
    TimeProvider time,
    ILogger<LibraryLinkSuggestionRunner> logger) : ILibraryLinkSuggestionRunner
{
    /// <summary>
    /// How many identified works of each library have their details read for its profile (decision 3): fifty tell a
    /// library of cartoons from one of documentaries many times over (<c>LibraryGenreProfile</c> needs three), and the
    /// cache keeps them for a month, so a large library costs fifty reads once rather than one per film.
    /// </summary>
    public const int ProfileSampleSize = 50;

    /// <summary>
    /// How many of a work's search results are kept as its candidates, each read through the cache for its runtime,
    /// genres, collection and directors: ten hold the exact titles and the next best of TMDb's order with room to spare,
    /// while the dialog shows five (<see cref="LibraryLinkSuggestionReader.SuggestionsShown"/>).
    /// </summary>
    public const int CandidatesPerWork = 10;

    /// <summary>Why a run failed when its metadata catalog is removed while it searches.</summary>
    internal static readonly Message CatalogRemovedError = Message.Of("library-links.suggestions.catalog-removed");

    public async Task RunAsync(Guid runId, CancellationToken cancellationToken = default)
    {
        // One worker runs one run at a time, so a running run other than this one is dead: its worker couldn't mark it
        // failed. Left running, it would hold IX_LibraryLinkSuggestionRuns_OneRunPerActiveStatus's one running slot, and
        // every claim from here to the next start would break the index.
        await db.LibraryLinkSuggestionRuns
            .Where(run => run.Id != runId && run.Status == LibraryLinkSuggestionRunStatus.Running)
            .MarkFailedAsync(LibraryLinkSuggestionRunWorker.AppErrorMessage, time.GetUtcNow().UtcDateTime);

        // The atomic claim: only a queued run turns running, and only once, whoever else holds its id.
        var startedUtc = time.GetUtcNow().UtcDateTime;
        var claimed = await db.LibraryLinkSuggestionRuns
            .Where(run => run.Id == runId && run.Status == LibraryLinkSuggestionRunStatus.Queued)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(run => run.Status, LibraryLinkSuggestionRunStatus.Running)
                    .SetProperty(run => run.StartedUtc, startedUtc),
                cancellationToken) == 1;
        if (!claimed)
            return;
        // Read after the claim: a queued run's trigger can still change — the owner's request joining it (R21).
        var trigger = await db.LibraryLinkSuggestionRuns.Where(run => run.Id == runId).Select(run => run.Trigger).SingleAsync(cancellationToken);

        // Checked again, not trusted from the queuing: the catalog may have been removed or switched off since.
        var connected = await catalogs.ConnectAsync(cancellationToken);
        if (connected.Connection is not { } catalog)
        {
            // No secret was read: the connector's messages name the catalog's integration only, so nothing is masked.
            await FailAsync(runId, connected.Status.ToString(), connected.Error!, settings: null);
            return;
        }

        // The gate the library sync uses (MAM-8): a catalog that refuses the key or answers unlike itself is refused
        // before anything is asked of it.
        var test = await catalog.Adapter.TestConnectionAsync(catalog.Settings, cancellationToken);
        if (!test.IsSuccess)
        {
            await FailAsync(runId, test.Status.ToString(), test.Message, catalog.Settings);
            return;
        }

        var run = new Preparation(runId, catalog, bypassesCache: trigger == LibraryLinkSuggestionRunTrigger.Requested);
        Failure? failure;
        try
        {
            failure = await PrepareAsync(run, cancellationToken);
        }
        catch (DbUpdateException)
        {
            // The catalog was removed after the run's gates: what it answers can't be stored as its own any more (a
            // catalog row's source must exist), so the store's save fails. The owner's doing, not a bug: the run fails
            // with a sentence, and the works searched so far stay. Any other failure to save is a bug.
            db.ChangeTracker.Clear();
            if (await db.IntegrationConnections.AnyAsync(integration => integration.Id == catalog.IntegrationId, cancellationToken))
                throw;
            await FailAsync(runId, "CatalogRemoved", CatalogRemovedError, catalog.Settings);
            return;
        }

        if (failure is not null)
        {
            // A failure of the catalog ends the run; the works it searched are saved, and the next run finds them fresh. The
            // count says how far it got.
            await WriteProgressAsync(run, CancellationToken.None);
            await FailAsync(runId, failure.Status.ToString(), failure.Error, catalog.Settings);
            return;
        }

        await db.LibraryLinkSuggestionRuns
            .Where(row => row.Id == runId && row.Status == LibraryLinkSuggestionRunStatus.Running)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(row => row.Status, LibraryLinkSuggestionRunStatus.Succeeded)
                    .SetProperty(row => row.FinishedUtc, time.GetUtcNow().UtcDateTime)
                    .SetProperty(row => row.WorksTotal, (int?)run.Total)
                    .SetProperty(row => row.WorksDone, run.Done),
                CancellationToken.None);
        logger.LogInformation(
            "TMDb suggestion run {RunId} finished: {Works} works, {Searched} searched, {Kept} kept from the cache, {Gone} gone meanwhile.",
            runId, run.Total, run.Searched, run.Kept, run.Gone);
    }

    /// <summary>
    /// Ends the run as failed with <paramref name="error"/>, its values masked again with the call's secrets — a second
    /// pass after the adapter's own (the MAM-7 rule). Logged as a Warning with <paramref name="status"/> only, never the
    /// message, which can carry the address.
    /// </summary>
    private async Task FailAsync(Guid runId, string status, Message error, IntegrationConnectionSettings? settings)
    {
        var masked = settings is null ? error : error.Redacted(settings.Secrets);
        await db.LibraryLinkSuggestionRuns
            .Where(run => run.Id == runId && run.Status == LibraryLinkSuggestionRunStatus.Running)
            .MarkFailedAsync(masked, time.GetUtcNow().UtcDateTime);
        logger.LogWarning("TMDb suggestion run {RunId} failed ({Status}); the works searched so far are kept.", runId, status);
    }

    /// <summary>What one run keeps in memory: its catalog, whether it searches every work again, and its counts.</summary>
    private sealed class Preparation(Guid runId, CatalogConnection catalog, bool bypassesCache)
    {
        public Guid RunId { get; } = runId;

        public CatalogConnection Catalog { get; } = catalog;

        /// <summary>R21 — the owner asked for the run: every work is searched again, whatever the cache holds.</summary>
        public bool BypassesCache { get; } = bypassesCache;

        /// <summary>The works to search.</summary>
        public int Total { get; set; }

        /// <summary>The works done so far: searched, kept from the cache, or gone meanwhile.</summary>
        public int Done { get; set; }

        public int Searched { get; set; }

        public int Kept { get; set; }

        public int Gone { get; set; }

        /// <summary>The clock's timestamp of the last progress write.</summary>
        public long WrittenAt { get; set; }

        /// <summary>
        /// The catalog's answers this run already has, by kind, query and year: the works of one folder share its name as
        /// a query (161 Tom &amp; Jerry shorts, one folder), and one question goes out once a run. Only answers — a
        /// failure ends the run. The query is compared as it is sent; nothing outlives the run, so "Prepare again" still
        /// asks everything anew (R21).
        /// </summary>
        public Dictionary<(MediaWorkKind Kind, string Query, int? Year), IReadOnlyList<CatalogWorkSummary>> Answers { get; } = [];
    }

    /// <summary>A catalog call's failure: its status and why, already masked by the adapter.</summary>
    private sealed record Failure(IntegrationCallStatus Status, Message Error)
    {
        public static Failure Of<T>(IntegrationCallResult<T> result) where T : class => new(result.Status, result.Error!);
    }
}
