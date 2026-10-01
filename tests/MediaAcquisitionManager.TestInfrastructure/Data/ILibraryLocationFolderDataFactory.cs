using MediaAcquisitionManager.Core.Entities;

namespace MediaAcquisitionManager.TestInfrastructure.Data;

/// <summary>Creates root folders of a media library directly in the database (MAM-11).</summary>
public interface ILibraryLocationFolderDataFactory
{
    /// <summary>A saved root folder <paramref name="servicePath"/> of the library
    /// <paramref name="libraryLocationId"/>, stored as given, at <paramref name="position"/> in the
    /// library's list.</summary>
    Task<LibraryLocationFolderEntity> Create(Guid libraryLocationId, string servicePath, int position = 0);
}
