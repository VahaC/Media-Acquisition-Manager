using MediaAcquisitionManager.Core.Entities;

namespace MediaAcquisitionManager.TestInfrastructure.Data;

/// <summary>Creates destination mappings of a download client directly in the database.</summary>
public interface IDestinationMappingDataFactory
{
    /// <summary>A saved logical destination <paramref name="name"/> of the client
    /// <paramref name="downloadClientConnectionId"/>, sent to it as <paramref name="clientDestinationKey"/>.</summary>
    Task<DestinationMappingEntity> Create(Guid downloadClientConnectionId, string name = "Movies", string clientDestinationKey = "Movies");
}
