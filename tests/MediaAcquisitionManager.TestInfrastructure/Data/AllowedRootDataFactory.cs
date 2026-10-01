using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Infrastructure.Persistence;

namespace MediaAcquisitionManager.TestInfrastructure.Data;

/// <inheritdoc cref="IAllowedRootDataFactory"/>
public sealed class AllowedRootDataFactory(ApplicationDbContext db) : IAllowedRootDataFactory
{
    public async Task<AllowedRootEntity> Create(string path, int position = 0)
    {
        var root = new AllowedRootEntity { Path = path, Position = position };
        db.AllowedRoots.Add(root);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return root;
    }
}
