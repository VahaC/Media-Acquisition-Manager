using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Api.Mapping;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Library;
using MediaAcquisitionManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Services.LibraryLinks;

/// <inheritdoc cref="ILibraryLinkSuggestionReader"/>
/// <remarks>
/// <para>
/// Split by facet: this file reads the stored candidates and ranks them; <c>LibraryLinkSuggestionReader.Subjects.cs</c>
/// builds what a work is to them (its cleaned titles, year, measured runtime, library and linked neighbours).
/// </para>
/// <para>
/// Batched, no N+1: the confident list reads every work without an id at once, so each step is one query for every work
/// asked about — their candidates, their files, the catalog rows — however many there are.
/// </para>
/// </remarks>
public sealed partial class LibraryLinkSuggestionReader(
    ApplicationDbContext db,
    ILibraryLinkSuggestionMapper mapper) : ILibraryLinkSuggestionReader
{
    /// <summary>
    /// How many suggestions a dialog shows: five fit the review's keys 1–5, and a work whose film isn't among the five
    /// best of TMDb's answers is one the owner searches for anyway. The confident one is always among them (R21).
    /// </summary>
    public const int SuggestionsShown = 5;

    public async Task<LibraryLinkSuggestions> ReadAsync(Guid workId, CancellationToken cancellationToken = default)
    {
        var search = await db.LibraryLinkSearches.AsNoTracking().SingleOrDefaultAsync(row => row.MediaWorkId == workId, cancellationToken);
        var pending = await db.LibraryLinkSuggestionRuns.AnyAsync(
            run => run.Status == LibraryLinkSuggestionRunStatus.Queued || run.Status == LibraryLinkSuggestionRunStatus.Running, cancellationToken);
        var inPopulation = await LibraryLinkPopulation.Of(db).AnyAsync(work => work.Id == workId, cancellationToken);
        var state = new LibraryLinkSearchStateDto(
            // "None fits" said before any search leaves a row with no query (LibraryLinkSuggestionService): never searched.
            search is { QueryKey.Length: > 0 } ? search.SearchedUtc : null,
            search?.NoneFitsUtc,
            pending,
            inPopulation);
        if (search is null)
            return new LibraryLinkSuggestions([], state);

        var ranked = (await RankStoredAsync([workId], cancellationToken)).GetValueOrDefault(workId) ?? [];
        return new LibraryLinkSuggestions([.. Shown(ranked)], state);
    }

    public async Task<IReadOnlyDictionary<Guid, LinkCandidateDto>> ReadConfidentAsync(
        IReadOnlyCollection<Guid> workIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workIds);
        return (await RankStoredAsync(workIds, cancellationToken))
            .Where(pair => pair.Value.Any(candidate => candidate.IsConfident))
            .ToDictionary(pair => pair.Key, pair => pair.Value.First(candidate => candidate.IsConfident));
    }

    public async Task<IReadOnlyList<LinkCandidateDto>> RankAsync(
        Guid workId,
        IReadOnlyList<LinkCandidateKey> candidates,
        int? seasonNumber = null,
        int? episodeNumber = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        var numbers = seasonNumber is null && episodeNumber is null ? null : new EpisodeNumbers(seasonNumber, episodeNumber);
        var ranked = await RankManyAsync(
            new Dictionary<Guid, IReadOnlyList<LinkCandidateKey>> { [workId] = [.. candidates.Distinct()] }, numbers, cancellationToken);
        return ranked.GetValueOrDefault(workId) ?? [];
    }

    // ── ranking ──────────────────────────────────────────────────────────────────────────────────

    /// <summary>The dialog's few: the best <see cref="SuggestionsShown"/>, the confident one in place of the last when it
    /// ranks below them — it is the one "Accept all confident" would link, so the owner sees it before that.</summary>
    private static IEnumerable<LinkCandidateDto> Shown(IReadOnlyList<LinkCandidateDto> ranked)
    {
        var shown = ranked.Take(SuggestionsShown).ToList();
        if (ranked.Skip(SuggestionsShown).FirstOrDefault(candidate => candidate.IsConfident) is { } confident)
            shown[^1] = confident;
        return shown;
    }

    /// <summary>Each work's stored candidates, in the catalog's order, ranked; a work never searched is left out.</summary>
    private async Task<Dictionary<Guid, IReadOnlyList<LinkCandidateDto>>> RankStoredAsync(
        IReadOnlyCollection<Guid> workIds, CancellationToken cancellationToken)
    {
        if (workIds.Count == 0)
            return [];

        // One JSON parameter (EF.Parameter): the confident list asks for every work without an id at once.
        List<Guid> ids = [.. workIds.Distinct()];
        var candidates = await (
                from search in db.LibraryLinkSearches.AsNoTracking()
                join candidate in db.LibraryLinkCandidates.AsNoTracking() on search.Id equals candidate.LibraryLinkSearchId
                where EF.Parameter(ids).Contains(search.MediaWorkId)
                select new { search.MediaWorkId, candidate.Position, candidate.CatalogKind, candidate.CatalogId })
            .ToListAsync(cancellationToken);
        var byWork = candidates
            .GroupBy(candidate => candidate.MediaWorkId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<LinkCandidateKey>)
                [
                    .. group
                        .OrderBy(candidate => candidate.Position)
                        .Select(candidate => new LinkCandidateKey(candidate.CatalogKind, candidate.CatalogId)),
                ]);
        return await RankManyAsync(byWork, numbers: null, cancellationToken);
    }

    /// <summary>
    /// The candidates of each work ranked for it (<see cref="LinkSuggestionRanking"/>): their facts from the catalog
    /// cache, the work's from the library, the position each one's place in the list given.
    /// </summary>
    private async Task<Dictionary<Guid, IReadOnlyList<LinkCandidateDto>>> RankManyAsync(
        IReadOnlyDictionary<Guid, IReadOnlyList<LinkCandidateKey>> candidatesByWork, EpisodeNumbers? numbers, CancellationToken cancellationToken)
    {
        var subjects = await SubjectsAsync([.. candidatesByWork.Keys], cancellationToken);
        var rows = await LibraryLinkCatalogRows.LoadAsync(
            db, [.. candidatesByWork.Values.SelectMany(keys => keys).Select(key => (key.Kind, key.CatalogId)).Distinct()], cancellationToken);

        var answer = new Dictionary<Guid, IReadOnlyList<LinkCandidateDto>>();
        foreach (var (workId, keys) in candidatesByWork)
        {
            if (!subjects.TryGetValue(workId, out var subject))
                continue;

            var facts = keys
                .Select((key, position) => rows.TryGetValue((key.Kind, key.CatalogId), out var row) ? FactsOf(row, position) : null)
                .OfType<LinkCandidateFacts>()
                .ToList();
            answer[workId] =
            [
                .. LinkSuggestionRanking.Rank(subject.Subject, facts).Select(ranked =>
                {
                    var row = rows[(ranked.Candidate.Kind, ranked.Candidate.Id)];
                    var (season, episode) = NumbersFor(subject, row, numbers);
                    return mapper.ToCandidateDto(ranked, row.Work, season, episode);
                }),
            ];
        }

        return answer;
    }

    private static LinkCandidateFacts FactsOf(LibraryLinkCatalogRow row, int position) =>
        new(
            row.Work.Kind,
            row.Work.CatalogId,
            row.Work.Title,
            row.Work.OriginalTitle,
            row.AlternativeTitles,
            row.Work.ReleaseDate?.Year,
            row.Work.RuntimeMinutes,
            row.GenreRoles,
            row.Work.CollectionId,
            row.DirectorIds,
            position);

    /// <summary>
    /// The season and episode to prefill for a show suggested to a film or to an episode with no series (an
    /// episode-to-show link): the numbers a pasted link named; else an episode marker of the work's names ("S01E08",
    /// "_E08"), its season 1 when it names none and the show has a single regular season. A hint only (ADR 0017): the
    /// owner sees and changes them before saving.
    /// </summary>
    private static (int? Season, int? Episode) NumbersFor(WorkSubject subject, LibraryLinkCatalogRow row, EpisodeNumbers? numbers)
    {
        if (row.Work.Kind != MediaWorkKind.Series || subject.Subject.Kind is not (MediaWorkKind.Movie or MediaWorkKind.Episode))
            return (null, null);
        if (numbers is not null)
            return (numbers.Season, numbers.Episode);

        int? singleSeason = row.RegularSeasons == 1 ? 1 : null;
        return subject.Marker is { } marker ? (marker.Season ?? singleSeason, marker.Episode) : (singleSeason, null);
    }

    /// <summary>The numbers a pasted TMDb episode link named.</summary>
    private sealed record EpisodeNumbers(int? Season, int? Episode);
}
