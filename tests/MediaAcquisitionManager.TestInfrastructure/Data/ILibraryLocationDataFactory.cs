using MediaAcquisitionManager.Core.Entities;

namespace MediaAcquisitionManager.TestInfrastructure.Data;

/// <summary>Creates the libraries of a media library integration directly in the database.</summary>
public interface ILibraryLocationDataFactory
{
    /// <summary>
    /// A saved library <paramref name="name"/> as the library integration
    /// <paramref name="sourceConnectionId"/> reported it, with its own id <paramref name="sourceItemId"/>.
    /// </summary>
    Task<LibraryLocationEntity> Create(Guid? sourceConnectionId, string? sourceName, string sourceItemId, string name);
}
