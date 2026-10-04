using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Infrastructure.Persistence;

namespace MediaAcquisitionManager.TestInfrastructure.Data;

/// <inheritdoc cref="IDestinationSectionDataFactory"/>
public sealed class DestinationSectionDataFactory(ApplicationDbContext db, TimeProvider time) : IDestinationSectionDataFactory
{
    public async Task<DestinationSectionEntity> Create(
        Guid destinationMappingId,
        Guid releaseSourceConnectionId,
        string categoryId,
        string name,
        bool isSourceSpecific = true)
    {
        var section = new DestinationSectionEntity
        {
            DestinationMappingId = destinationMappingId,
            ReleaseSourceConnectionId = releaseSourceConnectionId,
            CategoryId = categoryId,
            Name = name,
            IsSourceSpecific = isSourceSpecific,
            CreatedUtc = time.GetUtcNow().UtcDateTime,
        };

        db.DestinationSections.Add(section);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return section;
    }
}
