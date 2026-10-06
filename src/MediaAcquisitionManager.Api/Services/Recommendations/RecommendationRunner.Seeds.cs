using MediaAcquisitionManager.Api.Services.Catalog;
using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Library;
using MediaAcquisitionManager.Core.Matching;
using MediaAcquisitionManager.Core.Media;
using MediaAcquisitionManager.Core.Recommendations;

namespace MediaAcquisitionManager.Api.Services.Recommendations;

// MAM-33 — the seeds: the catalog works the library holds, each with its details, which every signal starts from.
public sealed partial class RecommendationRunner
{
    /// <summary>
    /// Finds each library work's catalog work — of a kind some tab wants — and reads its details. A work the
    /// catalog doesn't have (its details answer NotFound) is skipped, and a lookup that named it is cached as
    /// nothing; any other failure ends the run.
    /// </summary>
    /// <remarks>
    /// MAM-34 — a show an episode-to-show link names is a seed too, when the series tab is wanted (R15): the linked works
    /// are its episodes, the seasons the links name are held, and a link naming none leaves its seasons unknown.
    /// </remarks>
    private async Task<Failure?> ResolveSeedsAsync(Computation run, CancellationToken cancellationToken)
    {
        var found = new Dictionary<(MediaWorkKind Kind, string CatalogId), (List<LibraryWork> Works, List<CatalogLookupQuery> Lookups, List<int?> LinkedSeasons)>();
        // In a fixed order — by kind, then as the Library page orders titles — so a run's calls and its reasons'
        // titles come out the same every time.
        var works = run.Library.Works
            .Where(work => run.Wants(work.Kind))
            .OrderBy(work => work.Kind)
            .ThenBy(work => work.DisplayTitle, LibraryTitleOrder.Comparer)
            .ThenBy(work => work.Id)
            .ToList();
        // Counted in the works the catalog is asked about: a work with a catalog id of its own costs no call.
        await BeginStageAsync(run, RecommendationRunStage.FindingLibraryWorks, works.Count(LooksUp), cancellationToken);
        foreach (var work in works)
        {
            var looksUp = LooksUp(work);
            var (key, lookup, failure) = await CatalogWorkOfAsync(run, work, cancellationToken);
            if (looksUp)
                await AdvanceStageAsync(run, cancellationToken);
            if (failure is not null)
                return failure;
            if (key is not { } catalogKey)
                continue;

            if (!found.TryGetValue(catalogKey, out var seed))
                found[catalogKey] = seed = ([], [], []);
            seed.Works.Add(work);
            if (lookup is not null)
                seed.Lookups.Add(lookup);
        }

        if (run.Wants(MediaWorkKind.Series))
        {
            // In the works' order, as above; a link's show id is the app's own, checked as the library's would be.
            foreach (var link in run.Library.ShowLinks
                         .Where(link => MediaIdentifierRules.IsTmdbId(link.ShowId))
                         .OrderBy(link => link.Work.Kind)
                         .ThenBy(link => link.Work.DisplayTitle, LibraryTitleOrder.Comparer)
                         .ThenBy(link => link.Work.Id)
                         .ThenBy(link => link.SeasonNumber))
            {
                (MediaWorkKind Kind, string CatalogId) key = (MediaWorkKind.Series, link.ShowId);
                if (!found.TryGetValue(key, out var seed))
                    found[key] = seed = ([], [], []);
                if (!seed.Works.Contains(link.Work))
                    seed.Works.Add(link.Work);
                seed.LinkedSeasons.Add(link.SeasonNumber);
            }
        }

        await BeginStageAsync(run, RecommendationRunStage.ReadingWorks, found.Count, cancellationToken);
        foreach (var (key, (seedWorks, lookups, linkedSeasons)) in found)
        {
            var details = await store.GetWorkAsync(run.Catalog, key.Kind, key.CatalogId, cancellationToken);
            await AdvanceStageAsync(run, cancellationToken);
            if (details.Status == IntegrationCallStatus.NotFound)
            {
                // A work the catalog no longer has (merged, removed): nothing to recommend from, and a lookup that led
                // here is wrong, so the next run doesn't follow it again.
                foreach (var lookup in lookups)
                    await store.ForgetLookupAsync(run.Catalog, lookup, cancellationToken);
                continue;
            }

            if (!details.IsSuccess)
                return Failure.Of(details);

            var seed = new Seed(details.Value!);
            seed.Works.AddRange(seedWorks);
            seed.LinkedSeasons.AddRange(linkedSeasons);
            run.Seeds[key] = seed;
            // A possible match counts as held from here on, as a work's own id does (Vasyl's ticket): it is the
            // library's best guess at what it has, used for recommendations only.
            run.Library.CatalogIds.Add(key);
        }

        return null;
    }

    /// <summary>
    /// The catalog work <paramref name="work"/> is: its own catalog id; else — a possible match, best effort — the
    /// one the catalog files under its IMDb id; else the one work its title and year match, each title cleaned of
    /// what a file name adds (<see cref="SeedTitleCleaning"/>). <c>null</c> when none; the lookup that found it,
    /// to forget should the work turn out not to exist.
    /// </summary>
    /// <remarks>
    /// MAM-34 — a work holding two catalog ids of its own (a link beside a different library id) is none of them:
    /// neither wins, both are held, and it seeds nothing; so is a work whose episode-to-show link the library contradicts
    /// with a film id it gave the item later — the owner said it is an episode, the library says a film. A linked work is
    /// never guessed at (R15), whatever its link names — an episode-to-show link seeds its show instead — and whether or
    /// not it disagrees with the library.
    /// </remarks>
    private async Task<((MediaWorkKind Kind, string CatalogId)? Key, CatalogLookupQuery? Lookup, Failure? Failure)> CatalogWorkOfAsync(
        Computation run, LibraryWork work, CancellationToken cancellationToken)
    {
        if (work.HasConflict)
            return (null, null, null);
        var own = OwnCatalogIds(work);
        if (own.Count == 1)
            return ((work.Kind, own[0]), null, null);
        if (own.Count > 1 || work.IsLinked)
            return (null, null, null);

        var lookups = new List<CatalogLookupQuery>();
        lookups.AddRange(work.Identifiers
            .Where(identifier => identifier.Scheme == MediaIdentifierScheme.Imdb)
            .Select(identifier => identifier.Value.Trim())
            .Where(MediaIdentifierRules.IsImdbId)
            .Order(StringComparer.OrdinalIgnoreCase)
            .Select(imdbId => CatalogLookupQuery.ByImdbId(work.Kind, imdbId)));
        // A displayed title before an original one; a title with no year can't match by title and year, so it isn't
        // asked about.
        foreach (var title in work.Titles.OrderBy(title => title.Kind).ThenBy(title => title.Text, StringComparer.Ordinal))
        {
            var cleaned = SeedTitleCleaning.Clean(title.Text);
            if (TitleMatchKey.For(cleaned).Length > 0 && (title.Year ?? SeedTitleCleaning.BracketedYear(title.Text)) is { } year)
                lookups.Add(CatalogLookupQuery.ByTitle(work.Kind, cleaned, year));
        }

        foreach (var lookup in lookups.DistinctBy(lookup => lookup.Key, StringComparer.Ordinal))
        {
            var answer = await store.LookupAsync(run.Catalog, lookup, cancellationToken);
            if (!answer.IsSuccess)
                return (null, null, Failure.Of(answer));
            if (answer.Value! is { IsFound: true, Kind: { } kind, CatalogId: { } catalogId })
                return ((kind, catalogId), lookup, null);
        }

        return (null, null, null);
    }

    /// <summary>
    /// The catalog ids <paramref name="work"/> carries of its own, each once, in ordinal order — MAM-34: its link's
    /// included; none for a work without. A malformed id is the library's data, not the catalog's to be asked about: the
    /// adapter would refuse it.
    /// </summary>
    private static IReadOnlyList<string> OwnCatalogIds(LibraryWork work) =>
        [
            .. work.Identifiers
                .Where(identifier => identifier.Scheme == CatalogScheme(work.Kind))
                .Select(identifier => identifier.Value.Trim())
                .Where(MediaIdentifierRules.IsTmdbId)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal),
        ];

    /// <summary>Whether <paramref name="work"/> costs a catalog lookup: it carries no catalog id of its own and —
    /// MAM-34 — no item of it is linked.</summary>
    private static bool LooksUp(LibraryWork work) => OwnCatalogIds(work).Count == 0 && !work.IsLinked;
}
