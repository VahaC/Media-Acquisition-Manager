using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.TestInfrastructure.Seeders;

/// <summary>Audit rows for a test — history that happened before it started.</summary>
public interface IAuditEventSeeder
{
    /// <summary>An audit row recording <paramref name="action"/>, done by <paramref name="actor"/> to
    /// <paramref name="target"/>.</summary>
    Task<AuditEventEntity> Create(AuditAction action, UserEntity? actor = null, UserEntity? target = null);
}
