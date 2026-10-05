namespace MediaAcquisitionManager.Api.Services.Recommendations;

/// <summary>
/// MAM-33 — reads the release sources' newest releases in the background ("New on Toloka"): a pass right after the
/// app starts, then one <see cref="PassInterval"/> after each pass ends. Only the loop: each pass is
/// <see cref="IRecommendationFeedPass"/>'s, in a scope of its own (the <c>DownloadJobTracker</c> shape).
/// </summary>
/// <remarks>
/// <para>
/// The wait comes after a pass, so two passes never overlap. A pass that throws is logged and the next one runs as
/// usual: one bug can't stop the feed for good.
/// </para>
/// <para>
/// A loop of its own, apart from the availability check's: a source that hangs one of them holds up only that one.
/// </para>
/// </remarks>
public sealed class RecommendationFeedReader(
    IServiceScopeFactory scopes,
    TimeProvider time,
    ILogger<RecommendationFeedReader> logger) : BackgroundService
{
    /// <summary>
    /// MAM-33 — the pause between reads. Toloka lists its latest 90 releases per kind, about a week of them
    /// (VendorSamples: 90 films over 2026-09-26 to 10-05), so a read every three hours misses none, and a release is a
    /// recommendation the same evening. A constant, not a setting (the house rule for tuned numbers).
    /// </summary>
    public static readonly TimeSpan PassInterval = TimeSpan.FromHours(3);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await ReadOnceAsync(stoppingToken);
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
    /// Runs one read of the newest releases, in a scope of its own, and returns once it is over. The loop above calls
    /// it; tests drive it directly, as the test host runs no hosted services.
    /// </summary>
    public async Task ReadOnceAsync(CancellationToken stoppingToken)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<IRecommendationFeedPass>().RunAsync(stoppingToken);
        }
        // A shutdown is not an error: what was read is written, and an entry left unresolved is resolved by the next
        // start's first pass. Anything else — a cancellation too, when it isn't the shutdown's — is a bug.
        catch (Exception exception) when (!(exception is OperationCanceledException && stoppingToken.IsCancellationRequested))
        {
            logger.LogError(exception, "A read of the release sources' newest releases threw; the next read runs as usual.");
        }
    }
}
