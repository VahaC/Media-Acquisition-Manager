using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace MediaAcquisitionManager.TestInfrastructure.Data;

/// <summary>
/// A transaction interceptor the test host attaches to every DbContext (MAM-11). Unarmed it does nothing;
/// armed with <see cref="BeforeNextTransaction"/>, it runs a test's code once, just before the first
/// transaction that matches begins — the point where a service has made every check and is about to take
/// SQLite's write lock. <see cref="SaveChangesHook"/> can't stage a race there for a service that wraps
/// its save in a transaction of its own (the default destination's, say): that save reaches the save
/// interceptor only inside the transaction, after a first statement has taken the lock, so another
/// request's write would wait for it rather than land first.
/// </summary>
/// <remarks>
/// The app attaches only save interceptors from the container (<c>PersistenceInfrastructureExtensions</c>),
/// so the factory adds this one to the context's options itself. Disarmed by the factory's
/// <c>ResetAsync</c>.
/// </remarks>
public sealed class TransactionHook : DbTransactionInterceptor
{
    private Armed? _armed;

    /// <summary>
    /// Runs <paramref name="action"/> once, just before the next transaction of a context for which
    /// <paramref name="when"/> is true begins, then disarms itself — so a transaction the action causes,
    /// through a context of its own, passes untouched.
    /// </summary>
    public void BeforeNextTransaction(Func<DbContext, bool> when, Func<CancellationToken, Task> action) =>
        Volatile.Write(ref _armed, new Armed(when, action));

    /// <summary>Disarms the hook.</summary>
    public void Reset() => Volatile.Write(ref _armed, null);

    public override async ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(
        DbConnection connection,
        TransactionStartingEventData eventData,
        InterceptionResult<DbTransaction> result,
        CancellationToken cancellationToken = default)
    {
        await RunArmedAsync(eventData.Context, cancellationToken);
        return result;
    }

    public override InterceptionResult<DbTransaction> TransactionStarting(
        DbConnection connection, TransactionStartingEventData eventData, InterceptionResult<DbTransaction> result)
    {
        RunArmedAsync(eventData.Context, CancellationToken.None).GetAwaiter().GetResult();
        return result;
    }

    private async Task RunArmedAsync(DbContext? context, CancellationToken cancellationToken)
    {
        if (context is null || Volatile.Read(ref _armed) is not { } armed || !armed.When(context))
            return;
        // Claimed before it runs: two transactions matching at once run it once.
        if (Interlocked.CompareExchange(ref _armed, null, armed) != armed)
            return;
        await armed.Action(cancellationToken);
    }

    private sealed record Armed(Func<DbContext, bool> When, Func<CancellationToken, Task> Action);
}
