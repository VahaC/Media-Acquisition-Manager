using MediaAcquisitionManager.Core.Entities;

namespace MediaAcquisitionManager.TestInfrastructure.Seeders;

/// <summary>Release snapshots for a test, as a release search integration listed them.</summary>
public interface IReleaseSeeder
{
    /// <summary>A release snapshot listed by <paramref name="source"/>, with its id and name as the
    /// provenance of the row.</summary>
    Task<ReleaseEntity> Create(IntegrationConnectionEntity source);
}
