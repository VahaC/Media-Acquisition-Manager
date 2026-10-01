using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Library;
using MediaAcquisitionManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

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
        string? sourceName = null,
        Guid? libraryLocationId = null)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var file = new PhysicalFileEntity
        {
            MediaVersionId = versionId,
            ServicePath = servicePath,
            NormalizedServicePath = LibrarySearchText.Normalize(servicePath),
            SizeBytes = sizeBytes,
            DeviceId = deviceId,
            Inode = inode,
            SourceKind = sourceConnectionId is null ? ProvenanceSourceKind.FileInspection : ProvenanceSourceKind.MediaLibrary,
            SourceConnectionId = sourceConnectionId,
            SourceName = sourceName,
            LibraryLocationId = libraryLocationId,
            CreatedUtc = now,
            ObservedUtc = now,
        };

        db.PhysicalFiles.Add(file);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return file;
    }

    // Set-based, like FileInspectionRunner.WriteAsync.
    public Task Link(Guid fileId, Guid readingId, FileInspectionStatus status = FileInspectionStatus.Inspected)
    {
        FileInspectionStatus? inspectionStatus = status;
        Guid? mediaInspectionId = readingId;
        var inspectedUtc = time.GetUtcNow().UtcDateTime;
        return db.PhysicalFiles
            .Where(file => file.Id == fileId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(file => file.InspectionStatus, inspectionStatus)
                .SetProperty(file => file.InspectedUtc, inspectedUtc)
                .SetProperty(file => file.MediaInspectionId, mediaInspectionId));
    }
}
