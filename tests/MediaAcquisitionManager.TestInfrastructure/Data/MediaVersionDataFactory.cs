using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Infrastructure.Persistence;

namespace MediaAcquisitionManager.TestInfrastructure.Data;

/// <inheritdoc cref="IMediaVersionDataFactory"/>
public sealed class MediaVersionDataFactory(ApplicationDbContext db, TimeProvider time) : IMediaVersionDataFactory
{
    public async Task<MediaVersionEntity> Create(
        Guid workId,
        string? sourceItemId,
        string? sourceVersionId = null,
        string? editionLabel = null,
        Guid? sourceConnectionId = null,
        string? sourceName = null)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var version = new MediaVersionEntity
        {
            MediaWorkId = workId,
            SourceVersionId = sourceVersionId,
            EditionLabel = editionLabel,
            SourceKind = ProvenanceSourceKind.MediaLibrary,
            SourceConnectionId = sourceConnectionId,
            SourceName = sourceName,
            SourceItemId = sourceItemId,
            CreatedUtc = now,
            ObservedUtc = now,
        };

        db.MediaVersions.Add(version);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return version;
    }
}
