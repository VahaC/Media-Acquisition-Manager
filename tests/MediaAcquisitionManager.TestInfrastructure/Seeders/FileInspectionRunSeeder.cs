using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.TestInfrastructure.Data;

namespace MediaAcquisitionManager.TestInfrastructure.Seeders;

/// <inheritdoc cref="IFileInspectionRunSeeder"/>
public sealed class FileInspectionRunSeeder(IServiceProvider services) : SeederBase(services), IFileInspectionRunSeeder
{
    private IFileInspectionRunDataFactory Runs => new FileInspectionRunDataFactory(Db, Resolve<TimeProvider>());

    public Task<FileInspectionRunEntity> Create(
        FileInspectionRunStatus status,
        FileInspectionTrigger trigger = FileInspectionTrigger.Requested,
        UserEntity? requestedBy = null,
        DateTime? createdUtc = null,
        string? error = null,
        int? filesTotal = null,
        int filesChecked = 0,
        int filesProbed = 0,
        PlantedHashing? hashing = null) =>
        Runs.Create(status, trigger, requestedBy?.Id, requestedBy?.UserName, createdUtc, error, filesTotal, filesChecked, filesProbed, hashing);
}
