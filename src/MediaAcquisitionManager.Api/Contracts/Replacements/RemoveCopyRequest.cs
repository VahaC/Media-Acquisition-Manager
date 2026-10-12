namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>
/// MAM-40 — the owner's Remove this copy on the Duplicates page: the group and the file he pressed it on, and the identity
/// the page showed him, which the file must still have.
/// </summary>
/// <remarks>Every member nullable and without <c>[Required]</c>, <see cref="DeleteQuarantinedRequest"/>'s reason: a missing
/// group or file reaches the service's sentence, not a validation body.</remarks>
/// <param name="GroupKey">The group's key (<c>LibraryDuplicateGroup.key</c>).</param>
/// <param name="FileId">The file's id in the group (<c>LibraryDuplicateFile.file.id</c>).</param>
/// <param name="DeviceId">The file's device as the page showed it; <c>null</c> when it showed none.</param>
/// <param name="Inode">The file's inode as the page showed it; <c>null</c> when it showed none.</param>
/// <param name="SizeBytes">The file's size as the app's last look read it, as the page showed it; <c>null</c> when it
/// showed none.</param>
public sealed record RemoveCopyRequest(
    string? GroupKey = null,
    Guid? FileId = null,
    long? DeviceId = null,
    long? Inode = null,
    long? SizeBytes = null);
