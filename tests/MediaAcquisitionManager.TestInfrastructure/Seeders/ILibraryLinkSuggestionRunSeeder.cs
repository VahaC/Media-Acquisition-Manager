using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.TestInfrastructure.Seeders;

/// <summary>MAM-34 — preparations of the TMDb suggestions for a test: runs that happened, or are under way, before it
/// started.</summary>
public interface ILibraryLinkSuggestionRunSeeder
{
    /// <summary>A run in <paramref name="status"/>, asked for by <paramref name="requestedBy"/> when given.</summary>
    Task<LibraryLinkSuggestionRunEntity> Create(
        LibraryLinkSuggestionRunStatus status,
        LibraryLinkSuggestionRunTrigger trigger = LibraryLinkSuggestionRunTrigger.Requested,
        UserEntity? requestedBy = null,
        DateTime? createdUtc = null,
        int? worksTotal = null,
        int worksDone = 0,
        string? error = null);
}
