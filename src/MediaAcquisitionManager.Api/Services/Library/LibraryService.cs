using System.Globalization;
using System.Linq.Expressions;
using MediaAcquisitionManager.Api.Auth;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Api.Mapping;
using MediaAcquisitionManager.Api.Services.LibraryLinks;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Library;
using MediaAcquisitionManager.Core.Paging;
using MediaAcquisitionManager.Core.Texts;
using MediaAcquisitionManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Services.Library;

/// <inheritdoc cref="ILibraryService"/>
/// <remarks>
/// MAM-32 — every filter is a set of work ids in SQL (the works a search finds, the works with a file in a
/// library, the works lacking an id of their kind), so one rule serves a movie, an episode and a series
/// alike: a movie or an episode of no series passes when it is in every set, a series when one of its
/// episodes is — the series' own membership standing in for its episodes' where a search or a missing-ids
/// filter is about the series itself. A library holds files, so only the episodes' files answer it.
/// </remarks>
public sealed class LibraryService(ApplicationDbContext db, ILibraryMapper mapper, ILibraryWorkReader reader, ILibraryLinkReader links) : ILibraryService
{
    /// <summary>How much of a location id that isn't one a sentence repeats: a Guid is 36 characters, and
    /// a pasted page of text shouldn't come back whole.</summary>
    private const int LocationEchoLength = 64;

    /// <summary>How much of an unknown identifiers or kind filter, or season, a sentence repeats; the
    /// longest a filter takes is 7 characters.</summary>
    private const int FilterEchoLength = 32;

    /// <summary>The one identifiers filter: works lacking an id of their kind.</summary>
    private const string MissingIdentifiersFilter = "Missing";

    /// <summary>MAM-32 — the season of the episodes the library gives no season number.</summary>
    private const string NoSeason = "None";

    /// <summary>MAM-32 — the highest season number taken; past it is no season a library numbers.</summary>
    private const int SeasonMaximum = 9999;

    /// <summary>MAM-32 — where the specials (season 0) sort among the seasons: after every regular one,
    /// before the episodes without a season.</summary>
    private const int SpecialsOrder = int.MaxValue - 1;

    public async Task<LibraryWorkListResult> ListWorksAsync(
        string? search,
        string? locationId,
        string? identifiers,
        string? kind,
        string? sort,
        int page,
        int pageSize,
        Guid callerUserId,
        CancellationToken cancellationToken = default)
    {
        if (PagingRules.Validate(page, pageSize) is { } pagingError)
            return LibraryWorkListResult.Invalid(pagingError);
        if (ReadFilters(search, locationId, identifiers) is not { } filters)
            return LibraryWorkListResult.Invalid(FilterError(locationId, identifiers)!);
        var order = LibrarySort.Title;
        if (!string.IsNullOrWhiteSpace(sort))
        {
            // By name, never by number: Enum.TryParse would take "5" for Largest.
            var name = Enum.GetNames<LibrarySort>().FirstOrDefault(candidate => candidate.Equals(sort.Trim(), StringComparison.OrdinalIgnoreCase));
            if (name is null)
                return LibraryWorkListResult.Invalid(Message.Of("library.list.sort-unknown", ("value", Echo(sort.Trim(), FilterEchoLength))));
            order = Enum.Parse<LibrarySort>(name);
        }

        MediaWorkKind? kindFilter = null;
        if (!string.IsNullOrWhiteSpace(kind))
        {
            kindFilter = kind.Trim() switch
            {
                var value when string.Equals(value, nameof(MediaWorkKind.Movie), StringComparison.OrdinalIgnoreCase) => MediaWorkKind.Movie,
                var value when string.Equals(value, nameof(MediaWorkKind.Series), StringComparison.OrdinalIgnoreCase) => MediaWorkKind.Series,
                _ => null,
            };
            if (kindFilter is null)
                return LibraryWorkListResult.Invalid(Message.Of("library.list.kind-unknown", ("value", Echo(kind.Trim(), FilterEchoLength))));
        }

        // A movie, or an episode no position puts in a series (the Tom & Jerry Kids item): listed itself.
        var standalone = db.MediaWorks.Where(work =>
            (kindFilter != MediaWorkKind.Series && work.Kind == MediaWorkKind.Movie)
            || (kindFilter != MediaWorkKind.Movie && work.Kind == MediaWorkKind.Episode
                && !db.MediaEpisodePositions.Any(position => position.MediaWorkId == work.Id && position.SeriesWorkId != null)));
        if (filters.SearchHits is { } searchHits)
            standalone = standalone.Where(work => searchHits.Contains(work.Id));
        if (filters.LocationHits is { } locationHits)
            standalone = standalone.Where(work => locationHits.Contains(work.Id));
        if (filters.MissingHits is { } missingHits)
            standalone = standalone.Where(work => missingHits.Contains(work.Id));

        // A series: listed when one of its episodes passes.
        var keptPositions = KeptPositions(filters);
        var standaloneIds = standalone.Select(work => work.Id);
        var seriesIds = keptPositions.Select(position => position.SeriesWorkId!.Value);
        var works = kindFilter == MediaWorkKind.Movie
            ? db.MediaWorks.AsNoTracking().Where(work => standaloneIds.Contains(work.Id))
            : db.MediaWorks.AsNoTracking().Where(work =>
                standaloneIds.Contains(work.Id) || (work.Kind == MediaWorkKind.Series && seriesIds.Contains(work.Id)));

        var totalCount = await works.CountAsync(cancellationToken);
        // Past the last page there is nothing to read; and a page far enough out would overflow the
        // offset, which EF Core takes as an int (the UserAccountService guard).
        var offset = (long)(page - 1) * pageSize;
        if (offset >= totalCount)
            return LibraryWorkListResult.Listed(new LibraryWorkListResponse([], totalCount, page, pageSize));

        var pageWorks = await OrderWorks(works, order)
            .Skip((int)offset)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        var pageSeries = pageWorks.Where(work => work.Kind == MediaWorkKind.Series).Select(work => work.Id).ToList();
        var seasons = pageSeries.Count == 0
            ? new Dictionary<Guid, IReadOnlyList<LibrarySeasonDto>>()
            : await LibrarySeasonCounts.ReadAsync(keptPositions, pageSeries, cancellationToken);
        var items = await reader.ReadAsync(pageWorks, await db.IsInstallationOwnerAsync(callerUserId, cancellationToken), seasons, cancellationToken);
        return LibraryWorkListResult.Listed(new LibraryWorkListResponse(items, totalCount, page, pageSize));
    }

    public async Task<LibraryWorkListResult> ListEpisodesAsync(
        Guid seriesId,
        string? season,
        string? search,
        string? locationId,
        string? identifiers,
        int page,
        int pageSize,
        Guid callerUserId,
        CancellationToken cancellationToken = default)
    {
        if (!await db.MediaWorks.AnyAsync(work => work.Id == seriesId && work.Kind == MediaWorkKind.Series, cancellationToken))
            return LibraryWorkListResult.NotFound;
        if (PagingRules.Validate(page, pageSize) is { } pagingError)
            return LibraryWorkListResult.Invalid(pagingError);
        if (ReadFilters(search, locationId, identifiers) is not { } filters)
            return LibraryWorkListResult.Invalid(FilterError(locationId, identifiers)!);

        var positions = KeptPositions(filters).Where(position => position.SeriesWorkId == seriesId);
        if (!string.IsNullOrWhiteSpace(season))
        {
            var trimmed = season.Trim();
            if (string.Equals(trimmed, NoSeason, StringComparison.OrdinalIgnoreCase))
            {
                positions = positions.Where(position => position.SeasonNumber == null);
            }
            else if (int.TryParse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number <= SeasonMaximum)
            {
                positions = positions.Where(position => position.SeasonNumber == number);
            }
            else
            {
                // The template names NoSeason ("None") itself: an API token, the same in every language.
                return LibraryWorkListResult.Invalid(Message.Of("library.list.season-unknown", ("value", Echo(trimmed, FilterEchoLength))));
            }
        }

        var episodeIds = positions.Select(position => position.MediaWorkId);
        var works = db.MediaWorks.AsNoTracking().Where(work => episodeIds.Contains(work.Id));
        var totalCount = await works.CountAsync(cancellationToken);
        var offset = (long)(page - 1) * pageSize;
        if (offset >= totalCount)
            return LibraryWorkListResult.Listed(new LibraryWorkListResponse([], totalCount, page, pageSize));

        // By the place the listed positions give each episode — the seasons in the order a series lists them
        // (LibrarySeasonCounts: the regular ones, then the specials, then none), so every season reads as
        // one run —, a number missing put last, then by title: the halves of an episode sit side by side,
        // and the order is total.
        var pageWorks = await ThenByTitle(works
                .OrderBy(work => positions.Where(position => position.MediaWorkId == work.Id)
                    .Min(position => position.SeasonNumber == 0 ? SpecialsOrder : position.SeasonNumber) ?? int.MaxValue)
                .ThenBy(work => positions.Where(position => position.MediaWorkId == work.Id).Min(position => position.EpisodeNumber) ?? int.MaxValue))
            .Skip((int)offset)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        var items = await reader.ReadAsync(pageWorks, await db.IsInstallationOwnerAsync(callerUserId, cancellationToken), cancellationToken: cancellationToken);
        return LibraryWorkListResult.Listed(new LibraryWorkListResponse(items, totalCount, page, pageSize));
    }

    public async Task<LibraryLocationListResponse> ListLocationsAsync(CancellationToken cancellationToken = default)
    {
        // Only libraries that hold a file: the filter never offers a choice that lists nothing.
        var locations = await db.LibraryLocations
            .AsNoTracking()
            .Where(location => db.PhysicalFiles.Any(file => file.LibraryLocationId == location.Id))
            .OrderBy(location => EF.Functions.Collate(location.Name, LibraryTitleOrder.CollationName))
            .ThenBy(location => location.SourceName)
            .ThenBy(location => location.Id)
            .ToListAsync(cancellationToken);
        return new LibraryLocationListResponse([.. locations.Select(mapper.ToDto)]);
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The filters as sets of work ids, each <c>null</c> when not asked for; <c>null</c> as a whole when a
    /// filter isn't one the list takes (<see cref="FilterError"/> says why).
    /// </summary>
    private Filters? ReadFilters(string? search, string? locationId, string? identifiers)
    {
        if (FilterError(locationId, identifiers) is not null)
            return null;

        IQueryable<Guid>? locationHits = null;
        if (!string.IsNullOrWhiteSpace(locationId))
        {
            // An id no library has is not an error: it simply holds nothing (a library a sync just
            // removed, in a page loaded before it).
            var location = Guid.Parse(locationId.Trim());
            locationHits = db.MediaVersions
                .Where(version => db.PhysicalFiles.Any(file => file.MediaVersionId == version.Id && file.LibraryLocationId == location))
                .Select(version => version.MediaWorkId);
        }

        return new Filters(
            string.IsNullOrWhiteSpace(search) ? null : SearchHits(search.Trim()),
            locationHits,
            string.IsNullOrWhiteSpace(identifiers) ? null : MissingHits());
    }

    /// <summary>Which filter the list doesn't take, as a message the Api renders (ADR 0042); <c>null</c> when it takes
    /// them all.</summary>
    private static Message? FilterError(string? locationId, string? identifiers)
    {
        if (!string.IsNullOrWhiteSpace(locationId) && !Guid.TryParse(locationId.Trim(), out _))
            return Message.Of("library.list.location-unknown", ("value", Echo(locationId.Trim(), LocationEchoLength)));
        // The template names MissingIdentifiersFilter ("Missing") itself: an API token, the same in every language.
        if (!string.IsNullOrWhiteSpace(identifiers) && !string.Equals(identifiers.Trim(), MissingIdentifiersFilter, StringComparison.OrdinalIgnoreCase))
            return Message.Of("library.list.identifiers-unknown", ("value", Echo(identifiers.Trim(), FilterEchoLength)));
        return null;
    }

    /// <summary>
    /// MAM-32 — the positions of the episodes the filters keep: an episode passes a search or a missing-ids
    /// filter its series answers, and passes a library only by its own files.
    /// </summary>
    private IQueryable<MediaEpisodePositionEntity> KeptPositions(Filters filters)
    {
        var positions = db.MediaEpisodePositions.AsNoTracking().Where(position => position.SeriesWorkId != null);
        if (filters.SearchHits is { } searchHits)
            positions = positions.Where(position => searchHits.Contains(position.SeriesWorkId!.Value) || searchHits.Contains(position.MediaWorkId));
        if (filters.LocationHits is { } locationHits)
            positions = positions.Where(position => locationHits.Contains(position.MediaWorkId));
        if (filters.MissingHits is { } missingHits)
            positions = positions.Where(position => missingHits.Contains(position.SeriesWorkId!.Value) || missingHits.Contains(position.MediaWorkId));
        return positions;
    }

    /// <summary>
    /// The works a search term finds: a title, an identifier or a file path containing it, in any letter
    /// case — or, when the term is a year, a title given with that year.
    /// </summary>
    /// <remarks>
    /// <para>
    /// MAM-8 — compared in their normalised copies (<see cref="LibrarySearchText"/>), as SQLite folds only
    /// ASCII letters; "чужі" finds "Чужі". An identifier is ASCII, so SQLite's own <c>upper()</c> is
    /// enough for it. Contains becomes <c>instr()</c>, which reads '%' and '_' as themselves: a search is
    /// text, not a <c>LIKE</c> pattern.
    /// </para>
    /// <para>
    /// MAM-34 — the identifiers as the cards count them (<see cref="ILibraryLinkReader"/>): a linked id finds its work,
    /// and the id a link replaces doesn't while the card hides it.
    /// </para>
    /// </remarks>
    private IQueryable<Guid> SearchHits(string term)
    {
        var normalized = LibrarySearchText.Normalize(term);
        // Checked for a value in the query itself: without it, a term that isn't a year would compare
        // Year with null and find every title without a year.
        int? year = int.TryParse(term, NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number is >= 1 and <= 9999
            ? number
            : null;

        return db.MediaWorkTitles
            .Where(title => title.NormalizedText.Contains(normalized) || (year != null && title.Year == year))
            .Select(title => title.MediaWorkId)
            .Union(links.WorksWithAnIdContaining(normalized))
            .Union(db.MediaVersions
                .Where(version => db.PhysicalFiles.Any(file => file.MediaVersionId == version.Id && file.NormalizedServicePath.Contains(normalized)))
                .Select(version => version.MediaWorkId));
    }

    /// <summary>
    /// The works lacking an id of their kind (<see cref="LibraryReliableSchemes"/>): specification §3.1
    /// matches on them, and a work lacking one can't be matched reliably by a release naming it.
    /// </summary>
    /// <remarks>
    /// MAM-34 — never a work that counts as linked (decision 9), nor an episode of a linked series (R16): the mapper
    /// gives their cards no missing id, and a work the owner linked has nothing left to fix. The ids of the rest are their
    /// library's alone, so the library's rows answer for them.
    /// </remarks>
    private IQueryable<Guid> MissingHits()
    {
        var linked = links.LinkedWorkIds();
        return db.MediaWorks
            .Where(work => !linked.Contains(work.Id))
            .Where(work =>
                (work.Kind == MediaWorkKind.Movie
                    && (!db.MediaWorkIdentifiers.Any(identifier => identifier.MediaWorkId == work.Id && identifier.Scheme == MediaIdentifierScheme.Imdb)
                        || !db.MediaWorkIdentifiers.Any(identifier => identifier.MediaWorkId == work.Id && identifier.Scheme == MediaIdentifierScheme.Tmdb)))
                || (work.Kind == MediaWorkKind.Episode
                    && (!db.MediaWorkIdentifiers.Any(identifier => identifier.MediaWorkId == work.Id && identifier.Scheme == MediaIdentifierScheme.Imdb)
                        || !db.MediaWorkIdentifiers.Any(identifier => identifier.MediaWorkId == work.Id && identifier.Scheme == MediaIdentifierScheme.Tvdb)))
                || (work.Kind == MediaWorkKind.Series
                    && (!db.MediaWorkIdentifiers.Any(identifier => identifier.MediaWorkId == work.Id && identifier.Scheme == MediaIdentifierScheme.Imdb)
                        || !db.MediaWorkIdentifiers.Any(identifier => identifier.MediaWorkId == work.Id && identifier.Scheme == MediaIdentifierScheme.TmdbTv)
                        || !db.MediaWorkIdentifiers.Any(identifier => identifier.MediaWorkId == work.Id && identifier.Scheme == MediaIdentifierScheme.Tvdb))))
            .Select(work => work.Id);
    }

    /// <summary>
    /// By the work's title in Ukrainian alphabetical order, letter case ignored, then by id, so the order
    /// is total and no work shows on two pages.
    /// </summary>
    /// <remarks>
    /// MAM-8 — the title is the one the mapper shows: the first displayed title in that order, else the
    /// first original one, else none (sorted first), each picked by a correlated subquery. Both the pick
    /// and the order use the <see cref="LibraryTitleOrder.CollationName"/> collation, which the SQLite
    /// interceptor registers on every connection; SQLite's own collations compare code points, which
    /// would put І, Є and Ї before А and Ґ after Я.
    /// </remarks>
    private IOrderedQueryable<MediaWorkEntity> OrderByTitle(IQueryable<MediaWorkEntity> works) =>
        works.OrderBy(TitleOrderKey()).ThenBy(work => work.Id);

    /// <summary>MAM-32 — <see cref="OrderByTitle"/>'s order after an order of the caller's.</summary>
    private IOrderedQueryable<MediaWorkEntity> ThenByTitle(IOrderedQueryable<MediaWorkEntity> works) =>
        works.ThenBy(TitleOrderKey()).ThenBy(work => work.Id);

    /// <summary>
    /// The works in <paramref name="sort"/>'s order (<see cref="LibrarySort"/>). Every order but the title's own falls
    /// back to the title's, so a run of equal keys — a first sync stamps every work with one instant, and a library
    /// has many films of one year — still reads in the order the page started with.
    /// </summary>
    private IOrderedQueryable<MediaWorkEntity> OrderWorks(IQueryable<MediaWorkEntity> works, LibrarySort sort) => sort switch
    {
        LibrarySort.TitleDescending => works.OrderByDescending(TitleOrderKey()).ThenBy(work => work.Id),
        // A missing year is put last by the number it stands for: 0 is last when the years fall, int.MaxValue when they rise.
        LibrarySort.Newest => ThenByTitle(works.OrderByDescending(YearOrderKey(missing: 0))),
        LibrarySort.Oldest => ThenByTitle(works.OrderBy(YearOrderKey(missing: int.MaxValue))),
        LibrarySort.Largest => ThenByTitle(works.OrderByDescending(SizeOrderKey())),
        LibrarySort.Added => ThenByTitle(works.OrderByDescending(AddedOrderKey())),
        _ => OrderByTitle(works),
    };

    // The year the card shows — its mapper's pick: the first displayed title's, else the first original one's — with
    // `missing` for a work that has none. Spelled out as TitleOrderKey is, for the same reason: an expression EF Core
    // translates wherever it is used.
    private Expression<Func<MediaWorkEntity, int>> YearOrderKey(int missing) =>
        work =>
            db.MediaWorkTitles
                .Where(title => title.MediaWorkId == work.Id && title.Kind == MediaTitleKind.Display)
                .OrderBy(title => EF.Functions.Collate(title.Text, LibraryTitleOrder.CollationName))
                .ThenBy(title => title.Text)
                .ThenBy(title => title.Id)
                .Select(title => title.Year)
                .FirstOrDefault()
            ?? db.MediaWorkTitles
                .Where(title => title.MediaWorkId == work.Id && title.Kind == MediaTitleKind.Original)
                .OrderBy(title => EF.Functions.Collate(title.Text, LibraryTitleOrder.CollationName))
                .ThenBy(title => title.Text)
                .ThenBy(title => title.Id)
                .Select(title => title.Year)
                .FirstOrDefault()
            ?? missing;

    // The sizes its files report, added up: those of its own versions, and for a series (which has none of its own) of
    // every episode's. A hard link is a name of its own here, as on the card; a work with no file, or none that reports
    // a size, is 0.
    private Expression<Func<MediaWorkEntity, long>> SizeOrderKey() =>
        work => db.PhysicalFiles
            .Where(file => db.MediaVersions.Any(version => version.Id == file.MediaVersionId
                && (version.MediaWorkId == work.Id
                    || db.MediaEpisodePositions.Any(position => position.SeriesWorkId == work.Id && position.MediaWorkId == version.MediaWorkId))))
            .Sum(file => file.SizeBytes ?? 0);

    // When the app first saw the work; a series by its latest episode, which is how a new season brings a show up.
    // The series' own row is made with its first episode, so the latest episode is never earlier than it.
    private Expression<Func<MediaWorkEntity, DateTime>> AddedOrderKey() =>
        work => db.MediaWorks
            .Where(episode => db.MediaEpisodePositions.Any(position => position.SeriesWorkId == work.Id && position.MediaWorkId == episode.Id))
            .Max(episode => (DateTime?)episode.CreatedUtc) ?? work.CreatedUtc;

    // The title a card shows, as SQL in the list's collation: the first displayed title in title order,
    // else the first original one, else none. An expression rather than a method, so EF Core translates it
    // wherever it is used.
    private Expression<Func<MediaWorkEntity, string>> TitleOrderKey() =>
        work => EF.Functions.Collate(
            db.MediaWorkTitles
                .Where(title => title.MediaWorkId == work.Id && title.Kind == MediaTitleKind.Display)
                .OrderBy(title => EF.Functions.Collate(title.Text, LibraryTitleOrder.CollationName))
                .ThenBy(title => title.Text)
                .ThenBy(title => title.Id)
                .Select(title => title.Text)
                .FirstOrDefault()
            ?? db.MediaWorkTitles
                .Where(title => title.MediaWorkId == work.Id && title.Kind == MediaTitleKind.Original)
                .OrderBy(title => EF.Functions.Collate(title.Text, LibraryTitleOrder.CollationName))
                .ThenBy(title => title.Text)
                .ThenBy(title => title.Id)
                .Select(title => title.Text)
                .FirstOrDefault()
            ?? "",
            LibraryTitleOrder.CollationName);

    private static string Echo(string value, int maximumLength) =>
        value.Length <= maximumLength ? value : string.Concat(value.AsSpan(0, maximumLength), "…");

    /// <summary>The filters asked for, each the ids of the works it keeps; <c>null</c> when not asked
    /// for.</summary>
    private sealed record Filters(IQueryable<Guid>? SearchHits, IQueryable<Guid>? LocationHits, IQueryable<Guid>? MissingHits);
}
