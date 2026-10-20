using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Texts;
using MediaAcquisitionManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Services.Recommendations;

/// <summary>
/// MAM-33 — settles the computations a stop interrupted, the <c>FileInspectionReconciler</c> way: a running run
/// is failed, a queued one is queued again.
/// </summary>
/// <remarks>
/// A running run died with the process; it had replaced nothing yet — the candidates are replaced in one
/// transaction at its very end — so the previous run's candidates still stand. A queued one asks for nothing but
/// "compute again", which is still worth doing. Unlike the inspection, the start queues no run of its own: the
/// candidates in the database are as good after a restart as before it.
/// </remarks>
public sealed class RecommendationRunReconciler(
    IServiceScopeFactory scopes,
    RecommendationRunQueue queue,
    TimeProvider time,
    ILogger<RecommendationRunReconciler> logger) : IHostedService
{
    private static readonly Message RestartedError = Message.Of("recommendations.run.restarted");

    public Task StartAsync(CancellationToken cancellationToken) => ReconcileAsync(cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>Fails the running run and queues the queued one again. Start calls it; tests drive it directly, as
    /// the test host runs no hosted services.</summary>
    public async Task ReconcileAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        // Never cancelled: a terminal write (RecommendationRunUpdates).
        var failed = await db.RecommendationRuns
            .Where(run => run.Status == RecommendationRunStatus.Running)
            .MarkFailedAsync(RestartedError, time.GetUtcNow().UtcDateTime);
        if (failed > 0)
        {
            // Information, not Warning: nothing is lost — the previous candidates stand, and the next trigger
            // computes again.
            logger.LogInformation("{Count} recommendation runs were running when the app stopped; they are marked failed.", failed);
        }

        // The queue lives in memory, so a run queued before the stop has no id in it any more. One the queue
        // refuses is failed with the service's reason: left queued with no id, every later trigger would join it
        // and nothing would run until the next start.
        var queued = await db.RecommendationRuns
            .Where(run => run.Status == RecommendationRunStatus.Queued)
            .Select(run => run.Id)
            .ToListAsync(cancellationToken);
        foreach (var runId in queued.Where(runId => !queue.TryEnqueue(runId)))
        {
            await db.RecommendationRuns
                .Where(run => run.Id == runId && run.Status == RecommendationRunStatus.Queued)
                .MarkFailedAsync(RecommendationRunService.QueueFullError, time.GetUtcNow().UtcDateTime);
        }
    }
}
