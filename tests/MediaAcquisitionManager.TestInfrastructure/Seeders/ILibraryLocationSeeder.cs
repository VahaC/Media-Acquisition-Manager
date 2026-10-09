using MediaAcquisitionManager.Core.Entities;

namespace MediaAcquisitionManager.TestInfrastructure.Seeders;

/// <summary>The libraries of a media library integration for a test, with their root folders, as a sync stores them
/// (MAM-11) — without a Jellyfin to sync from.</summary>
public interface ILibraryLocationSeeder
{
    /// <summary>
    /// The library <paramref name="name"/> of <paramref name="integration"/>, with its root folders
    /// <paramref name="servicePaths"/> as the server reports them, in their order; its own id made up.
    /// </summary>
    Task<LibraryLocationEntity> Create(IntegrationConnectionEntity integration, string name, params string[] servicePaths);
}
