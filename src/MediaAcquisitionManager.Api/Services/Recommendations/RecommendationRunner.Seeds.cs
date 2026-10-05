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
    private async Task<Failure?> ResolveSeedsAsync(Computation run, CancellationToken cancellationToken)
    {
        var found = new Dictionary<(MediaWorkKind Kind, string CatalogId), (List<LibraryWork> Works, List<CatalogLookupQuery> Lookups)>();
        // In a fixed order — by kind, then as the Library page orders titles — so a run's calls and its reasons'
        // titles come out the same every time.
        var works = run.Library.Works
            .Where(work => run.Wants(work.Kind))
            .OrderBy(work => work.Kind)
            .ThenBy(work => work.DisplayTitle, LibraryTitleOrder.Comparer)
            .ThenBy(work => work.Id)
            .ToList();
        // Counted in the works the catalog is asked about: a work with a catalog id of its own costs no call.
        await BeginStageAsync(run, RecommendationRunStage.FindingLibraryWorks, works.Count(work => OwnCatalogId(work) is null), cancellationToken);
        foreach (var work in works)
        {
            var looksUp = OwnCatalogId(work) is null;
            var (key, lookup, failure) = await CatalogWorkOfAsync(run, work, cancellationToken);
            if (looksUp)
                await AdvanceStageAsync(run, cancellationToken);
            if (failure is not null)
                return failure;
            if (key is not { } catalogKey)
                continue;

            if (!found.TryGetValue(catalogKey, out var seed))
                found[catalogKey] = seed = ([], []);
            seed.Works.Add(work);
            if (lookup is not null)
                seed.Lookups.Add(lookup);
        }

        await BeginStageAsync(run, RecommendationRunStage.ReadingWorks, found.Count, cancellationToken);
        foreach (var (key, (seedWorks, lookups)) in found)
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
    private async Task<((MediaWorkKind Kind, string CatalogId)? Key, CatalogLookupQuery? Lookup, Failure? Failure)> CatalogWorkOfAsync(
        Computation run, LibraryWork work, CancellationToken cancellationToken)
    {
        if (OwnCatalogId(work) is { } own)
            return ((work.Kind, own), null, null);

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
    /// The catalog id <paramref name="work"/> carries of its own, the least by ordinal order when a library gave it
    /// several; <c>null</c> for none. A malformed id is the library's data, not the catalog's to be asked about: the
    /// adapter would refuse it.
    /// </summary>
    private static string? OwnCatalogId(LibraryWork work) =>
        work.Identifiers
            .Where(identifier => identifier.Scheme == CatalogScheme(work.Kind))
            .Select(identifier => identifier.Value.Trim())
            .Where(MediaIdentifierRules.IsTmdbId)
            .Order(StringComparer.Ordinal)
            .FirstOrDefault();
}
