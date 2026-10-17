using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.TestInfrastructure.Seeders;

/// <summary>MAM-33 — the metadata catalog's work rows for a test, planted without asking the catalog.</summary>
public interface ICatalogWorkSeeder
{
    /// <summary>
    /// A catalog work as <paramref name="catalog"/> (the metadata integration, or none) reported it: released on
    /// <paramref name="releaseDate"/>, of <paramref name="genreIds"/>, rated <paramref name="rating"/> by
    /// <paramref name="voteCount"/>; <paramref name="detailsUtc"/> and <paramref name="imdbId"/> plant what a read of its
    /// details would have left on the row. Its original title is <paramref name="originalTitle"/>, or
    /// <paramref name="title"/> when that is <c>null</c>; its poster is at <paramref name="posterPath"/> (<c>/abc.jpg</c>), or it has
    /// none. MAM-46 — its details are stamped as read in <paramref name="detailsLanguage"/>, else in
    /// <paramref name="catalog"/>'s language, else in <see cref="IntegrationConnectionSeeder.CatalogLanguage"/>.
    /// </summary>
    Task<CatalogWorkEntity> Create(
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
        string? detailsLanguage = null);
}
