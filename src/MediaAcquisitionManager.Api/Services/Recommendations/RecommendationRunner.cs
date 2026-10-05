using MediaAcquisitionManager.Api.Services.Catalog;
using MediaAcquisitionManager.Core.Abstractions;
using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Recommendations;
using MediaAcquisitionManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Services.Recommendations;

/// <inheritdoc cref="IRecommendationRunner"/>
/// <remarks>
/// <para>
/// Split by facet: this file claims the run, passes its gates and ends it; <c>RecommendationRunner.State.cs</c>
/// holds what one run keeps in memory; <c>RecommendationRunner.Library.cs</c> reads the library into memory and
/// says whether it holds a work; <c>RecommendationRunner.Seeds.cs</c> finds the library's catalog works;
/// <c>RecommendationRunner.Signals.cs</c> collects every reason; <c>RecommendationRunner.Candidates.cs</c> keeps,
/// scores and stores the candidates; <c>RecommendationRunner.Progress.cs</c> tells the page which stage it is in.
/// </para>
/// <para>
/// Every catalog answer goes through <see cref="ICatalogStore"/>, one call at a time — the catalog's adapter
/// paces itself, and a cached answer costs no call at all. No call is made per candidate: a candidate comes with
/// its summary from the list that named it (a recommendation, a collection, a person's works, a discovery page),
/// so the calls grow with the library, not with what it recommends.
/// </para>
/// <para>
/// Logs carry ids and counts only, never a title or an address.
/// </para>
/// </remarks>
public sealed partial class RecommendationRunner(
    ApplicationDbContext db,
    ICatalogConnector catalogs,
    ICatalogStore store,
    IRecommendationSettingsReader settingsReader,
    TimeProvider time,
    ILogger<RecommendationRunner> logger) : IRecommendationRunner
{
    /// <summary>
    /// MAM-33 — a tab with fewer seeds than this has too little of the owner's taste to recommend from, so the
    /// catalog's best-rated works of the tab fill it (<see cref="RecommendationReasonKind.TopRated"/>), each
    /// listed only once a release source carries it. Ten: a shelf of a few films says little of a taste, and a
    /// library with a dozen has recommendations of its own to show.
    /// </summary>
    public const int FewSeeds = 10;

    /// <summary>
    /// MAM-33 — how many pages of the catalog's best-rated works a short tab asks for: two pages, about forty
    /// works, fill a tab's first screens and keep the fallback to a few calls.
    /// </summary>
    public const int DiscoverPages = 2;

    /// <summary>
    /// MAM-33 — how many library works a "Recommended" reason names: five still read as a line on a phone, and
    /// the count beside them (<c>Held</c>) says how many more there were.
    /// </summary>
    public const int MaximumReasonTitles = 5;

    /// <summary>MAM-33 — the sentence a run gets when its metadata catalog is removed while it computes.</summary>
    internal const string CatalogRemovedError =
        "The metadata catalog was removed while these recommendations were computed. The previous ones are kept.";

    /// <summary>
    /// MAM-33 — how long a release a source published stays "new" (<see cref="RecommendationReasonKind.NewAtSource"/>),
    /// from when the app first saw it: two weeks, past which it is what the tracker had all along rather than
    /// news.
    /// </summary>
    public static readonly TimeSpan FeedFreshness = TimeSpan.FromDays(14);

    public async Task RunAsync(Guid runId, CancellationToken cancellationToken = default)
    {
        // One worker runs one run at a time, so a running run other than this one is dead: its worker couldn't mark
        // it failed. Left running, it would hold IX_RecommendationRuns_OneRunPerActiveStatus's one running slot,
        // and every claim from here to the next start would break the index.
        await db.RecommendationRuns
            .Where(run => run.Id != runId && run.Status == RecommendationRunStatus.Running)
            .MarkFailedAsync(RecommendationRunWorker.AppErrorMessage, time.GetUtcNow().UtcDateTime);

        // The atomic claim: only a queued run turns running, and only once, whoever else holds its id.
        var startedUtc = time.GetUtcNow().UtcDateTime;
        var claimed = await db.RecommendationRuns
            .Where(run => run.Id == runId && run.Status == RecommendationRunStatus.Queued)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(run => run.Status, RecommendationRunStatus.Running)
                    .SetProperty(run => run.StartedUtc, startedUtc),
                cancellationToken) == 1;
        if (!claimed)
            return;

        // Checked again, not trusted from the queuing: the catalog may have been removed or switched off since.
        var connected = await catalogs.ConnectAsync(cancellationToken);
        if (connected.Connection is not { } catalog)
        {
            await FailAsync(runId, connected.Status.ToString(), connected.Error!, settings: null);
            return;
        }

        // The tabs some user who can sign in has on (their saved ones, or the defaults); nobody's tab, no call.
        var users = await settingsReader.ReadEnabledUsersAsync(cancellationToken);
        var tabs = RecommendationTabs.All.Where(tab => users.Any(user => user.Settings.IsOn(tab))).ToList();
        if (tabs.Count == 0)
        {
            await StoreAsync(runId, seedCount: 0, [], cancellationToken);
            logger.LogInformation("Recommendation run {RunId} finished: every tab is off for every user, so nothing was computed.", runId);
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

        var run = new Computation(runId, catalog, tabs, DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime));
        Failure? failure;
        try
        {
            failure = await ComputeAsync(run, cancellationToken);
        }
        catch (DbUpdateException)
        {
            // MAM-33 — the catalog was removed after the run's gates: what it answers can't be stored as its own any more
            // (a catalog row's source must exist), so the store's save fails. The owner's doing, not a bug: the run fails
            // with a sentence, and the previous recommendations stay. Any other failure to save is a bug.
            if (await db.IntegrationConnections.AnyAsync(integration => integration.Id == catalog.IntegrationId, cancellationToken))
                throw;
            await FailAsync(runId, "CatalogRemoved", CatalogRemovedError, catalog.Settings);
            return;
        }

        if (failure is not null)
        {
            // MAM-33 — a failure of the catalog fails the whole run: candidates computed from half the library would
            // replace good ones with fewer, so the previous run's stay.
            await FailAsync(runId, failure.Status.ToString(), failure.Error, catalog.Settings);
            return;
        }

        await BeginStageAsync(run, RecommendationRunStage.Saving, total: null, cancellationToken);
        var candidates = await FinishAsync(run, cancellationToken);
        await StoreAsync(runId, run.Seeds.Count, candidates, cancellationToken);
        logger.LogInformation(
            "Recommendation run {RunId} finished: {Seeds} seeds, {Candidates} candidates.", runId, run.Seeds.Count, candidates.Count);
    }

    /// <summary>Every step that asks the catalog, in order; the first failure ends it.</summary>
    private async Task<Failure?> ComputeAsync(Computation run, CancellationToken cancellationToken)
    {
        // The genre lists, a call or two a month (cached), count as reading the library: nothing to show of them alone.
        await BeginStageAsync(run, RecommendationRunStage.ReadingLibrary, total: null, cancellationToken);
        foreach (var kind in run.Kinds)
        {
            var genres = await store.GetGenresAsync(run.Catalog, kind, cancellationToken);
            if (!genres.IsSuccess)
                return Failure.Of(genres);
            foreach (var genre in genres.Value!)
                run.GenreRoles[(kind, genre.Id)] = genre.Role;
        }

        run.Library = await ReadLibraryAsync(cancellationToken);
        return await ResolveSeedsAsync(run, cancellationToken)
            ?? await CollectCollectionsAsync(run, cancellationToken)
            ?? await CollectPeopleAsync(run, cancellationToken)
            ?? CollectRecommendations(run)
            ?? CollectMissingSeasons(run)
            ?? await CollectNewAtSourceAsync(run, cancellationToken)
            ?? await CollectTopRatedAsync(run, cancellationToken);
    }

    /// <summary>
    /// Ends the run as failed with <paramref name="error"/>, masked again with the call's secrets — a second pass
    /// after the adapter's own (the MAM-7 rule) — and cut to its column. Logged as a Warning with
    /// <paramref name="status"/> only, never the sentence, which can carry the address.
    /// </summary>
    private async Task FailAsync(Guid runId, string status, string error, IntegrationConnectionSettings? settings)
    {
        var masked = settings is null ? error : SecretRedaction.Redact(error, settings.Secrets)!;
        await db.RecommendationRuns
            .Where(run => run.Id == runId && run.Status == RecommendationRunStatus.Running)
            .MarkFailedAsync(masked, time.GetUtcNow().UtcDateTime);
        logger.LogWarning("Recommendation run {RunId} failed ({Status}); the previous recommendations are kept.", runId, status);
    }

    /// <summary>A catalog call's failure: its status and its sentence, already masked by the adapter.</summary>
    private sealed record Failure(IntegrationCallStatus Status, string Error)
    {
        public static Failure Of<T>(IntegrationCallResult<T> result) where T : class => new(result.Status, result.Error!);
    }
}
