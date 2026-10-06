using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Infrastructure.Persistence;

namespace MediaAcquisitionManager.TestInfrastructure.Data;

/// <inheritdoc cref="ILibraryLinkSuggestionRunDataFactory"/>
public sealed class LibraryLinkSuggestionRunDataFactory(ApplicationDbContext db, TimeProvider time) : ILibraryLinkSuggestionRunDataFactory
{
    public async Task<LibraryLinkSuggestionRunEntity> Create(
        LibraryLinkSuggestionRunStatus status,
        LibraryLinkSuggestionRunTrigger trigger = LibraryLinkSuggestionRunTrigger.Requested,
        Guid? requestedByUserId = null,
        string? requestedByUserName = null,
        DateTime? createdUtc = null,
        int? worksTotal = null,
        int worksDone = 0,
        string? error = null)
    {
        var queued = createdUtc ?? time.GetUtcNow().UtcDateTime;
        var run = new LibraryLinkSuggestionRunEntity
        {
            Status = status,
            Trigger = trigger,
            RequestedByUserId = requestedByUserId,
            RequestedByUserName = requestedByUserName,
            StartedUtc = status == LibraryLinkSuggestionRunStatus.Queued ? null : queued,
            FinishedUtc = status is LibraryLinkSuggestionRunStatus.Succeeded or LibraryLinkSuggestionRunStatus.Failed ? queued : null,
            WorksTotal = worksTotal,
            WorksDone = worksDone,
            Error = error,
            CreatedUtc = queued,
        };

        db.LibraryLinkSuggestionRuns.Add(run);
        await db.SaveChangesAsync();
        // Detached, so the test's later reads come from the database, not from this tracked copy.
        db.ChangeTracker.Clear();
        return run;
    }
}
