namespace MediaAcquisitionManager.Api.Services.Replacements;

/// <summary>
/// MAM-25 — checks the new files of the replacements whose download is done, in the background (ADR 0035): a pass right
/// after the app starts, then one <see cref="PassInterval"/> after each pass ends. Only the loop: each pass is
/// <see cref="IReplacementVerifier"/>'s, in a scope of its own.
/// </summary>
/// <remarks>
/// <para>
/// A loop of its own rather than a third step of <c>DownloadJobTracker</c>'s pass: a check reads every new file with the
/// media inspector and asks the library, which would hold the downloads' 2 s following up for as long — and one bug in
/// either can't stop the other. The rows are the queue: a <c>Downloaded</c> replacement is taken up on the next pass.
/// </para>
/// <para>
/// The wait comes after a pass, not on a fixed beat, so two passes never overlap, however long one takes. A pass that
/// throws is logged and the next one runs as usual. Nothing is kept here between passes: a restart resumes where the rows
/// are, every wait reckoned from a time stored on the job.
/// </para>
/// </remarks>
public sealed class ReplacementVerificationTracker(
    IServiceScopeFactory scopes,
    TimeProvider time,
    ILogger<ReplacementVerificationTracker> logger) : BackgroundService
{
    /// <summary>
    /// The pause between passes. A pass that has nothing to check is one query; one that has reads the client, the disk
    /// and the library for each job being checked. Jellyfin looks at a new file a minute after it is asked
    /// (its <c>LibraryMonitorDelay</c>) and refreshes it after, and the waits run an hour, so 15 s sees each step soon
    /// enough without asking the library every couple of seconds. A constant, not a setting (ADR 0014's rule for tuned
    /// numbers).
    /// </summary>
    public static readonly TimeSpan PassInterval = TimeSpan.FromSeconds(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await RunOnceAsync(stoppingToken);
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
    /// Runs one pass over the replacements being checked, in a scope of its own, and returns once it is over. The loop
    /// above calls it; tests drive it directly, as the test host runs no hosted services.
    /// </summary>
    public async Task RunOnceAsync(CancellationToken stoppingToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        try
        {
            await scope.ServiceProvider.GetRequiredService<IReplacementVerifier>().VerifyAsync(stoppingToken);
        }
        // A shutdown is not an error: the rows are as the last write left them, and the next start's first pass goes on
        // from there. Anything else is a bug. Error, with the type and the stack, never the message.
        catch (Exception exception) when (!(exception is OperationCanceledException && stoppingToken.IsCancellationRequested))
        {
            logger.LogError(
                "A pass over the replacements being checked threw {ExceptionType} {StackTrace}; the next pass runs as usual.",
                exception.GetType().FullName,
                exception.StackTrace);
        }
    }
}
