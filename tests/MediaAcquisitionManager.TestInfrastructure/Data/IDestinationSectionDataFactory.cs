using MediaAcquisitionManager.Core.Entities;

namespace MediaAcquisitionManager.TestInfrastructure.Data;

/// <summary>Creates release source sections mapped to destinations directly in the database (MAM-32).</summary>
public interface IDestinationSectionDataFactory
{
    /// <summary>A saved mapping of the category <paramref name="categoryId"/> of the release source
    /// <paramref name="releaseSourceConnectionId"/>, named <paramref name="name"/>, to the destination
    /// <paramref name="destinationMappingId"/>; the database refuses a second mapping of one category of a
    /// source.</summary>
    Task<DestinationSectionEntity> Create(
        Guid destinationMappingId,
        Guid releaseSourceConnectionId,
        string categoryId,
        string name,
        bool isSourceSpecific = true);
}
