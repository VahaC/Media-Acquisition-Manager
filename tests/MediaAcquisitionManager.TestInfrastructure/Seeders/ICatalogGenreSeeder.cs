using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.TestInfrastructure.Seeders;

/// <summary>MAM-33 — the metadata catalog's genre lists for a test, planted without asking the catalog.</summary>
public interface ICatalogGenreSeeder
{
    /// <summary>The genre <paramref name="genreId"/> of the catalog's list for <paramref name="kind"/>, named
    /// <paramref name="name"/>.</summary>
    Task<CatalogGenreEntity> Create(MediaWorkKind kind, int genreId, string name, CatalogGenreRole role = CatalogGenreRole.Other);
}
