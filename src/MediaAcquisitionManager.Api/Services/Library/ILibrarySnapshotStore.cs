using MediaAcquisitionManager.Core.Abstractions.Library;

namespace MediaAcquisitionManager.Api.Services.Library;

/// <summary>
/// Stores what a media library integration reported as that integration's snapshot of works, versions,
/// files and libraries with their root folders (MAM-11), and forgets it again when the integration is
/// removed (MAM-8, ADR 0008).
/// </summary>
/// <remarks>
/// <para>
/// A snapshot is one source's alone: a sync replaces only the rows of that integration whose source kind
/// is <c>MediaLibrary</c>, and never puts its facts on a work that holds another source's (joining two
/// libraries' works waits for a second library, ADR 0008).
/// </para>
/// <para>
/// Ids are stable across syncs: a fact is found again by its provenance keys, not re-created, so a work,
/// a version or a file keeps its id and first-seen time while the library keeps reporting it.
/// </para>
/// </remarks>
public interface ILibrarySnapshotStore
{
    /// <summary>
    /// Replaces the snapshot of <paramref name="integrationId"/> with <paramref name="locations"/> and
    /// <paramref name="items"/> — a library read in full — and marks the run <paramref name="runId"/>
    /// succeeded with its counts, all in one transaction: the snapshot and the run's outcome are
    /// committed together or not at all.
    /// </summary>
    /// <returns>
    /// <see cref="LibrarySnapshotStatus.SourceRemoved"/> when the integration is gone, and
    /// <see cref="LibrarySnapshotStatus.RunNoLongerRunning"/> when something else has finished the run;
    /// nothing is written in either case.
    /// </returns>
    Task<LibrarySnapshotResult> ApplyAsync(
        Guid runId,
        Guid integrationId,
        IReadOnlyList<LibraryLocation> locations,
        IReadOnlyList<LibraryItem> items,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes the snapshot of <paramref name="integrationId"/> — its titles, identifiers, versions,
    /// episode positions (MAM-32), files and libraries with their root folders, and the works left with
    /// none — inside the caller's transaction. The facts of any other integration or source kind stay.
    /// </summary>
    /// <remarks>
    /// A removed version takes all its files with it by the database's cascade, whatever source
    /// reported them — so does a version a sync no longer reports. Only the library itself puts files on
    /// its versions: MAM-9's inspection writes what it found on the library's own names and keeps what it
    /// read in readings keyed by the file's identity, which no name owns, so nothing it read goes with a
    /// version (ADR 0009); the next inspection forgets the readings no name points at.
    /// </remarks>
    Task PurgeAsync(Guid integrationId, CancellationToken cancellationToken = default);
}
