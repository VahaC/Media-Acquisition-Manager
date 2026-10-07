using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Infrastructure.Persistence;

namespace MediaAcquisitionManager.TestInfrastructure.Data;

/// <inheritdoc cref="IDuplicateMatchDecisionDataFactory"/>
public sealed class DuplicateMatchDecisionDataFactory(ApplicationDbContext db, TimeProvider time) : IDuplicateMatchDecisionDataFactory
{
    public async Task<DuplicateMatchDecisionEntity> Create(
        string groupKey, DuplicateMatchDecision decision, Guid? decidedByUserId = null, string decidedByUserName = "owner")
    {
        var now = time.GetUtcNow().UtcDateTime;
        var row = new DuplicateMatchDecisionEntity
        {
            GroupKey = groupKey,
            Decision = decision,
            DecidedByUserId = decidedByUserId,
            DecidedByUserName = decidedByUserName,
            DecidedUtc = now,
            CreatedUtc = now,
        };

        db.DuplicateMatchDecisions.Add(row);
        await db.SaveChangesAsync();
        // Detached, so the test's later reads come from the database, not from this tracked copy.
        db.ChangeTracker.Clear();
        return row;
    }
}
