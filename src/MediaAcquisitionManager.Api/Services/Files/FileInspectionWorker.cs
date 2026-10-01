using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Infrastructure.Persistence;

namespace MediaAcquisitionManager.Api.Services.Files;

/// <summary>
/// Runs the queued file inspections one after another in the background (Queue/Worker, MAM-9): a
/// request answers 202 at once, and the Library page polls the sources.
/// </summary>
/// <remarks>
/// One scope per run and one try/catch per run, the <c>LibrarySyncWorker</c> rules: a bug that throws out
/// of one run fails that run — logged — and the next still goes. One run at a time: a run already reads
/// two files at once (<see cref="FileInspectionRunner.MaximumConcurrentReads"/>), and two runs would read
/// the same files twice.
/// </remarks>
public sealed class FileInspectionWorker(
    FileInspectionQueue queue,
    IServiceScopeFactory scopes,
    TimeProvider time,
    ILogger<FileInspectionWorker> logger) : BackgroundService
{
    internal const string AppErrorMessage = "The inspection stopped because of an error in the app. Its log says more.";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (await queue.Reader.WaitToReadAsync(stoppingToken))
            await RunQueuedAsync(stoppingToken);
    }

    /// <summary>
    /// Runs every inspection queued right now, in order, and returns once the queue is empty. The loop
    /// above calls it; tests drive it directly, as the test host runs no hosted services.
    /// </summary>
    public async Task RunQueuedAsync(CancellationToken stoppingToken)
    {
        while (queue.Reader.TryRead(out var runId))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<IFileInspectionRunner>().RunAsync(runId, stoppingToken);
            }
            // A shutdown is not an error: the run stays running, and the reconciler settles it at the next
            // start. Anything else — a cancellation too, when it isn't the shutdown's — is a bug.
            catch (Exception exception) when (!(exception is OperationCanceledException && stoppingToken.IsCancellationRequested))
            {
                logger.LogError(exception, "File inspection {RunId} threw out of the runner.", runId);
                await MarkFailedAsync(runId);
            }
        }
    }

    // Best effort, in a fresh scope: the run's own context may be what broke. If even this fails, the next
    // run's claim settles it (FileInspectionRunner), or the reconciler at the next start.
    private async Task MarkFailedAsync(Guid runId)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().FileInspectionRuns
                .Where(run => run.Id == runId && (run.Status == FileInspectionRunStatus.Queued || run.Status == FileInspectionRunStatus.Running))
                .MarkFailedAsync(AppErrorMessage, time.GetUtcNow().UtcDateTime);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "File inspection {RunId} could not be marked failed.", runId);
        }
    }
}
