using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Services.LibraryLinks;

/// <summary>
/// MAM-34 — settles the preparations of the TMDb suggestions a stop interrupted, the <c>RecommendationRunReconciler</c>
/// way: a running run is failed, a queued one is queued again.
/// </summary>
/// <remarks>
/// A running run died with the process; what it had searched is kept — a run saves each work's candidates as it goes —
/// and the next run finds those searches fresh in its cache. A queued one asks for nothing but "search again", which is
/// still worth doing. The start queues no run of its own: the stored suggestions are as good after a restart as before.
/// </remarks>
public sealed class LibraryLinkSuggestionRunReconciler(
    IServiceScopeFactory scopes,
    LibraryLinkSuggestionRunQueue queue,
    TimeProvider time,
    ILogger<LibraryLinkSuggestionRunReconciler> logger) : IHostedService
{
    private const string RestartedError =
        "The app restarted before these TMDb suggestions were prepared. The works searched so far are kept.";

    public Task StartAsync(CancellationToken cancellationToken) => ReconcileAsync(cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>Fails the running run and queues the queued one again. Start calls it; tests drive it directly, as the
    /// test host runs no hosted services.</summary>
    public async Task ReconcileAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        // Never cancelled: a terminal write (LibraryLinkSuggestionRunUpdates).
        var failed = await db.LibraryLinkSuggestionRuns
            .Where(run => run.Status == LibraryLinkSuggestionRunStatus.Running)
            .MarkFailedAsync(RestartedError, time.GetUtcNow().UtcDateTime);
        if (failed > 0)
        {
            // Information, not Warning: nothing is lost — the works searched are kept, and the next trigger searches the
            // rest.
            logger.LogInformation("{Count} TMDb suggestion runs were running when the app stopped; they are marked failed.", failed);
        }

        // The queue lives in memory, so a run queued before the stop has no id in it any more. One the queue refuses is
        // failed with the service's sentence: left queued with no id, every later trigger would join it and nothing would
        // run until the next start.
        var queued = await db.LibraryLinkSuggestionRuns
            .Where(run => run.Status == LibraryLinkSuggestionRunStatus.Queued)
            .Select(run => run.Id)
            .ToListAsync(cancellationToken);
        foreach (var runId in queued.Where(runId => !queue.TryEnqueue(runId)))
        {
            await db.LibraryLinkSuggestionRuns
                .Where(run => run.Id == runId && run.Status == LibraryLinkSuggestionRunStatus.Queued)
                .MarkFailedAsync(LibraryLinkSuggestionRunService.QueueFullError, time.GetUtcNow().UtcDateTime);
        }
    }
}
