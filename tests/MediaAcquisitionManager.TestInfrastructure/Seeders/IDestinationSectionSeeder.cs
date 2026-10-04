using MediaAcquisitionManager.Core.Entities;

namespace MediaAcquisitionManager.TestInfrastructure.Seeders;

/// <summary>Release source sections mapped to destinations for a test (MAM-32), planted without the
/// settings page.</summary>
public interface IDestinationSectionSeeder
{
    /// <summary>
    /// Maps each of <paramref name="categoryIds"/> of <paramref name="source"/> to
    /// <paramref name="destination"/> as the section <paramref name="name"/> — the source's own section by
    /// default, a standard category when <paramref name="isSourceSpecific"/> is false. Stored as given,
    /// without asking the source, so a test can plant a category the source doesn't list.
    /// </summary>
    Task<IReadOnlyList<DestinationSectionEntity>> Map(
        DestinationMappingEntity destination,
        IntegrationConnectionEntity source,
        string name,
        bool isSourceSpecific = true,
        params string[] categoryIds);
}
