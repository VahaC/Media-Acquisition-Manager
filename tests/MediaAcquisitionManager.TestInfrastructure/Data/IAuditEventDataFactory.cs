using MediaAcquisitionManager.Api.Services.Audit;
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

    /// <summary>
    /// MAM-34 — a saved audit row recording <paramref name="action"/> on the TMDb link <paramref name="linkId"/> named
    /// <paramref name="targetName"/>, with the ids it found and left (<paramref name="identifiers"/>, in their order), at
    /// <paramref name="createdUtc"/> (the clock's now when <c>null</c>): the history the repair reads to tell a sent Apply. Stored as
    /// given, as the app's <see cref="IAuditTrail"/> would add it.
    /// </summary>
    Task<AuditEventEntity> CreateForLink(
        AuditAction action,
        Guid linkId,
        string targetName,
        IReadOnlyList<AuditIdentifier> identifiers,
        UserEntity? actor = null,
        DateTime? createdUtc = null);
}
