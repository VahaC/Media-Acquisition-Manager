using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Abstractions.Media;
using MediaAcquisitionManager.Core.Abstractions.Metadata;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Matching;
using MediaAcquisitionManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Services.Catalog;

/// <inheritdoc cref="ICatalogStore"/>
/// <remarks>
/// Split by facet: this file answers from the rows or asks the catalog (read-through); <c>CatalogStore.Writes.cs</c>
/// stores what the catalog said. The cache is keyed by the catalog's own ids, whichever metadata integration
/// reported them: one catalog type exists, and its ids don't change with the key it is reached by. MAM-46 — a row
/// that holds text in the catalog's language (a work's details, a genre list, a collection, a person) says which
/// language it was read in, and is fresh only in that one (<see cref="CatalogConnection.Language"/>); so is a lookup by
/// a title, as the catalog matches a title in the language it is asked in, while a lookup by an IMDb id holds ids alone
/// and is fresh in any. A connection built before the owner changed the language stores nothing over a row
/// (<see cref="SaveInLanguageAsync"/>).
/// </remarks>
public sealed partial class CatalogStore(ApplicationDbContext db, TimeProvider time, ILogger<CatalogStore> logger) : ICatalogStore
{
    /// <summary>
    /// MAM-33 — how long an answer of the catalog is served from the rows before it is read again. A catalog's
    /// facts move slowly — a rating by a fraction, a show's new season a few times a year — while a run reads
    /// the details of every work in the library: 30 days keeps the runs of an unchanged library to a handful
    /// of calls, and still shows a new season within a month. A constant, not a setting (the house rule for a
    /// tuned number).
    /// </summary>
    public static readonly TimeSpan RefreshAge = TimeSpan.FromDays(30);

    /// <summary>
    /// MAM-34 (R22) — which reading of a work's details the rows hold today (<see cref="CatalogWorkEntity.DetailsVersion"/>):
    /// 1 since the details keep a work's runtime, overview, alternative titles and a show's IMDb id. Details stored by an
    /// older reading are not fresh, however young: a row cached before MAM-34 would otherwise be served for up to
    /// <see cref="RefreshAge"/> without what the owner's suggestions are ranked by. Raised by one whenever the details
    /// keep something more, so no migration has to clear the cache by hand.
    /// </summary>
    public const int CurrentDetailsVersion = 1;

    public async Task<IntegrationCallResult<IReadOnlyList<CatalogGenre>>> GetGenresAsync(
        CatalogConnection catalog, MediaWorkKind kind, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        var rows = await db.CatalogGenres.AsNoTracking().Where(genre => genre.Kind == kind).ToListAsync(cancellationToken);
        // A list is read whole, so it is as old as its oldest row; MAM-46 — and in the catalog's language only when every
        // row was read in it.
        if (rows.Count > 0 && rows.Min(genre => genre.ObservedUtc) >= Cutoff() && rows.All(genre => genre.Language == catalog.Language))
            return Succeeded<IReadOnlyList<CatalogGenre>>([.. rows.OrderBy(genre => genre.GenreId).Select(ToGenre)]);

        var answer = await catalog.Catalog.GetGenresAsync(catalog.Settings, kind, cancellationToken);
        if (!answer.IsSuccess)
            return answer;

        await SaveInLanguageAsync(catalog, current => StageGenresAsync(catalog, kind, answer.Value!, current, cancellationToken), cancellationToken);
        return Succeeded<IReadOnlyList<CatalogGenre>>([.. answer.Value!.OrderBy(genre => genre.Id)]);
    }

    public async Task<IntegrationCallResult<CatalogWork>> GetWorkAsync(
        CatalogConnection catalog, MediaWorkKind kind, string catalogId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(catalogId);
        var row = await db.CatalogWorks.AsNoTracking()
            .SingleOrDefaultAsync(work => work.Kind == kind && work.CatalogId == catalogId, cancellationToken);
        // MAM-46 — details read in another language than the catalog's of now are read again in it.
        if (row?.DetailsUtc is { } detailsUtc
            && detailsUtc >= Cutoff()
            && row.DetailsVersion >= CurrentDetailsVersion
            && row.DetailsLanguage == catalog.Language)
            return Succeeded(await ReadDetailsAsync(row, cancellationToken));

        var answer = await catalog.Catalog.GetWorkAsync(catalog.Settings, kind, catalogId, cancellationToken);
        if (!answer.IsSuccess)
            return answer;

        await SaveInLanguageAsync(catalog, current => StageDetailsAsync(catalog, answer.Value!, current, cancellationToken), cancellationToken);
        return answer;
    }

    public async Task<IntegrationCallResult<CatalogCollection>> GetCollectionAsync(
        CatalogConnection catalog, CatalogReference collection, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(collection);
        var row = await db.CatalogCollections.AsNoTracking()
            .SingleOrDefaultAsync(stored => stored.CatalogId == collection.Id, cancellationToken);
        if (row is not null && row.ObservedUtc >= Cutoff() && row.Language == catalog.Language)
        {
            var parts = await db.CatalogCollectionParts.AsNoTracking()
                .Where(part => part.CatalogCollectionId == row.Id)
                .OrderBy(part => part.Position)
                .Select(part => part.MovieCatalogId)
                .ToListAsync(cancellationToken);
            var summaries = await ReadSummariesAsync([.. parts.Select(id => (MediaWorkKind.Movie, id))], cancellationToken);
            return Succeeded(new CatalogCollection(row.CatalogId, row.Name, summaries));
        }

        var answer = await catalog.Catalog.GetCollectionAsync(catalog.Settings, collection.Id, cancellationToken);
        if (!answer.IsSuccess)
            return answer;

        var named = answer.Value! with { Name = NameOr(answer.Value!.Name, collection.Name) };
        await SaveInLanguageAsync(catalog, current => StageCollectionAsync(catalog, named, current, cancellationToken), cancellationToken);
        return Succeeded(named);
    }

    public async Task<IntegrationCallResult<CatalogPerson>> GetPersonAsync(
        CatalogConnection catalog, CatalogReference person, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(person);
        var row = await db.CatalogPeople.AsNoTracking()
            .SingleOrDefaultAsync(stored => stored.CatalogId == person.Id, cancellationToken);
        if (row is not null && row.ObservedUtc >= Cutoff() && row.Language == catalog.Language)
        {
            var works = await db.CatalogPersonWorks.AsNoTracking()
                .Where(work => work.CatalogPersonId == row.Id)
                .OrderBy(work => work.Position)
                .Select(work => new { work.Kind, work.CatalogId })
                .ToListAsync(cancellationToken);
            var summaries = await ReadSummariesAsync([.. works.Select(work => (work.Kind, work.CatalogId))], cancellationToken);
            return Succeeded(new CatalogPerson(row.CatalogId, row.Name, summaries));
        }

        var answer = await catalog.Catalog.GetPersonAsync(catalog.Settings, person.Id, cancellationToken);
        if (!answer.IsSuccess)
            return answer;

        // The catalog's list of a person's works carries no name (TMDb's combined credits), so the name the
        // work's details gave stands in for it.
        var named = answer.Value! with { Name = NameOr(answer.Value!.Name, person.Name) };
        await SaveInLanguageAsync(catalog, current => StagePersonAsync(catalog, named, current, cancellationToken), cancellationToken);
        return Succeeded(named);
    }

    public async Task<IntegrationCallResult<CatalogLookupResult>> LookupAsync(
        CatalogConnection catalog, CatalogLookupQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(query);
        var key = query.Key;
        var row = await db.CatalogLookups.AsNoTracking().SingleOrDefaultAsync(lookup => lookup.QueryKey == key, cancellationToken);
        // MAM-46 — a lookup by a title answers what the catalog matched in the language it was asked in (a work is kept by
        // its title in that language or its original one), found or not, so it is fresh only in that language; one by an
        // IMDb id is the same in any.
        if (row is not null && row.ObservedUtc >= Cutoff() && (query.ImdbId is not null || row.Language == catalog.Language))
            return Succeeded(new CatalogLookupResult(row.ResultKind, row.ResultCatalogId, row.Evidence));

        var metadataQuery = query.ImdbId is { } imdbId
            ? new MetadataQuery(query.Kind, [new MediaIdentifier(MediaIdentifierScheme.Imdb, imdbId)], null, null)
            : new MetadataQuery(query.Kind, [], query.Title, query.Year);
        var answer = await catalog.Resolver.LookupAsync(catalog.Settings, metadataQuery, cancellationToken);
        if (!answer.IsSuccess)
            return IntegrationCallResult<CatalogLookupResult>.Failed(answer.Status, answer.Error!);

        // MAM-33 — a possible match is used only when it is the one: an id the catalog files two works of the
        // kind under, or a title two works of that year share, names neither for sure (specification §3.1: a
        // title and year never decide between candidates). Up to a year, the works of the title the search found —
        // it searched without a year — that began by then, as ReleaseWorkClaims.YearsToMatch allows a later-season
        // pack: two of them name neither, and one begun later is no candidate at all.
        var wanted = query.ImdbId is null ? MatchEvidence.TitleAndYear : MatchEvidence.Identifier;
        var matches = answer.Value!
            .Where(candidate => query.LatestYear is { } latest
                ? candidate.Titles.Any(title => new ClaimedYears(null, latest).Holds(title.Year))
                : candidate.Evidence == wanted)
            .Select(candidate => candidate.SourceItemId)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var result = matches.Count == 1 ? new CatalogLookupResult(query.Kind, matches[0], wanted) : CatalogLookupResult.Nothing;
        await SaveAsync(() => StageLookupAsync(catalog, key, result, cancellationToken), cancellationToken);
        return Succeeded(result);
    }

    public async Task ForgetLookupAsync(CatalogConnection catalog, CatalogLookupQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(query);
        await SaveAsync(() => StageLookupAsync(catalog, query.Key, CatalogLookupResult.Nothing, cancellationToken), cancellationToken);
    }

    public async Task<IntegrationCallResult<IReadOnlyList<CatalogWorkSummary>>> DiscoverAsync(
        CatalogConnection catalog,
        MediaWorkKind kind,
        IReadOnlyList<int> genreIds,
        IReadOnlyList<int> excludedGenreIds,
        int page,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        var answer = await catalog.Catalog.DiscoverAsync(catalog.Settings, kind, genreIds, excludedGenreIds, page, cancellationToken);
        if (answer.IsSuccess)
            await SaveSummariesAsync(catalog, answer.Value!, cancellationToken);
        return answer;
    }

    public async Task<IntegrationCallResult<IReadOnlyList<CatalogWorkSummary>>> SearchAsync(
        CatalogConnection catalog, MediaWorkKind kind, string title, int? year, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        var answer = await catalog.Catalog.SearchAsync(catalog.Settings, kind, title, year, cancellationToken);
        if (answer.IsSuccess)
            await SaveSummariesAsync(catalog, answer.Value!, cancellationToken);
        return answer;
    }

    public async Task<IntegrationCallResult<IReadOnlyList<CatalogWorkSummary>>> FindByImdbIdAsync(
        CatalogConnection catalog, string imdbId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        var answer = await catalog.Catalog.FindByImdbIdAsync(catalog.Settings, imdbId, cancellationToken);
        if (answer.IsSuccess)
            await SaveSummariesAsync(catalog, answer.Value!, cancellationToken);
        return answer;
    }

    public Task SaveSummariesAsync(CatalogConnection catalog, IReadOnlyList<CatalogWorkSummary> works, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(works);
        return works.Count == 0
            ? Task.CompletedTask
            : SaveInLanguageAsync(catalog, async current => await StageSummariesAsync(catalog, works, current, cancellationToken), cancellationToken);
    }

    // ── reading the rows back ───────────────────────────────────────────────────────────────────

    /// <summary>A work's stored details in the contract's shape: the recommended works' summaries from their own
    /// rows, one that has none left out; MAM-34 — its runtime, overview and alternative titles in the details' order.</summary>
    private async Task<CatalogWork> ReadDetailsAsync(CatalogWorkEntity row, CancellationToken cancellationToken)
    {
        var genres = await db.CatalogWorkGenres.AsNoTracking()
            .Where(genre => genre.CatalogWorkId == row.Id)
            .Select(genre => genre.GenreId)
            .ToListAsync(cancellationToken);
        var people = await db.CatalogWorkPeople.AsNoTracking()
            .Where(person => person.CatalogWorkId == row.Id)
            .OrderBy(person => person.Position)
            .ToListAsync(cancellationToken);
        var seasons = await db.CatalogWorkSeasons.AsNoTracking()
            .Where(season => season.CatalogWorkId == row.Id)
            .OrderBy(season => season.Number)
            .ToListAsync(cancellationToken);
        var recommended = await db.CatalogWorkRecommendations.AsNoTracking()
            .Where(recommendation => recommendation.CatalogWorkId == row.Id)
            .OrderBy(recommendation => recommendation.Position)
            .Select(recommendation => new { recommendation.Kind, recommendation.CatalogId })
            .ToListAsync(cancellationToken);
        var alternativeTitles = await db.CatalogWorkAlternativeTitles.AsNoTracking()
            .Where(title => title.CatalogWorkId == row.Id)
            .OrderBy(title => title.Position)
            .Select(title => title.Title)
            .ToListAsync(cancellationToken);

        return new CatalogWork(
            ToSummary(row, genres),
            row.ImdbId,
            row.CollectionId is { } collectionId ? new CatalogReference(collectionId, row.CollectionName ?? "") : null,
            [.. people.Select(person => new CatalogReference(person.PersonCatalogId, person.Name))],
            [.. seasons.Select(season => new CatalogSeason(season.Number, season.AirDate, season.EpisodeCount))],
            await ReadSummariesAsync([.. recommended.Select(work => (work.Kind, work.CatalogId))], cancellationToken),
            row.RuntimeMinutes,
            row.Overview,
            alternativeTitles);
    }

    /// <summary>
    /// The stored summaries of <paramref name="keys"/>, in their order; a key with no row is left out. Two queries
    /// a kind, however many: the rows, then their genres.
    /// </summary>
    private async Task<IReadOnlyList<CatalogWorkSummary>> ReadSummariesAsync(
        IReadOnlyList<(MediaWorkKind Kind, string CatalogId)> keys, CancellationToken cancellationToken)
    {
        var found = new Dictionary<(MediaWorkKind, string), CatalogWorkSummary>();
        foreach (var kind in keys.Select(key => key.Kind).Distinct())
        {
            var ids = keys.Where(key => key.Kind == kind).Select(key => key.CatalogId).Distinct().ToList();
            // MAM-33 — each list of ids as one JSON parameter (EF.Parameter, the LibrarySnapshotStore rule), not one
            // parameter per id: a person's works or a collection's parts run to hundreds.
            var rows = await db.CatalogWorks.AsNoTracking()
                .Where(work => work.Kind == kind && EF.Parameter(ids).Contains(work.CatalogId))
                .ToListAsync(cancellationToken);
            var rowIds = rows.Select(work => work.Id).ToList();
            var genres = (await db.CatalogWorkGenres.AsNoTracking()
                    .Where(genre => EF.Parameter(rowIds).Contains(genre.CatalogWorkId))
                    .ToListAsync(cancellationToken))
                .ToLookup(genre => genre.CatalogWorkId, genre => genre.GenreId);
            foreach (var row in rows)
                found[(kind, row.CatalogId)] = ToSummary(row, [.. genres[row.Id]]);
        }

        return [.. keys.Select(key => found.GetValueOrDefault(key)).OfType<CatalogWorkSummary>()];
    }

    /// <summary>A row as the summary a list gives, its genre ids in id order.</summary>
    internal static CatalogWorkSummary ToSummary(CatalogWorkEntity row, IEnumerable<int> genreIds) =>
        new(
            row.Kind,
            row.CatalogId,
            row.Title,
            row.OriginalTitle,
            row.OriginalLanguage,
            row.ReleaseDate,
            [.. genreIds.Distinct().Order()],
            row.Rating,
            row.VoteCount,
            row.PosterPath);

    private static CatalogGenre ToGenre(CatalogGenreEntity row) => new(row.GenreId, row.Name, row.Role);

    private static string NameOr(string name, string fallback) => string.IsNullOrWhiteSpace(name) ? fallback : name;

    private DateTime Cutoff() => time.GetUtcNow().UtcDateTime - RefreshAge;

    private static IntegrationCallResult<T> Succeeded<T>(T value) where T : class => IntegrationCallResult<T>.Succeeded(value);
}
