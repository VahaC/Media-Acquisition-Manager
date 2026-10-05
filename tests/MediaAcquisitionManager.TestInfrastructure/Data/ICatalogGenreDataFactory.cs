using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.TestInfrastructure.Data;

/// <summary>MAM-33 — creates rows of the metadata catalog's genre lists directly in the database.</summary>
public interface ICatalogGenreDataFactory
{
    /// <summary>A saved genre <paramref name="genreId"/> of the catalog's list for <paramref name="kind"/>, named
    /// <paramref name="name"/>, with <paramref name="role"/>, reported at the clock's now.</summary>
    Task<CatalogGenreEntity> Create(MediaWorkKind kind, int genreId, string name, CatalogGenreRole role = CatalogGenreRole.Other);
}
