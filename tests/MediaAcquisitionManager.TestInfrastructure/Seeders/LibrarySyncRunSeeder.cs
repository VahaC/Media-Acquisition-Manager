using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.TestInfrastructure.Data;

namespace MediaAcquisitionManager.TestInfrastructure.Seeders;

/// <inheritdoc cref="ILibrarySyncRunSeeder"/>
public sealed class LibrarySyncRunSeeder(IServiceProvider services) : SeederBase(services), ILibrarySyncRunSeeder
{
    private ILibrarySyncRunDataFactory Runs => new LibrarySyncRunDataFactory(Db, Resolve<TimeProvider>());

    public Task<LibrarySyncRunEntity> Create(
        IntegrationConnectionEntity integration,
        LibrarySyncStatus status,
        UserEntity? requestedBy = null,
        DateTime? createdUtc = null,
        string? error = null) =>
        Runs.Create(integration.Id, integration.Name, status, requestedBy?.Id, requestedBy?.UserName, createdUtc, error);
}
