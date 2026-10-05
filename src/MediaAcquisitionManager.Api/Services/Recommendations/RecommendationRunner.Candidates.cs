using MediaAcquisitionManager.Core.Abstractions.Metadata;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Library;
using MediaAcquisitionManager.Core.Recommendations;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Services.Recommendations;

// MAM-33 — from what the signals named to the candidates stored: the tab, the exclusions, the rating floor, the
// score, and the one transaction that replaces the previous run's.
public sealed partial class RecommendationRunner
{
    /// <summary>The reasons for taste alone, which the rating floor applies to (<see cref="RecommendationRatingFloor"/>).</summary>
    private static readonly HashSet<RecommendationReasonKind> TasteReasons =
        [RecommendationReasonKind.Recommended, RecommendationReasonKind.Director, RecommendationReasonKind.Creator, RecommendationReasonKind.TopRated];

    /// <summary>
    /// Every named work that keeps a reason, in the tab its genres put it, scored: a work in a tab nobody has on goes;
    /// a work the library holds keeps only a missing-seasons reason; a work under the rating floor keeps only the
    /// reasons the library gives (collection, missing seasons, new at a source); a work left with no reason goes.
    /// </summary>
    private async Task<IReadOnlyList<StoredCandidate>> FinishAsync(Computation run, CancellationToken cancellationToken)
    {
        // Each named work's row — every one was stored by the read that named it — and its IMDb id where its details
        // were ever read: the one way the library's IMDb ids can match a work known from a list.
        var rows = new Dictionary<(MediaWorkKind Kind, string CatalogId), (Guid Id, string? ImdbId)>();
        foreach (var kind in run.Candidates.Keys.Select(key => key.Kind).Distinct())
        {
            var ids = run.Candidates.Keys.Where(key => key.Kind == kind).Select(key => key.CatalogId).ToList();
            // MAM-33 — one JSON parameter (EF.Parameter, the LibrarySnapshotStore rule), not EF Core 10's default of one
            // parameter per id: a run names thousands of works, past SQLite's limit on the number of parameters.
            foreach (var row in await db.CatalogWorks.AsNoTracking()
                         .Where(work => work.Kind == kind && EF.Parameter(ids).Contains(work.CatalogId))
                         .Select(work => new { work.Id, work.CatalogId, work.ImdbId })
                         .ToListAsync(cancellationToken))
                rows[(kind, row.CatalogId)] = (row.Id, row.ImdbId);
        }

        // The affinities weigh a work against the seeds of its own kind: a film's genres are numbered in the films' list.
        var seedGenres = run.Seeds.Values
            .GroupBy(seed => seed.Summary.Kind)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<IReadOnlyCollection<int>>)[.. group.Select(seed => seed.Summary.GenreIds.ToHashSet())]);
        var seedYears = run.Seeds.Values
            .GroupBy(seed => seed.Summary.Kind)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<int?>)[.. group.Select(seed => seed.Summary.ReleaseDate?.Year)]);

        var stored = new List<StoredCandidate>();
        foreach (var (key, candidate) in run.Candidates)
        {
            var work = candidate.Summary;
            var tab = run.TabOf(work);
            if (!run.Tabs.Contains(tab) || !rows.TryGetValue(key, out var row))
                continue;

            List<Reason> reasons = [.. candidate.Reasons];
            if (candidate.Recommenders.Count > 0)
                reasons.Add(RecommendedReason(run, candidate));
            // Not for missing seasons: that candidate is a series the library holds, by definition.
            if (IsHeld(run, work, row.ImdbId))
                reasons.RemoveAll(reason => reason.Kind != RecommendationReasonKind.MissingSeasons);
            if (!RecommendationRatingFloor.Passes(work.Rating, work.VoteCount))
                reasons.RemoveAll(reason => TasteReasons.Contains(reason.Kind));
            if (reasons.Count == 0)
                continue;

            var kinds = reasons.Select(reason => reason.Kind).ToHashSet();
            var score = RecommendationScoring.Score(
                kinds,
                candidate.Recommenders.Count,
                RecommendationScoring.GenreAffinity(work.GenreIds, seedGenres.GetValueOrDefault(work.Kind) ?? []),
                RecommendationScoring.DecadeAffinity(work.ReleaseDate?.Year, seedYears.GetValueOrDefault(work.Kind) ?? []),
                work.Rating,
                work.VoteCount);
            stored.Add(new StoredCandidate(
                row.Id,
                work,
                tab,
                score,
                // A fallback alone earns a place only by being there to download; any other reason stands on its own.
                RequiresAvailability: kinds.All(reasonKind => reasonKind == RecommendationReasonKind.TopRated),
                kinds.Contains(RecommendationReasonKind.MissingSeasons) ? candidate.MissingSeasons : null,
                [.. reasons.OrderBy(reason => RecommendationReasonOrder.PositionOf(reason.Kind)).ThenBy(reason => reason.Name, StringComparer.Ordinal)]));
        }

        // ADR 0023's order, so the rows go in the order the list reads them.
        return
        [
            .. stored
                .OrderByDescending(candidate => candidate.Score)
                .ThenByDescending(candidate => candidate.Work.VoteCount)
                .ThenBy(candidate => candidate.Work.Title, StringComparer.Ordinal)
                .ThenBy(candidate => candidate.Work.Id, StringComparer.Ordinal),
        ];
    }

    /// <summary>
    /// Replaces every candidate and reason with <paramref name="candidates"/> and marks the run succeeded with its
    /// counts, in one transaction: a run that something else finished meanwhile (the reconciler) replaces nothing,
    /// and the list never shows half of one run and half of another.
    /// </summary>
    private async Task StoreAsync(Guid runId, int seedCount, IReadOnlyList<StoredCandidate> candidates, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow().UtcDateTime;
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        // The reasons go with their candidates in the database (Cascade).
        await db.RecommendationCandidates.ExecuteDeleteAsync(cancellationToken);
        foreach (var candidate in candidates)
        {
            var row = new RecommendationCandidateEntity
            {
                CatalogWorkId = candidate.CatalogWorkId,
                Tab = candidate.Tab,
                Score = candidate.Score,
                RequiresAvailability = candidate.RequiresAvailability,
                MissingSeasons = candidate.MissingSeasons,
                CreatedUtc = now,
            };
            db.RecommendationCandidates.Add(row);
            db.RecommendationReasons.AddRange(candidate.Reasons.Select((reason, position) => new RecommendationReasonEntity
            {
                RecommendationCandidateId = row.Id,
                Position = position,
                Kind = reason.Kind,
                Name = reason.Name,
                Held = reason.Held,
                Total = reason.Total,
                Titles = reason.Titles,
                Seasons = reason.Seasons,
                DateUtc = reason.DateUtc,
            }));
        }

        await db.SaveChangesAsync(cancellationToken);
        db.ChangeTracker.Clear();

        // Never cancelled, the RecommendationRunUpdates rule: once the outcome is decided it is written.
        var finished = await db.RecommendationRuns
            .Where(run => run.Id == runId && run.Status == RecommendationRunStatus.Running)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(run => run.Status, RecommendationRunStatus.Succeeded)
                    .SetProperty(run => run.FinishedUtc, now)
                    .SetProperty(run => run.SeedCount, seedCount)
                    .SetProperty(run => run.CandidateCount, candidates.Count)
                    // Done: the stage it ended in goes with it, as a failed run's does (RecommendationRunUpdates).
                    .SetProperty(run => run.Stage, (RecommendationRunStage?)null)
                    .SetProperty(run => run.StageDone, (int?)null)
                    .SetProperty(run => run.StageTotal, (int?)null),
                CancellationToken.None) == 1;
        if (finished)
        {
            await transaction.CommitAsync(CancellationToken.None);
            return;
        }

        await transaction.RollbackAsync(CancellationToken.None);
        logger.LogWarning("Recommendation run {RunId} was finished elsewhere before its candidates were stored; nothing was replaced.", runId);
    }

    /// <summary>
    /// The Recommended reason of <paramref name="candidate"/>: how many seeds recommend it, and the display titles of
    /// the library works behind them — at most <see cref="MaximumReasonTitles"/>, in the Library page's title order.
    /// </summary>
    private static Reason RecommendedReason(Computation run, Candidate candidate)
    {
        var titles = candidate.Recommenders
            .SelectMany(key => run.Seeds[key].Works.Select(work => work.DisplayTitle))
            .Where(title => title.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .Order(LibraryTitleOrder.Comparer)
            .ThenBy(title => title, StringComparer.Ordinal)
            .Take(MaximumReasonTitles);
        return new Reason(
            RecommendationReasonKind.Recommended,
            Held: candidate.Recommenders.Count,
            Titles: string.Join('\n', titles));
    }

    /// <summary>A candidate as it is stored.</summary>
    private sealed record StoredCandidate(
        Guid CatalogWorkId,
        CatalogWorkSummary Work,
        RecommendationTab Tab,
        double Score,
        bool RequiresAvailability,
        string? MissingSeasons,
        IReadOnlyList<Reason> Reasons);
}
