using MediaAcquisitionManager.Core.Entities;

namespace MediaAcquisitionManager.TestInfrastructure.Seeders;

/// <summary>MAM-33 — what a check of the release sources found for a catalog work, planted without asking any
/// source.</summary>
public interface IReleaseAvailabilitySeeder
{
    /// <summary>
    /// A check of <paramref name="work"/> made <paramref name="ago"/> before now (an hour when <c>null</c>) that found
    /// <paramref name="releaseCount"/> releases — <c>Found</c> when there is one — with the best of them.
    /// </summary>
    Task<ReleaseAvailabilityEntity> Create(
        CatalogWorkEntity work,
        int releaseCount,
        int? bestResolution = null,
        bool hasUkrainianAudio = false,
        int? maxSeeders = null,
        TimeSpan? ago = null);
}
