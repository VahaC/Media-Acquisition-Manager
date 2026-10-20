using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Texts;
using MediaAcquisitionManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Services.Files;

/// <summary>
/// Settles the file inspections a stop interrupted, and queues the one the app's start owes (MAM-9): a
/// running run is failed, a queued one is queued again, and a run is queued for the start itself.
/// </summary>
/// <remarks>
/// <para>
/// Unlike a sync's (MAM-8), a queued inspection is queued again rather than failed: it asks for nothing
/// but "look at the files", which is still worth doing, and the start would queue one anyway. A running
/// one died with the process; what it had written on the files stays, so the new run only reads what the
/// cache doesn't hold.
/// </para>
/// <para>
/// The owner chose that the app's start inspects (it picks up whatever a stop interrupted, and files that
/// changed while the app was down). With no folder allowed, which is the default, nothing is queued.
/// </para>
/// </remarks>
public sealed class FileInspectionReconciler(
    IServiceScopeFactory scopes,
    FileInspectionQueue queue,
    TimeProvider time,
    ILogger<FileInspectionReconciler> logger) : IHostedService
{
    private static readonly Message RestartedError = Message.Of("files.inspection.restarted");

    public Task StartAsync(CancellationToken cancellationToken) => ReconcileAsync(cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>Fails the running run, queues the queued one again and queues the start's run. Start
    /// calls it; tests drive it directly, as the test host runs no hosted services.</summary>
    public async Task ReconcileAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        // Never cancelled: a terminal write (FileInspectionRunUpdates).
        var failed = await db.FileInspectionRuns
            .Where(run => run.Status == FileInspectionRunStatus.Running)
            .MarkFailedAsync(RestartedError, time.GetUtcNow().UtcDateTime);
        if (failed > 0)
        {
            // Information, not Warning: nothing is lost — the start's run carries on from what was written.
            logger.LogInformation("{Count} file inspections were running when the app stopped; they are marked failed.", failed);
        }

        // The queue lives in memory, so a run queued before the stop has no id in it any more. One the
        // queue refuses is failed with the service's sentence: left queued with no id, every later
        // trigger would join it and nothing would run until the next start.
        var queued = await db.FileInspectionRuns
            .Where(run => run.Status == FileInspectionRunStatus.Queued)
            .Select(run => run.Id)
            .ToListAsync(cancellationToken);
        foreach (var runId in queued.Where(runId => !queue.TryEnqueue(runId)))
        {
            await db.FileInspectionRuns
                .Where(run => run.Id == runId && run.Status == FileInspectionRunStatus.Queued)
                .MarkFailedAsync(FileInspectionService.QueueFullError, time.GetUtcNow().UtcDateTime);
        }

        // Joins the run queued again just above, if there is one; does nothing without an allowed root.
        await scope.ServiceProvider.GetRequiredService<IFileInspectionService>()
            .EnsureQueuedAsync(FileInspectionTrigger.AppStarted, cancellationToken);
    }
}
