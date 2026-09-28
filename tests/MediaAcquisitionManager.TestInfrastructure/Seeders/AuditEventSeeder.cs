using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.TestInfrastructure.Data;

namespace MediaAcquisitionManager.TestInfrastructure.Seeders;

/// <inheritdoc cref="IAuditEventSeeder"/>
public sealed class AuditEventSeeder(IServiceProvider services) : SeederBase(services), IAuditEventSeeder
{
    private IAuditEventDataFactory AuditEvents => new AuditEventDataFactory(Db, Resolve<TimeProvider>());

    public Task<AuditEventEntity> Create(AuditAction action, UserEntity? actor = null, UserEntity? target = null) =>
        AuditEvents.Create(action, actor, target);
}
