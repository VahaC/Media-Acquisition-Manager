using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.TestInfrastructure.Seeders;

/// <summary>MAM-33 — searches the availability check ran for a user's list, planted in its budget log without asking
/// any release source.</summary>
public interface IRecommendationSearchSeeder
{
    /// <summary><paramref name="count"/> searches of <paramref name="user"/>'s <paramref name="tab"/> list, run
    /// <paramref name="ago"/> before now (an hour when <c>null</c>): inside the 24-hour budget window unless older.</summary>
    Task<IReadOnlyList<RecommendationSearchEntity>> Log(UserEntity user, RecommendationTab tab, int count, TimeSpan? ago = null);
}
