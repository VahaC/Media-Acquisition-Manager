using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.TestInfrastructure.Data;

namespace MediaAcquisitionManager.TestInfrastructure.Seeders;

/// <inheritdoc cref="IReleaseAvailabilitySeeder"/>
public sealed class ReleaseAvailabilitySeeder(IServiceProvider services) : SeederBase(services), IReleaseAvailabilitySeeder
{
    private IReleaseAvailabilityDataFactory Checks => new ReleaseAvailabilityDataFactory(Db);

    public Task<ReleaseAvailabilityEntity> Create(
        CatalogWorkEntity work,
        int releaseCount,
        int? bestResolution = null,
        bool hasUkrainianAudio = false,
        int? maxSeeders = null,
        TimeSpan? ago = null)
    {
        ArgumentNullException.ThrowIfNull(work);
        var checkedUtc = Resolve<TimeProvider>().GetUtcNow().UtcDateTime - (ago ?? TimeSpan.FromHours(1));
        return Checks.Create(work.Id, checkedUtc, releaseCount > 0, releaseCount, bestResolution, hasUkrainianAudio, maxSeeders);
    }
}
