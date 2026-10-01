using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.TestInfrastructure.Data;

/// <summary>Creates library sync runs directly in the database.</summary>
public interface ILibrarySyncRunDataFactory
{
    /// <summary>
    /// A saved run of <paramref name="integrationId"/> in <paramref name="status"/>, requested at
    /// <paramref name="createdUtc"/> (the clock's now when <c>null</c>): started at that instant unless
    /// it is still queued, and finished at it when it succeeded or failed. Values are stored as given,
    /// so a test can plant a row the app would never write (a second active run, say).
    /// </summary>
    Task<LibrarySyncRunEntity> Create(
        Guid? integrationId,
        string integrationName,
        LibrarySyncStatus status,
        Guid? requestedByUserId = null,
        string? requestedByUserName = null,
        DateTime? createdUtc = null,
        string? error = null);
}
