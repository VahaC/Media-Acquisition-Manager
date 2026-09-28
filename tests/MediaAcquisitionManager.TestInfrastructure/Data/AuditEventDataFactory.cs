using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Infrastructure.Persistence;

namespace MediaAcquisitionManager.TestInfrastructure.Data;

/// <inheritdoc cref="IAuditEventDataFactory"/>
public sealed class AuditEventDataFactory(ApplicationDbContext db, TimeProvider time) : IAuditEventDataFactory
{
    public async Task<AuditEventEntity> Create(AuditAction action, UserEntity? actor = null, UserEntity? target = null)
    {
        var auditEvent = new AuditEventEntity
        {
            Action = action,
            ActorUserId = actor?.Id,
            ActorUserName = actor?.UserName,
            TargetKind = target is null ? null : AuditTargetKind.User,
            TargetId = target?.Id,
            TargetName = target?.UserName,
            CreatedUtc = time.GetUtcNow().UtcDateTime,
        };

        db.AuditEvents.Add(auditEvent);
        await db.SaveChangesAsync();
        // Detached, so the test's later reads come from the database, not from this tracked copy.
        db.ChangeTracker.Clear();
        return auditEvent;
    }
}
