using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.TestInfrastructure.Data;

namespace MediaAcquisitionManager.TestInfrastructure.Seeders;

/// <inheritdoc cref="IDestinationSectionSeeder"/>
public sealed class DestinationSectionSeeder(IServiceProvider services) : SeederBase(services), IDestinationSectionSeeder
{
    private IDestinationSectionDataFactory Sections => new DestinationSectionDataFactory(Db, Resolve<TimeProvider>());

    public async Task<IReadOnlyList<DestinationSectionEntity>> Map(
        DestinationMappingEntity destination,
        IntegrationConnectionEntity source,
        string name,
        bool isSourceSpecific = true,
        params string[] categoryIds)
    {
        var sections = new List<DestinationSectionEntity>();
        foreach (var categoryId in categoryIds)
            sections.Add(await Sections.Create(destination.Id, source.Id, categoryId, name, isSourceSpecific));
        return sections;
    }
}
