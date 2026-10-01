using System.Globalization;
using MediaAcquisitionManager.Api.Auth;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Api.Mapping;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Library;
using MediaAcquisitionManager.Core.Paging;
using MediaAcquisitionManager.Core.Paths;
using MediaAcquisitionManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Services.Library;

/// <inheritdoc cref="ILibraryService"/>
public sealed class LibraryService(ApplicationDbContext db, ILibraryMapper mapper) : ILibraryService
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
        var items = await ToDtosAsync(pageWorks, await db.IsInstallationOwnerAsync(callerUserId, cancellationToken), cancellationToken);
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

    /// <summary>
    /// The page's works with everything the cards show, a file's path problem only
    /// <paramref name="forOwner"/>. Batched, no N+1: one query each for the titles, the identifiers, the
    /// versions, their files, the files' libraries, the path mappings of the integrations that
    /// reported them, and the readings of the files the app inspected with their tracks (MAM-9) —
    /// whatever the page size.
    /// </summary>
    private async Task<List<LibraryWorkDto>> ToDtosAsync(List<MediaWorkEntity> works, bool forOwner, CancellationToken cancellationToken)
    {
        var workIds = works.Select(work => work.Id).ToList();
        var titles = (await db.MediaWorkTitles.AsNoTracking()
                .Where(title => workIds.Contains(title.MediaWorkId))
                .ToListAsync(cancellationToken))
            .ToLookup(title => title.MediaWorkId);
        var identifiers = (await db.MediaWorkIdentifiers.AsNoTracking()
                .Where(identifier => workIds.Contains(identifier.MediaWorkId))
                .ToListAsync(cancellationToken))
            .ToLookup(identifier => identifier.MediaWorkId);
        var versions = await db.MediaVersions.AsNoTracking()
            .Where(version => workIds.Contains(version.MediaWorkId))
            .ToListAsync(cancellationToken);

        var versionIds = versions.Select(version => version.Id).ToList();
        var files = await db.PhysicalFiles.AsNoTracking()
            .Where(file => versionIds.Contains(file.MediaVersionId))
            .ToListAsync(cancellationToken);

        var locationIds = files.Select(file => file.LibraryLocationId).OfType<Guid>().Distinct().ToList();
        var locationNames = await db.LibraryLocations.AsNoTracking()
            .Where(location => locationIds.Contains(location.Id))
            .ToDictionaryAsync(location => location.Id, location => location.Name, cancellationToken);

        // ADR 0003 — the reporting integration's mappings as they are now; a file whose integration has
        // none (or that the app read itself) maps as it is.
        var connectionIds = files.Select(file => file.SourceConnectionId).OfType<Guid>().Distinct().ToList();
        var pathMappings = (await db.PathMappings.AsNoTracking()
                .Where(mapping => connectionIds.Contains(mapping.IntegrationConnectionId))
                .ToListAsync(cancellationToken))
            .GroupBy(mapping => mapping.IntegrationConnectionId)
            .ToDictionary(
                integration => integration.Key,
                integration => (IReadOnlyList<PathMappingPair>)[.. integration
                    .OrderBy(mapping => mapping.Position)
                    .Select(mapping => new PathMappingPair(mapping.ServicePrefix, mapping.AppPrefix))]);

        // MAM-9 — what the media inspector read from each file's bytes; two names of one inode share one.
        var readingIds = files.Select(file => file.MediaInspectionId).OfType<Guid>().Distinct().ToList();
        var readings = await db.MediaInspections.AsNoTracking()
            .Where(reading => readingIds.Contains(reading.Id))
            .ToDictionaryAsync(reading => reading.Id, cancellationToken);
        var tracks = (await db.MediaInspectionTracks.AsNoTracking()
                .Where(track => readingIds.Contains(track.MediaInspectionId))
                .ToListAsync(cancellationToken))
            .ToLookup(track => track.MediaInspectionId);

        var versionsByWork = versions.ToLookup(version => version.MediaWorkId);
        var filesByVersion = files.ToLookup(file => file.MediaVersionId);
        return
        [
            .. works.Select(work => mapper.ToDto(
                work,
                [.. titles[work.Id]],
                [.. identifiers[work.Id]],
                [.. versionsByWork[work.Id]],
                version => [.. filesByVersion[version.Id].Select(file => mapper.ToDto(
                    file,
                    file.SourceConnectionId is { } connectionId ? pathMappings.GetValueOrDefault(connectionId) ?? [] : [],
                    file.LibraryLocationId is { } location ? locationNames.GetValueOrDefault(location) : null,
                    file.MediaInspectionId is { } readingId ? readings.GetValueOrDefault(readingId) : null,
                    file.MediaInspectionId is { } trackReadingId ? [.. tracks[trackReadingId]] : [],
                    forOwner))])),
        ];
    }

    private static string Echo(string value, int maximumLength) =>
        value.Length <= maximumLength ? value : string.Concat(value.AsSpan(0, maximumLength), "…");
}
