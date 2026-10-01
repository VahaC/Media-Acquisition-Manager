using MediaAcquisitionManager.Core.Downloads;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Infrastructure.Persistence;

namespace MediaAcquisitionManager.TestInfrastructure.Data;

/// <inheritdoc cref="IDestinationMappingDataFactory"/>
public sealed class DestinationMappingDataFactory(ApplicationDbContext db, TimeProvider time) : IDestinationMappingDataFactory
{
    public async Task<DestinationMappingEntity> Create(
        Guid downloadClientConnectionId,
        string name = "Movies",
        string clientDestinationKey = "Movies",
        bool isDefault = false)
    {
        var destination = new DestinationMappingEntity
        {
            DownloadClientConnectionId = downloadClientConnectionId,
            Name = name,
            // The rules' own form (MAM-11), so a seeded row collides with one the API saves.
            NormalizedName = DestinationRules.NormalizeName(name),
            ClientDestinationKey = clientDestinationKey,
            IsDefault = isDefault,
            CreatedUtc = time.GetUtcNow().UtcDateTime,
        };

        db.DestinationMappings.Add(destination);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return destination;
    }
}
