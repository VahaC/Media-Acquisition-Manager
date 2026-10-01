using MediaAcquisitionManager.Core.Entities;

namespace MediaAcquisitionManager.TestInfrastructure.Seeders;

/// <summary>Destinations for a test (MAM-11), planted without the settings page.</summary>
public interface IDestinationMappingSeeder
{
    /// <summary>
    /// A saved destination <paramref name="name"/> of <paramref name="client"/>, sent to it as
    /// <paramref name="clientDestinationKey"/>; the installation's default when <paramref name="isDefault"/>.
    /// Stored as given, without asking the client, so a test can plant one whose category the client no
    /// longer has.
    /// </summary>
    Task<DestinationMappingEntity> Create(
        IntegrationConnectionEntity client,
        string name = "Movies",
        string clientDestinationKey = "Movies",
        bool isDefault = false);
}
