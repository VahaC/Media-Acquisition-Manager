using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Library;
using MediaAcquisitionManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Services.LibraryLinks;

/// <summary>
/// MAM-34, decision 3 — what each library mostly holds, as the genre evidence of a TMDb suggestion compares it: derived
/// from the TMDb genres of the works in that library whose TMDb id is known (their library's own, or a link), never from
/// the library's name ("Мульфільми" says nothing the app may read, ADR 0017).
/// </summary>
/// <remarks>
/// The genres are the catalog's cached rows (<c>CatalogWorks</c>): the suggestion run reads a sample of each library's
/// identified works through the catalog cache (<see cref="LibraryLinkSuggestionRunner.ProfileSampleSize"/>), and the
/// profile is computed whenever the suggestions are read (decision 7), from whatever is cached then.
/// </remarks>
internal static class LibraryLinkProfiles
{
    /// <summary>Every library work whose TMDb film or show is known, with the libraries its files sit in, by work id.</summary>
    public static async Task<List<LibraryLinkIdentifiedWork>> IdentifiedAsync(ApplicationDbContext db, CancellationToken cancellationToken)
    {
        // The library's own: a film's TMDb id, a series' TMDb TV id (ADR 0017: each in its own numbering).
        var libraryIds = await (
                from identifier in db.MediaWorkIdentifiers.AsNoTracking()
                join work in db.MediaWorks.AsNoTracking() on identifier.MediaWorkId equals work.Id
                where (work.Kind == MediaWorkKind.Movie && identifier.Scheme == MediaIdentifierScheme.Tmdb)
                    || (work.Kind == MediaWorkKind.Series && identifier.Scheme == MediaIdentifierScheme.TmdbTv)
                select new { WorkId = work.Id, identifier.Scheme, identifier.Value })
            .ToListAsync(cancellationToken);
        // The owner's links, an episode's link to its show included: the show is what its folder holds.
        var linkIds = await db.LibraryItemLinks
            .AsNoTracking()
            .Where(link => link.MediaWorkId != null)
            .Select(link => new { WorkId = link.MediaWorkId!.Value, link.Scheme, link.Value, link.SetUtc })
            .ToListAsync(cancellationToken);

        // One catalog work per library work: the latest link (the owner's word), else the library's id in id order.
        var catalogOf = new Dictionary<Guid, (MediaWorkKind Kind, string Id)>();
        foreach (var link in linkIds.OrderByDescending(link => link.SetUtc))
            catalogOf.TryAdd(link.WorkId, (KindOf(link.Scheme), link.Value));
        foreach (var identifier in libraryIds.OrderBy(identifier => identifier.Value, StringComparer.Ordinal))
            catalogOf.TryAdd(identifier.WorkId, (KindOf(identifier.Scheme), identifier.Value));

        var locations = (await LibraryLinkFiles.LoadAsync(db, catalogOf.Keys, cancellationToken))
            .Where(file => file.LibraryLocationId is not null)
            .ToLookup(file => file.WorkId, file => file.LibraryLocationId!.Value);
        return
        [
            .. catalogOf
                .Select(pair => new LibraryLinkIdentifiedWork(pair.Key, pair.Value.Kind, pair.Value.Id, [.. locations[pair.Key].Distinct()]))
                .OrderBy(work => work.WorkId),
        ];
    }

    /// <summary>
    /// The genre each library mostly holds (<see cref="LibraryGenreProfile"/>), by library id: Animation, Documentary, or
    /// none — a library missing from the answer holds no identified work whose genres are cached.
    /// </summary>
    public static async Task<Dictionary<Guid, CatalogGenreRole?>> RolesAsync(ApplicationDbContext db, CancellationToken cancellationToken)
    {
        var identified = await IdentifiedAsync(db, cancellationToken);
        var genres = await LibraryLinkCatalogRows.GenreRolesAsync(
            db, [.. identified.Select(work => (work.CatalogKind, work.CatalogId)).Distinct()], cancellationToken);
        return identified
            .Where(work => genres.ContainsKey((work.CatalogKind, work.CatalogId)))
            .SelectMany(work => work.LocationIds.Select(locationId => (LocationId: locationId, Roles: genres[(work.CatalogKind, work.CatalogId)])))
            .GroupBy(entry => entry.LocationId)
            .ToDictionary(group => group.Key, group => LibraryGenreProfile.Of([.. group.Select(entry => entry.Roles)]));
    }

    /// <summary>The kind of catalog work a TMDb id names, by its numbering (ADR 0017).</summary>
    public static MediaWorkKind KindOf(MediaIdentifierScheme scheme) =>
        scheme == MediaIdentifierScheme.TmdbTv ? MediaWorkKind.Series : MediaWorkKind.Movie;
}
