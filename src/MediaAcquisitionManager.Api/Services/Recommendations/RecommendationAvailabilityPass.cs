using MediaAcquisitionManager.Api.Services.Releases;
using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Abstractions.Search;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Recommendations;
using MediaAcquisitionManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Services.Recommendations;

/// <inheritdoc cref="IRecommendationAvailabilityPass"/>
/// <remarks>
/// <para>
/// Split by facet: this file picks what is searched, for whom, and writes what was found;
/// <c>RecommendationAvailabilityPass.Matching.cs</c> says which releases are the work;
/// <c>RecommendationAvailabilityPass.State.cs</c> holds what one pass keeps in memory.
/// </para>
/// <para>
/// A work is due when it was never checked, or its last check is older than <see cref="FoundRecheck"/> (found) or
/// <see cref="NotFoundRecheck"/> (not found). The check is shared: a work checked for one user is not due for
/// anyone until then. A search serves one user's list and counts against that user alone; their "Not interested"
/// works are never due for them, and neither are the works of a tab they have off.
/// </para>
/// <para>
/// Logs carry ids and counts only, never a title or an address.
/// </para>
/// </remarks>
public sealed partial class RecommendationAvailabilityPass(
    ApplicationDbContext db,
    IReleaseSources sources,
    IRecommendationSettingsReader settingsReader,
    RecommendationSourceFailures failures,
    TimeProvider time,
    ILogger<RecommendationAvailabilityPass> logger) : IRecommendationAvailabilityPass
{
    /// <summary>
    /// MAM-33 — the most searches one pass runs, whoever they serve. Four every 15 minutes is 384 a day at most,
    /// more than the default limits of a household's users spend, yet a source sees a few requests at a time, never
    /// a burst of a day's worth after a run fills the lists: each search is a capabilities read and one or two
    /// queries, and Toloka's gateway is the same tracker its users browse.
    /// </summary>
    public const int SearchesPerPass = 4;

    /// <summary>
    /// MAM-33 — how long a check that found releases stands: a day. What a tracker carries for a work changes
    /// slowly — a better rip, a new season's pack — and the list's line ("4 releases · up to 2160p · Ukr") is
    /// still right the next day, while the seeders it shows move by the hour.
    /// </summary>
    public static readonly TimeSpan FoundRecheck = TimeSpan.FromDays(1);

    /// <summary>
    /// MAM-33 — how long a check that found nothing stands: a week. Most works the trackers lack they lack for good,
    /// and rechecking them daily would spend the budget on the same empty answers; a week still catches a release
    /// that appears, and the "New on Toloka" feed finds a brand-new one sooner.
    /// </summary>
    public static readonly TimeSpan NotFoundRecheck = TimeSpan.FromDays(7);

    /// <summary>MAM-33 — the window a user's daily search limit counts in: the last 24 hours, rolling (Vasyl's
    /// decision 5's "daily").</summary>
    public static readonly TimeSpan BudgetWindow = TimeSpan.FromHours(24);

    /// <summary>
    /// MAM-33 — how long a search stays in the budget log: two days, the window and a day's margin, so a clock
    /// moved back or a pass that ran late never forgets a search that still counts.
    /// </summary>
    public static readonly TimeSpan SearchLogLifetime = TimeSpan.FromDays(2);

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        // A switched-off source is asked nothing (MAM-7's switch), and with none at all there is nothing to search.
        if (!await db.IntegrationConnections.AnyAsync(
                integration => integration.Kind == IntegrationKind.ReleaseSearch && integration.IsEnabled, cancellationToken))
            return;

        var now = time.GetUtcNow().UtcDateTime;
        await PruneAsync(now, cancellationToken);

        // A user who can't sign in sees no list, and a limit of 0 asks for none (Vasyl's decision 5).
        var users = (await settingsReader.ReadEnabledUsersAsync(cancellationToken))
            .Where(user => user.Settings.DailySearchLimit > 0 && user.Settings.Tabs.Count > 0)
            .ToList();
        if (users.Count == 0)
            return;

        var due = await ReadDueAsync(users, now, cancellationToken);
        if (due.Count == 0)
            return;

        var budgets = await ReadBudgetsAsync(users, now, cancellationToken);
        var searches = 0;
        var turn = 0;
        // Round-robin: one search per user in turn, so one user's long lists never use up a pass. A full round with
        // nobody able to search ends the pass early.
        for (var idle = 0; searches < SearchesPerPass && idle < budgets.Count; turn = (turn + 1) % budgets.Count)
        {
            var budget = budgets[turn];
            if (NextFor(budget, due) is not { } work)
            {
                idle++;
                continue;
            }

            idle = 0;
            var outcome = await CheckAsync(budget, work, now, cancellationToken);
            if (outcome == CheckOutcome.NotAsked)
            {
                // MAM-33 — no source could even be asked for a work of this kind (none searches it, or none can be
                // used at all): no search was sent, so none counts, and no source is down. The next work of the kind
                // would be refused the same way, so they all wait for the next pass; the other kind's go on.
                logger.LogInformation(
                    "Availability check of catalog work {CatalogWorkId} asked no release source: none can search a {Kind} release; such works wait for the next pass.",
                    work.CatalogWorkId,
                    work.ReleaseKind);
                due.RemoveAll(other => other.ReleaseKind == work.ReleaseKind);
                continue;
            }

            searches++;
            if (outcome == CheckOutcome.Failed)
            {
                // Every source asked failed: the next search would fail the same way, and spend another user's budget
                // for nothing. The work stays due for the next pass.
                logger.LogInformation(
                    "Availability check of catalog work {CatalogWorkId} for user {UserId} got no answer from any release source; the pass stops.",
                    work.CatalogWorkId,
                    budget.UserId);
                break;
            }

            due.Remove(work);
        }

        if (searches > 0)
            logger.LogInformation("Availability pass ran {Searches} searches.", searches);
    }

    /// <summary>
    /// Searches the release sources for <paramref name="work"/> on behalf of <paramref name="budget"/>'s user, logs
    /// the search against their budget and, when any source answered, stores what was found. Whether any source
    /// answered, every one asked failed, or none could be asked at all.
    /// </summary>
    private async Task<CheckOutcome> CheckAsync(UserBudget budget, DueWork work, DateTime now, CancellationToken cancellationToken)
    {
        var outcomes = await sources.SearchKindAsync(work.QueryText, work.QueryYear, work.ReleaseKind, cancellationToken);
        // MAM-31 — a source that refused or failed is said on the Recommendations page, as on the Search page.
        failures.Record(outcomes);

        // MAM-33 — not one source was asked (each was left out: no search of this kind, a type this build can't use, a
        // key it can't read): no search was sent, so there is nothing to count against the budget, nor to store.
        if (outcomes.All(outcome => outcome.Status is null))
            return CheckOutcome.NotAsked;

        // Every search sent counts, answered or not (ADR 0024): the trackers were asked all the same.
        db.RecommendationSearches.Add(new RecommendationSearchEntity
        {
            UserId = budget.UserId,
            Tab = work.Tab,
            CatalogWorkId = work.CatalogWorkId,
            SearchedUtc = now,
        });
        budget.Count(work.Tab);

        var answered = outcomes.Where(outcome => outcome.Status == IntegrationCallStatus.Succeeded).ToList();
        if (answered.Count > 0)
        {
            // What the sources that answered carry: a source that failed beside them only makes the answer smaller,
            // and the next check, a day or a week on, asks it again.
            var found = answered.SelectMany(outcome => outcome.Hits).Where(hit => IsRelease(work, hit)).ToList();
            await StageAvailabilityAsync(work, found, now, cancellationToken);
            logger.LogInformation(
                "Availability of catalog work {CatalogWorkId} checked for user {UserId} ({Tab}): {Count} releases.",
                work.CatalogWorkId,
                budget.UserId,
                work.Tab,
                found.Count);
        }

        await db.SaveChangesAsync(cancellationToken);
        db.ChangeTracker.Clear();
        return answered.Count > 0 ? CheckOutcome.Answered : CheckOutcome.Failed;
    }

    /// <summary>The work's check, a new row or its own updated: one row per work, shared by every user.</summary>
    private async Task StageAvailabilityAsync(DueWork work, IReadOnlyList<ReleaseSearchHit> found, DateTime now, CancellationToken cancellationToken)
    {
        var row = await db.ReleaseAvailabilities.AsTracking()
            .SingleOrDefaultAsync(availability => availability.CatalogWorkId == work.CatalogWorkId, cancellationToken);
        if (row is null)
        {
            row = new ReleaseAvailabilityEntity { CatalogWorkId = work.CatalogWorkId, CreatedUtc = now };
            db.ReleaseAvailabilities.Add(row);
        }

        var summary = Summarize(found);
        row.CheckedUtc = now;
        row.Found = found.Count > 0;
        row.ReleaseCount = found.Count;
        row.BestResolution = summary.BestResolution;
        row.HasUkrainianAudio = summary.HasUkrainianAudio;
        row.MaxSeeders = summary.MaxSeeders;
    }

    /// <summary>
    /// The candidates due, in the order they are searched: never checked before checked, then by score (the list's
    /// order), most rated, title and id — so the same rows always give the same order. Only the tabs some user has
    /// on; a work whose titles leave no query is never due.
    /// </summary>
    private async Task<List<DueWork>> ReadDueAsync(IReadOnlyList<UserRecommendationSettings> users, DateTime now, CancellationToken cancellationToken)
    {
        var tabs = RecommendationTabs.All.Where(tab => users.Any(user => user.Settings.IsOn(tab))).ToList();
        var candidates = await db.RecommendationCandidates.AsNoTracking()
            .Where(candidate => tabs.Contains(candidate.Tab))
            .Join(
                db.CatalogWorks.AsNoTracking(),
                candidate => candidate.CatalogWorkId,
                work => work.Id,
                (candidate, work) => new
                {
                    candidate.CatalogWorkId,
                    candidate.Tab,
                    candidate.Score,
                    candidate.MissingSeasons,
                    work.Kind,
                    work.CatalogId,
                    work.Title,
                    work.OriginalTitle,
                    work.ReleaseDate,
                    work.ImdbId,
                    work.VoteCount,
                })
            .ToListAsync(cancellationToken);
        var checks = await db.ReleaseAvailabilities.AsNoTracking()
            .Select(availability => new { availability.CatalogWorkId, availability.CheckedUtc, availability.Found })
            .ToDictionaryAsync(availability => availability.CatalogWorkId, cancellationToken);

        return
        [
            .. candidates
                .Select(candidate => (
                    Candidate: candidate,
                    Check: checks.TryGetValue(candidate.CatalogWorkId, out var check) ? check : null))
                .Where(pair => pair.Check is null
                    || pair.Check.CheckedUtc <= now - (pair.Check.Found ? FoundRecheck : NotFoundRecheck))
                .Select(pair => new DueWork(
                    pair.Candidate.CatalogWorkId,
                    pair.Candidate.Tab,
                    pair.Candidate.Score,
                    NeverChecked: pair.Check is null,
                    pair.Candidate.Kind,
                    pair.Candidate.CatalogId,
                    pair.Candidate.Title,
                    pair.Candidate.OriginalTitle,
                    pair.Candidate.ReleaseDate,
                    pair.Candidate.ImdbId,
                    pair.Candidate.VoteCount,
                    pair.Candidate.MissingSeasons))
                .Where(work => work.QueryText.Length > 0)
                .OrderBy(work => work.NeverChecked ? 0 : 1)
                .ThenByDescending(work => work.Score)
                .ThenByDescending(work => work.VoteCount)
                .ThenBy(work => work.Title, StringComparer.Ordinal)
                .ThenBy(work => work.CatalogId, StringComparer.Ordinal),
        ];
    }

    /// <summary>
    /// Each user's budget — their searches of the last <see cref="BudgetWindow"/> per tab — and "Not interested"
    /// works, in the order a pass serves them: the user searched for least recently first (never, within the window,
    /// before anyone), then by user name, so every pass starts with someone else and no user waits behind the others
    /// for good. Two queries, however many users.
    /// </summary>
    private async Task<List<UserBudget>> ReadBudgetsAsync(IReadOnlyList<UserRecommendationSettings> users, DateTime now, CancellationToken cancellationToken)
    {
        var userIds = users.Select(user => user.UserId).ToList();
        var since = now - BudgetWindow;
        var searches = (await db.RecommendationSearches.AsNoTracking()
                .Where(search => userIds.Contains(search.UserId) && search.SearchedUtc > since)
                .Select(search => new { search.UserId, search.Tab, search.SearchedUtc })
                .ToListAsync(cancellationToken))
            .ToLookup(search => search.UserId);
        var dismissals = (await db.RecommendationDismissals.AsNoTracking()
                .Where(dismissal => userIds.Contains(dismissal.UserId))
                .Select(dismissal => new { dismissal.UserId, dismissal.Kind, dismissal.CatalogId })
                .ToListAsync(cancellationToken))
            .ToLookup(dismissal => dismissal.UserId, dismissal => (dismissal.Kind, dismissal.CatalogId));

        return
        [
            .. users
                .Select((user, position) => (
                    Budget: new UserBudget(
                        user.UserId,
                        user.Settings,
                        searches[user.UserId].GroupBy(search => search.Tab).ToDictionary(group => group.Key, group => group.Count()),
                        searches[user.UserId].Count(),
                        [.. dismissals[user.UserId]]),
                    LastSearchUtc: searches[user.UserId].Select(search => (DateTime?)search.SearchedUtc).Max(),
                    Position: position))
                .OrderBy(user => user.LastSearchUtc ?? DateTime.MinValue)
                .ThenBy(user => user.Position)
                .Select(user => user.Budget),
        ];
    }

    /// <summary>The work <paramref name="budget"/>'s next search checks, or <c>null</c> when it runs none now: the
    /// first due work of the tab <see cref="RecommendationSearchBudget.NextTab"/> picks among those with one due for
    /// the user.</summary>
    private static DueWork? NextFor(UserBudget budget, List<DueWork> due)
    {
        var tabsWithDue = due.Where(budget.Wants).Select(work => work.Tab).ToHashSet();
        var tab = RecommendationSearchBudget.NextTab(
            budget.Settings.DailySearchLimit, budget.Settings.Tabs, budget.UsedByTab, budget.UsedTotal, tabsWithDue);
        return tab is { } chosen ? due.First(work => work.Tab == chosen && budget.Wants(work)) : null;
    }

    /// <summary>Forgets the searches no budget counts any more (<see cref="SearchLogLifetime"/>).</summary>
    private async Task PruneAsync(DateTime now, CancellationToken cancellationToken)
    {
        var cutoff = now - SearchLogLifetime;
        var removed = await db.RecommendationSearches.Where(search => search.SearchedUtc < cutoff).ExecuteDeleteAsync(cancellationToken);
        if (removed > 0)
            logger.LogInformation("Removed {Count} recommendation searches older than two days.", removed);
    }
}
