using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Abstractions.Library;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Texts;
using MediaAcquisitionManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Services.LibraryLinks;

/// <summary>
/// MAM-34 — settles at startup what a stop left of the links' identifications: a link still <c>Writing</c> was cut by the
/// stop, and the links still <c>Queued</c> wait for the background writer.
/// </summary>
/// <remarks>
/// <para>
/// An identification a stop cut may have landed, or not, and may still be running inside the library (Jellyfin's Apply
/// carries on after the app that sent it is gone). A link that is identified under today's rules
/// (<see cref="LibraryLinkWritability"/>: a link to the work on a kind of item its library identifies, an episode's link with
/// its numbers on a film item) becomes <c>NotWritten</c> with <see cref="RestartedError"/>: Retry reads the item first and
/// sends nothing when it already shows the match. Nothing is recorded or put back — the app restores nothing since 0.2.1
/// (ADR 0026) — and the link's identified time is kept, as the restart proves nothing about the item.
/// </para>
/// <para>
/// Any other <c>Writing</c> link was never identified by rule — a claim a restart cut before it settled <c>KeptInApp</c> — and
/// goes back to <c>KeptInApp</c> without an error.
/// </para>
/// <para>
/// Before the worker starts (<c>HostedServiceCollectionExtensions</c>), and only at startup, when nothing can be
/// identifying: the links queued are signalled once, and the worker takes them from the rows (R13).
/// </para>
/// </remarks>
public sealed class LibraryLinkWriteReconciler(
    IServiceScopeFactory scopes,
    LibraryLinkWriteQueue queue,
    TimeProvider time,
    ILogger<LibraryLinkWriteReconciler> logger) : IHostedService
{
    /// <summary>
    /// MAM-47 (ADR 0042) — the key of <see cref="RestartedError"/>: the repair (<c>LibraryLinkService.CatchUp</c>) leaves a
    /// link with this error alone, so the key is what marks an identification a restart cut.
    /// </summary>
    internal const string RestartedKey = "library-links.write.restarted";

    /// <summary>
    /// What an identification a restart cut leaves as the link's error. Value-less, so it is stored as the same JSON every
    /// time and the repair's query compares the column with it in SQL.
    /// </summary>
    internal static readonly Message RestartedError = Message.Of(RestartedKey);

    public Task StartAsync(CancellationToken cancellationToken) => ReconcileAsync(cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>The startup pass; tests call it directly, as the test host runs no hosted services.</summary>
    public async Task ReconcileAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var adapters = scope.ServiceProvider.GetRequiredService<IIntegrationAdapterRegistry>();
        var now = time.GetUtcNow().UtcDateTime;

        // One transaction: the cut links' new states go together, or none does.
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var cut = await db.LibraryItemLinks
            .AsNoTracking()
            .Where(link => link.WriteState == LibraryLinkWriteState.Writing)
            .OrderBy(link => link.Id)
            .ToListAsync(cancellationToken);
        List<Guid> connectionIds = [.. cut.Select(link => link.IntegrationConnectionId).Distinct()];
        var connections = await db.IntegrationConnections
            .AsNoTracking()
            .Where(connection => connectionIds.Contains(connection.Id))
            .ToDictionaryAsync(connection => connection.Id, cancellationToken);
        // The writer's own rule (LibraryLinkWritability): only a link that is identified by rule was ever sent to its library.
        List<Guid> interruptedIds =
        [
            .. cut
                .Where(link => connections.TryGetValue(link.IntegrationConnectionId, out var connection)
                    && LibraryLinkWritability.KeptReason(link, adapters.Find<IMediaLibraryItemIdentifier>(connection.AdapterType), connection.Name) is null)
                .Select(link => link.Id),
        ];

        var interrupted = await db.LibraryItemLinks
            .Where(link => link.WriteState == LibraryLinkWriteState.Writing && interruptedIds.Contains(link.Id))
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(link => link.WriteState, LibraryLinkWriteState.NotWritten)
                    .SetProperty(link => link.WriteError, RestartedError)
                    .SetProperty(link => link.WriteStateUtc, now),
                cancellationToken);
        var neverIdentified = await db.LibraryItemLinks
            .Where(link => link.WriteState == LibraryLinkWriteState.Writing)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(link => link.WriteState, LibraryLinkWriteState.KeptInApp)
                    .SetProperty(link => link.WriteError, (Message?)null)
                    .SetProperty(link => link.WriteStateUtc, now),
                cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var queued = await db.LibraryItemLinks.CountAsync(link => link.WriteState == LibraryLinkWriteState.Queued, cancellationToken);
        if (queued > 0)
            queue.Signal();

        // Information: what a restart interrupted is worth knowing, and nothing is broken. Counts only.
        if (interrupted > 0 || neverIdentified > 0 || queued > 0)
        {
            logger.LogInformation(
                "TMDb links at startup: {Interrupted} identifications a restart cut marked not written, {NeverIdentified} links never identified by rule kept in the app, {Queued} queued links left to identify.",
                interrupted, neverIdentified, queued);
        }
    }
}
