using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.TestInfrastructure.Data;

/// <summary>Creates audit rows directly in the database.</summary>
public interface IAuditEventDataFactory
{
    /// <summary>
    /// A saved audit row recording <paramref name="action"/>, done by <paramref name="actor"/> to
    /// <paramref name="target"/>, with their names copied the way the app copies them.
    /// </summary>
    Task<AuditEventEntity> Create(AuditAction action, UserEntity? actor = null, UserEntity? target = null);
}
