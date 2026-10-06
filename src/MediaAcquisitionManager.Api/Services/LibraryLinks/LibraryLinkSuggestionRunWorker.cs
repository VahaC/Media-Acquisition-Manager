using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Infrastructure.Persistence;

namespace MediaAcquisitionManager.Api.Services.LibraryLinks;

/// <summary>
/// MAM-34 — runs the queued preparations of the TMDb suggestions one after another in the background (Queue/Worker): a
/// request answers 202 at once, and the TMDb page polls the latest run.
/// </summary>
/// <remarks>
/// One scope per run and one try/catch per run, the <c>RecommendationRunWorker</c> rules: a bug that throws out of one
/// run fails that run — logged — and the next still goes. One run at a time: two would ask the catalog for the same works
/// and replace each other's candidates.
/// </remarks>
public sealed class LibraryLinkSuggestionRunWorker(
    LibraryLinkSuggestionRunQueue queue,
    IServiceScopeFactory scopes,
    TimeProvider time,
    ILogger<LibraryLinkSuggestionRunWorker> logger) : BackgroundService
{
    internal const string AppErrorMessage = "The TMDb suggestions stopped being prepared because of an error in the app. Its log says more.";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (await queue.Reader.WaitToReadAsync(stoppingToken))
            await RunQueuedAsync(stoppingToken);
    }

    /// <summary>
    /// Runs every preparation queued right now, in order, and returns once the queue is empty. The loop above calls it;
    /// tests drive it directly, as the test host runs no hosted services.
    /// </summary>
    public async Task RunQueuedAsync(CancellationToken stoppingToken)
    {
        while (queue.Reader.TryRead(out var runId))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<ILibraryLinkSuggestionRunner>().RunAsync(runId, stoppingToken);
            }
            // A shutdown is not an error: the run stays running, and the reconciler settles it at the next start.
            // Anything else — a cancellation too, when it isn't the shutdown's — is a bug.
            catch (Exception exception) when (!(exception is OperationCanceledException && stoppingToken.IsCancellationRequested))
            {
                logger.LogError(exception, "TMDb suggestion run {RunId} threw out of the runner.", runId);
                await MarkFailedAsync(runId);
            }
        }
    }

    // Best effort, in a fresh scope: the run's own context may be what broke. If even this fails, the next run's claim
    // settles it (LibraryLinkSuggestionRunner), or the reconciler at the next start.
    private async Task MarkFailedAsync(Guid runId)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().LibraryLinkSuggestionRuns
                .Where(run => run.Id == runId
                    && (run.Status == LibraryLinkSuggestionRunStatus.Queued || run.Status == LibraryLinkSuggestionRunStatus.Running))
                .MarkFailedAsync(AppErrorMessage, time.GetUtcNow().UtcDateTime);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "TMDb suggestion run {RunId} could not be marked failed.", runId);
        }
    }
}
