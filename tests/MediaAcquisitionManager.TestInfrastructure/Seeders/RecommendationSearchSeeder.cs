using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.TestInfrastructure.Data;

namespace MediaAcquisitionManager.TestInfrastructure.Seeders;

/// <inheritdoc cref="IRecommendationSearchSeeder"/>
public sealed class RecommendationSearchSeeder(IServiceProvider services) : SeederBase(services), IRecommendationSearchSeeder
{
    private IRecommendationSearchDataFactory Searches => new RecommendationSearchDataFactory(Db);

    public Task<IReadOnlyList<RecommendationSearchEntity>> Log(UserEntity user, RecommendationTab tab, int count, TimeSpan? ago = null)
    {
        ArgumentNullException.ThrowIfNull(user);
        var searchedUtc = Resolve<TimeProvider>().GetUtcNow().UtcDateTime - (ago ?? TimeSpan.FromHours(1));
        return Searches.Create(user.Id, tab, count, searchedUtc);
    }
}
