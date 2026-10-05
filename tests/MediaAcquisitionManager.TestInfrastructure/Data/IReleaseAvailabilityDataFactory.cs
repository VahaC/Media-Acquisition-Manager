using MediaAcquisitionManager.Core.Entities;

namespace MediaAcquisitionManager.TestInfrastructure.Data;

/// <summary>MAM-33 — creates the availability checks of catalog works directly in the database.</summary>
public interface IReleaseAvailabilityDataFactory
{
    /// <summary>
    /// A saved check of the catalog work <paramref name="catalogWorkId"/> made at <paramref name="checkedUtc"/>: whether it
    /// <paramref name="found"/> releases, and the best of them.
    /// </summary>
    Task<ReleaseAvailabilityEntity> Create(
        Guid catalogWorkId,
        DateTime checkedUtc,
        bool found,
        int releaseCount = 0,
        int? bestResolution = null,
        bool hasUkrainianAudio = false,
        int? maxSeeders = null);
}
