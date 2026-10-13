namespace MediaAcquisitionManager.Api.Services.Files;

/// <summary>
/// MAM-42 — the files the scan of the media folders found that no library reports now (ADR 0021): listed for every
/// signed-in user, and ignored or counted again on the installation owner's word. Ignoring changes nothing on the disk
/// or in a library; it only takes the file off the counts. Expected failures come back as result statuses, never as
/// exceptions.
/// </summary>
public interface IUnreportedFileService
{
    /// <summary>
    /// One page of the files of <paramref name="view"/> (blank: every file the app counts), with what each tab holds;
    /// the reasons the app couldn't read a file only when <paramref name="callerUserId"/> is the installation owner.
    /// <see cref="UnreportedFileListStatus.Invalid"/> for a page, page size or view the list doesn't take.
    /// </summary>
    Task<UnreportedFileListResult> ListAsync(string? view, int page, int pageSize, Guid callerUserId, CancellationToken cancellationToken = default);

    /// <summary>Stops counting the file <paramref name="fileId"/>, with an audit row for the owner
    /// <paramref name="ownerUserId"/>; nothing recorded when it already was ignored.</summary>
    Task<UnreportedFileIgnoreStatus> IgnoreAsync(Guid fileId, Guid ownerUserId, CancellationToken cancellationToken = default);

    /// <summary>Counts the file <paramref name="fileId"/> again, with an audit row for the owner
    /// <paramref name="ownerUserId"/>; nothing recorded when it wasn't ignored.</summary>
    Task<UnreportedFileIgnoreStatus> UndoIgnoreAsync(Guid fileId, Guid ownerUserId, CancellationToken cancellationToken = default);
}
