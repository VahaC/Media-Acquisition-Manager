using MediaAcquisitionManager.Api.Services.Audit;
using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Abstractions.Library;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Services.LibraryLinks;

/// <summary>
/// MAM-34 — settles at startup what a stop left of the links' writes: a link still <c>Writing</c> was cut by the stop,
/// and the links still <c>Queued</c> wait for the background writer.
/// </summary>
/// <remarks>
/// <para>
/// A write cut by a stop may have landed, or not (R10). A link that writes ids of its own (<see cref="LibraryLinkWritability"/>:
/// a link to the work on a kind of item its library writes, an episode's link with its numbers on a film item) becomes
/// <c>NotWritten</c> with <see cref="RestartedError"/> — Retry reads the item again first — and a removal's restore that
/// was cut leaves its link in place to be removed again. Its write is recorded first, as a <c>LibraryIdsWritten</c> row
/// of what it meant to leave (<see cref="LibraryLinkWriteHistory.CutWriteAsync"/>), unless one already stands for it: a
/// removal or a replace later starts from that row, whatever a sync or a Retry makes of the link meanwhile. Recording a
/// write that never landed costs nothing — a restore only takes back what the item still holds of what the app wrote
/// (decision 13). An episode's link whose IMDb id TMDb hadn't given yet sent nothing of its own — the id is kept on the
/// link before anything is sent —, so no write is recorded for it.
/// </para>
/// <para>
/// An episode's link that writes no id of its own, only ever <c>Writing</c> while what the app wrote for the item is put
/// back, becomes <c>KeptInApp</c> with <see cref="RestartedRestoreError"/>: Retry has nothing to write for it, and
/// removing it puts the item back. A link to the work its library never writes — an episode item's in Jellyfin, any link
/// of a library type that can't set ids — is <c>Writing</c> only while a removal holds it, which sends nothing: it goes
/// back to <c>KeptInApp</c> as it was, with no write recorded for it.
/// </para>
/// <para>
/// Before the worker starts (<c>HostedServiceCollectionExtensions</c>), and only at startup, when nothing can be writing:
/// the links queued are signalled once, and the worker takes them from the rows (R13).
/// </para>
/// </remarks>
public sealed class LibraryLinkWriteReconciler(
    IServiceScopeFactory scopes,
    LibraryLinkWriteQueue queue,
    TimeProvider time,
    ILogger<LibraryLinkWriteReconciler> logger) : IHostedService
{
    internal const string RestartedError = "The app restarted while writing the ids into the library. Retry.";

    internal const string RestartedRestoreError =
        "The app restarted while putting back the ids it had written into the library. Remove the link to put them back.";

    public Task StartAsync(CancellationToken cancellationToken) => ReconcileAsync(cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>The startup pass; tests call it directly, as the test host runs no hosted services.</summary>
    public async Task ReconcileAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var auditTrail = scope.ServiceProvider.GetRequiredService<IAuditTrail>();
        var adapters = scope.ServiceProvider.GetRequiredService<IIntegrationAdapterRegistry>();
        var now = time.GetUtcNow().UtcDateTime;

        // One transaction: a cut write's row and its link's new state go together, or neither does.
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
        // The writer's own rule (LibraryLinkWritability): only a link that writes ids of its own was ever sent as a write.
        var cutWrites = cut
            .Where(link => connections.TryGetValue(link.IntegrationConnectionId, out var connection)
                && LibraryLinkWritability.KeptReason(link, adapters.Find<IMediaLibraryIdentifierWriter>(connection.AdapterType), connection.Name) is null)
            .ToList();
        List<Guid> neverWrittenIds = [.. cut.Except(cutWrites).Where(link => link.Target == LibraryLinkTarget.Work).Select(link => link.Id)];
        List<Guid> cutWriteIds = [.. cutWrites.Select(link => link.Id)];
        var recorded = 0;
        // An episode's link whose IMDb id TMDb hadn't given yet sent nothing of its own: nothing to record.
        foreach (var link in cutWrites.Where(link => LibraryLinkEdits.WrittenKey(link) is not null))
        {
            if (await LibraryLinkWriteHistory.CutWriteAsync(db, link, cancellationToken) is not { } identifiers)
                continue;
            // R13's actor: whoever set the link the write was for.
            var actor = link.SetByUserId is { } userId ? new AuditActor(userId, link.SetByUserName) : null;
            auditTrail.Record(AuditAction.LibraryIdsWritten, actor, AuditTarget.LibraryLink(link.Id, LibraryLinkWriter.TargetName(link)), identifiers);
            recorded++;
        }

        await db.SaveChangesAsync(cancellationToken);
        var writes = await db.LibraryItemLinks
            .Where(link => link.WriteState == LibraryLinkWriteState.Writing && cutWriteIds.Contains(link.Id))
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(link => link.WriteState, LibraryLinkWriteState.NotWritten)
                    .SetProperty(link => link.WriteError, RestartedError)
                    .SetProperty(link => link.WriteStateUtc, now),
                cancellationToken);
        var neverWritten = await db.LibraryItemLinks
            .Where(link => link.WriteState == LibraryLinkWriteState.Writing && neverWrittenIds.Contains(link.Id))
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(link => link.WriteState, LibraryLinkWriteState.KeptInApp)
                    .SetProperty(link => link.WriteError, (string?)null)
                    .SetProperty(link => link.WriteStateUtc, now),
                cancellationToken);
        var restores = await db.LibraryItemLinks
            .Where(link => link.WriteState == LibraryLinkWriteState.Writing)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(link => link.WriteState, LibraryLinkWriteState.KeptInApp)
                    .SetProperty(link => link.WriteError, RestartedRestoreError)
                    .SetProperty(link => link.WriteStateUtc, now),
                cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var queued = await db.LibraryItemLinks.CountAsync(link => link.WriteState == LibraryLinkWriteState.Queued, cancellationToken);
        if (queued > 0)
            queue.Signal();

        // Information: what a restart interrupted is worth knowing, and nothing is broken. Counts only.
        if (writes > 0 || neverWritten > 0 || restores > 0 || queued > 0)
        {
            logger.LogInformation(
                "TMDb links at startup: {Writes} writes a restart cut marked not written ({Recorded} recorded as possibly sent), {NeverWritten} links never written kept in the app, {Restores} restores a restart cut kept in the app, {Queued} queued links left to write.",
                writes, recorded, neverWritten, restores, queued);
        }
    }
}
