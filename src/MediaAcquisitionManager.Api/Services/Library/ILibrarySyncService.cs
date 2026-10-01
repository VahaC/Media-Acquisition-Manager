using MediaAcquisitionManager.Api.Contracts;

namespace MediaAcquisitionManager.Api.Services.Library;

/// <summary>
/// The Library page's sync side (MAM-8): the media library integrations with where their syncs stand,
/// one run, and starting a sync, which queues it for <see cref="LibrarySyncWorker"/> (202 + run row +
/// reconciler). Expected failures come back as result statuses, never as exceptions.
/// </summary>
/// <remarks>
/// Starting checks nothing about who calls — that only the owner may is the endpoint's business. The
/// reads take the caller (MAM-8): a run's error can name the service's address, a source's path problem
/// quotes the owner's path mappings, and the name of who started a run is the owner's sign-in name, as
/// only the owner starts one; integrations and accounts are the owner's, so all three are left out for
/// anyone else.
/// </remarks>
public interface ILibrarySyncService
{
    /// <summary>
    /// Records a queued sync of <paramref name="integrationId"/> requested by
    /// <paramref name="requestedByUserId"/> and queues it. <see cref="LibrarySyncStartStatus.NotFound"/>
    /// when no media library integration has the id; <see cref="LibrarySyncStartStatus.Conflict"/> when it
    /// is switched off, of a type this version can't read a library from, or already being synced — also
    /// when another start won the race; <see cref="LibrarySyncStartStatus.Unavailable"/> when the queue is
    /// full, with the run recorded as failed.
    /// </summary>
    Task<LibrarySyncStartResult> StartAsync(Guid integrationId, Guid requestedByUserId, CancellationToken cancellationToken = default);

    /// <summary>The run <paramref name="runId"/>, its error and who started it only when
    /// <paramref name="callerUserId"/> is the installation owner; <c>null</c> when there is no such
    /// run.</summary>
    Task<LibrarySyncRunDto?> GetRunAsync(Guid runId, Guid callerUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Every media library integration, ordered by name, with its latest and last successful run and how
    /// many of its files the current path mappings give no path in this app; the runs' errors and who
    /// started them, and the path problem, only when <paramref name="callerUserId"/> is the installation
    /// owner.
    /// </summary>
    Task<LibrarySourceListResponse> ListSourcesAsync(Guid callerUserId, CancellationToken cancellationToken = default);
}
