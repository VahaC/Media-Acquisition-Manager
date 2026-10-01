using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.TestInfrastructure.Data;

namespace MediaAcquisitionManager.TestInfrastructure.Seeders;

/// <inheritdoc cref="IDestinationMappingSeeder"/>
public sealed class DestinationMappingSeeder(IServiceProvider services) : SeederBase(services), IDestinationMappingSeeder
{
    private IDestinationMappingDataFactory Destinations => new DestinationMappingDataFactory(Db, Resolve<TimeProvider>());

    public Task<DestinationMappingEntity> Create(
        IntegrationConnectionEntity client,
        string name = "Movies",
        string clientDestinationKey = "Movies",
        bool isDefault = false) =>
        Destinations.Create(client.Id, name, clientDestinationKey, isDefault);
}
