namespace MediaAcquisitionManager.Api.Services.Downloads;

/// <summary>
/// MAM-14 — follows the download jobs in their clients in the background (ADR 0014): a pass right after the app
/// starts, then one <see cref="PollInterval"/> after each pass ends. Only the loop: each pass is
/// <see cref="IDownloadJobPoller"/>'s, in a scope of its own.
/// </summary>
/// <remarks>
/// <para>
/// The wait comes after a pass, not on a fixed beat, so two passes never overlap, however long one takes — a
/// client that doesn't answer holds its pass up to its 15 s timeout per request. A pass that throws is logged
/// and the next one runs as usual: one bug can't stop the following of every job for good.
/// </para>
/// <para>
/// Nothing is kept here between passes: a restart resumes where the rows are (<see cref="IDownloadJobPoller"/>).
/// </para>
/// </remarks>
public sealed class DownloadJobTracker(
    IServiceScopeFactory scopes,
    TimeProvider time,
    ILogger<DownloadJobTracker> logger) : BackgroundService
{
    /// <summary>
    /// The pause between passes. A pass is <c>GET app/version</c> and <c>GET torrents/info?hashes=…</c> per
    /// client on a session it keeps, and a files read per job that moved — trivial for a qBittorrent on the LAN,
    /// whose own Web UI asks every 1.5 s — while 10 s keeps the Jobs page, which refreshes as often, close to
    /// what the client shows. A constant, not a setting: nothing about an installation makes another number
    /// better (the house rule for tuned numbers).
    /// </summary>
    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(10);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await PollOnceAsync(stoppingToken);
            try
            {
                await Task.Delay(PollInterval, time, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>
    /// Runs one pass over the followed jobs, in a scope of its own, and returns once it is over. The loop above
    /// calls it; tests drive it directly, as the test host runs no hosted services.
    /// </summary>
    public async Task PollOnceAsync(CancellationToken stoppingToken)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<IDownloadJobPoller>().PollAsync(stoppingToken);
        }
        // A shutdown is not an error: the rows are as the last write left them, and the next start's first
        // pass goes on from there. Anything else — a cancellation too, when it isn't the shutdown's — is a bug.
        catch (Exception exception) when (!(exception is OperationCanceledException && stoppingToken.IsCancellationRequested))
        {
            logger.LogError(exception, "A pass over the followed download jobs threw; the next pass runs as usual.");
        }
    }
}
