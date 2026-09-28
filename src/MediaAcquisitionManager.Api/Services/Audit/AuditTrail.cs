using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Infrastructure.Persistence;

namespace MediaAcquisitionManager.Api.Services.Audit;

/// <inheritdoc cref="IAuditTrail"/>
public sealed class AuditTrail(ApplicationDbContext db, TimeProvider time) : IAuditTrail
{
    public void Record(AuditAction action, AuditActor? actor, AuditTarget? target) =>
        db.AuditEvents.Add(new AuditEventEntity
        {
            Action = action,
            ActorUserId = actor?.UserId,
            ActorUserName = actor?.UserName,
            TargetKind = target?.Kind,
            TargetId = target?.Id,
            TargetName = target?.Name,
            CreatedUtc = time.GetUtcNow().UtcDateTime,
        });
}
