using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Infrastructure.Persistence;

namespace MediaAcquisitionManager.TestInfrastructure.Data;

/// <inheritdoc cref="IRecommendationDismissalDataFactory"/>
public sealed class RecommendationDismissalDataFactory(ApplicationDbContext db, TimeProvider time) : IRecommendationDismissalDataFactory
{
    public async Task<RecommendationDismissalEntity> Create(Guid userId, MediaWorkKind kind, string catalogId)
    {
        var dismissal = new RecommendationDismissalEntity
        {
            UserId = userId,
            Kind = kind,
            CatalogId = catalogId,
            CreatedUtc = time.GetUtcNow().UtcDateTime,
        };

        db.RecommendationDismissals.Add(dismissal);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return dismissal;
    }
}
