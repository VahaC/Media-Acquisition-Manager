using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Abstractions.Library;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Texts;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Services.LibraryLinks;

// MAM-34 — the repair (the TMDb page's "Update Jellyfin"; Vasyl's decisions of 2026-10-06, ADR 0026): every link the library
// has not been confirmed to show yet — those set before the app identified items, and any whose identification was refused
// or failed before anything was sent — queued for the background writer on the owner's click, with progress. Each is an
// Identify of its own, with its own audit row; a link whose library already shows the match is found so and sent nothing.
// The route keeps its name (api/library/tmdb/catch-up).
public sealed partial class LibraryLinkService
{
    /// <inheritdoc/>
    public async Task<LibraryLinkCatchUpResponse> ReadCatchUpAsync(CancellationToken cancellationToken = default) =>
        new((await RepairCandidatesAsync(cancellationToken)).Count, await ProgressAsync(cancellationToken));

    /// <inheritdoc/>
    public async Task<LibraryLinkCatchUpResponse> StartCatchUpAsync(CancellationToken cancellationToken = default)
    {
        var candidates = await RepairCandidatesAsync(cancellationToken);
        if (candidates.Count == 0)
            return new LibraryLinkCatchUpResponse(0, null);

        // From here on the request's token is no longer taken: links half queued would be queued all the same at the next
        // signal, but the answer would say otherwise.
        var batchId = Guid.NewGuid();
        var now = time.GetUtcNow().UtcDateTime;
        var queued = await RepairCandidateLinks()
            // The ids as one JSON parameter (EF.Parameter, the LibrarySnapshotStore rule): a repair may queue every link of
            // the library, past SQLite's limit on the number of parameters. The candidate rules are asked again in SQL, so a
            // link a Retry, a removal's delete or the worker took meanwhile is left to it.
            .Where(link => EF.Parameter(candidates).Contains(link.Id))
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(link => link.WriteState, LibraryLinkWriteState.Queued)
                    .SetProperty(link => link.WriteError, (Message?)null)
                    .SetProperty(link => link.WriteStateUtc, now)
                    // The click's id: progress survives a reload, and the links are protected (ProtectHeldIds) — the repair
                    // never replaces an id the library holds on its own.
                    .SetProperty(link => link.BatchId, (Guid?)batchId),
                CancellationToken.None);
        if (queued > 0)
            writeQueue.Signal();

        // Information: counts only — what the owner's click queued.
        logger.LogInformation("The TMDb links' repair {BatchId} queued {Queued} links to identify.", batchId, queued);
        return new LibraryLinkCatchUpResponse(queued, queued > 0 ? new LibraryLinkProgressDto(batchId, queued, 0) : null);
    }

    /// <summary>
    /// The links the repair would identify: attached (the library still reports the item), never confirmed identified
    /// (<see cref="LibraryItemLinkEntity.IdentifiedUtc"/> null), not already queued or being identified, in a library switched
    /// on (R12: one switched off is never identified in; its links wait for it) — and, for a <c>NotWritten</c> link, only
    /// when no Apply was ever sent for its current setting.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A link refused or failed before an Apply (a conflicting id, a library that saves beside the media, a locked item, no
    /// match, the library down, a stopped batch) changed nothing in the library, so trying it again is cheap — one read and
    /// one search — and true: it is "not identified yet". A link whose Apply was sent and not confirmed (the answer lost, a
    /// 5xx, a timeout, only the ids taken) has had its images deleted, and a repeat would send a new Apply that deletes them
    /// again — a series for another ~159 s each time — for an item that keeps failing the confirmation. Those wait for the
    /// owner's own Retry, per link, once he has read the sentence. The <c>LibraryItemIdentified</c> audit row is the app's
    /// only record that an Apply was sent, written whenever one left. Rows older than the link's current setting (<c>SetUtc</c>:
    /// a replaced link) belong to another id and don't count.
    /// </para>
    /// <para>
    /// An identification a restart cut is the one exception to "the audit row says": the row is written only after the Apply's
    /// answer, so a process that died inside an Apply left none, and Jellyfin goes on with that Apply (a series for minutes)
    /// after the app that sent it is gone. Such a link — the reconciler's <c>RestartedError</c> — waits for the owner's own
    /// Retry too, as its sentence says: a repair click right after the restart would send a second Apply into a series still
    /// being refreshed. MAM-47 (ADR 0042) — compared in SQL as the column keeps it: the value-less message goes through the
    /// column's converter, so the query compares the stored JSON of its key, and the startup converter turns an older
    /// build's English into it before any request.
    /// </para>
    /// </remarks>
    private IQueryable<LibraryItemLinkEntity> RepairCandidateLinks() =>
        db.LibraryItemLinks.Where(link => link.MediaWorkId != null
            && link.IdentifiedUtc == null
            && link.WriteState != LibraryLinkWriteState.Queued
            && link.WriteState != LibraryLinkWriteState.Writing
            && db.IntegrationConnections.Any(connection => connection.Id == link.IntegrationConnectionId && connection.IsEnabled)
            && (link.WriteState != LibraryLinkWriteState.NotWritten
                || (link.WriteError != LibraryLinkWriteReconciler.RestartedError
                    && !db.AuditEvents.Any(auditEvent => auditEvent.Action == AuditAction.LibraryItemIdentified
                        && auditEvent.TargetKind == AuditTargetKind.LibraryLink
                        && auditEvent.TargetId == link.Id
                        && auditEvent.CreatedUtc >= link.SetUtc))));

    /// <summary>
    /// The ids of the links the repair identifies: <see cref="RepairCandidateLinks"/> judged in memory by the library's own
    /// capability (ADR 0005), which no SQL can ask — those that are identified by rule today
    /// (<see cref="LibraryLinkWritability"/>). An episode's link TMDb already said has no IMDb id is left alone: asking again
    /// would keep it in the app all the same; one whose IMDb id is still to be asked counts only while a catalog is connected,
    /// as without one its identification could only fail.
    /// </summary>
    /// <remarks>There are as many candidates as there are links, a few hundred in the reference library.</remarks>
    private async Task<List<Guid>> RepairCandidatesAsync(CancellationToken cancellationToken)
    {
        var candidates = await RepairCandidateLinks().AsNoTracking().OrderBy(link => link.Id).ToListAsync(cancellationToken);
        var connections = await ConnectionsAsync([.. candidates.Select(link => link.IntegrationConnectionId).Distinct()], cancellationToken);
        bool? catalogConnected = null;

        var ids = new List<Guid>();
        foreach (var link in candidates)
        {
            if (!connections.TryGetValue(link.IntegrationConnectionId, out var connection))
                continue;
            if (LibraryLinkWritability.KeptReason(link, adapters.Find<IMediaLibraryItemIdentifier>(connection.AdapterType), connection.Name) is not null)
                continue;
            // The writer asks TMDb for the episode's IMDb id first (LibraryLinkWriter).
            if (link is { Target: LibraryLinkTarget.ShowEpisode, EpisodeImdbId: null }
                && !(catalogConnected ??= await catalogs.HasCatalogAsync(cancellationToken)))
                continue;
            ids.Add(link.Id);
        }

        return ids;
    }

    /// <summary>
    /// The batch under way — the one of the link <c>Queued</c> or <c>Writing</c> with a batch whose state changed last (ties
    /// by the batch id) — as done of total: the links carrying its id, and how many of them are no longer queued or being
    /// identified. <c>null</c> when nothing is pending. A link the owner removed meanwhile just leaves the total.
    /// </summary>
    private async Task<LibraryLinkProgressDto?> ProgressAsync(CancellationToken cancellationToken)
    {
        var batchId = await db.LibraryItemLinks
            .AsNoTracking()
            .Where(link => link.BatchId != null && (link.WriteState == LibraryLinkWriteState.Queued || link.WriteState == LibraryLinkWriteState.Writing))
            .OrderByDescending(link => link.WriteStateUtc)
            .ThenBy(link => link.BatchId)
            .Select(link => link.BatchId)
            .FirstOrDefaultAsync(cancellationToken);
        if (batchId is null)
            return null;

        var links = db.LibraryItemLinks.AsNoTracking().Where(link => link.BatchId == batchId);
        var total = await links.CountAsync(cancellationToken);
        var pending = await links.CountAsync(
            link => link.WriteState == LibraryLinkWriteState.Queued || link.WriteState == LibraryLinkWriteState.Writing, cancellationToken);
        return new LibraryLinkProgressDto(batchId.Value, total, total - pending);
    }
}
