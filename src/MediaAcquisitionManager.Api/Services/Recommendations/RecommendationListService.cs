using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Api.Mapping;
using MediaAcquisitionManager.Api.Services.Catalog;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Library;
using MediaAcquisitionManager.Core.Recommendations;
using MediaAcquisitionManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Services.Recommendations;

/// <inheritdoc cref="IRecommendationListService"/>
public sealed partial class RecommendationListService(
    ApplicationDbContext db,
    IRecommendationSettingsReader settings,
    IRecommendationRunService runs,
    ICatalogConnector catalogs,
    IRecommendationMapper mapper) : IRecommendationListService
{
    public async Task<RecommendationListResult> ListAsync(
        RecommendationListRequest request, Guid callerUserId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (RecommendationListRules.Validate(
                request.Tab,
                request.Reason,
                request.Available,
                request.GenreId,
                request.Decade,
                request.Kind,
                request.Sort,
                request.Page,
                request.PageSize,
                out var filter)
            is { } error)
            return RecommendationListResult.Invalid(error);
        // The caller's own tabs only (Vasyl's decision 5): a tab switched off is no list of theirs, not even by its URL.
        if (!(await settings.ReadAsync(callerUserId, cancellationToken)).IsOn(filter!.Tab))
            return RecommendationListResult.TabSwitchedOff;

        var tab = await ReadTabAsync(filter.Tab, callerUserId, cancellationToken);
        // Before the request's filters, so the option picked — and the others — stay on offer.
        var facets = FacetsOf(tab);
        // Ordered after the filters and before the page (Vasyl asked to sort the list); the facets don't depend on it.
        var kept = Sorted(tab.Works.Where(work => Keeps(filter, work)), filter.Sort).ToList();

        // Past the last page there is nothing to read; and a page far enough out would overflow the offset as an int
        // (the LibraryService guard).
        var offset = (long)(filter.Page - 1) * filter.PageSize;
        IReadOnlyList<RecommendationDto> items = offset >= kept.Count
            ? []
            : [
                .. kept
                    .Skip((int)offset)
                    .Take(filter.PageSize)
                    .Select(work => mapper.ToDto(work.Work, GenreNamesOf(work, tab.GenreNames), work.Reasons, work.Availability, work.Downloading)),
            ];
        return RecommendationListResult.Listed(new RecommendationListResponse(
            items,
            kept.Count,
            filter.Page,
            filter.PageSize,
            facets,
            await catalogs.HasCatalogAsync(cancellationToken),
            // runs/latest's own rule: the running run, else the newest; its requester and error for the owner only.
            await runs.GetLatestAsync(callerUserId, cancellationToken)));
    }

    // ── reading ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The works of <paramref name="tab"/> <paramref name="callerUserId"/> sees, with what a card, the filters and the
    /// orders need of each; and the catalog's genre names.
    /// </summary>
    /// <remarks>
    /// A handful of queries, each joined to the tab's candidates in SQL rather than sent a list of their ids — a tab
    /// holds thousands — and put together in memory: no query per work.
    /// </remarks>
    private async Task<ListedTab> ReadTabAsync(RecommendationTab tab, Guid callerUserId, CancellationToken cancellationToken)
    {
        var rows = await (
                from candidate in db.RecommendationCandidates.AsNoTracking()
                where candidate.Tab == tab
                join work in db.CatalogWorks.AsNoTracking() on candidate.CatalogWorkId equals work.Id
                select new { Candidate = candidate, Work = work })
            .ToListAsync(cancellationToken);
        var reasons = (await (
                    from reason in db.RecommendationReasons.AsNoTracking()
                    join candidate in db.RecommendationCandidates.AsNoTracking() on reason.RecommendationCandidateId equals candidate.Id
                    where candidate.Tab == tab
                    select reason)
                .ToListAsync(cancellationToken))
            .ToLookup(reason => reason.RecommendationCandidateId);
        var genres = (await (
                    from genre in db.CatalogWorkGenres.AsNoTracking()
                    join candidate in db.RecommendationCandidates.AsNoTracking() on genre.CatalogWorkId equals candidate.CatalogWorkId
                    where candidate.Tab == tab
                    select genre)
                .ToListAsync(cancellationToken))
            .ToLookup(genre => genre.CatalogWorkId, genre => genre.GenreId);
        var checks = await (
                from check in db.ReleaseAvailabilities.AsNoTracking()
                join candidate in db.RecommendationCandidates.AsNoTracking() on check.CatalogWorkId equals candidate.CatalogWorkId
                where candidate.Tab == tab
                select check)
            .ToDictionaryAsync(check => check.CatalogWorkId, cancellationToken);
        // Named from the catalog's list of the work's own kind: films and shows have lists of their own. A few dozen rows.
        var genreNames = (await db.CatalogGenres.AsNoTracking()
                .Select(genre => new { genre.Kind, genre.GenreId, genre.Name })
                .ToListAsync(cancellationToken))
            .ToDictionary(genre => (genre.Kind, genre.GenreId), genre => genre.Name);
        var dismissed = (await db.RecommendationDismissals.AsNoTracking()
                .Where(dismissal => dismissal.UserId == callerUserId)
                .Select(dismissal => new { dismissal.Kind, dismissal.CatalogId })
                .ToListAsync(cancellationToken))
            .Select(dismissal => (dismissal.Kind, dismissal.CatalogId))
            .ToHashSet();

        var shown = rows
            // The caller's "Not interested" marks, by kind and catalog id (Vasyl's decision 3): theirs alone.
            .Where(row => !dismissed.Contains((row.Work.Kind, row.Work.CatalogId)))
            // A fallback ("top rated", recommended for nothing the library holds) earns its place only by being there to
            // download: listed once a check found it at a release source, and not before.
            .Where(row => !row.Candidate.RequiresAvailability || checks.GetValueOrDefault(row.Work.Id)?.Found == true)
            .ToList();
        var downloads = await DownloadStatesAsync([.. shown.Select(row => MatchableWork.Of(row.Work, row.Candidate.MissingSeasons))], cancellationToken);

        var works = shown
            .Select(row => (Row: row, Download: downloads.GetValueOrDefault(row.Work.Id)))
            // Downloaded, waiting to be linked, or stopped by whoever sent it: the household has dealt with it.
            .Where(listed => listed.Download != DownloadState.Ended)
            .Select(listed => new ListedWork(
                listed.Row.Candidate,
                listed.Row.Work,
                [.. genres[listed.Row.Work.Id].Distinct()],
                [.. reasons[listed.Row.Candidate.Id].OrderBy(reason => reason.Position)],
                checks.GetValueOrDefault(listed.Row.Work.Id),
                listed.Download == DownloadState.Downloading))
            .ToList();
        return new ListedTab(works, genreNames);
    }

    // ── facets and filters ──────────────────────────────────────────────────────────────────────

    /// <summary>What the filters offer over every work of <paramref name="tab"/>, each with how many works have it; a
    /// value no work has is left out.</summary>
    private static RecommendationFacetsDto FacetsOf(ListedTab tab)
    {
        var reasonCounts = tab.Works
            .SelectMany(work => work.Reasons.Select(reason => reason.Kind).Distinct())
            .CountBy(kind => kind)
            .ToDictionary();
        var genres = tab.Works
            .SelectMany(work => work.GenreIds.Select(genreId => (work.Work.Kind, GenreId: genreId)))
            .GroupBy(genre => genre.GenreId)
            // In a tab of films and shows, the name of the films' list first: the catalog's two lists name a shared id
            // alike, and the choice must not depend on the order the works were read in.
            .Select(group => new
            {
                Id = group.Key,
                Name = group.Select(genre => genre.Kind).Distinct().Order()
                    .Select(kind => tab.GenreNames.GetValueOrDefault((kind, group.Key)))
                    .FirstOrDefault(name => name is not null),
                Count = group.Count(),
            })
            // A genre the catalog's lists don't name can't be offered by name; the run reads the lists before it stores.
            .Where(genre => genre.Name is not null)
            .OrderBy(genre => genre.Name, LibraryTitleOrder.Comparer)
            .ThenBy(genre => genre.Id)
            .Select(genre => new RecommendationGenreFacetDto(genre.Id, genre.Name!, genre.Count));
        var decades = tab.Works
            .Select(work => work.Decade)
            .OfType<int>()
            .CountBy(decade => decade)
            .OrderBy(decade => decade.Key)
            .Select(decade => new RecommendationDecadeFacetDto(decade.Key, decade.Value));
        var kinds = tab.Works
            .CountBy(work => work.Work.Kind)
            .OrderBy(kind => kind.Key)
            .Select(kind => new RecommendationKindFacetDto(kind.Key, kind.Value));
        return new RecommendationFacetsDto(
            // MAM-33 — a card's order, strongest first: the one the run stores each candidate's reasons in.
            [.. RecommendationReasonOrder.Kinds.Where(reasonCounts.ContainsKey).Select(kind => new RecommendationReasonFacetDto(kind, reasonCounts[kind]))],
            [.. genres],
            [.. decades],
            [.. kinds]);
    }

    /// <summary>
    /// <paramref name="works"/> in <paramref name="sort"/>'s order (<see cref="RecommendationSort"/>). Every order falls
    /// back to the score's — the score, how many rated it, then the title (ordinal) and the catalog id, the kind last as
    /// a tab can hold a film and a show of one id — so each is total and a page never reshuffles between two reads.
    /// </summary>
    private static IOrderedEnumerable<ListedWork> Sorted(IEnumerable<ListedWork> works, RecommendationSort sort)
    {
        var ordered = sort switch
        {
            // The rating floor's weighted average, as the score weighs it: a 10.0 of three votes is no top rating.
            RecommendationSort.Rating => works
                .OrderByDescending(work => RecommendationRatingFloor.WeightedRating(work.Work.Rating, work.Work.VoteCount))
                .ThenByDescending(work => work.Work.VoteCount),
            RecommendationSort.Votes => works.OrderByDescending(work => work.Work.VoteCount),
            // A work of no known date last either way: it is neither new nor old.
            RecommendationSort.Newest => works.OrderBy(work => work.Work.ReleaseDate is null).ThenByDescending(work => work.Work.ReleaseDate),
            RecommendationSort.Oldest => works.OrderBy(work => work.Work.ReleaseDate is null).ThenBy(work => work.Work.ReleaseDate),
            // The Library page's collation, so "Ґ" and "І" sit where a Ukrainian reader looks for them.
            RecommendationSort.Title => works
                .OrderBy(work => work.Work.Title, LibraryTitleOrder.Comparer)
                .ThenBy(work => work.Work.OriginalTitle ?? "", LibraryTitleOrder.Comparer),
            // Found by the last check, the most releases first; then checked and not found; then never checked.
            RecommendationSort.Releases => works
                .OrderBy(work => work.Availability switch
                {
                    { Found: true } => 0,
                    not null => 1,
                    null => 2,
                })
                .ThenByDescending(work => work.Availability?.ReleaseCount ?? 0),
            // Score, the default.
            _ => works.OrderByDescending(work => work.Candidate.Score),
        };
        return ordered
            .ThenByDescending(work => work.Candidate.Score)
            .ThenByDescending(work => work.Work.VoteCount)
            .ThenBy(work => work.Work.Title, StringComparer.Ordinal)
            .ThenBy(work => work.Work.CatalogId, StringComparer.Ordinal)
            .ThenBy(work => work.Work.Kind);
    }

    /// <summary>Whether <paramref name="work"/> passes every filter of <paramref name="filter"/>.</summary>
    private static bool Keeps(RecommendationListFilter filter, ListedWork work) =>
        (filter.Reason is not { } reason || work.Reasons.Any(listed => listed.Kind == reason))
        && (!filter.AvailableOnly || work.Availability?.Found == true)
        && (filter.GenreId is not { } genreId || work.GenreIds.Contains(genreId))
        && (filter.Decade is not { } decade || work.Decade == decade)
        && (filter.Kind is not { } kind || work.Work.Kind == kind);

    /// <summary>The names of <paramref name="work"/>'s genres, alphabetical; a genre the catalog's list of its kind
    /// doesn't name is left out.</summary>
    private static IReadOnlyList<string> GenreNamesOf(ListedWork work, IReadOnlyDictionary<(MediaWorkKind Kind, int GenreId), string> genreNames) =>
        [
            .. work.GenreIds
                .Select(genreId => genreNames.GetValueOrDefault((work.Work.Kind, genreId)))
                .OfType<string>()
                .Distinct(StringComparer.Ordinal)
                .Order(LibraryTitleOrder.Comparer)
                .ThenBy(name => name, StringComparer.Ordinal),
        ];

    // ── state ───────────────────────────────────────────────────────────────────────────────────

    /// <summary>A tab's works as the caller sees them, and the catalog's genre names by kind and id.</summary>
    private sealed record ListedTab(
        IReadOnlyList<ListedWork> Works,
        IReadOnlyDictionary<(MediaWorkKind Kind, int GenreId), string> GenreNames);

    /// <summary>One work of the list, with everything a card and the filters read of it.</summary>
    /// <param name="Candidate">The run's candidate: its score and whether it waits for a check.</param>
    /// <param name="Work">The catalog work.</param>
    /// <param name="GenreIds">The catalog's genre ids of it, each once.</param>
    /// <param name="Reasons">Its reasons, by position.</param>
    /// <param name="Availability">The last check of the release sources for it; <c>null</c> before one.</param>
    /// <param name="Downloading">Whether a followed download job is of it.</param>
    private sealed record ListedWork(
        RecommendationCandidateEntity Candidate,
        CatalogWorkEntity Work,
        IReadOnlyList<int> GenreIds,
        IReadOnlyList<RecommendationReasonEntity> Reasons,
        ReleaseAvailabilityEntity? Availability,
        bool Downloading)
    {
        /// <summary>The first year of the decade it came out in; <c>null</c> without a date.</summary>
        public int? Decade => Work.ReleaseDate?.Year / 10 * 10;
    }
}
