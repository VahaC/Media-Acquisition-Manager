using System.Globalization;
using MediaAcquisitionManager.Api.Services.Catalog;
using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Abstractions.Metadata;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Recommendations;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Services.Recommendations;

// MAM-33 — the signals (ADR 0023): each names works with a reason. Works not released yet are never named; whether
// the library holds a work, which tab it goes to and whether its rating carries it are settled once every signal is
// in (RecommendationRunner.Candidates.cs).
public sealed partial class RecommendationRunner
{
    /// <summary>
    /// Collection: each film seed's collection — every released part the library lacks, with how many of the released
    /// parts it holds ("2 of 3").
    /// </summary>
    private async Task<Failure?> CollectCollectionsAsync(Computation run, CancellationToken cancellationToken)
    {
        var collections = run.Seeds.Values
            .Where(seed => seed.Summary.Kind == MediaWorkKind.Movie && seed.Details.Collection is not null)
            .Select(seed => seed.Details.Collection!)
            .DistinctBy(collection => collection.Id, StringComparer.Ordinal)
            .OrderBy(collection => collection.Id, StringComparer.Ordinal)
            .ToList();
        await BeginStageAsync(run, RecommendationRunStage.ReadingCollections, collections.Count, cancellationToken);
        foreach (var reference in collections)
        {
            var answer = await store.GetCollectionAsync(run.Catalog, reference, cancellationToken);
            await AdvanceStageAsync(run, cancellationToken);
            if (answer.Status == IntegrationCallStatus.NotFound)
                continue;
            if (!answer.IsSuccess)
                return Failure.Of(answer);

            var collection = answer.Value!;
            var released = collection.Parts.Where(run.IsReleased).DistinctBy(part => part.Id, StringComparer.Ordinal).ToList();
            var held = released.Where(part => IsHeld(run, part, imdbId: null)).ToList();
            foreach (var part in released.Except(held))
            {
                run.CandidateFor(part).Reasons.Add(
                    new Reason(RecommendationReasonKind.Collection, Name: collection.Name, Held: held.Count, Total: released.Count));
            }
        }

        return null;
    }

    /// <summary>
    /// Director and Creator: a person at least two seeds of a kind are by — a film's director, a show's creator — and
    /// their released works of that kind, with how many seeds are theirs.
    /// </summary>
    private async Task<Failure?> CollectPeopleAsync(Computation run, CancellationToken cancellationToken)
    {
        // Every kind's people first, so the stage knows how many it reads; asked kind by kind, each by catalog id.
        var people = run.Kinds
            .SelectMany(kind => run.Seeds.Values
                .Where(seed => seed.Summary.Kind == kind)
                .SelectMany(seed => seed.Details.Directors.DistinctBy(person => person.Id, StringComparer.Ordinal).Select(person => (Person: person, Seed: seed)))
                .GroupBy(pair => pair.Person.Id, StringComparer.Ordinal)
                .Where(group => group.Count() >= 2)
                .OrderBy(group => group.Key, StringComparer.Ordinal)
                .Select(group => (Kind: kind, group.First().Person, Seeds: group.Count())))
            .ToList();
        await BeginStageAsync(run, RecommendationRunStage.ReadingPeople, people.Count, cancellationToken);
        foreach (var (kind, person, seeds) in people)
        {
            var reasonKind = kind == MediaWorkKind.Series ? RecommendationReasonKind.Creator : RecommendationReasonKind.Director;
            var answer = await store.GetPersonAsync(run.Catalog, person, cancellationToken);
            await AdvanceStageAsync(run, cancellationToken);
            if (answer.Status == IntegrationCallStatus.NotFound)
                continue;
            if (!answer.IsSuccess)
                return Failure.Of(answer);

            foreach (var work in answer.Value!.Works.Where(work => work.Kind == kind && run.IsReleased(work)))
                run.CandidateFor(work).Reasons.Add(new Reason(reasonKind, Name: answer.Value!.Name, Held: seeds));
        }

        return null;
    }

    /// <summary>Recommended: every seed's recommendations that are released, each remembering the seeds recommending
    /// it; the reason itself is made once all are counted.</summary>
    private static Failure? CollectRecommendations(Computation run)
    {
        foreach (var (key, seed) in run.Seeds)
        {
            foreach (var work in seed.Details.Recommendations.Where(run.IsReleased))
                run.CandidateFor(work).Recommenders.Add(key);
        }

        return null;
    }

    /// <summary>
    /// MissingSeasons: a show seed's regular seasons that have aired (by today) and of which the library holds no
    /// episode. The candidate is the held series itself; a season still to come, the specials and a partly held
    /// season are never missing.
    /// </summary>
    private static Failure? CollectMissingSeasons(Computation run)
    {
        foreach (var seed in run.Seeds.Values.Where(seed => seed.Summary.Kind == MediaWorkKind.Series && run.IsReleased(seed.Summary)))
        {
            // MAM-34 — a show seeded by an episode-to-show link that names no season: which of its seasons the library
            // holds is unknown, so none is called missing — every one would be, the held one among them.
            if (seed.LinkedSeasons.Any(season => season is null))
                continue;
            // MAM-34 — a season an episode-to-show link names is held, as a season one episode of is.
            var held = seed.Works.SelectMany(work => work.HeldSeasons).Concat(seed.LinkedSeasons.OfType<int>()).ToHashSet();
            var missing = seed.Details.Seasons
                .Where(season => season.Number > 0 && season.AirDate is { } aired && aired <= run.Today && !held.Contains(season.Number))
                .Select(season => season.Number)
                .Order()
                .ToList();
            if (missing.Count == 0)
                continue;

            var seasons = string.Join(',', missing.Select(number => number.ToString(CultureInfo.InvariantCulture)));
            var candidate = run.CandidateFor(seed.Summary);
            candidate.MissingSeasons = seasons;
            candidate.Reasons.Add(new Reason(RecommendationReasonKind.MissingSeasons, Seasons: seasons));
        }

        return null;
    }

    /// <summary>
    /// NewAtSource: the works the newest releases of a release source were resolved to, first seen within
    /// <see cref="FeedFreshness"/> — one reason per source, dated by its latest release of the work. Each work's summary is
    /// its row's; MAM-46 — a row stamped in another language than the catalog's of now (resolved before the owner changed
    /// it) has its details read again through the store, once, so its card shows the title and poster of the catalog's
    /// language too: the feed resolves an entry once, and no other signal may list the work again. A work the catalog no
    /// longer has is left out.
    /// </summary>
    private async Task<Failure?> CollectNewAtSourceAsync(Computation run, CancellationToken cancellationToken)
    {
        var since = time.GetUtcNow().UtcDateTime - FeedFreshness;
        var entries = await db.SourceFeedEntries.AsNoTracking()
            .Where(entry => entry.ResolvedUtc != null && entry.CatalogWorkId != null && entry.CreatedUtc >= since)
            .Select(entry => new { CatalogWorkId = entry.CatalogWorkId!.Value, entry.SourceName, entry.PublishedUtc })
            .ToListAsync(cancellationToken);
        if (entries.Count == 0)
            return null;

        var workIds = entries.Select(entry => entry.CatalogWorkId).Distinct().ToList();
        // MAM-33 — one JSON parameter (EF.Parameter, the LibrarySnapshotStore rule), not one parameter per id: two weeks
        // of every source's newest releases can name more works than SQLite takes parameters.
        var rows = await db.CatalogWorks.AsNoTracking().Where(work => EF.Parameter(workIds).Contains(work.Id)).ToListAsync(cancellationToken);
        var genres = (await db.CatalogWorkGenres.AsNoTracking()
                .Where(genre => EF.Parameter(workIds).Contains(genre.CatalogWorkId))
                .ToListAsync(cancellationToken))
            .ToLookup(genre => genre.CatalogWorkId, genre => genre.GenreId);
        // Each work once, before its sources: a work two sources published costs one read at most.
        var summaries = new Dictionary<Guid, CatalogWorkSummary>();
        foreach (var row in rows.Where(row => run.Wants(row.Kind)).OrderBy(row => row.Id))
        {
            if (row.DetailsLanguage == run.Catalog.Language)
            {
                summaries[row.Id] = CatalogStore.ToSummary(row, genres[row.Id]);
                continue;
            }

            var answer = await store.GetWorkAsync(run.Catalog, row.Kind, row.CatalogId, cancellationToken);
            if (answer.Status == IntegrationCallStatus.NotFound)
                continue;
            if (!answer.IsSuccess)
                return Failure.Of(answer);
            summaries[row.Id] = answer.Value!.Summary;
        }

        foreach (var source in entries
                     .GroupBy(entry => (entry.CatalogWorkId, entry.SourceName))
                     .OrderBy(group => group.Key.SourceName, StringComparer.Ordinal))
        {
            if (!summaries.TryGetValue(source.Key.CatalogWorkId, out var work) || !run.IsReleased(work))
                continue;

            run.CandidateFor(work).Reasons.Add(new Reason(
                RecommendationReasonKind.NewAtSource, Name: source.Key.SourceName, DateUtc: source.Max(entry => entry.PublishedUtc)));
        }

        return null;
    }

    /// <summary>
    /// TopRated (the fallback): for a wanted tab fewer than <see cref="FewSeeds"/> seeds are in, the catalog's
    /// best-rated works of the tab's genres — Movies and Series without Animation or Documentary, Cartoons with
    /// Animation and without Documentary, Documentaries with Documentary — films and shows as the tab holds them,
    /// <see cref="DiscoverPages"/> pages of each.
    /// </summary>
    private async Task<Failure?> CollectTopRatedAsync(Computation run, CancellationToken cancellationToken)
    {
        var seedsPerTab = run.Seeds.Values.GroupBy(seed => run.TabOf(seed.Summary)).ToDictionary(group => group.Key, group => group.Count());
        // Every discovery first, so the stage knows how many pages it reads at most.
        var discoveries = new List<(MediaWorkKind Kind, IReadOnlyList<int> With, IReadOnlyList<int> Without)>();
        foreach (var tab in run.Tabs.Where(tab => seedsPerTab.GetValueOrDefault(tab) < FewSeeds))
        {
            foreach (var kind in RecommendationTabs.KindsOf(tab).Where(run.Wants))
            {
                var animation = run.GenresOf(kind, CatalogGenreRole.Animation);
                var documentary = run.GenresOf(kind, CatalogGenreRole.Documentary);
                (IReadOnlyList<int> With, IReadOnlyList<int> Without)? genres = tab switch
                {
                    RecommendationTab.Movies or RecommendationTab.Series => ([], [.. animation, .. documentary]),
                    // The genre itself, the first of its role: with every genre listed, two of one role would ask for
                    // works of both. A list without the role has nothing to discover.
                    RecommendationTab.Cartoons => animation.Count > 0 ? ([animation[0]], documentary) : null,
                    _ => documentary.Count > 0 ? ([documentary[0]], []) : null,
                };
                if (genres is { } asked)
                    discoveries.Add((kind, asked.With, asked.Without));
            }
        }

        await BeginStageAsync(run, RecommendationRunStage.Discovering, discoveries.Count * DiscoverPages, cancellationToken);
        foreach (var (kind, with, without) in discoveries)
        {
            for (var page = 1; page <= DiscoverPages; page++)
            {
                var answer = await store.DiscoverAsync(run.Catalog, kind, with, without, page, cancellationToken);
                if (!answer.IsSuccess)
                    return Failure.Of(answer);
                foreach (var work in answer.Value!.Where(run.IsReleased))
                {
                    var candidate = run.CandidateFor(work);
                    if (!candidate.Reasons.Exists(reason => reason.Kind == RecommendationReasonKind.TopRated))
                        candidate.Reasons.Add(new Reason(RecommendationReasonKind.TopRated));
                }

                // An empty page is the last: the next would be empty too, so the pages after it count as read.
                var last = answer.Value!.Count == 0;
                await AdvanceStageAsync(run, cancellationToken, last ? DiscoverPages - page + 1 : 1);
                if (last)
                    break;
            }
        }

        return null;
    }
}
