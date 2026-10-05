namespace MediaAcquisitionManager.Api.Services.Recommendations;

/// <summary>
/// MAM-33, ADR 0024 — checks in the background whether the release sources carry the recommended works: a pass
/// right after the app starts, then one <see cref="PassInterval"/> after each pass ends. Only the loop: each pass is
/// <see cref="IRecommendationAvailabilityPass"/>'s, in a scope of its own (the <c>DownloadJobTracker</c> shape).
/// </summary>
/// <remarks>
/// <para>
/// The wait comes after a pass, not on a fixed beat, so two passes never overlap, however long the sources take to
/// answer. A pass that throws is logged and the next one runs as usual: one bug can't stop the checking for good.
/// </para>
/// <para>
/// A loop of its own, apart from the feed reader's: a tracker that hangs one of them holds up only that one.
/// </para>
/// </remarks>
public sealed class RecommendationAvailabilityChecker(
    IServiceScopeFactory scopes,
    TimeProvider time,
    ILogger<RecommendationAvailabilityChecker> logger) : BackgroundService
{
    /// <summary>
    /// MAM-33 — the pause between passes. With <see cref="RecommendationAvailabilityPass.SearchesPerPass"/> searches a
    /// pass, 15 minutes spreads a user's default 60 searches over the day as the budget means them to be spread — a
    /// new run's candidates are checked within hours, never in one burst at the trackers. A constant, not a setting:
    /// the daily limit is the knob a user turns (the house rule for tuned numbers).
    /// </summary>
    public static readonly TimeSpan PassInterval = TimeSpan.FromMinutes(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await CheckOnceAsync(stoppingToken);
            try
            {
                await Task.Delay(PassInterval, time, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>
    /// Runs one pass of the availability check, in a scope of its own, and returns once it is over. The loop above
    /// calls it; tests drive it directly, as the test host runs no hosted services.
    /// </summary>
    public async Task CheckOnceAsync(CancellationToken stoppingToken)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<IRecommendationAvailabilityPass>().RunAsync(stoppingToken);
        }
        // A shutdown is not an error: every search is written with its check, and the next start's first pass goes on
        // from the rows. Anything else — a cancellation too, when it isn't the shutdown's — is a bug.
        catch (Exception exception) when (!(exception is OperationCanceledException && stoppingToken.IsCancellationRequested))
        {
            logger.LogError(exception, "A pass of the recommendations' availability check threw; the next pass runs as usual.");
        }
    }
}
