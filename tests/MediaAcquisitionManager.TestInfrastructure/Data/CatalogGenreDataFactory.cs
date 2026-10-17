using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Infrastructure.Persistence;
using MediaAcquisitionManager.TestInfrastructure.Seeders;

namespace MediaAcquisitionManager.TestInfrastructure.Data;

/// <inheritdoc cref="ICatalogGenreDataFactory"/>
public sealed class CatalogGenreDataFactory(ApplicationDbContext db, TimeProvider time) : ICatalogGenreDataFactory
{
    public async Task<CatalogGenreEntity> Create(MediaWorkKind kind, int genreId, string name, CatalogGenreRole role = CatalogGenreRole.Other)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var genre = new CatalogGenreEntity
        {
            Kind = kind,
            GenreId = genreId,
            Name = name,
            Role = role,
            // MAM-46 — the language a seeded catalog is asked in, so the planted list is fresh for it.
            Language = IntegrationConnectionSeeder.CatalogLanguage,
            SourceKind = ProvenanceSourceKind.Metadata,
            SourceItemId = $"genre/{(kind == MediaWorkKind.Series ? "tv" : "movie")}/{genreId}",
            CreatedUtc = now,
            ObservedUtc = now,
        };

        db.CatalogGenres.Add(genre);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return genre;
    }
}
