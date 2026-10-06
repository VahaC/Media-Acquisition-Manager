using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Api.Mapping;
using MediaAcquisitionManager.Api.Services.Audit;
using MediaAcquisitionManager.Api.Services.Catalog;
using MediaAcquisitionManager.Api.Services.Library;
using MediaAcquisitionManager.Api.Services.Recommendations;
using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Abstractions.Library;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Services.LibraryLinks;

/// <inheritdoc cref="ILibraryLinkService"/>
/// <remarks>
/// Split by facet: the dialog and what every facet shares here; setting and "Accept all confident" in
/// <c>LibraryLinkService.Set.cs</c>, removal in <c>.Remove.cs</c>, writing again in <c>.Write.cs</c>, the TMDb page's
/// lists in <c>.Lists.cs</c>.
/// </remarks>
public sealed partial class LibraryLinkService(
    ApplicationDbContext db,
    ILibraryWorkReader workReader,
    ILibraryLinkMapper linkMapper,
    ILibraryLinkSuggestionReader suggestionReader,
    ICatalogConnector catalogs,
    ICatalogStore catalogStore,
    IIntegrationAdapterRegistry adapters,
    ILibraryLinkWriter writer,
    LibraryLinkWriteQueue writeQueue,
    IAuditTrail auditTrail,
    IRecommendationRunService recommendations,
    TimeProvider time,
    ILogger<LibraryLinkService> logger) : ILibraryLinkService
{
    /// <summary>R9 — one write at a time per work: a second one would race the first for the same library items.</summary>
    internal const string UnderWayError = "A write to the library is under way for this work; try again in a moment.";

    internal const string NoItemsError = "No library reports this work, so there is nothing to link.";

    /// <summary>The longest part of a request echoed back in a sentence.</summary>
    private const int EchoLength = 64;

    public async Task<LibraryLinkResult> ReadDialogAsync(Guid workId, CancellationToken cancellationToken = default) =>
        await DialogAsync(workId, cancellationToken) is { } dialog ? LibraryLinkResult.Succeeded(dialog) : LibraryLinkResult.NotFound;

    // ── the dialog ───────────────────────────────────────────────────────────────────────────────

    /// <summary>The work's dialog as it stands now; <c>null</c> when the work is gone.</summary>
    private async Task<LibraryLinkDialogResponse?> DialogAsync(Guid workId, CancellationToken cancellationToken)
    {
        var work = await db.MediaWorks.AsNoTracking().SingleOrDefaultAsync(row => row.Id == workId, cancellationToken);
        if (work is null)
            return null;

        // The card exactly as the Library page shows it — the owner's, as only the owner opens this.
        var card = (await workReader.ReadAsync([work], forOwner: true, cancellationToken: cancellationToken)).Single();
        var items = await ItemsOfAsync(work.Id, cancellationToken);
        var links = await LinksOfAsync(work.Id, items, tracking: false, cancellationToken);
        var rows = await LibraryRowsOfAsync(work.Id, cancellationToken);

        // A link on the work whose item no longer reports a fact of it (a sync is about to move or unattach it) still
        // shows, with what the link remembers of its item.
        var stray = links.Where(link => !items.Any(item => item.Holds(link))).ToList();
        var strayConnections = await ConnectionsAsync([.. stray.Select(link => link.IntegrationConnectionId)], cancellationToken);
        var shown = items
            .Concat(stray
                .Where(link => strayConnections.ContainsKey(link.IntegrationConnectionId))
                .Select(link => new WorkItem(strayConnections[link.IntegrationConnectionId], link.SourceItemId, link.ItemTitle)))
            .ToList();

        var itemDtos = shown
            .Select(item =>
            {
                var link = links.FirstOrDefault(item.Holds);
                var (canWrite, reason) = WriteAbility(item.Connection, work.Kind);
                return linkMapper.ToItemDto(
                    item.SourceItemId,
                    item.Connection.Name,
                    item.Title,
                    work.Kind,
                    canWrite,
                    // An item that takes ids may still hold a link that writes none: an episode's link says why.
                    reason ?? (link is null ? null : EpisodeKeptReason(link)),
                    link,
                    [.. rows[(item.Connection.Id, item.SourceItemId)]]);
            })
            .ToList();

        var suggestions = await suggestionReader.ReadAsync(work.Id, cancellationToken);
        // The free search starts from the work's title cleaned as the preparation's queries are, so the owner's first
        // Search finds what the preparation would: the library's raw title ("120. Landing Stripling") finds nothing.
        var (searchTitle, searchYear) = LibraryLinkWorkTitles.SearchOf(
            await db.MediaWorkTitles.AsNoTracking().Where(title => title.MediaWorkId == work.Id).ToListAsync(cancellationToken));
        return new LibraryLinkDialogResponse(
            card, itemDtos, await catalogs.HasCatalogAsync(cancellationToken), suggestions.Suggestions, suggestions.Search, searchTitle, searchYear);
    }

    // ── what every facet shares ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// The work's library items: each (integration, item id) whose library reported a title, an id or a version of the
    /// work, while the integration still exists, ordered by the integration's name and the item's id. With the title
    /// the item gave, for a link's <c>ItemTitle</c>.
    /// </summary>
    private async Task<List<WorkItem>> ItemsOfAsync(Guid workId, CancellationToken cancellationToken)
    {
        var titles = await db.MediaWorkTitles
            .AsNoTracking()
            .Where(title => title.MediaWorkId == workId && title.SourceKind == ProvenanceSourceKind.MediaLibrary
                && title.SourceConnectionId != null && title.SourceItemId != null)
            .ToListAsync(cancellationToken);
        var identifiers = await db.MediaWorkIdentifiers
            .AsNoTracking()
            .Where(identifier => identifier.MediaWorkId == workId && identifier.SourceKind == ProvenanceSourceKind.MediaLibrary
                && identifier.SourceConnectionId != null && identifier.SourceItemId != null)
            .Select(identifier => new { ConnectionId = identifier.SourceConnectionId!.Value, ItemId = identifier.SourceItemId! })
            .ToListAsync(cancellationToken);
        var versions = await db.MediaVersions
            .AsNoTracking()
            .Where(version => version.MediaWorkId == workId && version.SourceKind == ProvenanceSourceKind.MediaLibrary
                && version.SourceConnectionId != null && version.SourceItemId != null)
            .Select(version => new { ConnectionId = version.SourceConnectionId!.Value, ItemId = version.SourceItemId! })
            .ToListAsync(cancellationToken);

        var reported = titles
            .Select(title => (ConnectionId: title.SourceConnectionId!.Value, ItemId: title.SourceItemId!))
            .Concat(identifiers.Select(identifier => (identifier.ConnectionId, identifier.ItemId)))
            .Concat(versions.Select(version => (version.ConnectionId, version.ItemId)))
            .Distinct()
            .ToList();
        var connections = await ConnectionsAsync([.. reported.Select(item => item.ConnectionId).Distinct()], cancellationToken);
        return
        [
            .. reported
                .Where(item => connections.ContainsKey(item.ConnectionId))
                .Select(item => new WorkItem(
                    connections[item.ConnectionId],
                    item.ItemId,
                    TitleOf([.. titles.Where(title => title.SourceConnectionId == item.ConnectionId && title.SourceItemId == item.ItemId)])))
                .OrderBy(item => item.Connection.Name, StringComparer.Ordinal)
                .ThenBy(item => item.SourceItemId, StringComparer.Ordinal),
        ];
    }

    /// <summary>The links of the work's items, and any other link sitting on the work.</summary>
    private async Task<List<LibraryItemLinkEntity>> LinksOfAsync(Guid workId, IReadOnlyList<WorkItem> items, bool tracking, CancellationToken cancellationToken)
    {
        List<string> itemIds = [.. items.Select(item => item.SourceItemId).Distinct()];
        var query = db.LibraryItemLinks.Where(link => link.MediaWorkId == workId || itemIds.Contains(link.SourceItemId));
        var links = await (tracking ? query.AsTracking() : query.AsNoTracking()).ToListAsync(cancellationToken);
        return [.. links.Where(link => link.MediaWorkId == workId || items.Any(item => item.Holds(link))).OrderBy(link => link.SourceItemId, StringComparer.Ordinal)];
    }

    /// <summary>The ids the libraries reported for the work's items: each item's own, by (integration, item id).</summary>
    private async Task<ILookup<(Guid ConnectionId, string ItemId), MediaWorkIdentifierEntity>> LibraryRowsOfAsync(Guid workId, CancellationToken cancellationToken) =>
        (await db.MediaWorkIdentifiers
            .AsNoTracking()
            .Where(identifier => identifier.MediaWorkId == workId && identifier.SourceKind == ProvenanceSourceKind.MediaLibrary
                && identifier.SourceConnectionId != null && identifier.SourceItemId != null)
            .ToListAsync(cancellationToken))
        .ToLookup(identifier => (identifier.SourceConnectionId!.Value, identifier.SourceItemId!));

    private async Task<Dictionary<Guid, IntegrationConnectionEntity>> ConnectionsAsync(List<Guid> connectionIds, CancellationToken cancellationToken) =>
        connectionIds.Count == 0
            ? []
            : await db.IntegrationConnections
                .AsNoTracking()
                .Where(connection => connectionIds.Contains(connection.Id))
                .ToDictionaryAsync(connection => connection.Id, cancellationToken);

    /// <summary>
    /// Whether ids would be written into an item of <paramref name="workKind"/> in its library, and why not: a library
    /// type without the capability keeps its links in the app (ADR 0005), and so does one for a kind of item it doesn't
    /// write — the library's own sentence (Jellyfin: an episode item); a library switched off is never written (R12).
    /// </summary>
    private (bool CanWrite, string? Reason) WriteAbility(IntegrationConnectionEntity connection, MediaWorkKind workKind)
    {
        if (LibraryLinkWritability.ItemReason(adapters.Find<IMediaLibraryIdentifierWriter>(connection.AdapterType), connection.Name, workKind) is { } reason)
            return (false, reason);
        return connection.IsEnabled ? (true, null) : (false, LibraryLinkWriter.SwitchedOffError(connection.Name));
    }

    /// <summary>MAM-34 — why an episode's link on an item that takes ids writes none: without its numbers, or with an
    /// episode TMDb gives no IMDb id; <c>null</c> for any other link.</summary>
    private static string? EpisodeKeptReason(LibraryItemLinkEntity link) =>
        LibraryLinkWritability.LinkReason(link.Target, link.SeasonNumber, link.EpisodeNumber)
        ?? (LibraryLinkWritability.KnowsNoEpisodeImdb(link) ? LibraryLinkWritability.NoEpisodeImdbReason : null);

    /// <summary>The work's display title as its card shows it — an audit row's name of its links.</summary>
    private async Task<string?> WorkTitleAsync(Guid workId, CancellationToken cancellationToken) =>
        TitleOf(await db.MediaWorkTitles.AsNoTracking().Where(title => title.MediaWorkId == workId).ToListAsync(cancellationToken));

    // The card's rule (LibraryMapper): the first display title in title order, else the first original one.
    private static string? TitleOf(IReadOnlyList<MediaWorkTitleEntity> titles) =>
        (LibraryMapper.FirstInTitleOrder(titles, MediaTitleKind.Display) ?? LibraryMapper.FirstInTitleOrder(titles, MediaTitleKind.Original))?.Text;

    private Task<AuditActor> ActorAsync(Guid userId, CancellationToken cancellationToken) =>
        db.Users.Where(user => user.Id == userId).Select(user => new AuditActor(user.Id, user.UserName)).SingleAsync(cancellationToken);

    /// <summary>
    /// The conditional claim of a link for a write or a removal (R9): only while it is as it was read — not
    /// <c>Writing</c>, under the same attempt — so of two callers only one wins. The new attempt id, or <c>null</c> when
    /// the link changed meanwhile.
    /// </summary>
    private async Task<Guid?> ClaimAsync(LibraryItemLinkEntity link)
    {
        var attemptId = Guid.NewGuid();
        var claimed = await db.LibraryItemLinks
            .Where(row => row.Id == link.Id
                && row.WriteState == link.WriteState
                && row.WriteState != LibraryLinkWriteState.Writing
                && row.WriteAttemptId == link.WriteAttemptId)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(row => row.WriteState, LibraryLinkWriteState.Writing)
                    .SetProperty(row => row.WriteAttemptId, attemptId)
                    .SetProperty(row => row.WriteStateUtc, time.GetUtcNow().UtcDateTime),
                CancellationToken.None) == 1;
        return claimed ? attemptId : null;
    }

    /// <summary>
    /// A link change may change which catalog works the library holds: the recommendations are computed again. Best
    /// effort — the change is saved, and a run that can't be queued is logged, not failed (the MAM-33 trigger rule).
    /// </summary>
    private async Task QueueRecommendationsAsync(Guid ownerUserId)
    {
        try
        {
            await recommendations.EnsureQueuedAsync(RecommendationRunTrigger.LinkChanged, ownerUserId, CancellationToken.None);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "A TMDb link was changed, but no recommendation run could be queued after it.");
        }
    }

    private static string Echo(string value) =>
        value.Length <= EchoLength ? value : string.Concat(value.AsSpan(0, EchoLength - 1), "…");

    /// <summary>One library item of a work: its integration, its id there, and the title it gave.</summary>
    private sealed record WorkItem(IntegrationConnectionEntity Connection, string SourceItemId, string? Title)
    {
        public bool Holds(LibraryItemLinkEntity link) =>
            link.IntegrationConnectionId == Connection.Id && string.Equals(link.SourceItemId, SourceItemId, StringComparison.Ordinal);
    }
}
