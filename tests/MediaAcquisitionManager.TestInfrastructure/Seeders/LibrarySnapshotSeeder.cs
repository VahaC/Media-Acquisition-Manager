using MediaAcquisitionManager.Api.Services.Library;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.TestInfrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.TestInfrastructure.Seeders;

/// <inheritdoc cref="ILibrarySnapshotSeeder"/>
public sealed class LibrarySnapshotSeeder(IServiceProvider services) : SeederBase(services), ILibrarySnapshotSeeder
{
    private ILibrarySyncRunDataFactory Runs => new LibrarySyncRunDataFactory(Db, Resolve<TimeProvider>());

    public async Task<LibrarySyncRunEntity> Sync(IntegrationConnectionEntity integration, UserEntity? requestedBy = null)
    {
        var run = await Runs.Create(integration.Id, integration.Name, LibrarySyncStatus.Queued, requestedBy?.Id, requestedBy?.UserName);
        // The runner itself rather than the queue and the worker: the test gets the run's outcome here,
        // and nothing else it queued is taken along.
        await Resolve<ILibrarySyncRunner>().RunAsync(run.Id);
        Db.ChangeTracker.Clear();
        return await Db.LibrarySyncRuns.AsNoTracking().SingleAsync(stored => stored.Id == run.Id);
    }
}
