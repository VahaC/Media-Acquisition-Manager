using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Infrastructure.Persistence;

namespace MediaAcquisitionManager.TestInfrastructure.Data;

/// <inheritdoc cref="IFileHashDataFactory"/>
public sealed class FileHashDataFactory(ApplicationDbContext db, TimeProvider time) : IFileHashDataFactory
{
    public async Task<FileHashEntity> Create(long deviceId, long inode, long sizeBytes, DateTime modifiedUtc, string sha256, DateTime? createdUtc = null)
    {
        var hash = new FileHashEntity
        {
            DeviceId = deviceId,
            Inode = inode,
            SizeBytes = sizeBytes,
            ModifiedUtc = modifiedUtc,
            Sha256 = sha256,
            CreatedUtc = createdUtc ?? time.GetUtcNow().UtcDateTime,
        };

        db.FileHashes.Add(hash);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return hash;
    }
}
