using MediaAcquisitionManager.Api.Services.Audit;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Infrastructure.Persistence;

namespace MediaAcquisitionManager.TestInfrastructure.Data;

/// <inheritdoc cref="IAuditEventDataFactory"/>
/// <remarks>MAM-34 — an audit row's identifiers are its own rows, so they are added through the row's factory rather
/// than a factory of their own.</remarks>
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

    public async Task<AuditEventEntity> CreateForLink(
        AuditAction action,
        Guid linkId,
        string targetName,
        IReadOnlyList<AuditIdentifier> identifiers,
        UserEntity? actor = null,
        DateTime? createdUtc = null)
    {
        var auditEvent = new AuditEventEntity
        {
            Action = action,
            ActorUserId = actor?.Id,
            ActorUserName = actor?.UserName,
            TargetKind = AuditTargetKind.LibraryLink,
            TargetId = linkId,
            TargetName = targetName,
            CreatedUtc = createdUtc ?? time.GetUtcNow().UtcDateTime,
        };

        db.AuditEvents.Add(auditEvent);
        db.AuditEventIdentifiers.AddRange(identifiers.Select((identifier, position) => new AuditEventIdentifierEntity
        {
            AuditEventId = auditEvent.Id,
            Role = identifier.Role,
            Scheme = identifier.Scheme,
            Value = identifier.Value,
            Position = position,
        }));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return auditEvent;
    }
}
