using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.TestInfrastructure.Data;

/// <summary>MAM-21 — creates the owner's decisions on possible matches directly in the database.</summary>
public interface IDuplicateMatchDecisionDataFactory
{
    /// <summary>
    /// A saved <paramref name="decision"/> on the group <paramref name="groupKey"/>, by <paramref name="decidedByUserId"/>
    /// named <paramref name="decidedByUserName"/>, at the clock's now; MAM-40 — with <paramref name="keptMembers"/> for
    /// kept copies. Stored as given — any key, one no group has included — so a test can plant a row the app would never
    /// write, or one another request wrote first.
    /// </summary>
    Task<DuplicateMatchDecisionEntity> Create(
        string groupKey, DuplicateMatchDecision decision, Guid? decidedByUserId = null, string decidedByUserName = "owner", string? keptMembers = null);
}
