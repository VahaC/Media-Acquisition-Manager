using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Infrastructure.Persistence;

namespace MediaAcquisitionManager.TestInfrastructure.Data;

/// <inheritdoc cref="IReleaseAvailabilityDataFactory"/>
public sealed class ReleaseAvailabilityDataFactory(ApplicationDbContext db) : IReleaseAvailabilityDataFactory
{
    public async Task<ReleaseAvailabilityEntity> Create(
        Guid catalogWorkId,
        DateTime checkedUtc,
        bool found,
        int releaseCount = 0,
        int? bestResolution = null,
        bool hasUkrainianAudio = false,
        int? maxSeeders = null)
    {
        var check = new ReleaseAvailabilityEntity
        {
            CatalogWorkId = catalogWorkId,
            CheckedUtc = checkedUtc,
            Found = found,
            ReleaseCount = releaseCount,
            BestResolution = bestResolution,
            HasUkrainianAudio = hasUkrainianAudio,
            MaxSeeders = maxSeeders,
            CreatedUtc = checkedUtc,
        };

        db.ReleaseAvailabilities.Add(check);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return check;
    }
}
