using MediaAcquisitionManager.Core.Abstractions.Metadata;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Services.Catalog;

// MAM-33 — storing what the catalog said: each answer is staged on the tracker and saved in one save, a row
// updated in place or added, the child rows of the part read replaced, and every row stamped with the catalog
// that reported it (ADR 0006). Nothing here touches a library table (ADR 0025).
public sealed partial class CatalogStore
{
    /// <summary>
    /// Stages with <paramref name="stage"/> and saves, leaving nothing tracked. Another writer of the catalog
    /// (the feed reader, a later slice) can add a row this one is adding — the unique indexes keep one — so a
    /// save that loses that race is staged again from the rows as they now are, once; anything else, or a
    /// second loss, stays a failure.
    /// </summary>
    private async Task SaveAsync(Func<Task> stage, CancellationToken cancellationToken)
    {
        try
        {
            await stage();
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
        {
            db.ChangeTracker.Clear();
            logger.LogInformation(exception, "Another writer stored a catalog row first; the answer is stored again over it.");
            await stage();
            await db.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            db.ChangeTracker.Clear();
        }
    }

    /// <summary>
    /// Stages the summaries of <paramref name="works"/> — the last one of a work listed twice — and their genres,
    /// and returns every row by kind and catalog id. A list says nothing of a work's details, so those stay.
    /// </summary>
    private async Task<Dictionary<(MediaWorkKind Kind, string CatalogId), CatalogWorkEntity>> StageSummariesAsync(
        CatalogConnection catalog, IEnumerable<CatalogWorkSummary> works, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var latest = works
            .GroupBy(work => (work.Kind, work.Id))
            .ToDictionary(group => group.Key, group => group.Last());
        var rows = new Dictionary<(MediaWorkKind Kind, string CatalogId), CatalogWorkEntity>();
        foreach (var kind in latest.Keys.Select(key => key.Kind).Distinct())
        {
            var ids = latest.Keys.Where(key => key.Kind == kind).Select(key => key.Id).ToList();
            // MAM-33 — each list of ids as one JSON parameter (EF.Parameter, the LibrarySnapshotStore rule), not one
            // parameter per id: a person's works or a collection's parts run to hundreds.
            var existing = await db.CatalogWorks.AsTracking()
                .Where(work => work.Kind == kind && EF.Parameter(ids).Contains(work.CatalogId))
                .ToDictionaryAsync(work => work.CatalogId, StringComparer.Ordinal, cancellationToken);
            var existingIds = existing.Values.Select(work => work.Id).ToList();
            var storedGenres = (await db.CatalogWorkGenres.AsTracking()
                    .Where(genre => EF.Parameter(existingIds).Contains(genre.CatalogWorkId))
                    .ToListAsync(cancellationToken))
                .ToLookup(genre => genre.CatalogWorkId);

            foreach (var id in ids)
            {
                var summary = latest[(kind, id)];
                if (!existing.TryGetValue(id, out var row))
                {
                    row = new CatalogWorkEntity { Kind = kind, CatalogId = id };
                    db.CatalogWorks.Add(row);
                }

                row.Title = summary.Title;
                row.OriginalTitle = summary.OriginalTitle;
                row.OriginalLanguage = summary.OriginalLanguage;
                row.ReleaseDate = summary.ReleaseDate;
                row.Rating = summary.Rating;
                row.VoteCount = summary.VoteCount;
                row.PosterPath = summary.PosterPath;
                Stamp(row, catalog, $"{KindSegment(kind)}/{id}", now);

                // The genres the list gives replace the stored ones: a stored one it still gives stays as it is.
                var missing = summary.GenreIds.ToHashSet();
                foreach (var stored in storedGenres[row.Id])
                {
                    if (!missing.Remove(stored.GenreId))
                        db.CatalogWorkGenres.Remove(stored);
                }

                foreach (var genreId in missing.Order())
                    db.CatalogWorkGenres.Add(new CatalogWorkGenreEntity { CatalogWorkId = row.Id, GenreId = genreId });

                rows[(kind, id)] = row;
            }
        }

        return rows;
    }

    /// <summary>Stages a work's details: its summary and the recommended works' as rows, then its details-only
    /// columns and its people, seasons, recommendations and — MAM-34 — alternative titles, which replace the ones
    /// read before; the row is marked as read by today's reading (<see cref="CurrentDetailsVersion"/>).</summary>
    private async Task StageDetailsAsync(CatalogConnection catalog, CatalogWork work, CancellationToken cancellationToken)
    {
        var rows = await StageSummariesAsync(catalog, [work.Summary, .. work.Recommendations], cancellationToken);
        var row = rows[(work.Summary.Kind, work.Summary.Id)];
        row.ImdbId = work.ImdbId;
        row.CollectionId = work.Collection?.Id;
        row.CollectionName = work.Collection?.Name;
        row.RuntimeMinutes = work.RuntimeMinutes;
        row.Overview = work.Overview;
        row.DetailsUtc = time.GetUtcNow().UtcDateTime;
        row.DetailsVersion = CurrentDetailsVersion;

        db.CatalogWorkAlternativeTitles.RemoveRange(
            await db.CatalogWorkAlternativeTitles.AsTracking().Where(title => title.CatalogWorkId == row.Id).ToListAsync(cancellationToken));
        db.CatalogWorkAlternativeTitles.AddRange((work.AlternativeTitles ?? []).Select((title, position) => new CatalogWorkAlternativeTitleEntity
        {
            CatalogWorkId = row.Id,
            Position = position,
            Title = title,
        }));

        db.CatalogWorkPeople.RemoveRange(
            await db.CatalogWorkPeople.AsTracking().Where(person => person.CatalogWorkId == row.Id).ToListAsync(cancellationToken));
        db.CatalogWorkPeople.AddRange(work.Directors.Select((person, position) => new CatalogWorkPersonEntity
        {
            CatalogWorkId = row.Id,
            Position = position,
            PersonCatalogId = person.Id,
            Name = person.Name,
        }));

        db.CatalogWorkSeasons.RemoveRange(
            await db.CatalogWorkSeasons.AsTracking().Where(season => season.CatalogWorkId == row.Id).ToListAsync(cancellationToken));
        db.CatalogWorkSeasons.AddRange(work.Seasons.DistinctBy(season => season.Number).Select(season => new CatalogWorkSeasonEntity
        {
            CatalogWorkId = row.Id,
            Number = season.Number,
            AirDate = season.AirDate,
            EpisodeCount = season.EpisodeCount,
        }));

        db.CatalogWorkRecommendations.RemoveRange(
            await db.CatalogWorkRecommendations.AsTracking().Where(recommendation => recommendation.CatalogWorkId == row.Id).ToListAsync(cancellationToken));
        db.CatalogWorkRecommendations.AddRange(work.Recommendations.Select((recommended, position) => new CatalogWorkRecommendationEntity
        {
            CatalogWorkId = row.Id,
            Position = position,
            Kind = recommended.Kind,
            CatalogId = recommended.Id,
        }));
    }

    /// <summary>Stages a collection, its parts' summaries as rows, and its parts in order.</summary>
    private async Task StageCollectionAsync(CatalogConnection catalog, CatalogCollection collection, CancellationToken cancellationToken)
    {
        await StageSummariesAsync(catalog, collection.Parts, cancellationToken);
        var row = await db.CatalogCollections.AsTracking().SingleOrDefaultAsync(stored => stored.CatalogId == collection.Id, cancellationToken);
        if (row is null)
        {
            row = new CatalogCollectionEntity { CatalogId = collection.Id };
            db.CatalogCollections.Add(row);
        }

        row.Name = collection.Name;
        Stamp(row, catalog, $"collection/{collection.Id}", time.GetUtcNow().UtcDateTime);
        db.CatalogCollectionParts.RemoveRange(
            await db.CatalogCollectionParts.AsTracking().Where(part => part.CatalogCollectionId == row.Id).ToListAsync(cancellationToken));
        db.CatalogCollectionParts.AddRange(collection.Parts.Select((part, position) => new CatalogCollectionPartEntity
        {
            CatalogCollectionId = row.Id,
            Position = position,
            MovieCatalogId = part.Id,
        }));
    }

    /// <summary>Stages a person, their works' summaries as rows, and their works in order.</summary>
    private async Task StagePersonAsync(CatalogConnection catalog, CatalogPerson person, CancellationToken cancellationToken)
    {
        await StageSummariesAsync(catalog, person.Works, cancellationToken);
        var row = await db.CatalogPeople.AsTracking().SingleOrDefaultAsync(stored => stored.CatalogId == person.Id, cancellationToken);
        if (row is null)
        {
            row = new CatalogPersonEntity { CatalogId = person.Id };
            db.CatalogPeople.Add(row);
        }

        row.Name = person.Name;
        Stamp(row, catalog, $"person/{person.Id}", time.GetUtcNow().UtcDateTime);
        db.CatalogPersonWorks.RemoveRange(
            await db.CatalogPersonWorks.AsTracking().Where(work => work.CatalogPersonId == row.Id).ToListAsync(cancellationToken));
        db.CatalogPersonWorks.AddRange(person.Works.Select((work, position) => new CatalogPersonWorkEntity
        {
            CatalogPersonId = row.Id,
            Position = position,
            Kind = work.Kind,
            CatalogId = work.Id,
        }));
    }

    /// <summary>Stages a kind's genre list as the catalog gave it: a genre it no longer lists goes.</summary>
    private async Task StageGenresAsync(CatalogConnection catalog, MediaWorkKind kind, IReadOnlyList<CatalogGenre> genres, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var existing = await db.CatalogGenres.AsTracking().Where(genre => genre.Kind == kind).ToListAsync(cancellationToken);
        var reported = genres.DistinctBy(genre => genre.Id).ToDictionary(genre => genre.Id);
        db.CatalogGenres.RemoveRange(existing.Where(row => !reported.ContainsKey(row.GenreId)));
        foreach (var genre in reported.Values)
        {
            var row = existing.SingleOrDefault(stored => stored.GenreId == genre.Id);
            if (row is null)
            {
                row = new CatalogGenreEntity { Kind = kind, GenreId = genre.Id };
                db.CatalogGenres.Add(row);
            }

            row.Name = genre.Name;
            row.Role = genre.Role;
            Stamp(row, catalog, $"genre/{KindSegment(kind)}/{genre.Id}", now);
        }
    }

    /// <summary>Stages a lookup's answer under its key.</summary>
    private async Task StageLookupAsync(CatalogConnection catalog, string key, CatalogLookupResult result, CancellationToken cancellationToken)
    {
        var row = await db.CatalogLookups.AsTracking().SingleOrDefaultAsync(lookup => lookup.QueryKey == key, cancellationToken);
        if (row is null)
        {
            row = new CatalogLookupEntity { QueryKey = key };
            db.CatalogLookups.Add(row);
        }

        row.ResultKind = result.Kind;
        row.ResultCatalogId = result.CatalogId;
        row.Evidence = result.Evidence;
        Stamp(row, catalog, key, time.GetUtcNow().UtcDateTime);
    }

    /// <summary>Who said it and when (ADR 0006): the metadata integration, its name copied, the catalog's own id
    /// of the record; first seen once, last seen now.</summary>
    private static void Stamp(ProvenancedEntity row, CatalogConnection catalog, string sourceItemId, DateTime now)
    {
        row.SourceKind = ProvenanceSourceKind.Metadata;
        row.SourceConnectionId = catalog.IntegrationId;
        row.SourceName = catalog.Name;
        row.SourceItemId = sourceItemId;
        row.ObservedUtc = now;
        if (row.CreatedUtc == default)
            row.CreatedUtc = now;
    }

    /// <summary>The catalog's own word for a kind in its record ids: <c>movie/603</c>, <c>tv/73586</c>.</summary>
    private static string KindSegment(MediaWorkKind kind) => kind == MediaWorkKind.Series ? "tv" : "movie";
}
