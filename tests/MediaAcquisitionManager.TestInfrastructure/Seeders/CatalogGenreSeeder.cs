using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.TestInfrastructure.Data;

namespace MediaAcquisitionManager.TestInfrastructure.Seeders;

/// <inheritdoc cref="ICatalogGenreSeeder"/>
public sealed class CatalogGenreSeeder(IServiceProvider services) : SeederBase(services), ICatalogGenreSeeder
{
    private ICatalogGenreDataFactory Genres => new CatalogGenreDataFactory(Db, Resolve<TimeProvider>());

    public Task<CatalogGenreEntity> Create(MediaWorkKind kind, int genreId, string name, CatalogGenreRole role = CatalogGenreRole.Other) =>
        Genres.Create(kind, genreId, name, role);
}
