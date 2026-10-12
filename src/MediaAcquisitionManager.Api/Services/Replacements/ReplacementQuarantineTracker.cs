using MediaAcquisitionManager.Api.Services.CopyRemovals;

namespace MediaAcquisitionManager.Api.Services.Replacements;

/// <summary>
/// MAM-26 — moves the old copies of the verified replacements into the quarantine, in the background (ADR 0030): a pass
/// right after the app starts, then one <see cref="PassInterval"/> after each pass ends. Only the loop: each pass is
/// <see cref="IReplacementQuarantiner"/>'s, in a scope of its own — MAM-40 — then <see cref="ICopyRemovalRecovery"/>'s,
/// which settles the owner's removals of a copy a restart cut and sends the notices they owe, in a scope of its own too.
/// </summary>
/// <remarks>
/// <para>
/// A loop of its own rather than a step of <see cref="ReplacementVerificationTracker"/>'s pass: the check of the new
/// files never moves anything, and one bug in either can't stop the other. The rows are the queue: a <c>Verified</c>
/// replacement is taken up on the next pass, and a job a restart cut is found there and settled.
/// </para>
/// <para>
/// The wait comes after a pass, not on a fixed beat, so two passes never overlap, however long one takes. A pass that
/// throws is logged and the next one runs as usual. Nothing is kept here between passes: a restart resumes where the rows
/// are.
/// </para>
/// </remarks>
public sealed class ReplacementQuarantineTracker(
    IServiceScopeFactory scopes,
    TimeProvider time,
    ILogger<ReplacementQuarantineTracker> logger) : BackgroundService
{
    /// <summary>
    /// The pause between passes, as the check's (<see cref="ReplacementVerificationTracker.PassInterval"/>): a pass that
    /// has nothing to move is a query or two, and a verified job waits at most this long for its move. A constant, not a
    /// setting (ADR 0014's rule for tuned numbers).
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
    /// Runs one pass over the replacements to move, in a scope of its own, and returns once it is over. The loop above
    /// calls it; tests drive it directly, as the test host runs no hosted services.
    /// </summary>
    public async Task RunOnceAsync(CancellationToken stoppingToken)
    {
        await using (var scope = scopes.CreateAsyncScope())
        {
            try
            {
                await scope.ServiceProvider.GetRequiredService<IReplacementQuarantiner>().QuarantineAsync(stoppingToken);
            }
            // A shutdown is not an error: the rows are as the last write left them, and the next start's first pass goes on
            // from there. Anything else is a bug. Error, with the type and the stack, never the message.
            catch (Exception exception) when (!(exception is OperationCanceledException && stoppingToken.IsCancellationRequested))
            {
                logger.LogError(
                    "A pass over the replacements to move threw {ExceptionType} {StackTrace}; the next pass runs as usual.",
                    exception.GetType().FullName,
                    exception.StackTrace);
            }
        }

        // MAM-40 — then the owner's removals of a copy a restart cut, and the notices they owe: in a scope of their own, so a
        // bug in either pass can't stop the other, and a shutdown during the first leaves the second for the next pass.
        if (stoppingToken.IsCancellationRequested)
            return;
        await using (var scope = scopes.CreateAsyncScope())
        {
            try
            {
                await scope.ServiceProvider.GetRequiredService<ICopyRemovalRecovery>().SettleAsync(stoppingToken);
            }
            catch (Exception exception) when (!(exception is OperationCanceledException && stoppingToken.IsCancellationRequested))
            {
                logger.LogError(
                    "A pass over the cut copy removals threw {ExceptionType} {StackTrace}; the next pass runs as usual.",
                    exception.GetType().FullName,
                    exception.StackTrace);
            }
        }
    }
}
