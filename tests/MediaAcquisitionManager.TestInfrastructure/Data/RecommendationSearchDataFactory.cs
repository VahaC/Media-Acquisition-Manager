using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Infrastructure.Persistence;

namespace MediaAcquisitionManager.TestInfrastructure.Data;

/// <inheritdoc cref="IRecommendationSearchDataFactory"/>
public sealed class RecommendationSearchDataFactory(ApplicationDbContext db) : IRecommendationSearchDataFactory
{
    public async Task<IReadOnlyList<RecommendationSearchEntity>> Create(Guid userId, RecommendationTab tab, int count, DateTime searchedUtc)
    {
        var searches = Enumerable.Range(0, count)
            .Select(_ => new RecommendationSearchEntity
            {
                UserId = userId,
                Tab = tab,
                CatalogWorkId = Guid.NewGuid(),
                SearchedUtc = searchedUtc,
            })
            .ToList();

        db.RecommendationSearches.AddRange(searches);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return searches;
    }
}
