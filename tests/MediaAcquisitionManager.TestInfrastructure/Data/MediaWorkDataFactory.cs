using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Library;
using MediaAcquisitionManager.Infrastructure.Persistence;

namespace MediaAcquisitionManager.TestInfrastructure.Data;

/// <inheritdoc cref="IMediaWorkDataFactory"/>
/// <remarks>Titles and identifiers are the work's own rows, so they are added through the work's
/// factory rather than factories of their own.</remarks>
public sealed class MediaWorkDataFactory(ApplicationDbContext db, TimeProvider time) : IMediaWorkDataFactory
{
    public async Task<MediaWorkEntity> Create(MediaWorkKind kind = MediaWorkKind.Movie)
    {
        var work = new MediaWorkEntity { Kind = kind, CreatedUtc = time.GetUtcNow().UtcDateTime };
        db.MediaWorks.Add(work);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return work;
    }

    public async Task<MediaWorkTitleEntity> AddTitle(
        Guid workId,
        string text,
        MediaTitleKind kind,
        int? year,
        Guid? sourceConnectionId,
        string? sourceName,
        string? sourceItemId)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var title = new MediaWorkTitleEntity
        {
            MediaWorkId = workId,
            Text = text,
            NormalizedText = LibrarySearchText.Normalize(text),
            Kind = kind,
            Year = year,
            SourceKind = ProvenanceSourceKind.MediaLibrary,
            SourceConnectionId = sourceConnectionId,
            SourceName = sourceName,
            SourceItemId = sourceItemId,
            CreatedUtc = now,
            ObservedUtc = now,
        };

        db.MediaWorkTitles.Add(title);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return title;
    }

    public async Task<MediaWorkIdentifierEntity> AddIdentifier(
        Guid workId,
        MediaIdentifierScheme scheme,
        string value,
        Guid? sourceConnectionId,
        string? sourceName,
        string? sourceItemId)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var identifier = new MediaWorkIdentifierEntity
        {
            MediaWorkId = workId,
            Scheme = scheme,
            Value = value,
            SourceKind = ProvenanceSourceKind.MediaLibrary,
            SourceConnectionId = sourceConnectionId,
            SourceName = sourceName,
            SourceItemId = sourceItemId,
            CreatedUtc = now,
            ObservedUtc = now,
        };

        db.MediaWorkIdentifiers.Add(identifier);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return identifier;
    }
}
