using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.TestInfrastructure.Data;

namespace MediaAcquisitionManager.TestInfrastructure.Seeders;

/// <inheritdoc cref="IRecommendationDismissalSeeder"/>
public sealed class RecommendationDismissalSeeder(IServiceProvider services) : SeederBase(services), IRecommendationDismissalSeeder
{
    private IRecommendationDismissalDataFactory Dismissals => new RecommendationDismissalDataFactory(Db, Resolve<TimeProvider>());

    public Task<RecommendationDismissalEntity> Dismiss(UserEntity user, CatalogWorkEntity work)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(work);
        return Dismissals.Create(user.Id, work.Kind, work.CatalogId);
    }
}
