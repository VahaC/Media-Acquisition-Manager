using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Api.Mapping;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Recommendations;
using MediaAcquisitionManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Services.Recommendations;

/// <inheritdoc cref="IRecommendationSettingsService"/>
public sealed class RecommendationSettingsService(
    ApplicationDbContext db,
    IRecommendationSettingsReader reader,
    IRecommendationRunService runs,
    IRecommendationMapper mapper,
    TimeProvider time,
    ILogger<RecommendationSettingsService> logger) : IRecommendationSettingsService
{
    /// <summary>
    /// The sentence a body that can't be read as the settings gets — not JSON, or a field of the wrong type, such as a
    /// limit of 1.5 — instead of the validation body MVC would send: it names every field and what it takes.
    /// </summary>
    public const string UnreadableBodyError =
        "Send the settings as JSON: movies, series, cartoons and documentaries true or false, and dailySearchLimit a whole number from 0 to 500.";

    public async Task<RecommendationSettingsDto> GetAsync(Guid userId, CancellationToken cancellationToken = default) =>
        mapper.ToDto(await reader.ReadAsync(userId, cancellationToken), await CountSearchesAsync(userId, cancellationToken));

    public async Task<RecommendationSettingsSaveResult> SaveAsync(
        RecommendationSettingsRequest? request, Guid userId, CancellationToken cancellationToken = default)
    {
        if (Validate(request, out var settings) is { } error)
            return RecommendationSettingsSaveResult.Invalid(error);

        // As they held before this save: a user who never saved had every tab on, so only a saved row can switch one on.
        var before = await reader.ReadAsync(userId, cancellationToken);
        await UpsertAsync(userId, settings!, cancellationToken);
        logger.LogInformation("User {UserId} saved their recommendation settings.", userId);

        var switchedOn = settings!.Tabs.Where(tab => !before.IsOn(tab)).ToList();
        if (switchedOn.Count > 0)
            await QueueForNewTabsAsync(userId, switchedOn, cancellationToken);
        return RecommendationSettingsSaveResult.Saved(mapper.ToDto(settings, await CountSearchesAsync(userId, cancellationToken)));
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The sentence for the first field missing — the tabs in the page's order, then the limit — or the limit out of
    /// range; <c>null</c> with the settings in <paramref name="settings"/>. A missing tab is never read as off: a save
    /// that quietly switched a tab off would hide a list without anyone asking.
    /// </summary>
    private static string? Validate(RecommendationSettingsRequest? request, out RecommendationSettings? settings)
    {
        settings = null;
        if (request?.Movies is not { } movies)
            return TabMissing(RecommendationTab.Movies);
        if (request.Series is not { } series)
            return TabMissing(RecommendationTab.Series);
        if (request.Cartoons is not { } cartoons)
            return TabMissing(RecommendationTab.Cartoons);
        if (request.Documentaries is not { } documentaries)
            return TabMissing(RecommendationTab.Documentaries);
        if (RecommendationSettingsRules.ValidateDailySearchLimit(request.DailySearchLimit) is { } limitError)
            return limitError;

        settings = new RecommendationSettings(movies, series, cartoons, documentaries, request.DailySearchLimit!.Value);
        return null;
    }

    private static string TabMissing(RecommendationTab tab) => $"Say whether the {tab} tab is on.";

    /// <summary>Writes <paramref name="settings"/> to <paramref name="userId"/>'s row, adding it on their first save.</summary>
    private async Task UpsertAsync(Guid userId, RecommendationSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            await WriteRowAsync(userId, settings, cancellationToken);
        }
        catch (DbUpdateException exception)
        {
            // Two first saves of one user (two tabs of the browser) can both find no row; the unique index on the user
            // lets one add it, and this one lost. Written over the winner's row, as the later save. Anything else (a
            // full disk) stays a failure.
            db.ChangeTracker.Clear();
            if (!await db.UserRecommendationSettings.AnyAsync(row => row.UserId == userId, cancellationToken))
                throw;
            logger.LogInformation(exception, "A concurrent save added user {UserId}'s recommendation settings first; this one is written over them.", userId);
            await WriteRowAsync(userId, settings, cancellationToken);
        }
        finally
        {
            db.ChangeTracker.Clear();
        }
    }

    private async Task WriteRowAsync(Guid userId, RecommendationSettings settings, CancellationToken cancellationToken)
    {
        var row = await db.UserRecommendationSettings.AsTracking().SingleOrDefaultAsync(saved => saved.UserId == userId, cancellationToken);
        if (row is null)
        {
            row = new UserRecommendationSettingsEntity { UserId = userId, CreatedUtc = time.GetUtcNow().UtcDateTime };
            db.UserRecommendationSettings.Add(row);
        }

        row.MoviesEnabled = settings.Movies;
        row.SeriesEnabled = settings.Series;
        row.CartoonsEnabled = settings.Cartoons;
        row.DocumentariesEnabled = settings.Documentaries;
        row.DailySearchLimit = settings.DailySearchLimit;
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Queues a computation (or joins the one waiting) when one of <paramref name="switchedOn"/> is on for no other user
    /// who can sign in — a user who never saved has every tab on — since the last run then computed none of its works.
    /// The save has happened and stands: a full queue or a failure here is logged, never the save's answer, and the
    /// queuing writes no audit row — the run row records who switched the tab on.
    /// </summary>
    private async Task QueueForNewTabsAsync(Guid userId, IReadOnlyList<RecommendationTab> switchedOn, CancellationToken cancellationToken)
    {
        try
        {
            var others = (await reader.ReadEnabledUsersAsync(cancellationToken)).Where(user => user.UserId != userId).ToList();
            if (switchedOn.All(tab => others.Exists(other => other.Settings.IsOn(tab))))
                return;
            await runs.EnsureQueuedAsync(RecommendationRunTrigger.TabEnabled, userId, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "User {UserId} switched a recommendation tab on, but no recommendation run could be queued after it.", userId);
        }
    }

    /// <summary>The searches run for <paramref name="userId"/>'s lists within the availability check's budget window: the
    /// count its daily limit is measured against.</summary>
    private Task<int> CountSearchesAsync(Guid userId, CancellationToken cancellationToken)
    {
        var since = time.GetUtcNow().UtcDateTime - RecommendationAvailabilityPass.BudgetWindow;
        return db.RecommendationSearches.CountAsync(search => search.UserId == userId && search.SearchedUtc > since, cancellationToken);
    }
}
