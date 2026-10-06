using MediaAcquisitionManager.Core.Entities;

namespace MediaAcquisitionManager.TestInfrastructure.Seeders;

/// <summary>MAM-34 — what file inspections measured, for a test: a library file's duration as ffprobe read it.</summary>
public interface IMediaInspectionSeeder
{
    /// <summary>
    /// What an inspection run stores for the library file named <paramref name="servicePath"/> when ffprobe measured it
    /// at <paramref name="duration"/>: a reading of its own — a fresh identity, so no two collide — and the file pointing
    /// at it, inspected now. Throws unless exactly one file has that path.
    /// </summary>
    Task<MediaInspectionEntity> Measure(string servicePath, TimeSpan duration);
}
