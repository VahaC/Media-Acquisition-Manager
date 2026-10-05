using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Infrastructure.Persistence;

namespace MediaAcquisitionManager.TestInfrastructure.Data;

/// <inheritdoc cref="ISourceFeedEntryDataFactory"/>
public sealed class SourceFeedEntryDataFactory(ApplicationDbContext db, TimeProvider time) : ISourceFeedEntryDataFactory
{
    public async Task<SourceFeedEntryEntity> Create(
        Guid sourceConnectionId,
        string sourceName,
        string sourceItemId,
        string title,
        ReleaseKind kind = ReleaseKind.Movie,
        DateTime? seenUtc = null,
        DateTime? publishedUtc = null,
        DateTime? resolvedUtc = null,
        Guid? catalogWorkId = null)
    {
        var seen = seenUtc ?? time.GetUtcNow().UtcDateTime;
        var entry = new SourceFeedEntryEntity
        {
            Title = title,
            Kind = kind,
            PublishedUtc = publishedUtc,
            ResolvedUtc = resolvedUtc,
            CatalogWorkId = catalogWorkId,
            SourceKind = ProvenanceSourceKind.ReleaseSearch,
            SourceConnectionId = sourceConnectionId,
            SourceName = sourceName,
            SourceItemId = sourceItemId,
            CreatedUtc = seen,
            ObservedUtc = seen,
        };

        db.SourceFeedEntries.Add(entry);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return entry;
    }
}
