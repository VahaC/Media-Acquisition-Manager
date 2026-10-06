using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Library;
using MediaAcquisitionManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Services.LibraryLinks;

/// <inheritdoc cref="ILibraryLinkReattacher"/>
/// <remarks>
/// <para>
/// The same rule as the sync's (<see cref="LinkReattachment"/>), over what is stored now rather than what a sync read:
/// a library's names whose identity an inspection read (device, inode and the size it read — R2), each with the item
/// that reported it and the work its version sits on. A film or an episode is the item of its own names; a series has
/// none, so its episodes' names are its (R3): an episode item's own position names its series work, and the item that
/// reported that series work's titles and ids is the series item.
/// </para>
/// <para>
/// What is stored names an episode's series work, not its series item: when a series work holds several series items of
/// one library (the Penguins' two folders, The Grand Tour's four), which of them an episode belongs to is unknown here,
/// so none of them gets it. A link on such an item is left to the sync, which knows each episode's series item
/// (<c>LibrarySnapshotStore</c>) — handing it the other items' episodes would let it move onto an item none of its files
/// was ever under (decision 10), or leave it unattached for good.
/// </para>
/// <para>
/// A handful of queries over every link and the libraries' identified names (about 1,400 for the reference library),
/// indexed in memory — no query per link or per file.
/// </para>
/// </remarks>
public sealed class LibraryLinkReattacher(ApplicationDbContext db, ILogger<LibraryLinkReattacher> logger) : ILibraryLinkReattacher
{
    public async Task ReattachAsync(CancellationToken cancellationToken = default)
    {
        var links = await db.LibraryItemLinks
            .AsNoTracking()
            .Select(link => new LinkRow(link.Id, link.IntegrationConnectionId, link.SourceItemId, link.ItemKind, link.MediaWorkId))
            .ToListAsync(cancellationToken);
        if (links.Count == 0)
            return;

        var library = await LoadLibraryAsync([.. links.Select(link => link.IntegrationConnectionId).Distinct()], cancellationToken);

        // Moves first, so a link moved now gets its new item's identities as well.
        var (moved, ambiguous) = await MoveUnattachedAsync(links, library, cancellationToken);
        var added = await CopyIdentitiesAsync(moved, library, cancellationToken);

        // Information: counts only — never a title or an id.
        logger.LogInformation(
            "TMDb links after the file inspection: {Reattached} re-attached by their files, {Ambiguous} left unattached as their files lead to no single unlinked item, {Identities} file identities added.",
            moved.Count(link => link.Moved), ambiguous, added);
    }

    // ── re-attaching ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Moves every unattached link the rule allows; gives back every link as it stands afterwards, and how many stayed
    /// unattached although files of theirs are in the library.
    /// </summary>
    private async Task<(List<LinkRow> Links, int Ambiguous)> MoveUnattachedAsync(List<LinkRow> links, StoredLibrary library, CancellationToken cancellationToken)
    {
        var result = new List<LinkRow>(links.Count);
        var ambiguous = 0;
        foreach (var connection in links.GroupBy(link => link.IntegrationConnectionId))
        {
            var unattached = connection
                .Where(link => link.MediaWorkId is null)
                .Select(link => (link.Id, Holders: (IReadOnlySet<string>)library.HoldersOf(connection.Key, link.ItemKind, link.Id)))
                .ToList();
            var linkedItems = connection.Select(link => link.SourceItemId).ToHashSet(StringComparer.Ordinal);
            var moves = LinkReattachment.Moves(unattached, linkedItems);
            ambiguous += unattached.Count(link => link.Holders.Count > 0 && !moves.ContainsKey(link.Id));

            foreach (var link in connection)
            {
                if (moves.TryGetValue(link.Id, out var itemId)
                    && library.WorkOf(connection.Key, link.ItemKind, itemId) is { } workId
                    && await MoveAsync(link, itemId, workId, cancellationToken))
                    result.Add(link with { SourceItemId = itemId, MediaWorkId = workId, Moved = true });
                else
                    result.Add(link);
            }
        }

        return (result, ambiguous);
    }

    /// <summary>
    /// The move itself, only while nothing else changed the link or the item meanwhile: the link is still unattached on
    /// the item it had, no link holds the new item (the one-link-per-item index would refuse it), and the work is still
    /// there (a sync may have deleted it). <c>false</c> when any of that no longer holds; the next sync decides then.
    /// </summary>
    private async Task<bool> MoveAsync(LinkRow link, string itemId, Guid workId, CancellationToken cancellationToken) =>
        await db.LibraryItemLinks
            .Where(row => row.Id == link.Id
                && row.MediaWorkId == null
                && row.SourceItemId == link.SourceItemId
                && !db.LibraryItemLinks.Any(other => other.IntegrationConnectionId == row.IntegrationConnectionId && other.SourceItemId == itemId)
                && db.MediaWorks.Any(work => work.Id == workId))
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(row => row.SourceItemId, itemId)
                    .SetProperty(row => row.MediaWorkId, (Guid?)workId)
                    // A re-created item has its own file-derived metadata: it was never identified, whatever the item the link
                    // left was. Kept, the link would stay out of the repair and the dialog would say "identified" for it.
                    .SetProperty(row => row.IdentifiedUtc, (DateTime?)null),
                cancellationToken) == 1;

    // ── identities ───────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Adds to every attached link the identities of its item's names it doesn't hold yet; only ever adds (a name an
    /// inspection found nothing at — a mount away — keeps the link its way back). Returns how many were added.
    /// </summary>
    private async Task<int> CopyIdentitiesAsync(List<LinkRow> links, StoredLibrary library, CancellationToken cancellationToken)
    {
        var added = new List<LibraryItemLinkFileEntity>();
        foreach (var link in links)
        {
            if (link.MediaWorkId is null)
                continue;
            var held = library.IdentitiesOf(link.Id);
            foreach (var identity in library.FilesOf(link.IntegrationConnectionId, link.ItemKind, link.SourceItemId))
            {
                if (held.Add(identity))
                    added.Add(new LibraryItemLinkFileEntity { LibraryItemLinkId = link.Id, DeviceId = identity.DeviceId, Inode = identity.Inode, SizeBytes = identity.SizeBytes });
            }
        }

        if (added.Count == 0)
            return 0;
        db.LibraryItemLinkFiles.AddRange(added);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return added.Count;
        }
        catch (DbUpdateException exception)
        {
            // A sync or the owner got there first: a sync added one of these identities, or the link was removed. What
            // a sync reads it adds itself, so nothing is lost; Warning, as nothing is broken and nobody need act.
            db.ChangeTracker.Clear();
            logger.LogWarning(exception, "TMDb links after the file inspection: {Identities} file identities were not added, as the links changed meanwhile.", added.Count);
            return 0;
        }
    }

    // ── what is stored ───────────────────────────────────────────────────────────────────────────

    /// <summary>The identified library names of <paramref name="connectionIds"/>, the series each episode work belongs
    /// to, the items each series work holds, and every link's identities.</summary>
    private async Task<StoredLibrary> LoadLibraryAsync(List<Guid> connectionIds, CancellationToken cancellationToken)
    {
        // One JSON parameter (EF.Parameter): the SQLite parameter-limit rule for id lists.
        List<Guid?> sources = [.. connectionIds.Select(id => (Guid?)id)];
        var files = await (
                from version in db.MediaVersions.AsNoTracking()
                join file in db.PhysicalFiles.AsNoTracking() on (Guid?)version.Id equals file.MediaVersionId
                join work in db.MediaWorks.AsNoTracking() on version.MediaWorkId equals work.Id
                where file.SourceKind == ProvenanceSourceKind.MediaLibrary
                    && EF.Parameter(sources).Contains(file.SourceConnectionId)
                    && file.SourceItemId != null
                    && file.DeviceId != null
                    && file.Inode != null
                    && file.ReadSizeBytes != null
                select new
                {
                    ConnectionId = file.SourceConnectionId!.Value,
                    ItemId = file.SourceItemId!,
                    WorkId = version.MediaWorkId,
                    work.Kind,
                    DeviceId = file.DeviceId!.Value,
                    Inode = file.Inode!.Value,
                    SizeBytes = file.ReadSizeBytes!.Value,
                })
            .ToListAsync(cancellationToken);

        // R3 — which series work each episode item's own position names, in those libraries: by the item, not by the
        // episode work, which two copies of one episode under two series items share.
        var positions = await db.MediaEpisodePositions
            .AsNoTracking()
            .Where(position => position.SourceKind == ProvenanceSourceKind.MediaLibrary
                && EF.Parameter(sources).Contains(position.SourceConnectionId)
                && position.SourceItemId != null
                && position.SeriesWorkId != null)
            .Select(position => new { ConnectionId = position.SourceConnectionId!.Value, ItemId = position.SourceItemId!, SeriesWorkId = position.SeriesWorkId!.Value })
            .ToListAsync(cancellationToken);

        // The series items each series work holds: the items that reported its titles or its ids.
        var seriesItems = await db.MediaWorkTitles
            .AsNoTracking()
            .Where(title => title.SourceKind == ProvenanceSourceKind.MediaLibrary
                && EF.Parameter(sources).Contains(title.SourceConnectionId)
                && title.SourceItemId != null
                && db.MediaWorks.Any(work => work.Id == title.MediaWorkId && work.Kind == MediaWorkKind.Series))
            .Select(title => new { ConnectionId = title.SourceConnectionId!.Value, title.MediaWorkId, ItemId = title.SourceItemId! })
            .Concat(db.MediaWorkIdentifiers
                .AsNoTracking()
                .Where(identifier => identifier.SourceKind == ProvenanceSourceKind.MediaLibrary
                    && EF.Parameter(sources).Contains(identifier.SourceConnectionId)
                    && identifier.SourceItemId != null
                    && db.MediaWorks.Any(work => work.Id == identifier.MediaWorkId && work.Kind == MediaWorkKind.Series))
                .Select(identifier => new { ConnectionId = identifier.SourceConnectionId!.Value, identifier.MediaWorkId, ItemId = identifier.SourceItemId! }))
            .ToListAsync(cancellationToken);

        var identities = await db.LibraryItemLinkFiles
            .AsNoTracking()
            .Select(file => new { file.LibraryItemLinkId, file.DeviceId, file.Inode, file.SizeBytes })
            .ToListAsync(cancellationToken);

        return new StoredLibrary(
            [.. files.Select(file => new StoredFile(file.ConnectionId, file.ItemId, file.WorkId, file.Kind, new FileIdentity(file.DeviceId, file.Inode, file.SizeBytes)))],
            positions.ToLookup(position => (position.ConnectionId, position.ItemId), position => position.SeriesWorkId),
            seriesItems
                .DistinctBy(item => (item.ConnectionId, item.MediaWorkId, item.ItemId))
                .ToLookup(item => (item.ConnectionId, item.MediaWorkId), item => item.ItemId),
            identities
                .GroupBy(identity => identity.LibraryItemLinkId)
                .ToDictionary(link => link.Key, link => link.Select(identity => new FileIdentity(identity.DeviceId, identity.Inode, identity.SizeBytes)).ToHashSet()));
    }

    /// <summary>A link as this pass reads and moves it.</summary>
    private sealed record LinkRow(Guid Id, Guid IntegrationConnectionId, string SourceItemId, MediaWorkKind ItemKind, Guid? MediaWorkId, bool Moved = false);

    /// <summary>A file's identity as the inspection last read it (R2).</summary>
    private readonly record struct FileIdentity(long DeviceId, long Inode, long SizeBytes);

    /// <summary>One identified library name: the item that reported it, the work and kind its version sits on.</summary>
    private sealed record StoredFile(Guid ConnectionId, string ItemId, Guid WorkId, MediaWorkKind Kind, FileIdentity Identity);

    /// <summary>
    /// What is stored, indexed for the questions this pass asks: which items of a library and kind hold an identity,
    /// which work an item sits on, which identities an item's names have, and which a link holds.
    /// </summary>
    private sealed class StoredLibrary
    {
        private readonly ILookup<(Guid ConnectionId, MediaWorkKind Kind, FileIdentity Identity), string> _holders;
        private readonly ILookup<(Guid ConnectionId, MediaWorkKind Kind, string ItemId), (Guid WorkId, FileIdentity Identity)> _namesOfItem;
        private readonly Dictionary<Guid, HashSet<FileIdentity>> _identitiesOfLink;

        public StoredLibrary(
            List<StoredFile> files,
            ILookup<(Guid ConnectionId, string EpisodeItemId), Guid> seriesWorksOfEpisode,
            ILookup<(Guid ConnectionId, Guid SeriesWorkId), string> itemsOfSeriesWork,
            Dictionary<Guid, HashSet<FileIdentity>> identitiesOfLink)
        {
            // A film's and an episode's own names, and a series' through its episodes' (R3): an episode item's position
            // names its series work, and the series item is that work's one series item of the library — none when it
            // holds several, as which of them the episode is under isn't stored.
            var names = files
                .Concat(files
                    .Where(file => file.Kind == MediaWorkKind.Episode)
                    .SelectMany(file => seriesWorksOfEpisode[(file.ConnectionId, file.ItemId)]
                        .SelectMany(seriesWorkId => itemsOfSeriesWork[(file.ConnectionId, seriesWorkId)].ToList() is [var itemId]
                            ? [new StoredFile(file.ConnectionId, itemId, seriesWorkId, MediaWorkKind.Series, file.Identity)]
                            : Array.Empty<StoredFile>())))
                .Distinct()
                .ToList();
            _holders = names.ToLookup(name => (name.ConnectionId, name.Kind, name.Identity), name => name.ItemId);
            _namesOfItem = names.ToLookup(name => (name.ConnectionId, name.Kind, name.ItemId), name => (name.WorkId, name.Identity));
            _identitiesOfLink = identitiesOfLink;
        }

        /// <summary>The identities a link holds; a set this pass adds to.</summary>
        public HashSet<FileIdentity> IdentitiesOf(Guid linkId)
        {
            if (!_identitiesOfLink.TryGetValue(linkId, out var held))
                _identitiesOfLink[linkId] = held = [];
            return held;
        }

        /// <summary>The items of <paramref name="kind"/> in the library <paramref name="connectionId"/> whose names hold
        /// one of the identities of the link <paramref name="linkId"/>.</summary>
        public HashSet<string> HoldersOf(Guid connectionId, MediaWorkKind kind, Guid linkId) =>
            _identitiesOfLink.TryGetValue(linkId, out var held)
                ? held.SelectMany(identity => _holders[(connectionId, kind, identity)]).ToHashSet(StringComparer.Ordinal)
                : new HashSet<string>(StringComparer.Ordinal);

        /// <summary>The work the item's names sit on; <c>null</c> when they say none, or several (a guess is no
        /// answer).</summary>
        public Guid? WorkOf(Guid connectionId, MediaWorkKind kind, string itemId) =>
            _namesOfItem[(connectionId, kind, itemId)].Select(name => name.WorkId).Distinct().ToList() is [var workId] ? workId : null;

        /// <summary>The identities of the item's names: a film's or an episode's own, a series' its episodes' (R3) — none
        /// while its series work holds another series item of the library.</summary>
        public IEnumerable<FileIdentity> FilesOf(Guid connectionId, MediaWorkKind kind, string itemId) =>
            _namesOfItem[(connectionId, kind, itemId)].Select(name => name.Identity);
    }
}
