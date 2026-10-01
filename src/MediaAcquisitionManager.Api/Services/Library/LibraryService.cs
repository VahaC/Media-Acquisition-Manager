using System.Globalization;
using MediaAcquisitionManager.Api.Auth;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Api.Mapping;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Library;
using MediaAcquisitionManager.Core.Paging;
using MediaAcquisitionManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Services.Library;

/// <inheritdoc cref="ILibraryService"/>
public sealed class LibraryService(ApplicationDbContext db, ILibraryMapper mapper, ILibraryWorkReader reader) : ILibraryService
{
    /// <summary>How much of a location id that isn't one a sentence repeats: a Guid is 36 characters, and
    /// a pasted page of text shouldn't come back whole.</summary>
    private const int LocationEchoLength = 64;

    /// <summary>How much of an unknown identifiers filter a sentence repeats; the one filter is 7
    /// characters.</summary>
    private const int IdentifiersEchoLength = 32;

    /// <summary>The one identifiers filter: works lacking an IMDb or a TMDb id.</summary>
    private const string MissingIdentifiersFilter = "Missing";

    public async Task<LibraryWorkListResult> ListWorksAsync(
        string? search,
        string? locationId,
        string? identifiers,
        int page,
        int pageSize,
        Guid callerUserId,
        CancellationToken cancellationToken = default)
    {
        if (PagingRules.Validate(page, pageSize) is { } pagingError)
            return LibraryWorkListResult.Invalid(pagingError);

        var works = db.MediaWorks.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(locationId))
        {
            if (!Guid.TryParse(locationId.Trim(), out var location))
                return LibraryWorkListResult.Invalid($"“{Echo(locationId.Trim(), LocationEchoLength)}” is not a library.");

            // An id no library has is not an error: it simply holds nothing (a library a sync just
            // removed, in a page loaded before it).
            works = works.Where(work => db.MediaVersions.Any(version => version.MediaWorkId == work.Id
                && db.PhysicalFiles.Any(file => file.MediaVersionId == version.Id && file.LibraryLocationId == location)));
        }

        if (!string.IsNullOrWhiteSpace(identifiers))
        {
            if (!string.Equals(identifiers.Trim(), MissingIdentifiersFilter, StringComparison.OrdinalIgnoreCase))
            {
                return LibraryWorkListResult.Invalid(
                    $"“{Echo(identifiers.Trim(), IdentifiersEchoLength)}” is not an identifier filter. Use {MissingIdentifiersFilter}.");
            }

            // Specification §3.1 matches on both; a work lacking either can't be matched reliably.
            works = works.Where(work =>
                !db.MediaWorkIdentifiers.Any(identifier => identifier.MediaWorkId == work.Id && identifier.Scheme == MediaIdentifierScheme.Imdb)
                || !db.MediaWorkIdentifiers.Any(identifier => identifier.MediaWorkId == work.Id && identifier.Scheme == MediaIdentifierScheme.Tmdb));
        }

        if (!string.IsNullOrWhiteSpace(search))
            works = Search(works, search.Trim());

        var totalCount = await works.CountAsync(cancellationToken);
        // Past the last page there is nothing to read; and a page far enough out would overflow the
        // offset, which EF Core takes as an int (the UserAccountService guard).
        var offset = (long)(page - 1) * pageSize;
        if (offset >= totalCount)
            return LibraryWorkListResult.Listed(new LibraryWorkListResponse([], totalCount, page, pageSize));

        var pageWorks = await OrderByTitle(works)
            .Skip((int)offset)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        var items = await reader.ReadAsync(pageWorks, await db.IsInstallationOwnerAsync(callerUserId, cancellationToken), cancellationToken);
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
    /// The works a search term finds: a title, an identifier or a file path containing it, in any letter
    /// case — or, when the term is a year, a title given with that year.
    /// </summary>
    /// <remarks>
    /// MAM-8 — compared in their normalised copies (<see cref="LibrarySearchText"/>), as SQLite folds only
    /// ASCII letters; "чужі" finds "Чужі". An identifier is ASCII, so SQLite's own <c>upper()</c> is
    /// enough for it. Contains becomes <c>instr()</c>, which reads '%' and '_' as themselves: a search is
    /// text, not a <c>LIKE</c> pattern.
    /// </remarks>
    private IQueryable<MediaWorkEntity> Search(IQueryable<MediaWorkEntity> works, string term)
    {
        var normalized = LibrarySearchText.Normalize(term);
        // Checked for a value in the query itself: without it, a term that isn't a year would compare
        // Year with null and find every title without a year.
        int? year = int.TryParse(term, NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number is >= 1 and <= 9999
            ? number
            : null;

        return works.Where(work =>
            db.MediaWorkTitles.Any(title => title.MediaWorkId == work.Id
                && (title.NormalizedText.Contains(normalized) || (year != null && title.Year == year)))
            || db.MediaWorkIdentifiers.Any(identifier => identifier.MediaWorkId == work.Id
                && identifier.Value.ToUpper().Contains(normalized))
            || db.MediaVersions.Any(version => version.MediaWorkId == work.Id
                && db.PhysicalFiles.Any(file => file.MediaVersionId == version.Id && file.NormalizedServicePath.Contains(normalized))));
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
        works
            .OrderBy(work => EF.Functions.Collate(
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
                LibraryTitleOrder.CollationName))
            .ThenBy(work => work.Id);

    private static string Echo(string value, int maximumLength) =>
        value.Length <= maximumLength ? value : string.Concat(value.AsSpan(0, maximumLength), "…");
}
