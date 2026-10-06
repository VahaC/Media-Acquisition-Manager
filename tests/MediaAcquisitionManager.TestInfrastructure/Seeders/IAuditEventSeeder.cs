using MediaAcquisitionManager.Api.Services.Audit;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.TestInfrastructure.Seeders;

/// <summary>Audit rows for a test — history that happened before it started.</summary>
public interface IAuditEventSeeder
{
    /// <summary>An audit row recording <paramref name="action"/>, done by <paramref name="actor"/> to
    /// <paramref name="target"/>.</summary>
    Task<AuditEventEntity> Create(AuditAction action, UserEntity? actor = null, UserEntity? target = null);

    /// <summary>MAM-34 — an audit row recording <paramref name="action"/> on <paramref name="link"/>, named
    /// <paramref name="targetName"/>, with the ids it found and left, at <paramref name="createdUtc"/> when
    /// given.</summary>
    Task<AuditEventEntity> CreateForLink(
        AuditAction action,
        LibraryItemLinkEntity link,
        string targetName,
        IReadOnlyList<AuditIdentifier> identifiers,
        UserEntity? actor = null,
        DateTime? createdUtc = null);
}
