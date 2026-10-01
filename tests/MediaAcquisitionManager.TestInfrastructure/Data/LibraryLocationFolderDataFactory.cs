using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Infrastructure.Persistence;

namespace MediaAcquisitionManager.TestInfrastructure.Data;

/// <inheritdoc cref="ILibraryLocationFolderDataFactory"/>
public sealed class LibraryLocationFolderDataFactory(ApplicationDbContext db) : ILibraryLocationFolderDataFactory
{
    public async Task<LibraryLocationFolderEntity> Create(Guid libraryLocationId, string servicePath, int position = 0)
    {
        var folder = new LibraryLocationFolderEntity { LibraryLocationId = libraryLocationId, Position = position, ServicePath = servicePath };
        db.LibraryLocationFolders.Add(folder);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return folder;
    }
}
