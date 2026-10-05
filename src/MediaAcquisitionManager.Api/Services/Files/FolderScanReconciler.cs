using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Services.Files;

/// <summary>
/// Settles the scans of the media folders a stop interrupted (MAM-18): a running scan is failed and a
/// queued one is queued again. Unlike the inspection's, it queues no scan for the start itself.
/// </summary>
/// <remarks>
/// <para>
/// A running scan died with the process, and a scan stores what it found only when its walk is over, so
/// nothing it found was stored; the owner scans again. A queued one is queued again, as the inspection's
/// is: the owner asked for it, and it walks the folders as they are when it starts.
/// </para>
/// <para>
/// The owner chose that a scan runs only when asked (ADR 0021): no timer, not after a sync, not at the
/// app's start — a walk of every allowed folder is the owner's to start.
/// </para>
/// </remarks>
public sealed class FolderScanReconciler(
    IServiceScopeFactory scopes,
    FolderScanQueue queue,
    TimeProvider time,
    ILogger<FolderScanReconciler> logger) : IHostedService
{
    private const string RestartedError = "The app restarted before this scan finished, so nothing it found was stored. Scan the folders again.";

    public Task StartAsync(CancellationToken cancellationToken) => ReconcileAsync(cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>Fails the running scan and queues the queued one again. Start calls it; tests drive it
    /// directly, as the test host runs no hosted services.</summary>
    public async Task ReconcileAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        // Never cancelled: a terminal write (FolderScanRunUpdates).
        var failed = await db.FolderScanRuns
            .Where(run => run.Status == FolderScanStatus.Running)
            .MarkFailedAsync(RestartedError, time.GetUtcNow().UtcDateTime);
        if (failed > 0)
        {
            // Information, not Warning: nothing is lost — the names stored before stay as they were.
            logger.LogInformation("{Count} folder scans were running when the app stopped; they are marked failed.", failed);
        }

        // The queue lives in memory, so a run queued before the stop has no id in it any more. One the
        // queue refuses is failed with the service's sentence: left queued with no id, every later Scan
        // folders would join it and nothing would run until the next start.
        var queued = await db.FolderScanRuns
            .Where(run => run.Status == FolderScanStatus.Queued)
            .Select(run => run.Id)
            .ToListAsync(cancellationToken);
        foreach (var runId in queued.Where(runId => !queue.TryEnqueue(runId)))
        {
            await db.FolderScanRuns
                .Where(run => run.Id == runId && run.Status == FolderScanStatus.Queued)
                .MarkFailedAsync(FolderScanService.QueueFullError, time.GetUtcNow().UtcDateTime);
        }
    }
}
