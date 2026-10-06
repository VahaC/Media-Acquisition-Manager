using MediaAcquisitionManager.Api.Contracts;

namespace MediaAcquisitionManager.Api.Services.Library;

/// <summary>
/// The Library page's reading side (MAM-8): the works every library reported, searched, filtered and
/// paged on the server, and the libraries to filter by. Every signed-in user reads it; why a file has no
/// app path only the owner does.
/// </summary>
/// <remarks>
/// MAM-32 — the page lists movies, series and the episodes of no series; a series lists its seasons with
/// counts, and its episodes are read a season at a time (<see cref="ListEpisodesAsync"/>). The search and
/// the filters reach into a series: it is listed when one of its episodes passes them, and a search the
/// series itself holds (its title, its ids) keeps every one of its episodes.
/// </remarks>
public interface ILibraryService
{
    /// <summary>
    /// One page of works, ordered by <paramref name="sort"/> (<see cref="Core.Enums.LibrarySort"/> by name; title
    /// in Ukrainian alphabetical order, letter case ignored, when blank), then by that title, then by
    /// id, with how many match in all. <paramref name="search"/> matches, in any letter case, a title, a
    /// registry id or a file path containing it, or — when it is a year — a title given with that year;
    /// <paramref name="locationId"/> keeps the works with a file in that library;
    /// <paramref name="identifiers"/> <c>Missing</c> keeps the works that lack an id of their kind;
    /// <paramref name="kind"/> <c>Movie</c> keeps the movies, <c>Series</c> the series and the episodes of
    /// no series. A file's path problem only when <paramref name="callerUserId"/> is the installation
    /// owner: it quotes the owner's path mappings (MAM-8), as the sources list's does.
    /// <see cref="LibraryWorkListStatus.Invalid"/> for a page below 1, a page size outside 1 to 100, a
    /// library that isn't an id, another identifiers filter, another kind, or an order that isn't one.
    /// </summary>
    Task<LibraryWorkListResult> ListWorksAsync(
        string? search,
        string? locationId,
        string? identifiers,
        string? kind,
        string? sort,
        int page,
        int pageSize,
        Guid callerUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// MAM-32 — one page of the episodes of the series <paramref name="seriesId"/>, in its season
    /// <paramref name="season"/> (a number, or <c>None</c> for those without one; blank for every season),
    /// ordered by season as the series lists its seasons (the regular ones, then the specials, then none),
    /// then number (none last), then title, then id. The search and filters of
    /// <see cref="ListWorksAsync"/>: a search or a missing-ids filter the series itself answers keeps every
    /// episode. <see cref="LibraryWorkListStatus.NotFound"/> when no series has that id.
    /// </summary>
    Task<LibraryWorkListResult> ListEpisodesAsync(
        Guid seriesId,
        string? season,
        string? search,
        string? locationId,
        string? identifiers,
        int page,
        int pageSize,
        Guid callerUserId,
        CancellationToken cancellationToken = default);

    /// <summary>Every library that holds at least one file, ordered by name, then by source name.</summary>
    Task<LibraryLocationListResponse> ListLocationsAsync(CancellationToken cancellationToken = default);
}
