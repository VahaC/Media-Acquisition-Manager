using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Infrastructure.Persistence;

namespace MediaAcquisitionManager.TestInfrastructure.Data;

/// <inheritdoc cref="ICatalogWorkDataFactory"/>
/// <remarks>Its genres are the work's own rows, so they are added through the work's factory.</remarks>
public sealed class CatalogWorkDataFactory(ApplicationDbContext db, TimeProvider time) : ICatalogWorkDataFactory
{
    public async Task<CatalogWorkEntity> Create(
        MediaWorkKind kind,
        string catalogId,
        string title,
        DateOnly? releaseDate = null,
        IReadOnlyList<int>? genreIds = null,
        double? rating = 7.5,
        int voteCount = 1000,
        string? imdbId = null,
        DateTime? detailsUtc = null,
        DateTime? observedUtc = null,
        Guid? sourceConnectionId = null,
        string? sourceName = null,
        string? originalTitle = null,
        string? posterPath = null)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var work = new CatalogWorkEntity
        {
            Kind = kind,
            CatalogId = catalogId,
            Title = title,
            OriginalTitle = originalTitle ?? title,
            OriginalLanguage = "en",
            ReleaseDate = releaseDate,
            Rating = rating,
            VoteCount = voteCount,
            PosterPath = posterPath,
            ImdbId = imdbId,
            DetailsUtc = detailsUtc,
            SourceKind = ProvenanceSourceKind.Metadata,
            SourceConnectionId = sourceConnectionId,
            SourceName = sourceName,
            SourceItemId = $"{(kind == MediaWorkKind.Series ? "tv" : "movie")}/{catalogId}",
            CreatedUtc = now,
            ObservedUtc = observedUtc ?? now,
        };

        db.CatalogWorks.Add(work);
        db.CatalogWorkGenres.AddRange((genreIds ?? []).Select(genreId => new CatalogWorkGenreEntity { CatalogWorkId = work.Id, GenreId = genreId }));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return work;
    }
}
