using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Infrastructure.Persistence;

namespace MediaAcquisitionManager.TestInfrastructure.Data;

/// <inheritdoc cref="ILibraryLocationDataFactory"/>
public sealed class LibraryLocationDataFactory(ApplicationDbContext db, TimeProvider time) : ILibraryLocationDataFactory
{
    public async Task<LibraryLocationEntity> Create(Guid? sourceConnectionId, string? sourceName, string sourceItemId, string name)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var location = new LibraryLocationEntity
        {
            Name = name,
            SourceKind = ProvenanceSourceKind.MediaLibrary,
            SourceConnectionId = sourceConnectionId,
            SourceName = sourceName,
            SourceItemId = sourceItemId,
            CreatedUtc = now,
            ObservedUtc = now,
        };

        db.LibraryLocations.Add(location);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return location;
    }
}
