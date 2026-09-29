using MediaAcquisitionManager.Core.Entities;

namespace MediaAcquisitionManager.TestInfrastructure.Seeders;

/// <summary>Media works for a test, with what a library reported about them.</summary>
public interface IMediaWorkSeeder
{
    /// <summary>
    /// A movie with a title, an identifier, a version and a file — each asserted by
    /// <paramref name="source"/>, with its id and name as the provenance of the row.
    /// </summary>
    Task<MediaWorkEntity> Create(IntegrationConnectionEntity source);
}
