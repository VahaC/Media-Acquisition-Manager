using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Texts;
using MediaAcquisitionManager.TestInfrastructure.Data;

namespace MediaAcquisitionManager.TestInfrastructure.Seeders;

/// <inheritdoc cref="IRecommendationRunSeeder"/>
public sealed class RecommendationRunSeeder(IServiceProvider services) : SeederBase(services), IRecommendationRunSeeder
{
    private IRecommendationRunDataFactory Runs => new RecommendationRunDataFactory(Db, Resolve<TimeProvider>());

    public Task<RecommendationRunEntity> Create(
        RecommendationRunStatus status,
        RecommendationRunTrigger trigger = RecommendationRunTrigger.Requested,
        UserEntity? requestedBy = null,
        DateTime? createdUtc = null,
        int? seedCount = null,
        int? candidateCount = null,
        Message? error = null) =>
        Runs.Create(status, trigger, requestedBy?.Id, requestedBy?.UserName, createdUtc, seedCount, candidateCount, error);
}
