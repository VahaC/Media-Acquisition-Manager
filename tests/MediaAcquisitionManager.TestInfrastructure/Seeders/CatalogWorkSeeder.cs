using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.TestInfrastructure.Data;

namespace MediaAcquisitionManager.TestInfrastructure.Seeders;

/// <inheritdoc cref="ICatalogWorkSeeder"/>
public sealed class CatalogWorkSeeder(IServiceProvider services) : SeederBase(services), ICatalogWorkSeeder
{
    private ICatalogWorkDataFactory Works => new CatalogWorkDataFactory(Db, Resolve<TimeProvider>());

    public Task<CatalogWorkEntity> Create(
        MediaWorkKind kind,
        string catalogId,
        string title,
        DateOnly? releaseDate = null,
        IReadOnlyList<int>? genreIds = null,
        double? rating = 7.5,
        int voteCount = 1000,
        string? imdbId = null,
        DateTime? detailsUtc = null,
        IntegrationConnectionEntity? catalog = null,
        string? originalTitle = null,
        string? posterPath = null,
        string? detailsLanguage = null) =>
        Works.Create(
            kind,
            catalogId,
            title,
            releaseDate,
            genreIds,
            rating,
            voteCount,
            imdbId,
            detailsUtc,
            null,
            catalog?.Id,
            catalog?.Name,
            originalTitle,
            posterPath,
            detailsLanguage ?? catalog?.CatalogLanguage ?? IntegrationConnectionSeeder.CatalogLanguage);
}
