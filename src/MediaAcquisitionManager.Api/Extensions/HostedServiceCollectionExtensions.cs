using MediaAcquisitionManager.Api.Services.Downloads;
using MediaAcquisitionManager.Api.Services.Files;
using MediaAcquisitionManager.Api.Services.Library;

namespace MediaAcquisitionManager.Api.Extensions;

public static class HostedServiceCollectionExtensions
{
    /// <summary>
    /// Every background service of the app, in the order they start. Called last in <c>Program</c>, so
    /// the list reads in one place.
    /// </summary>
    /// <remarks>
    /// Each is registered once as a singleton and handed to the host from there, so a test resolves the
    /// very instance the host would run and drives it by hand (the test host removes the hosted
    /// services, never the singletons).
    /// </remarks>
    public static IServiceCollection AddMediaAcquisitionManagerHostedServices(this IServiceCollection services)
    {
        // MAM-8 — hosted services start in registration order, each StartAsync finishing before the next
        // begins: the reconciler fails the syncs a stop interrupted before the worker takes new ones, so
        // it can never fail a run the worker has just started.
        services.AddSingleton<LibrarySyncReconciler>();
        services.AddHostedService(provider => provider.GetRequiredService<LibrarySyncReconciler>());
        services.AddSingleton<LibrarySyncWorker>();
        services.AddHostedService(provider => provider.GetRequiredService<LibrarySyncWorker>());
        // MAM-9 — the same order for file inspections: the reconciler fails the run a stop interrupted and
        // queues the start's run before the worker takes any, so the worker never claims a run while a
        // dead one still holds the running slot (IX_FileInspectionRuns_OneRunPerActiveStatus).
        services.AddSingleton<FileInspectionReconciler>();
        services.AddHostedService(provider => provider.GetRequiredService<FileInspectionReconciler>());
        services.AddSingleton<FileInspectionWorker>();
        services.AddHostedService(provider => provider.GetRequiredService<FileInspectionWorker>());
        // MAM-18 — the same order for folder scans: the reconciler fails the scan a stop interrupted before the
        // worker takes any, so the worker never claims a scan while a dead one still holds the running slot
        // (IX_FolderScanRuns_OneRunPerActiveStatus). After the inspection's: a scan that finishes queues an
        // inspection, which then joins the start's run the inspection reconciler queued rather than racing it.
        services.AddSingleton<FolderScanReconciler>();
        services.AddHostedService(provider => provider.GetRequiredService<FolderScanReconciler>());
        services.AddSingleton<FolderScanWorker>();
        services.AddHostedService(provider => provider.GetRequiredService<FolderScanWorker>());
        // MAM-14 — the download jobs' tracker, last: it needs nothing settled before it, as every pass reads the
        // followed jobs from the rows (a restart resumes where they are), and a start that only enters its loop
        // holds up nothing after it. No reconciler: a send a stop interrupted is the tracker's own to settle,
        // once it is old enough (DownloadJobPoller.InterruptedSendAge).
        services.AddSingleton<DownloadJobTracker>();
        services.AddHostedService(provider => provider.GetRequiredService<DownloadJobTracker>());
        return services;
    }
}
