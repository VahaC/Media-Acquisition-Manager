using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Infrastructure.Persistence;

namespace MediaAcquisitionManager.TestInfrastructure.Data;

/// <inheritdoc cref="IPhysicalFileDataFactory"/>
public sealed class PhysicalFileDataFactory(ApplicationDbContext db, TimeProvider time) : IPhysicalFileDataFactory
{
    public async Task<PhysicalFileEntity> Create(
        Guid versionId,
        string servicePath,
        long? sizeBytes = null,
        long? deviceId = null,
        long? inode = null,
        Guid? sourceConnectionId = null,
        string? sourceName = null)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var file = new PhysicalFileEntity
        {
            MediaVersionId = versionId,
            ServicePath = servicePath,
            SizeBytes = sizeBytes,
            DeviceId = deviceId,
            Inode = inode,
            SourceKind = sourceConnectionId is null ? ProvenanceSourceKind.FileInspection : ProvenanceSourceKind.MediaLibrary,
            SourceConnectionId = sourceConnectionId,
            SourceName = sourceName,
            CreatedUtc = now,
            ObservedUtc = now,
        };

        db.PhysicalFiles.Add(file);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return file;
    }
}
