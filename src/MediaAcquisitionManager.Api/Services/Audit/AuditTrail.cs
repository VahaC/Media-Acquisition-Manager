using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Infrastructure.Persistence;

namespace MediaAcquisitionManager.Api.Services.Audit;

/// <inheritdoc cref="IAuditTrail"/>
public sealed class AuditTrail(ApplicationDbContext db, TimeProvider time) : IAuditTrail
{
    public void Record(AuditAction action, AuditActor? actor, AuditTarget? target) => Add(action, actor, target);

    public void Record(AuditAction action, AuditActor? actor, AuditTarget? target, IReadOnlyList<AuditIdentifier> identifiers)
    {
        ArgumentNullException.ThrowIfNull(identifiers);

        var auditEvent = Add(action, actor, target);
        // The row's id is known before the save (BaseEntity), so its ids go into the same save.
        db.AuditEventIdentifiers.AddRange(identifiers.Select((identifier, position) => new AuditEventIdentifierEntity
        {
            AuditEventId = auditEvent.Id,
            Role = identifier.Role,
            Scheme = identifier.Scheme,
            Value = identifier.Value,
            Position = position,
        }));
    }

    private AuditEventEntity Add(AuditAction action, AuditActor? actor, AuditTarget? target)
    {
        var auditEvent = new AuditEventEntity
        {
            Action = action,
            ActorUserId = actor?.UserId,
            ActorUserName = actor?.UserName,
            TargetKind = target?.Kind,
            TargetId = target?.Id,
            TargetName = target?.Name,
            CreatedUtc = time.GetUtcNow().UtcDateTime,
        };
        db.AuditEvents.Add(auditEvent);
        return auditEvent;
    }
}
