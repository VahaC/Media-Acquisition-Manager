using MediaAcquisitionManager.Core.Abstractions;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Search;
using MediaAcquisitionManager.Infrastructure.Persistence;

namespace MediaAcquisitionManager.TestInfrastructure.Data;

/// <inheritdoc cref="IReleaseDataFactory"/>
public sealed class ReleaseDataFactory(ApplicationDbContext db, IEncryptionService encryption, TimeProvider time) : IReleaseDataFactory
{
    public async Task<ReleaseEntity> Create(
        Guid? sourceConnectionId,
        string sourceName = "Torznab",
        string title = "Interstellar (2014)",
        string locator = "http://127.0.0.1:9/1/download?apikey=planted-release-key",
        Guid? searchSourceId = null,
        string? sourceItemId = null,
        string? infoHash = null,
        ReleasePayloadKind? payloadKind = null,
        string? categoryIds = null)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var release = new ReleaseEntity
        {
            SourceKind = ProvenanceSourceKind.ReleaseSearch,
            SourceConnectionId = sourceConnectionId,
            SourceName = sourceName,
            Title = title,
            SearchSourceId = searchSourceId,
            SourceItemId = sourceItemId,
            InfoHash = infoHash,
            PayloadKind = payloadKind,
            CategoryIds = categoryIds,
            CreatedUtc = now,
            ObservedUtc = now,
        };
        release.PayloadLocatorEncrypted = encryption.Encrypt(locator, ReleasePayloadLocatorPurpose.For(release.Id));

        db.Releases.Add(release);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return release;
    }
}
