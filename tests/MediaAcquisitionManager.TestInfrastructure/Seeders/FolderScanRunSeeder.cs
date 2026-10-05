using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.TestInfrastructure.Data;

namespace MediaAcquisitionManager.TestInfrastructure.Seeders;

/// <inheritdoc cref="IFolderScanRunSeeder"/>
public sealed class FolderScanRunSeeder(IServiceProvider services) : SeederBase(services), IFolderScanRunSeeder
{
    private IFolderScanRunDataFactory Runs => new FolderScanRunDataFactory(Db, Resolve<TimeProvider>());

    public Task<FolderScanRunEntity> Create(
        FolderScanStatus status,
        UserEntity? requestedBy = null,
        DateTime? createdUtc = null,
        int filesFound = 0,
        int? filesUnreported = null,
        int problemCount = 0,
        string? problem = null,
        string? error = null) =>
        Runs.Create(status, requestedBy?.Id, requestedBy?.UserName, createdUtc, filesFound, filesUnreported, problemCount, problem, error);
}
