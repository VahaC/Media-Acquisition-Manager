using MediaAcquisitionManager.Core.Abstractions;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Infrastructure.Persistence;

namespace MediaAcquisitionManager.TestInfrastructure.Data;

/// <inheritdoc cref="IReleaseDataFactory"/>
public sealed class ReleaseDataFactory(ApplicationDbContext db, IEncryptionService encryption, TimeProvider time) : IReleaseDataFactory
{
    /// <summary>
    /// The associated data a release's payload locator is sealed with, as <see cref="ReleaseEntity"/>
    /// documents it. Spelled out here because nothing in the app writes releases before MAM-13, which
    /// brings its own helper for it.
    /// </summary>
    public static string PayloadLocatorPurpose(Guid releaseId) => $"release/{releaseId:N}/payload-locator";

    public async Task<ReleaseEntity> Create(
        Guid? sourceConnectionId,
        string sourceName = "Torznab",
        string title = "Interstellar (2014)",
        string locator = "http://127.0.0.1:9/1/download?apikey=planted-release-key")
    {
        var now = time.GetUtcNow().UtcDateTime;
        var release = new ReleaseEntity
        {
            SourceKind = ProvenanceSourceKind.ReleaseSearch,
            SourceConnectionId = sourceConnectionId,
            SourceName = sourceName,
            Title = title,
            CreatedUtc = now,
            ObservedUtc = now,
        };
        release.PayloadLocatorEncrypted = encryption.Encrypt(locator, PayloadLocatorPurpose(release.Id));

        db.Releases.Add(release);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return release;
    }
}
