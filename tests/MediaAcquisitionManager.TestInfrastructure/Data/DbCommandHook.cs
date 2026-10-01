using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace MediaAcquisitionManager.TestInfrastructure.Data;

/// <summary>
/// A command interceptor the test host attaches to every DbContext (MAM-12). Unarmed it does nothing;
/// armed with <see cref="BeforeNextCommand"/>, it runs a test's code once, just before the first command
/// that matches is sent — the point where a service has started a read. It proves two reads overlap: the
/// search page reads the library while the release sources are being asked, so a test holds the first
/// command reading <c>MediaWorkTitles</c> until a source's request arrives, and that request until the
/// command arrives; one after the other, either would wait in vain. <see cref="SaveChangesHook"/> and
/// <see cref="TransactionHook"/> can't: a read neither saves nor opens a transaction.
/// </summary>
/// <remarks>
/// <para>
/// The app attaches only save interceptors from the container (<c>PersistenceInfrastructureExtensions</c>),
/// so the factory adds this one to the context's options itself, beside <see cref="TransactionHook"/>.
/// Every kind of command is seen — a query, a scalar, a statement — synchronous or not. Disarmed by the
/// factory's <c>ResetAsync</c>, before it empties the tables, so its own deletes never meet a hook a test
/// left armed.
/// </para>
/// <para>
/// Several can be armed at once, each for its own commands, and each runs once: a test can hold two reads
/// until both have started — the library's and the sources' rows, which two contexts send side by side
/// and one context refuses to (EF's "a second operation was started on this context"). Arming one more
/// never replaces one armed before, so a test that arms one is unchanged.
/// </para>
/// </remarks>
public sealed class DbCommandHook : DbCommandInterceptor
{
    private readonly Lock _gate = new();
    private readonly List<Armed> _armed = [];

    /// <summary>
    /// Runs <paramref name="action"/> once, just before the next command for which <paramref name="when"/>
    /// is true is sent (<see cref="DbCommand.CommandText"/> is the SQL, say), then disarms it — so a
    /// command the action causes, through a context of its own, passes untouched. Beside every action
    /// armed before and not yet run; a command that several would match runs the first armed of them
    /// only. Whatever <paramref name="action"/> throws, the command throws instead of being sent.
    /// </summary>
    public void BeforeNextCommand(Func<DbCommand, bool> when, Func<CancellationToken, Task> action)
    {
        lock (_gate)
            _armed.Add(new Armed(when, action));
    }

    /// <summary>Disarms every action not yet run.</summary>
    public void Reset()
    {
        lock (_gate)
            _armed.Clear();
    }

    public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        await RunArmedAsync(command, cancellationToken);
        return result;
    }

    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
    {
        RunArmedAsync(command, CancellationToken.None).GetAwaiter().GetResult();
        return result;
    }

    public override async ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<object> result,
        CancellationToken cancellationToken = default)
    {
        await RunArmedAsync(command, cancellationToken);
        return result;
    }

    public override InterceptionResult<object> ScalarExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<object> result)
    {
        RunArmedAsync(command, CancellationToken.None).GetAwaiter().GetResult();
        return result;
    }

    public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        await RunArmedAsync(command, cancellationToken);
        return result;
    }

    public override InterceptionResult<int> NonQueryExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
    {
        RunArmedAsync(command, CancellationToken.None).GetAwaiter().GetResult();
        return result;
    }

    private async Task RunArmedAsync(DbCommand command, CancellationToken cancellationToken)
    {
        Armed armed;
        lock (_gate)
        {
            var index = _armed.FindIndex(candidate => candidate.When(command));
            if (index < 0)
                return;
            // Claimed before it runs, and outside the lock while it does: two commands matching at once run
            // it once, and an action that waits for another command doesn't hold that command back.
            armed = _armed[index];
            _armed.RemoveAt(index);
        }

        await armed.Action(cancellationToken);
    }

    private sealed record Armed(Func<DbCommand, bool> When, Func<CancellationToken, Task> Action);
}
