using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Texts;
using MediaAcquisitionManager.Infrastructure.Persistence;

namespace MediaAcquisitionManager.Api.Services.Library;

/// <summary>
/// Runs the queued library syncs one after another in the background (Queue/Worker, MAM-8): the start
/// answers 202 at once, and the Library page polls the run row.
/// </summary>
/// <remarks>
/// One scope per run, so each sync has a DbContext of its own and nothing one sync tracked reaches the
/// next. One try/catch per run, so a bug that throws out of one sync fails that run — logged — and the
/// next queued run still goes. One at a time: syncs of different libraries don't need to overlap, and a
/// second writer would only wait for the first's write lock.
/// </remarks>
public sealed class LibrarySyncWorker(
    LibrarySyncQueue queue,
    IServiceScopeFactory scopes,
    TimeProvider time,
    ILogger<LibrarySyncWorker> logger) : BackgroundService
{
    private static readonly Message AppErrorMessage = Message.Of("library.sync.app-error");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (await queue.Reader.WaitToReadAsync(stoppingToken))
            await RunQueuedAsync(stoppingToken);
    }

    /// <summary>
    /// Runs every sync queued right now, in order, and returns once the queue is empty. The loop above
    /// calls it; tests drive it directly, as the test host runs no hosted services.
    /// </summary>
    public async Task RunQueuedAsync(CancellationToken stoppingToken)
    {
        while (queue.Reader.TryRead(out var runId))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<ILibrarySyncRunner>().RunAsync(runId, stoppingToken);
            }
            // A shutdown is not an error: the run stays running, and the reconciler fails it with the
            // restart sentence at the next start. Anything else — a cancellation too, when it isn't
            // the shutdown's — is a bug.
            catch (Exception exception) when (!(exception is OperationCanceledException && stoppingToken.IsCancellationRequested))
            {
                logger.LogError(exception, "Library sync {RunId} threw out of the runner.", runId);
                await MarkFailedAsync(runId);
            }
        }
    }

    // Best effort, in a fresh scope: the run's own context may be what broke. If even this fails, the
    // reconciler settles the run at the next start.
    private async Task MarkFailedAsync(Guid runId)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().LibrarySyncRuns
                .Where(run => run.Id == runId && (run.Status == LibrarySyncStatus.Queued || run.Status == LibrarySyncStatus.Running))
                .MarkFailedAsync(AppErrorMessage, time.GetUtcNow().UtcDateTime);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Library sync {RunId} could not be marked failed.", runId);
        }
    }
}
