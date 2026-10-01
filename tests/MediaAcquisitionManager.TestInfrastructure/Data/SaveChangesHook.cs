using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace MediaAcquisitionManager.TestInfrastructure.Data;

/// <summary>
/// A save interceptor the test host attaches to every DbContext (MAM-8). Unarmed it does nothing; armed
/// with <see cref="BeforeNextSave"/>, it runs a test's code once, just before the first save that
/// matches — the deterministic way to make a race happen: another request's write landing between a
/// service's check and its insert.
/// </summary>
/// <remarks>
/// The app attaches every <see cref="ISaveChangesInterceptor"/> it finds in the container
/// (<c>PersistenceInfrastructureExtensions</c>), so registering this one changes nothing else about the
/// host. Disarmed by the factory's <c>ResetAsync</c>.
/// </remarks>
public sealed class SaveChangesHook : SaveChangesInterceptor
{
    private Armed? _armed;

    /// <summary>
    /// Runs <paramref name="action"/> once, just before the next save of a context for which
    /// <paramref name="when"/> is true, then disarms itself — so the action's own save, through a context
    /// of its own, passes untouched.
    /// </summary>
    public void BeforeNextSave(Func<DbContext, bool> when, Func<CancellationToken, Task> action) =>
        Volatile.Write(ref _armed, new Armed(when, action));

    /// <summary>Disarms the hook.</summary>
    public void Reset() => Volatile.Write(ref _armed, null);

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        await RunArmedAsync(eventData.Context, cancellationToken);
        return result;
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        RunArmedAsync(eventData.Context, CancellationToken.None).GetAwaiter().GetResult();
        return result;
    }

    private async Task RunArmedAsync(DbContext? context, CancellationToken cancellationToken)
    {
        if (context is null || Volatile.Read(ref _armed) is not { } armed || !armed.When(context))
            return;
        // Claimed before it runs: two saves matching at once run it once.
        if (Interlocked.CompareExchange(ref _armed, null, armed) != armed)
            return;
        await armed.Action(cancellationToken);
    }

    private sealed record Armed(Func<DbContext, bool> When, Func<CancellationToken, Task> Action);
}
