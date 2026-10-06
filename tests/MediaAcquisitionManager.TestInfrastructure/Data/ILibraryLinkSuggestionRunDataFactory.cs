using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.TestInfrastructure.Data;

/// <summary>MAM-34 — creates preparations of the TMDb suggestions directly in the database.</summary>
public interface ILibraryLinkSuggestionRunDataFactory
{
    /// <summary>
    /// A saved run in <paramref name="status"/>, queued at <paramref name="createdUtc"/> (the clock's now when
    /// <c>null</c>), started then unless queued and finished then if it ended. Stored as given, so a test can plant two
    /// runs the app would never keep side by side.
    /// </summary>
    Task<LibraryLinkSuggestionRunEntity> Create(
        LibraryLinkSuggestionRunStatus status,
        LibraryLinkSuggestionRunTrigger trigger = LibraryLinkSuggestionRunTrigger.Requested,
        Guid? requestedByUserId = null,
        string? requestedByUserName = null,
        DateTime? createdUtc = null,
        int? worksTotal = null,
        int worksDone = 0,
        string? error = null);
}
