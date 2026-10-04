using MediaAcquisitionManager.Api.Auth;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Api.Mapping;
using MediaAcquisitionManager.Api.Services.Releases;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Matching;
using MediaAcquisitionManager.Core.Paging;
using MediaAcquisitionManager.Core.Search;
using MediaAcquisitionManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Services.Search;

/// <inheritdoc cref="ISearchService"/>
/// <remarks>
/// The sources are asked and their releases kept by the code the plain release search uses
/// (<see cref="IReleaseSources"/>, <see cref="IReleaseStore"/>; Vasyl's decision 3): one way to ask, one
/// way to store, one prune.
/// </remarks>
public sealed class SearchService(
    ApplicationDbContext db,
    IReleaseSources sources,
    IReleaseStore store,
    ILibraryMatcher library,
    IServiceScopeFactory scopes,
    ISearchMapper mapper,
    TimeProvider time) : ISearchService
{
    public async Task<SearchCreateResult> CreateAsync(
        string? text,
        string? year,
        string? imdbId,
        string? tmdbId,
        string? season,
        Guid callerUserId,
        CancellationToken cancellationToken = default)
    {
        if (ReleaseSearchTerms.Read(text, year, imdbId, tmdbId, season, out var terms) is { } error)
            return SearchCreateResult.Invalid(error);

        // Read before the two sides start: from then on the sources' read of their rows is this context's
        // one use until both are done.
        var forOwner = await db.IsInstallationOwnerAsync(callerUserId, cancellationToken);

        // The ticket's "the library and all enabled sources are queried concurrently": the page waits for
        // the slower of the two, not their sum. The library is read through a context of its own — a
        // context is not safe to share between concurrent calls, and the sources read theirs through this
        // one while the library is being read.
        var inLibrary = MatchInOwnScopeAsync(QueryOf(terms!), forOwner, cancellationToken);
        var outcomes = sources.SearchAsync(terms!.Text, terms.Year, terms.Season, terms.Identifiers, cancellationToken);
        await Task.WhenAll(inLibrary, outcomes);

        var search = new SearchEntity
        {
            UserId = callerUserId,
            Text = terms.Text,
            Year = terms.Year,
            ImdbId = terms.ImdbId,
            TmdbId = terms.TmdbId,
            Season = terms.Season,
        };
        await store.StoreAsync(await outcomes, search, cancellationToken);
        await store.PruneAsync(cancellationToken);

        return SearchCreateResult.Created(await ToResponseAsync(search, await inLibrary, forOwner, cancellationToken));
    }

    public async Task<SearchReadResult> GetAsync(Guid id, Guid callerUserId, CancellationToken cancellationToken = default)
    {
        if (await FindAsync(id, callerUserId, cancellationToken) is not { } search)
            return SearchReadResult.NotFound;

        var forOwner = await db.IsInstallationOwnerAsync(callerUserId, cancellationToken);
        // Matched again on every read, not stored: a sync since the search shows (and a work removed since
        // is no longer offered).
        var inLibrary = await library.MatchAsync(QueryOf(search), forOwner, cancellationToken);
        return SearchReadResult.Found(await ToResponseAsync(search, inLibrary, forOwner, cancellationToken));
    }

    public async Task<SearchReleaseListResult> ListReleasesAsync(
        Guid id,
        string? source,
        string? kind,
        string? language,
        string? quality,
        string? minSizeBytes,
        string? maxSizeBytes,
        string? sort,
        int page,
        int pageSize,
        Guid callerUserId,
        CancellationToken cancellationToken = default)
    {
        // Whose search first: someone else's answers as missing whatever the filters say.
        if (await FindAsync(id, callerUserId, cancellationToken) is not { } search)
            return SearchReleaseListResult.NotFound;
        if (PagingRules.Validate(page, pageSize) is { } pagingError)
            return SearchReleaseListResult.Invalid(pagingError);
        if (SearchReleaseListRules.Validate(source, kind, language, quality, minSizeBytes, maxSizeBytes, sort, out var filter) is { } filterError)
            return SearchReleaseListResult.Invalid(filterError);

        var rows = await (
                from release in db.Releases.AsNoTracking()
                join searchSource in db.SearchSources.AsNoTracking() on release.SearchSourceId equals (Guid?)searchSource.Id
                where searchSource.SearchId == search.Id
                select new { Release = release, SourcePosition = searchSource.Position, SourceName = searchSource.Name })
            .ToListAsync(cancellationToken);
        // What the releases are matched against: the works the search itself lists — MAM-12, those it lists
        // by their words included, each matched on the release's own evidence. Only their titles and ids are
        // read here, so nothing of the owner's is at stake.
        var libraryWorks = (await library.MatchAsync(QueryOf(search), forOwner: false, cancellationToken)).Works;
        var episodes = await EpisodesOfAsync(libraryWorks, cancellationToken);

        // In memory, on the server: what a release claims is read from its title on every read (MAM-10,
        // specification §3.1), so it can't be filtered or sorted in SQL; and a search holds at most 100
        // releases per source and family — movies, TV (ReleaseSearchPlanning.MaximumResults, MAM-32). The
        // browser still gets one page.
        var listed = Ordered(
                rows.Select(row => Read(row.Release, row.SourcePosition, row.SourceName, libraryWorks, episodes)).Where(release => Keeps(filter!, release)),
                filter!.Sort)
            .ToList();

        // Past the last page there is nothing to read; and a page far enough out would overflow the offset
        // as an int (the LibraryService guard).
        var offset = (long)(page - 1) * pageSize;
        IReadOnlyList<SearchReleaseDto> items = offset >= listed.Count
            ? []
            : [.. listed.Skip((int)offset).Take(pageSize).Select(mapper.ToDto)];
        return SearchReleaseListResult.Listed(new SearchReleaseListResponse(items, listed.Count, page, pageSize));
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The caller's search with <paramref name="id"/>, unless it is older than its releases' lifetime:
    /// gone then, whether or not a later search has pruned it yet, so it never answers with releases that
    /// may vanish under it.
    /// </summary>
    private Task<SearchEntity?> FindAsync(Guid id, Guid callerUserId, CancellationToken cancellationToken)
    {
        var cutoff = time.GetUtcNow().UtcDateTime - ReleaseStore.UnusedReleaseLifetime;
        return db.Searches
            .AsNoTracking()
            .FirstOrDefaultAsync(search => search.Id == id && search.UserId == callerUserId && search.CreatedUtc >= cutoff, cancellationToken);
    }

    /// <summary>
    /// The library's side on a scope, and so a DbContext, of its own — the one way to read the library
    /// while the sources' read goes through the request's context.
    /// </summary>
    private async Task<LibrarySearchResult> MatchInOwnScopeAsync(WorkClaim query, bool forOwner, CancellationToken cancellationToken)
    {
        // Started on its own, as each source is (ReleaseSources): SQLite's reads complete synchronously, so
        // without this the whole library read would run before the sources were even started.
        await Task.Yield();
        await using var scope = scopes.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ILibraryMatcher>().MatchAsync(query, forOwner, cancellationToken);
    }

    /// <summary>The search as the library is matched for it: its text as the one title, its year, its
    /// ids.</summary>
    private static WorkClaim QueryOf(ReleaseSearchTerms terms) => new([terms.Text], ClaimedYears.Exactly(terms.Year), terms.Identifiers);

    private static WorkClaim QueryOf(SearchEntity search) =>
        new([search.Text], ClaimedYears.Exactly(search.Year), ReleaseSearchTerms.IdentifiersOf(search.ImdbId, search.TmdbId));

    /// <summary>What the search page shows of a stored search: its sources as stored, with how many
    /// releases each listed, and what those releases claim, for the filters.</summary>
    private async Task<SearchResponse> ToResponseAsync(
        SearchEntity search, LibrarySearchResult inLibrary, bool forOwner, CancellationToken cancellationToken)
    {
        var searchSources = await db.SearchSources
            .AsNoTracking()
            .Where(source => source.SearchId == search.Id)
            .OrderBy(source => source.Position)
            .ToListAsync(cancellationToken);
        var releases = await db.Releases
            .AsNoTracking()
            .Where(release => db.SearchSources.Any(source => source.Id == release.SearchSourceId && source.SearchId == search.Id))
            .Select(release => new { release.SearchSourceId, release.Title, release.Kind })
            .ToListAsync(cancellationToken);

        var releaseCounts = releases
            .GroupBy(release => release.SearchSourceId!.Value)
            .ToDictionary(source => source.Key, source => source.Count());
        return mapper.ToResponse(
            search,
            inLibrary,
            searchSources,
            releaseCounts,
            [.. releases.Select(release => release.Title)],
            [.. releases.Select(release => release.Kind)],
            forOwner);
    }

    /// <summary>
    /// MAM-32 — where each episode of the series among <paramref name="libraryWorks"/> is placed, by
    /// series: what a TV release's held count is counted from. One query, whatever the series.
    /// </summary>
    private async Task<ILookup<Guid, (Guid EpisodeId, int? Season, int? Episode)>> EpisodesOfAsync(
        IReadOnlyList<LibraryWorkMatch> libraryWorks, CancellationToken cancellationToken)
    {
        var seriesIds = libraryWorks.Where(work => work.Work.Kind == MediaWorkKind.Series).Select(work => work.Work.Id).ToList();
        var places = await db.MediaEpisodePositions.AsNoTracking()
            .Where(position => position.SeriesWorkId != null && seriesIds.Contains(position.SeriesWorkId.Value))
            .Select(position => new { SeriesWorkId = position.SeriesWorkId!.Value, position.MediaWorkId, position.SeasonNumber, position.EpisodeNumber })
            .ToListAsync(cancellationToken);
        return places.ToLookup(place => place.SeriesWorkId, place => (place.MediaWorkId, place.SeasonNumber, place.EpisodeNumber));
    }

    /// <summary>A release with what its title and listing claim, and the search's works it matches.</summary>
    /// <remarks>
    /// <para>
    /// MAM-12 — by <see cref="WorkMatching.Match"/> alone, against every work the search lists: a work the
    /// search lists by its words is matched on the release's own evidence ("Rambo III (1988)" is that
    /// work's title and year), and never by words — a release's name may hold any word at all (ADR 0012).
    /// </para>
    /// <para>
    /// MAM-32 — against the works of its kind alone (ADR 0020): a TV release against series, by a show's
    /// ids (IMDb, TMDb TV, TVDB), then its title with a year its seasons allow
    /// (<see cref="ReleaseWorkClaims.YearsToMatch"/>: its first year exactly when it holds season 1, any
    /// year up to it when only later ones), then its title; a movie release against movies, as before. A film and a show of one title never match each other's releases, and
    /// an episode of no series is matched by none: a release of it would be its show's. A TV match counts
    /// the series' episodes among those the title claims (<paramref name="episodes"/>, by series), each
    /// once however many places it has.
    /// </para>
    /// </remarks>
    private static SearchRelease Read(
        ReleaseEntity release,
        int sourcePosition,
        string sourceName,
        IReadOnlyList<LibraryWorkMatch> libraryWorks,
        ILookup<Guid, (Guid EpisodeId, int? Season, int? Episode)> episodes)
    {
        var isTv = release.Kind == ReleaseKind.Tv;
        var claimedWork = isTv ? ReleaseWorkClaims.ReadTv(release.Title) : ReleaseWorkClaims.Read(release.Title);
        var identifiers = ReleaseSearchTerms.IdentifiersOf(release);
        var claim = new WorkClaim(claimedWork.Titles, ReleaseWorkClaims.YearsToMatch(claimedWork), identifiers);
        var kind = isTv ? MediaWorkKind.Series : MediaWorkKind.Movie;
        // Every work it matches, never just the best (ADR 0012): strongest first, and among equals in the
        // order the search lists the works — OrderBy is stable.
        List<ReleaseLibraryMatch> matches =
        [
            .. libraryWorks
                .Where(work => work.Work.Kind == kind)
                .Select(work => WorkMatching.Match(claim, work.Titles, work.Identifiers) is { } match
                    ? new ReleaseLibraryMatch(work.Work.Id, match, isTv ? HeldCount(episodes[work.Work.Id], claimedWork.Episodes) : null)
                    : null)
                .OfType<ReleaseLibraryMatch>()
                .OrderBy(match => match.Match.Evidence),
        ];
        return new SearchRelease(
            release, sourcePosition, sourceName, ReleaseTitleClaims.Read(release.Title), claimedWork, identifiers, matches);
    }

    /// <summary>MAM-32 — how many of a series' episodes, at <paramref name="places"/>, are among those
    /// <paramref name="claimed"/>; <c>null</c> when the title claims none, as nothing says which it
    /// holds.</summary>
    private static int? HeldCount(IEnumerable<(Guid EpisodeId, int? Season, int? Episode)> places, ClaimedEpisodes? claimed) =>
        claimed is null
            ? null
            : places.Where(place => claimed.Holds(place.Season, place.Episode)).Select(place => place.EpisodeId).Distinct().Count();

    /// <summary>Whether <paramref name="release"/> passes every filter; a release whose size is unknown
    /// fails either size bound, since it can't be shown to be inside it.</summary>
    private static bool Keeps(SearchReleaseFilter filter, SearchRelease release) =>
        (filter.SourceId is not { } sourceId || release.Release.SearchSourceId == sourceId)
        && (filter.Kind is not { } kind || release.Release.Kind == kind)
        && (filter.AudioLanguage is not { } language || release.Claimed.AudioLanguages.Contains(language, StringComparer.Ordinal))
        && (!filter.WithoutAudioLanguage || release.Claimed.AudioLanguages.Count == 0)
        && (filter.Resolution is not { } resolution || release.Claimed.Resolution == resolution)
        && (!filter.WithoutResolution || release.Claimed.Resolution is null)
        && (filter.MinimumSizeBytes is not { } minimum || release.Release.SizeBytes is { } atLeast && atLeast >= minimum)
        && (filter.MaximumSizeBytes is not { } maximum || release.Release.SizeBytes is { } atMost && atMost <= maximum);

    /// <summary>
    /// <paramref name="releases"/> in <paramref name="sort"/>'s order (<see cref="SearchReleaseSort"/>):
    /// a value the listing doesn't give always last, and ties in the search's own order — the source's
    /// place in the search, then the release's in its source's answer — so a page never reshuffles
    /// between two reads.
    /// </summary>
    private static IOrderedEnumerable<SearchRelease> Ordered(IEnumerable<SearchRelease> releases, SearchReleaseSort sort)
    {
        var ordered = sort switch
        {
            SearchReleaseSort.Largest => releases
                .OrderBy(release => release.Release.SizeBytes is null)
                .ThenByDescending(release => release.Release.SizeBytes),
            SearchReleaseSort.Smallest => releases
                .OrderBy(release => release.Release.SizeBytes is null)
                .ThenBy(release => release.Release.SizeBytes),
            SearchReleaseSort.Newest => releases
                .OrderBy(release => release.Release.PublishedUtc is null)
                .ThenByDescending(release => release.Release.PublishedUtc),
            SearchReleaseSort.Quality => releases
                .OrderBy(release => release.Claimed.Resolution is null)
                .ThenByDescending(release => release.Claimed.Resolution)
                .ThenBy(release => release.Release.Seeders is null)
                .ThenByDescending(release => release.Release.Seeders),
            // Seeders, the default.
            _ => releases
                .OrderBy(release => release.Release.Seeders is null)
                .ThenByDescending(release => release.Release.Seeders),
        };
        return ordered
            .ThenBy(release => release.SourcePosition)
            .ThenBy(release => release.Release.Position is null)
            .ThenBy(release => release.Release.Position)
            // Last, so the order is total even for two rows of one place (never written, but never assumed).
            .ThenBy(release => release.Release.Id);
    }
}
