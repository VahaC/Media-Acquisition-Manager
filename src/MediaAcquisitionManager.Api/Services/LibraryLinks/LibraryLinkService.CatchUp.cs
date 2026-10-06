using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Abstractions.Library;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Services.LibraryLinks;

// MAM-34 — the catch-up (Vasyl's decision of 2026-10-06): the links set before the app wrote series and episodes, and
// before it asked the library to look a written item up, brought to today's rules — on the owner's click only. A series'
// link and a numbered episode's kept in the app are written now; a written film link gets the lookup it never had. A
// link a catch-up couldn't write — the library unreachable, TMDb not answering — is counted again, so the next click
// takes it back. The background writer does it all, one link at a time, each with its own audit rows (Queue/Worker,
// as "Accept all confident"); the links list's Pending filter follows it.
public sealed partial class LibraryLinkService
{
    /// <inheritdoc/>
    public async Task<LibraryLinkCatchUpResponse> ReadCatchUpAsync(CancellationToken cancellationToken = default)
    {
        var (toWrite, toLookUp) = await CatchUpAsync(cancellationToken);
        return new LibraryLinkCatchUpResponse(toWrite.Count, toLookUp.Count);
    }

    /// <inheritdoc/>
    public async Task<LibraryLinkCatchUpResponse> StartCatchUpAsync(CancellationToken cancellationToken = default)
    {
        var (toWrite, toLookUp) = await CatchUpAsync(cancellationToken);
        // From here on the request's token is no longer taken: links half queued would be queued all the same at the next
        // signal, but the answer would say otherwise.
        var now = time.GetUtcNow().UtcDateTime;
        var written = await QueueAsync(toWrite, LibraryLinkWriteState.KeptInApp, now) + await QueueAsync(toWrite, LibraryLinkWriteState.NotWritten, now);
        var lookedUp = await QueueAsync(toLookUp, LibraryLinkWriteState.Written, now) + await QueueAsync(toLookUp, LibraryLinkWriteState.InLibrary, now);
        if (written + lookedUp > 0)
            writeQueue.Signal();

        // Information: counts only — what the owner's click queued.
        logger.LogInformation("The TMDb links' catch-up queued {Written} links to write and {LookedUp} to look up.", written, lookedUp);
        return new LibraryLinkCatchUpResponse(written, lookedUp);
    }

    /// <summary>
    /// The links the catch-up writes — those that write ids of their own under today's rules
    /// (<see cref="LibraryLinkWritability"/>) and the library doesn't hold yet: kept in the app under the first rules, or
    /// not written (a catch-up's write that failed among them) — and those it asks the library to look up — written or in
    /// the library, never looked up. Only links whose item the library still reports (attached), in a library switched on
    /// (R12: one switched off is never written; its links wait for it, and a click once it is on). An episode's link TMDb
    /// already said has no IMDb id is left alone: asking again would keep it in the app all the same; one whose IMDb id is
    /// still to be asked counts only while a catalog is connected, as without one its write could only fail.
    /// </summary>
    /// <remarks>
    /// The candidates are read in one query and judged in memory by the library's own capability (ADR 0005), which no
    /// SQL can ask; there are as many as there are links, a few hundred in the reference library.
    /// </remarks>
    private async Task<(List<Guid> ToWrite, List<Guid> ToLookUp)> CatchUpAsync(CancellationToken cancellationToken)
    {
        var candidates = await db.LibraryItemLinks
            .AsNoTracking()
            .Where(link => link.MediaWorkId != null
                && (link.WriteState == LibraryLinkWriteState.KeptInApp
                    || link.WriteState == LibraryLinkWriteState.NotWritten
                    || ((link.WriteState == LibraryLinkWriteState.Written || link.WriteState == LibraryLinkWriteState.InLibrary)
                        && link.LookupRequestedUtc == null))
                && db.IntegrationConnections.Any(connection => connection.Id == link.IntegrationConnectionId && connection.IsEnabled))
            .OrderBy(link => link.Id)
            .ToListAsync(cancellationToken);
        var connections = await ConnectionsAsync([.. candidates.Select(link => link.IntegrationConnectionId).Distinct()], cancellationToken);
        bool? catalogConnected = null;

        var toWrite = new List<Guid>();
        var toLookUp = new List<Guid>();
        foreach (var link in candidates)
        {
            if (!connections.TryGetValue(link.IntegrationConnectionId, out var connection))
                continue;
            var writer = adapters.Find<IMediaLibraryIdentifierWriter>(connection.AdapterType);
            if (link.WriteState is LibraryLinkWriteState.KeptInApp or LibraryLinkWriteState.NotWritten)
            {
                if (LibraryLinkWritability.KeptReason(link, writer, connection.Name) is not null)
                    continue;
                // The writer asks TMDb for the episode's IMDb id first (LibraryLinkWriter).
                if (link is { Target: LibraryLinkTarget.ShowEpisode, EpisodeImdbId: null }
                    && !(catalogConnected ??= await catalogs.HasCatalogAsync(cancellationToken)))
                    continue;
                toWrite.Add(link.Id);
            }
            else if (LibraryLinkWritability.ItemReason(writer, connection.Name, link.ItemKind) is null)
            {
                toLookUp.Add(link.Id);
            }
        }

        return (toWrite, toLookUp);
    }

    /// <summary>
    /// Queues those of <paramref name="linkIds"/> still in <paramref name="state"/> for the background writer (R13: the
    /// rows are the queue): a write a link got meanwhile — a Retry, a removal's claim — is left to it. A kept or not
    /// written link's sentence goes with it; the writer gives it a new one.
    /// </summary>
    private async Task<int> QueueAsync(List<Guid> linkIds, LibraryLinkWriteState state, DateTime now) =>
        linkIds.Count == 0
            ? 0
            : await db.LibraryItemLinks
                // The ids as one JSON parameter (EF.Parameter, the LibrarySnapshotStore rule): a catch-up may queue every
                // link of the library, past SQLite's limit on the number of parameters.
                .Where(link => EF.Parameter(linkIds).Contains(link.Id)
                    && link.WriteState == state
                    && (state == LibraryLinkWriteState.KeptInApp || state == LibraryLinkWriteState.NotWritten || link.LookupRequestedUtc == null))
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(link => link.WriteState, LibraryLinkWriteState.Queued)
                        .SetProperty(link => link.WriteError, (string?)null)
                        .SetProperty(link => link.WriteStateUtc, now),
                    CancellationToken.None);
}
