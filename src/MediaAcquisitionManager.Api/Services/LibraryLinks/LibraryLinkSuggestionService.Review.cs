using System.Globalization;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Api.Services.Catalog;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Library;
using MediaAcquisitionManager.Core.Media;
using MediaAcquisitionManager.Core.Paging;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Services.LibraryLinks;

// MAM-34 — the TMDb page's review of the works without a TMDb id: one at a time in the queue, the owner's "none fits",
// the confident suggestions with each library's numbers, and "Accept all confident". Filtered, paged and ordered by the
// server (house rule 7), however many works there are.
public sealed partial class LibraryLinkSuggestionService
{
    /// <summary>
    /// R20 — the most works one "Accept all confident" links: the largest page the confident list serves, so a click links
    /// the ticked rows of the page shown and nothing the owner hasn't seen.
    /// </summary>
    public const int MaximumAccepted = PagingRules.MaximumPageSize;

    private const string NothingAcceptedError = "Tick at least one work to link.";

    private const string AcceptItemMissingError = "Each ticked work needs its work, its kind and its TMDb id.";

    private const string WorkMissingError = "Give the work to link.";

    private const string IdMissingError = "Give the TMDb id to link.";

    private const string WorkGoneReason = "The work is no longer in the library.";

    private const string LinkedMeanwhileReason = "It was linked meanwhile.";

    private const string HasIdReason = "Its library reports a TMDb id for it now.";

    private const string NoneFitsReason = "You marked that none fits.";

    private const string NoLongerConfidentReason = "It is no longer the work's confident suggestion.";

    private static string TooManyAcceptedError => $"At most {MaximumAccepted} works can be linked at once.";

    private static string IndexInvalidError(string index) => $"“{Echo(index)}” is not a place in the queue. Use 0 or more.";

    private static string LocationInvalidError(string location) => $"“{Echo(location)}” is not a library id.";

    private static string WorkIdInvalidError(string workId) => $"“{Echo(workId)}” is not a work id.";

    private static string IdInvalidError(string id) => $"“{Echo(id)}” is not a TMDb id.";

    private static string TickedTwiceError(Guid workId) => $"“{workId}” is ticked twice.";

    public async Task<LibraryLinkQueueResult> ReadQueueAsync(string? index, CancellationToken cancellationToken = default)
    {
        var place = 0;
        if (!string.IsNullOrWhiteSpace(index)
            && (!int.TryParse(index.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out place) || place < 0))
            return LibraryLinkQueueResult.Invalid(IndexInvalidError(index.Trim()));

        // The works without an id the owner hasn't marked "none fits": after a save or a "none fits" the work leaves, and
        // the same index shows the next one.
        var noneFits = await NoneFitsAsync(workIds: null, cancellationToken);
        var queue = (await LibraryLinkPopulation.ListAsync(db, cancellationToken)).Where(work => !noneFits.Contains(work.Id)).ToList();
        var current = place < queue.Count ? (await links.ReadDialogAsync(queue[place].Id, cancellationToken)).Dialog : null;
        return LibraryLinkQueueResult.Read(new LibraryLinkQueueResponse(queue.Count, place, current));
    }

    public async Task<LibraryLinkStatus> MarkNoneFitsAsync(Guid workId, Guid ownerUserId, CancellationToken cancellationToken = default)
    {
        for (var attempt = 1; ; attempt++)
        {
            if (!await db.MediaWorks.AnyAsync(work => work.Id == workId, cancellationToken))
                return LibraryLinkStatus.NotFound;

            var now = time.GetUtcNow().UtcDateTime;
            var marked = await db.LibraryLinkSearches
                .Where(search => search.MediaWorkId == workId)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(search => search.NoneFitsUtc, now)
                        .SetProperty(search => search.NoneFitsByUserId, ownerUserId),
                    cancellationToken) == 1;
            if (marked)
                break;

            // Never searched: a search row of its own with no query — never shown as searched (LibraryLinkSuggestionReader)
            // — which the next run searches by the work's queries, keeping the mark.
            db.LibraryLinkSearches.Add(new LibraryLinkSearchEntity
            {
                MediaWorkId = workId,
                QueryKey = "",
                SearchedUtc = now,
                NoneFitsUtc = now,
                NoneFitsByUserId = ownerUserId,
                CreatedUtc = now,
            });
            try
            {
                await db.SaveChangesAsync(cancellationToken);
                break;
            }
            catch (DbUpdateException) when (attempt == 1)
            {
                // A run saved the work's search meanwhile, or a sync deleted the work: the next attempt sees which.
            }
            finally
            {
                db.ChangeTracker.Clear();
            }
        }

        logger.LogInformation("Work {WorkId} was marked as fitting none of its TMDb suggestions.", workId);
        return LibraryLinkStatus.Succeeded;
    }

    public async Task<LibraryLinkStatus> ClearNoneFitsAsync(Guid workId, CancellationToken cancellationToken = default)
    {
        if (!await db.MediaWorks.AnyAsync(work => work.Id == workId, cancellationToken))
            return LibraryLinkStatus.NotFound;

        await db.LibraryLinkSearches
            .Where(search => search.MediaWorkId == workId)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(search => search.NoneFitsUtc, (DateTime?)null)
                    .SetProperty(search => search.NoneFitsByUserId, (Guid?)null),
                cancellationToken);
        return LibraryLinkStatus.Succeeded;
    }

    public async Task<ConfidentSuggestionListResult> ListConfidentAsync(
        string? locationId, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        if (PagingRules.Validate(page, pageSize) is { } pagingError)
            return ConfidentSuggestionListResult.Invalid(pagingError);
        Guid? location = null;
        if (!string.IsNullOrWhiteSpace(locationId))
        {
            if (!Guid.TryParse(locationId.Trim(), out var parsed))
                return ConfidentSuggestionListResult.Invalid(LocationInvalidError(locationId.Trim()));
            location = parsed;
        }

        var population = await LibraryLinkPopulation.ListAsync(db, cancellationToken);
        List<Guid> ids = [.. population.Select(work => work.Id)];
        // A work's libraries: those its files sit in (a series' episodes'); none known — no file, or none any library
        // covers — counts under no library.
        var librariesOf = (await LibraryLinkFiles.LoadAsync(db, ids, cancellationToken)).ToLookup(file => file.WorkId, file => file.LibraryLocationId);
        var locationsOf = population.ToDictionary(
            work => work.Id,
            work => librariesOf[work.Id].OfType<Guid>().Distinct().Select(id => (Guid?)id).DefaultIfEmpty(null).ToList());
        var noneFits = await NoneFitsAsync(ids, cancellationToken);
        var confident = await suggestions.ReadConfidentAsync([.. ids.Where(id => !noneFits.Contains(id))], cancellationToken);

        // The libraries the Library page's filter offers (LibraryService.ListLocationsAsync): only those that hold a file.
        // Jellyfin's box sets library ("Колекції") holds none, so its works are never any work's.
        var libraries = await db.LibraryLocations
            .AsNoTracking()
            .Where(library => db.PhysicalFiles.Any(file => file.LibraryLocationId == library.Id))
            .Select(library => new { library.Id, library.Name })
            .ToListAsync(cancellationToken);
        var counts = libraries
            .OrderBy(library => library.Name, LibraryTitleOrder.Comparer)
            .ThenBy(library => library.Id)
            .Select(library => new LibraryLinkLocationCountDto(
                library.Id,
                library.Name,
                population.Count(work => locationsOf[work.Id].Contains(library.Id)),
                confident.Keys.Count(workId => locationsOf[workId].Contains(library.Id))))
            .ToList();
        var unknown = population.Count(work => locationsOf[work.Id] is [null]);
        if (unknown > 0)
            counts.Add(new LibraryLinkLocationCountDto(null, null, unknown, confident.Keys.Count(workId => locationsOf[workId] is [null])));

        // By the Library page's title order (the population's), one library's or all.
        var listed = population
            .Where(work => confident.ContainsKey(work.Id) && (location is null || locationsOf[work.Id].Contains(location)))
            .ToList();
        var offset = (long)(page - 1) * pageSize;
        List<Guid> pageIds = offset >= listed.Count ? [] : [.. listed.Skip((int)offset).Take(pageSize).Select(work => work.Id)];
        var works = (await db.MediaWorks.AsNoTracking().Where(work => EF.Parameter(pageIds).Contains(work.Id)).ToListAsync(cancellationToken))
            .ToDictionary(work => work.Id);
        List<MediaWorkEntity> ordered = [.. pageIds.Where(works.ContainsKey).Select(id => works[id])];
        // The cards exactly as the Library page shows them — the owner's, as only the owner opens this.
        var cards = await workReader.ReadAsync(ordered, forOwner: true, cancellationToken: cancellationToken);
        return ConfidentSuggestionListResult.Listed(new ConfidentSuggestionListResponse(
            [.. cards.Select(card => new ConfidentSuggestionDto(card, confident[card.Id]))],
            listed.Count,
            page,
            pageSize,
            counts));
    }

    public async Task<LibraryLinkAcceptResult> AcceptConfidentAsync(
        LibraryLinkAcceptRequest? request, Guid ownerUserId, CancellationToken cancellationToken = default)
    {
        var items = request?.Items ?? [];
        if (items.Count == 0)
            return LibraryLinkAcceptResult.Invalid(NothingAcceptedError);
        if (items.Count > MaximumAccepted)
            return LibraryLinkAcceptResult.Invalid(TooManyAcceptedError);

        var picks = new List<LibraryLinkBatchPick>();
        foreach (var item in items)
        {
            var (pick, error) = Parse(item);
            if (pick is null)
                return LibraryLinkAcceptResult.Invalid(error!);
            if (picks.Any(other => other.WorkId == pick.WorkId))
                return LibraryLinkAcceptResult.Invalid(TickedTwiceError(pick.WorkId));
            picks.Add(pick);
        }

        if (!await catalogs.HasCatalogAsync(cancellationToken))
            return LibraryLinkAcceptResult.Conflict(CatalogConnector.NoCatalogError);

        // Checked again now, not trusted from the page: a sync, another click or the owner's "none fits" may have changed
        // any of them since the list was read, and only a confident suggestion is linked unseen (decision 8).
        List<Guid> ids = [.. picks.Select(pick => pick.WorkId)];
        var existing = (await db.MediaWorks.AsNoTracking().Where(work => EF.Parameter(ids).Contains(work.Id)).Select(work => work.Id).ToListAsync(cancellationToken)).ToHashSet();
        var linked = (await db.LibraryItemLinks
                .AsNoTracking()
                .Where(link => link.MediaWorkId != null && EF.Parameter(ids).Contains(link.MediaWorkId!.Value))
                .Select(link => link.MediaWorkId!.Value)
                .ToListAsync(cancellationToken))
            .ToHashSet();
        var population = (await LibraryLinkPopulation.Of(db).Where(work => EF.Parameter(ids).Contains(work.Id)).Select(work => work.Id).ToListAsync(cancellationToken))
            .ToHashSet();
        var noneFits = await NoneFitsAsync(ids, cancellationToken);
        var confident = await suggestions.ReadConfidentAsync([.. ids.Where(id => population.Contains(id) && !noneFits.Contains(id))], cancellationToken);

        string? SkipReasonOf(LibraryLinkBatchPick pick) =>
            !existing.Contains(pick.WorkId) ? WorkGoneReason
            : linked.Contains(pick.WorkId) ? LinkedMeanwhileReason
            : !population.Contains(pick.WorkId) ? HasIdReason
            : noneFits.Contains(pick.WorkId) ? NoneFitsReason
            : confident.TryGetValue(pick.WorkId, out var candidate) && candidate.CatalogKind == pick.CatalogKind
                && string.Equals(candidate.CatalogId, pick.CatalogId, StringComparison.Ordinal)
                ? null
                : NoLongerConfidentReason;

        var batch = await links.LinkBatchAsync([.. picks.Select(pick => pick with { SkipReason = SkipReasonOf(pick) })], ownerUserId, cancellationToken);
        return LibraryLinkAcceptResult.Accepted(batch);
    }

    /// <summary>One ticked work parsed (R18: strings, so every mistake gets its sentence), or the sentence.</summary>
    private static (LibraryLinkBatchPick? Pick, string? Error) Parse(LibraryLinkAcceptItemRequest? item)
    {
        if (item is null)
            return (null, AcceptItemMissingError);

        var workText = item.WorkId?.Trim();
        if (string.IsNullOrEmpty(workText))
            return (null, WorkMissingError);
        if (!Guid.TryParse(workText, out var workId))
            return (null, WorkIdInvalidError(workText));

        var kindText = item.CatalogKind?.Trim();
        if (string.IsNullOrEmpty(kindText))
            return (null, KindMissingError);
        if (CatalogKindOf(kindText) is not { } kind)
            return (null, KindInvalidError(kindText));

        var id = item.CatalogId?.Trim();
        if (string.IsNullOrEmpty(id))
            return (null, IdMissingError);
        if (!MediaIdentifierRules.IsTmdbId(id))
            return (null, IdInvalidError(id));

        return (new LibraryLinkBatchPick(workId, kind, id), null);
    }
}
