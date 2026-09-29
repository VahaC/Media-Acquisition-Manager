using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Infrastructure.Persistence;

namespace MediaAcquisitionManager.TestInfrastructure.Data;

/// <inheritdoc cref="IDestinationMappingDataFactory"/>
public sealed class DestinationMappingDataFactory(ApplicationDbContext db, TimeProvider time) : IDestinationMappingDataFactory
{
    public async Task<DestinationMappingEntity> Create(Guid downloadClientConnectionId, string name = "Movies", string clientDestinationKey = "Movies")
    {
        var destination = new DestinationMappingEntity
        {
            DownloadClientConnectionId = downloadClientConnectionId,
            Name = name,
            // Trimmed and upper-cased, as the entity documents; MAM-11 brings the rules that write it.
            NormalizedName = name.Trim().ToUpperInvariant(),
            ClientDestinationKey = clientDestinationKey,
            CreatedUtc = time.GetUtcNow().UtcDateTime,
        };

        db.DestinationMappings.Add(destination);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return destination;
    }
}
