using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Texts;
using MediaAcquisitionManager.Infrastructure.Persistence;

namespace MediaAcquisitionManager.Api.Services.Library;

/// <summary>
/// Settles the library syncs a stop interrupted (MAM-8): at startup, every run still queued or running
/// is failed with a sentence saying to start it again.
/// </summary>
/// <remarks>
/// The queue lives in memory, so after a restart no worker will ever take a queued run, and a running
/// one died with the process; left alone, each would show "Syncing…" forever and its unique index would
/// refuse every new sync of its integration. Nothing else needs undoing: a sync writes its snapshot in
/// one transaction at its very end (ADR 0008), so an interrupted one stored nothing.
/// </remarks>
public sealed class LibrarySyncReconciler(
    IServiceScopeFactory scopes,
    TimeProvider time,
    ILogger<LibrarySyncReconciler> logger) : IHostedService
{
    private static readonly Message RestartedError = Message.Of("library.sync.restarted");

    public Task StartAsync(CancellationToken cancellationToken) => ReconcileAsync(cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>Fails every run still queued or running. Start calls it; tests drive it directly, as the
    /// test host runs no hosted services.</summary>
    /// <remarks><paramref name="cancellationToken"/> is not passed on: a terminal write is never
    /// cancelled (<see cref="LibrarySyncRunUpdates"/>).</remarks>
    public async Task ReconcileAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var failed = await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().LibrarySyncRuns
            .Where(run => run.Status == LibrarySyncStatus.Queued || run.Status == LibrarySyncStatus.Running)
            .MarkFailedAsync(RestartedError, time.GetUtcNow().UtcDateTime);
        if (failed > 0)
        {
            // Warning, so it ships off the box: a sync was cut short, and the owner has to start it again.
            logger.LogWarning("{Count} library syncs were still under way when the app stopped; they are marked failed.", failed);
        }
    }
}
