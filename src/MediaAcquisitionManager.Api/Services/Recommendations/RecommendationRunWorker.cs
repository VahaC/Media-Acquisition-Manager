using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Infrastructure.Persistence;

namespace MediaAcquisitionManager.Api.Services.Recommendations;

/// <summary>
/// MAM-33 — runs the queued computations of the recommendations one after another in the background
/// (Queue/Worker): a request answers 202 at once, and the Recommendations page polls the latest run.
/// </summary>
/// <remarks>
/// One scope per run and one try/catch per run, the <c>FileInspectionWorker</c> rules: a bug that throws out of
/// one run fails that run — logged — and the next still goes. One run at a time: two would ask the catalog for
/// the same works and replace each other's candidates.
/// </remarks>
public sealed class RecommendationRunWorker(
    RecommendationRunQueue queue,
    IServiceScopeFactory scopes,
    TimeProvider time,
    ILogger<RecommendationRunWorker> logger) : BackgroundService
{
    internal const string AppErrorMessage = "The recommendations stopped computing because of an error in the app. Its log says more.";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (await queue.Reader.WaitToReadAsync(stoppingToken))
            await RunQueuedAsync(stoppingToken);
    }

    /// <summary>
    /// Runs every computation queued right now, in order, and returns once the queue is empty. The loop above
    /// calls it; tests drive it directly, as the test host runs no hosted services.
    /// </summary>
    public async Task RunQueuedAsync(CancellationToken stoppingToken)
    {
        while (queue.Reader.TryRead(out var runId))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<IRecommendationRunner>().RunAsync(runId, stoppingToken);
            }
            // A shutdown is not an error: the run stays running, and the reconciler settles it at the next start.
            // Anything else — a cancellation too, when it isn't the shutdown's — is a bug.
            catch (Exception exception) when (!(exception is OperationCanceledException && stoppingToken.IsCancellationRequested))
            {
                logger.LogError(exception, "Recommendation run {RunId} threw out of the runner.", runId);
                await MarkFailedAsync(runId);
            }
        }
    }

    // Best effort, in a fresh scope: the run's own context may be what broke. If even this fails, the next run's
    // claim settles it (RecommendationRunner), or the reconciler at the next start.
    private async Task MarkFailedAsync(Guid runId)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().RecommendationRuns
                .Where(run => run.Id == runId && (run.Status == RecommendationRunStatus.Queued || run.Status == RecommendationRunStatus.Running))
                .MarkFailedAsync(AppErrorMessage, time.GetUtcNow().UtcDateTime);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Recommendation run {RunId} could not be marked failed.", runId);
        }
    }
}
