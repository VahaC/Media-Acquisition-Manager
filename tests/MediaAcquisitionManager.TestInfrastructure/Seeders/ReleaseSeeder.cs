using MediaAcquisitionManager.Core.Abstractions;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.TestInfrastructure.Data;

namespace MediaAcquisitionManager.TestInfrastructure.Seeders;

/// <inheritdoc cref="IReleaseSeeder"/>
public sealed class ReleaseSeeder(IServiceProvider services) : SeederBase(services), IReleaseSeeder
{
    private IReleaseDataFactory Releases => new ReleaseDataFactory(Db, Resolve<IEncryptionService>(), Resolve<TimeProvider>());

    public Task<ReleaseEntity> Create(IntegrationConnectionEntity source) => Releases.Create(source.Id, source.Name);
}
