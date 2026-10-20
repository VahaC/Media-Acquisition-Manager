using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Texts;
using MediaAcquisitionManager.Infrastructure.Persistence;

namespace MediaAcquisitionManager.Api.Services.Files;

/// <summary>
/// Runs the queued scans of the media folders one after another in the background (Queue/Worker, MAM-18):
/// a request answers 202 at once, and the Library page polls the sources.
/// </summary>
/// <remarks>
/// One scope per run and one try/catch per run, the <c>FileInspectionWorker</c> rules: a bug that throws
/// out of one run fails that run — logged — and the next still goes. One run at a time: two walks of the
/// same folders would find the same files and race to store them.
/// </remarks>
public sealed class FolderScanWorker(
    FolderScanQueue queue,
    IServiceScopeFactory scopes,
    TimeProvider time,
    ILogger<FolderScanWorker> logger) : BackgroundService
{
    internal static readonly Message AppErrorMessage = Message.Of("files.scan.app-error");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (await queue.Reader.WaitToReadAsync(stoppingToken))
            await RunQueuedAsync(stoppingToken);
    }

    /// <summary>
    /// Runs every scan queued right now, in order, and returns once the queue is empty. The loop above
    /// calls it; tests drive it directly, as the test host runs no hosted services.
    /// </summary>
    public async Task RunQueuedAsync(CancellationToken stoppingToken)
    {
        while (queue.Reader.TryRead(out var runId))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<IFolderScanRunner>().RunAsync(runId, stoppingToken);
            }
            // A shutdown is not an error: the run stays running, and the reconciler settles it at the next
            // start. Anything else — a cancellation too, when it isn't the shutdown's — is a bug.
            catch (Exception exception) when (!(exception is OperationCanceledException && stoppingToken.IsCancellationRequested))
            {
                logger.LogError(exception, "Folder scan {RunId} threw out of the runner.", runId);
                await MarkFailedAsync(runId);
            }
        }
    }

    // Best effort, in a fresh scope: the run's own context may be what broke. If even this fails, the next
    // run's claim settles it (FolderScanRunner), or the reconciler at the next start.
    private async Task MarkFailedAsync(Guid runId)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().FolderScanRuns
                .Where(run => run.Id == runId && (run.Status == FolderScanStatus.Queued || run.Status == FolderScanStatus.Running))
                .MarkFailedAsync(AppErrorMessage, time.GetUtcNow().UtcDateTime);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Folder scan {RunId} could not be marked failed.", runId);
        }
    }
}
