using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Infrastructure.Persistence;

namespace MediaAcquisitionManager.TestInfrastructure.Data;

/// <inheritdoc cref="IReplacementFolderDataFactory"/>
public sealed class ReplacementFolderDataFactory(ApplicationDbContext db) : IReplacementFolderDataFactory
{
    public async Task<ReplacementFolderEntity> Create(string path, int position = 0)
    {
        var folder = new ReplacementFolderEntity { Path = path, Position = position };
        db.ReplacementFolders.Add(folder);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return folder;
    }
}
