using MediaAcquisitionManager.Core.Abstractions;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.TestInfrastructure.Data;

namespace MediaAcquisitionManager.TestInfrastructure.Seeders;

/// <inheritdoc cref="IDownloadJobSeeder"/>
public sealed class DownloadJobSeeder(IServiceProvider services) : SeederBase(services), IDownloadJobSeeder
{
    private IReleaseDataFactory Releases => new ReleaseDataFactory(Db, Resolve<IEncryptionService>(), Resolve<TimeProvider>());

    private IDestinationMappingDataFactory Destinations => new DestinationMappingDataFactory(Db, Resolve<TimeProvider>());

    private IDownloadJobDataFactory Jobs => new DownloadJobDataFactory(Db, Resolve<TimeProvider>());

    public async Task<DownloadJobEntity> Create(IntegrationConnectionEntity client)
    {
        // Listed by no saved search integration: the job is about the client, and the release only has
        // to exist for the job to point at.
        var release = await Releases.Create(sourceConnectionId: null);
        var destination = await Destinations.Create(client.Id);
        return await Jobs.Create(release.Id, client.Id, client.Name, destination.Id);
    }
}
