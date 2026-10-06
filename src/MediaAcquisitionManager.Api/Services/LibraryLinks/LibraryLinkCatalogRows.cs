using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Services.LibraryLinks;

/// <summary>
/// MAM-34 — the cached catalog rows the TMDb suggestions are read from: a candidate's facts, a linked neighbour's
/// collection and directors, an identified work's genres. Read only — never the catalog itself (decision 7: opening a
/// dialog asks the catalog nothing).
/// </summary>
/// <remarks>
/// Keyed by kind and id, as a candidate names its catalog work (no foreign key, ADR 0025); a key with no row — a film the
/// catalog cache pruned, or never read — is left out of the answer.
/// </remarks>
internal static class LibraryLinkCatalogRows
{
    /// <summary>The rows of <paramref name="keys"/> with their genres, alternative titles, directors and seasons.</summary>
    public static async Task<Dictionary<(MediaWorkKind Kind, string Id), LibraryLinkCatalogRow>> LoadAsync(
        ApplicationDbContext db, IReadOnlyCollection<(MediaWorkKind Kind, string Id)> keys, CancellationToken cancellationToken)
    {
        var rows = await RowsAsync(db, keys, cancellationToken);
        if (rows.Count == 0)
            return [];

        List<Guid> rowIds = [.. rows.Select(row => row.Id)];
        var roles = await RolesOfAsync(db, rows, cancellationToken);
        var alternatives = (await db.CatalogWorkAlternativeTitles
                .AsNoTracking()
                .Where(title => EF.Parameter(rowIds).Contains(title.CatalogWorkId))
                .ToListAsync(cancellationToken))
            .OrderBy(title => title.Position)
            .ToLookup(title => title.CatalogWorkId, title => title.Title);
        var people = (await db.CatalogWorkPeople
                .AsNoTracking()
                .Where(person => EF.Parameter(rowIds).Contains(person.CatalogWorkId))
                .ToListAsync(cancellationToken))
            .OrderBy(person => person.Position)
            .ToLookup(person => person.CatalogWorkId, person => person.PersonCatalogId);
        var seasons = (await db.CatalogWorkSeasons
                .AsNoTracking()
                .Where(season => EF.Parameter(rowIds).Contains(season.CatalogWorkId) && season.Number > 0)
                .Select(season => season.CatalogWorkId)
                .ToListAsync(cancellationToken))
            .CountBy(rowId => rowId)
            .ToDictionary(pair => pair.Key, pair => pair.Value);

        return rows.ToDictionary(
            row => (row.Kind, row.CatalogId),
            row => new LibraryLinkCatalogRow(
                row,
                [.. roles[row.Id]],
                [.. alternatives[row.Id]],
                [.. people[row.Id].Distinct(StringComparer.Ordinal)],
                seasons.GetValueOrDefault(row.Id)));
    }

    /// <summary>What the genres of each cached row of <paramref name="keys"/> make it.</summary>
    public static async Task<Dictionary<(MediaWorkKind Kind, string Id), IReadOnlyList<CatalogGenreRole>>> GenreRolesAsync(
        ApplicationDbContext db, IReadOnlyCollection<(MediaWorkKind Kind, string Id)> keys, CancellationToken cancellationToken)
    {
        var rows = await RowsAsync(db, keys, cancellationToken);
        var roles = await RolesOfAsync(db, rows, cancellationToken);
        return rows.ToDictionary(row => (row.Kind, row.CatalogId), row => (IReadOnlyList<CatalogGenreRole>)[.. roles[row.Id]]);
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    private static async Task<List<CatalogWorkEntity>> RowsAsync(
        ApplicationDbContext db, IReadOnlyCollection<(MediaWorkKind Kind, string Id)> keys, CancellationToken cancellationToken)
    {
        if (keys.Count == 0)
            return [];

        // One JSON parameter (EF.Parameter, the CatalogStore rule); the kind is matched in memory, as a film and a show
        // may share a number (ADR 0017).
        List<string> ids = [.. keys.Select(key => key.Id).Distinct(StringComparer.Ordinal)];
        var wanted = keys.ToHashSet();
        return
        [
            .. (await db.CatalogWorks.AsNoTracking().Where(work => EF.Parameter(ids).Contains(work.CatalogId)).ToListAsync(cancellationToken))
                .Where(work => wanted.Contains((work.Kind, work.CatalogId))),
        ];
    }

    /// <summary>Each row's roles by its genres, each role once; a genre the catalog's list hasn't been read for gives
    /// none.</summary>
    private static async Task<ILookup<Guid, CatalogGenreRole>> RolesOfAsync(
        ApplicationDbContext db, IReadOnlyList<CatalogWorkEntity> rows, CancellationToken cancellationToken)
    {
        if (rows.Count == 0)
            return Array.Empty<CatalogGenreRole>().ToLookup(_ => Guid.Empty);

        List<Guid> rowIds = [.. rows.Select(row => row.Id)];
        var genres = await db.CatalogWorkGenres
            .AsNoTracking()
            .Where(genre => EF.Parameter(rowIds).Contains(genre.CatalogWorkId))
            .ToListAsync(cancellationToken);
        var roleOf = await db.CatalogGenres
            .AsNoTracking()
            .ToDictionaryAsync(genre => (genre.Kind, genre.GenreId), genre => genre.Role, cancellationToken);
        var kindOf = rows.ToDictionary(row => row.Id, row => row.Kind);
        return genres
            .Select(genre => (genre.CatalogWorkId, Role: roleOf.TryGetValue((kindOf[genre.CatalogWorkId], genre.GenreId), out var role) ? role : (CatalogGenreRole?)null))
            .Where(entry => entry.Role is not null)
            .Distinct()
            .OrderBy(entry => entry.Role)
            .ToLookup(entry => entry.CatalogWorkId, entry => entry.Role!.Value);
    }
}
