using MediaAcquisitionManager.Api.Mapping;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Library;
using MediaAcquisitionManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Services.LibraryLinks;

/// <summary>
/// MAM-34 — the library works the TMDb suggestions are for: a film without a TMDb id, a series without a TMDb TV id, an
/// episode that belongs to no series — and none with a link attached. The one rule the suggestion run searches by, the
/// review queue walks, the confident list counts and "Accept all confident" checks again.
/// </summary>
/// <remarks>
/// An episode of a series is known by its series' ids (decision 15), so it is never asked about on its own. The ids that
/// count are the library's own: a work holding a link is out whatever its library says, as its owner already decided.
/// </remarks>
internal static class LibraryLinkPopulation
{
    /// <summary>The population as SQL.</summary>
    public static IQueryable<MediaWorkEntity> Of(ApplicationDbContext db) =>
        db.MediaWorks.Where(work =>
            !db.LibraryItemLinks.Any(link => link.MediaWorkId == work.Id)
            && ((work.Kind == MediaWorkKind.Movie
                    && !db.MediaWorkIdentifiers.Any(identifier => identifier.MediaWorkId == work.Id && identifier.Scheme == MediaIdentifierScheme.Tmdb))
                || (work.Kind == MediaWorkKind.Series
                    && !db.MediaWorkIdentifiers.Any(identifier => identifier.MediaWorkId == work.Id && identifier.Scheme == MediaIdentifierScheme.TmdbTv))
                || (work.Kind == MediaWorkKind.Episode
                    && !db.MediaEpisodePositions.Any(position => position.MediaWorkId == work.Id && position.SeriesWorkId != null))));

    /// <summary>
    /// The population in the Library page's title order (the title the card shows, in <see cref="LibraryTitleOrder"/>),
    /// then by id, so the order is total and the review queue's index points at one work.
    /// </summary>
    /// <remarks>Sorted here, not in SQL: the card's title is picked from its titles by the same comparer, and a library has
    /// a few thousand works at most.</remarks>
    public static async Task<List<LibraryLinkPopulationWork>> ListAsync(ApplicationDbContext db, CancellationToken cancellationToken)
    {
        var population = Of(db);
        var works = await population.AsNoTracking().Select(work => new { work.Id, work.Kind }).ToListAsync(cancellationToken);
        var titles = (await db.MediaWorkTitles
                .AsNoTracking()
                .Where(title => population.Any(work => work.Id == title.MediaWorkId))
                .ToListAsync(cancellationToken))
            .ToLookup(title => title.MediaWorkId);
        return
        [
            .. works
                .Select(work => new LibraryLinkPopulationWork(work.Id, work.Kind, TitleOf([.. titles[work.Id]])))
                .OrderBy(work => work.Title ?? "", LibraryTitleOrder.Comparer)
                .ThenBy(work => work.Id),
        ];
    }

    // The card's rule (LibraryMapper): the first display title in title order, else the first original one.
    private static string? TitleOf(IReadOnlyList<MediaWorkTitleEntity> titles) =>
        (LibraryMapper.FirstInTitleOrder(titles, MediaTitleKind.Display) ?? LibraryMapper.FirstInTitleOrder(titles, MediaTitleKind.Original))?.Text;
}
