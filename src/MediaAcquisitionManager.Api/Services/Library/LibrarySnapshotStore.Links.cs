using MediaAcquisitionManager.Api.Services.LibraryLinks;
using MediaAcquisitionManager.Core.Abstractions.Library;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Library;
using MediaAcquisitionManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Services.Library;

// Re-applying the owner's TMDb links at every sync (MAM-34): a link is the owner's word, not a library fact, so the
// sync never stores it as one — it follows its item to whatever work the item lands on, counts in the grouping like the
// library's own ids, and learns from what the library reported whether the library holds it.
public sealed partial class LibrarySnapshotStore
{
    /// <summary>
    /// MAM-34 — the key an episode-to-show link with its numbers is grouped under: the show with the season and the
    /// episode, so two copies of one episode are one work while the eleven Planet Earth films linked to one show stay
    /// eleven. Never a <see cref="MediaIdentifierScheme"/> name, so it can't meet a registry id, nor the key of a
    /// series' own episodes (<see cref="SeriesPositionKeys"/>): an item the library calls a film stays apart from the
    /// library's episodes (ADR 0017).
    /// </summary>
    private const string LinkedShowKey = "LinkedShowTmdbTv";

    /// <summary>
    /// MAM-34 — the links of <paramref name="integrationId"/>'s items with their file identities, tracked, and the time
    /// the run <paramref name="runId"/> started reading the library: what a sync judges the links against.
    /// </summary>
    /// <remarks>
    /// Read inside the sync's transaction, like the snapshot: a link saved while the library was being read is applied
    /// rather than overwritten by a copy from before. Ordered by first-seen time, then id, so every pass over them goes
    /// the same way.
    /// </remarks>
    private async Task<SnapshotLinks> LoadLinksAsync(Guid runId, Guid integrationId, DateTime now, CancellationToken cancellationToken)
    {
        var links = await db.LibraryItemLinks
            .AsTracking()
            .Where(link => link.IntegrationConnectionId == integrationId)
            .OrderBy(link => link.CreatedUtc)
            .ThenBy(link => link.Id)
            .ToListAsync(cancellationToken);
        var files = await db.LibraryItemLinkFiles
            .AsTracking()
            .Where(file => db.LibraryItemLinks.Any(link => link.Id == file.LibraryItemLinkId && link.IntegrationConnectionId == integrationId))
            .ToListAsync(cancellationToken);
        var readStartedUtc = await db.LibrarySyncRuns
            .AsNoTracking()
            .Where(run => run.Id == runId)
            .Select(run => run.StartedUtc)
            .SingleOrDefaultAsync(cancellationToken);
        return new SnapshotLinks(db, links, files, readStartedUtc, now);
    }

    /// <summary>
    /// MAM-34 — one sync's work on the links of its library's items, in the order <see cref="ApplyAsync"/> calls it:
    /// <see cref="Reattach"/> the links whose item is gone, <see cref="JudgeWrites"/>, group by the
    /// <see cref="Effective"/> items with <see cref="ShowEpisodeKeys"/>, upsert the <see cref="Original"/> items,
    /// <see cref="Attach"/> each link to its item's work and <see cref="RefreshIdentities"/>. Every change is made on
    /// tracked rows and saved by the sync's one <c>SaveChangesAsync</c>, so a sync that fails changes no link either.
    /// </summary>
    /// <remarks>
    /// The sync stays GET-only toward the library: nothing here writes a link into it — that is the owner's Save,
    /// Retry or Write again, never a sync's.
    /// </remarks>
    private sealed class SnapshotLinks(
        ApplicationDbContext db,
        List<LibraryItemLinkEntity> links,
        List<LibraryItemLinkFileEntity> files,
        DateTime? readStartedUtc,
        DateTime now)
    {
        private readonly Dictionary<Guid, HashSet<FileIdentity>> _identities = files
            .GroupBy(file => file.LibraryItemLinkId)
            .ToDictionary(link => link.Key, link => link.Select(file => new FileIdentity(file.DeviceId, file.Inode, file.SizeBytes)).ToHashSet());

        // Filled by Effective: each linked item as the library reported it, by its id.
        private readonly Dictionary<string, LibraryItem> _originals = new(StringComparer.Ordinal);

        // Filled by Effective: the link of each item that has one, by the item's id.
        private Dictionary<string, LibraryItemLinkEntity> _linkOfItem = new(StringComparer.Ordinal);

        /// <summary>How many links the library's items have.</summary>
        public int Count => links.Count;

        /// <summary>How many links this sync moved to an item that came back under a new id.</summary>
        public int ReattachedCount { get; private set; }

        /// <summary>How many links whose item is gone stayed unattached although files of theirs are in the library: the
        /// files lead to several items, or to an item another link holds or claims too (R1).</summary>
        public int AmbiguousCount { get; private set; }

        /// <summary>
        /// Moves each link whose item the library no longer reports to the item that came back with its files (decision
        /// 10): an item of the link's kind whose stored files hold one of the link's identities — a series through its
        /// episodes' files (R3) — decided as a whole by <see cref="LinkReattachment"/> (R1). Done before anything is
        /// grouped, so a moved link counts in this sync's grouping already.
        /// </summary>
        /// <remarks>
        /// The stored files are the previous sync's names with what the inspection read since: a re-created item's own
        /// new names carry no identity until the inspection this sync queues has looked at them, so such a link moves
        /// right after that inspection (<c>LibraryLinkReattacher</c>) or, failing that, at the next sync, here.
        /// </remarks>
        public void Reattach(IReadOnlyList<LibraryItem> resolved, IReadOnlyList<PhysicalFileEntity> storedFiles)
        {
            var items = ItemsById(resolved);
            var gone = links.Where(link => !items.ContainsKey(link.SourceItemId)).ToList();
            if (gone.Count == 0)
                return;

            var holders = new Dictionary<(MediaWorkKind Kind, FileIdentity Identity), HashSet<string>>();
            foreach (var file in storedFiles)
            {
                if (IdentityOf(file) is not { } identity || file.SourceItemId is not { } itemId || !items.TryGetValue(itemId, out var item))
                    continue;
                Hold(holders, (item.Kind, identity), itemId);
                // R3 — a series item has no files of its own: its episodes' files are its.
                if (item.Episode is { SeriesSourceItemId: { } seriesItemId } && items.TryGetValue(seriesItemId, out var series) && series.Kind == MediaWorkKind.Series)
                    Hold(holders, (MediaWorkKind.Series, identity), seriesItemId);
            }

            var unattached = gone
                .Select(link => (link.Id, Holders: (IReadOnlySet<string>)IdentitiesOf(link)
                    .SelectMany(identity => holders.GetValueOrDefault((link.ItemKind, identity)) ?? [])
                    .ToHashSet(StringComparer.Ordinal)))
                .ToList();
            var linkedItems = links.Where(link => items.ContainsKey(link.SourceItemId)).Select(link => link.SourceItemId).ToHashSet(StringComparer.Ordinal);
            var moves = LinkReattachment.Moves(unattached, linkedItems);

            foreach (var link in gone)
            {
                if (moves.TryGetValue(link.Id, out var itemId))
                    link.SourceItemId = itemId;
            }

            ReattachedCount = moves.Count;
            AmbiguousCount = unattached.Count(link => link.Holders.Count > 0 && !moves.ContainsKey(link.Id));
        }

        /// <summary>
        /// R5 — what this sync's read says of each link whose item it read, by the link's written key
        /// (<see cref="LibraryLinkEdits.WrittenKey"/>: a link to the work, its TMDb id; MAM-34 — an episode's link, the
        /// episode's own IMDb id): the library holding it is <see cref="LibraryLinkWriteState.InLibrary"/>; a link the
        /// library held (written, or read there before) that it now reports without — no id, the replaced one, or a
        /// third — is <see cref="LibraryLinkWriteState.Lost"/> ("Write again"); anything else stays as it is.
        /// </summary>
        /// <remarks>
        /// <para>
        /// R4 — a link whose state changed at or after the moment this run started reading is left alone: its write may
        /// have reached the library after the item was read, and the read can't say what the library holds now. The next
        /// sync judges it. A link that writes nothing of its own — an episode's without its IMDb id — has nothing to
        /// judge; nor has a link kept in the app (<see cref="LibraryLinkWriteState.KeptInApp"/>: an episode item's in
        /// Jellyfin, any of a library type that can't set ids, a series' or an episode's set before the app wrote them),
        /// which stays so whatever the library reports — the id it replaced keeps giving way (R6) — until it is written. Nor
        /// has a link waiting for the background writer (<see cref="LibraryLinkWriteState.Queued"/>): its write, and the
        /// lookup that follows it, are still to come — taken out of the queue as "in the library", the link would never
        /// get the lookup the owner queued it for (the catch-up). The writer settles it; the next sync judges it.
        /// </para>
        /// <para>
        /// R9 — a link being written or put back (<see cref="LibraryLinkWriteState.Writing"/>) is its claimer's to settle,
        /// however long ago it was claimed: its write may still be on its way, and a removal deletes only the link it still
        /// holds the claim of, recording what it sent back in the same transaction. Taken from under it, the removal would
        /// answer 409 with its restore sent and unrecorded. A claim a restart cut is settled by the reconciler; the next
        /// sync judges what came of it.
        /// </para>
        /// </remarks>
        public void JudgeWrites(IReadOnlyList<LibraryItem> resolved)
        {
            var items = ItemsById(resolved);
            foreach (var link in links)
            {
                if (link.WriteState is LibraryLinkWriteState.Writing or LibraryLinkWriteState.KeptInApp or LibraryLinkWriteState.Queued
                    || LibraryLinkEdits.WrittenKey(link) is not { } key
                    || !items.TryGetValue(link.SourceItemId, out var item)
                    || !MaySeeWriteOf(link))
                    continue;

                // The first value of the scheme, as the snapshot stores it (SnapshotUpserts: the first fact of a key wins).
                var libraryValue = item.Identifiers
                    .FirstOrDefault(identifier => identifier.Scheme == key.Scheme && !string.IsNullOrWhiteSpace(identifier.Value))
                    ?.Value;
                if (libraryValue is not null && SameValue(libraryValue, key.Value))
                {
                    if (link.WriteState != LibraryLinkWriteState.InLibrary || link.WriteError is not null)
                        SetState(link, LibraryLinkWriteState.InLibrary, error: null);
                }
                else if (link.WriteState is LibraryLinkWriteState.Written or LibraryLinkWriteState.InLibrary)
                {
                    SetState(link, LibraryLinkWriteState.Lost, link.WriteError);
                }
            }
        }

        /// <summary>
        /// <paramref name="resolved"/> as the grouping sees it: each linked item with the ids it counts as holding once
        /// its link applies (<see cref="LinkedIdentifiers.Effective"/>), every other item as it is.
        /// </summary>
        /// <remarks>
        /// Copies, never the items themselves: a group's items are what <see cref="SnapshotUpserts.UpsertItem"/> writes,
        /// and a link's ids must never be stored as the library's — <see cref="Original"/> gives each item back as the
        /// library reported it. Called after <see cref="JudgeWrites"/>: whether a replaced id still gives way depends on
        /// the state this sync decided (R6).
        /// </remarks>
        public List<LibraryItem> Effective(IReadOnlyList<LibraryItem> resolved)
        {
            _linkOfItem = links
                .GroupBy(link => link.SourceItemId, StringComparer.Ordinal)
                .ToDictionary(item => item.Key, item => item.First(), StringComparer.Ordinal);

            var effective = new List<LibraryItem>(resolved.Count);
            foreach (var item in resolved)
            {
                if (!_linkOfItem.TryGetValue(item.SourceItemId, out var link))
                {
                    effective.Add(item);
                    continue;
                }

                _originals.TryAdd(item.SourceItemId, item);
                var facts = new ItemLinkFacts(link.Target, link.Scheme, link.Value, link.SupersededValue, link.ImdbId, link.WriteState);
                effective.Add(item with { Identifiers = LinkedIdentifiers.Effective(item.Identifiers, facts) });
            }

            return effective;
        }

        /// <summary>The item the library reported, for an item of <see cref="Effective"/>.</summary>
        public LibraryItem Original(LibraryItem item) => _originals.GetValueOrDefault(item.SourceItemId) ?? item;

        /// <summary>
        /// The grouping keys of an episode-to-show link that names its season and episode (<see cref="LinkedShowKey"/>);
        /// none for any other item: a link to a show alone says nothing of which episode the item is.
        /// </summary>
        public IEnumerable<(string Key, string Value)> ShowEpisodeKeys(LibraryItem item) =>
            _linkOfItem.TryGetValue(item.SourceItemId, out var link)
            && link is { Target: LibraryLinkTarget.ShowEpisode, SeasonNumber: { } season, EpisodeNumber: { } episode }
                ? [(LinkedShowKey, $"{link.Value.Trim()}/{season}/{episode}")]
                : [];

        /// <summary>
        /// Points each link at the work its item landed on this sync (<paramref name="workOfItem"/>, every kind), or at
        /// none while the library reports no such item ("unattached": kept, to be re-attached or removed by the owner).
        /// </summary>
        public void Attach(IReadOnlyDictionary<string, Guid> workOfItem)
        {
            foreach (var link in links)
                link.MediaWorkId = workOfItem.TryGetValue(link.SourceItemId, out var workId) ? workId : null;
        }

        /// <summary>
        /// Adds to each attached link the identities of its item's stored files that it doesn't hold yet — a series'
        /// through its episodes (R3) — so it can find the item again should the library re-create it (decision 10).
        /// Identities are only ever added: a file that was away for one inspection (a mount) must not cost the link its
        /// way back.
        /// </summary>
        public int RefreshIdentities(IReadOnlyList<LibraryItem> resolved, IReadOnlyList<PhysicalFileEntity> storedFiles)
        {
            var items = ItemsById(resolved);
            var identitiesOfItem = storedFiles
                .Where(file => file.SourceItemId is not null && IdentityOf(file) is not null)
                .GroupBy(file => file.SourceItemId!, StringComparer.Ordinal)
                .ToDictionary(item => item.Key, item => item.Select(file => IdentityOf(file)!.Value).ToList(), StringComparer.Ordinal);
            var episodesOfSeries = resolved
                .Where(item => item.Kind == MediaWorkKind.Episode && item.Episode?.SeriesSourceItemId is not null)
                .ToLookup(item => item.Episode!.SeriesSourceItemId!, item => item.SourceItemId, StringComparer.Ordinal);

            var added = 0;
            foreach (var link in links)
            {
                if (!items.TryGetValue(link.SourceItemId, out var item))
                    continue;
                IEnumerable<string> holders = item.Kind == MediaWorkKind.Series ? episodesOfSeries[item.SourceItemId] : [item.SourceItemId];
                if (!_identities.TryGetValue(link.Id, out var held))
                    _identities[link.Id] = held = [];
                foreach (var identity in holders.SelectMany(holder => identitiesOfItem.GetValueOrDefault(holder) ?? []))
                {
                    if (!held.Add(identity))
                        continue;
                    db.LibraryItemLinkFiles.Add(new LibraryItemLinkFileEntity
                    {
                        LibraryItemLinkId = link.Id,
                        DeviceId = identity.DeviceId,
                        Inode = identity.Inode,
                        SizeBytes = identity.SizeBytes,
                    });
                    added++;
                }
            }

            return added;
        }

        // R4 — whether this sync's read can have seen the link's last change: one made before the run started reading.
        // Without a start time (never, for a running run) nothing is judged.
        private bool MaySeeWriteOf(LibraryItemLinkEntity link) =>
            readStartedUtc is { } started && (link.WriteStateUtc is not { } changed || changed < started);

        private void SetState(LibraryItemLinkEntity link, LibraryLinkWriteState state, string? error)
        {
            link.WriteState = state;
            link.WriteError = error;
            link.WriteStateUtc = now;
        }

        private IEnumerable<FileIdentity> IdentitiesOf(LibraryItemLinkEntity link) =>
            _identities.GetValueOrDefault(link.Id) ?? [];

        private static void Hold(Dictionary<(MediaWorkKind, FileIdentity), HashSet<string>> holders, (MediaWorkKind, FileIdentity) key, string itemId)
        {
            if (!holders.TryGetValue(key, out var items))
                holders[key] = items = new HashSet<string>(StringComparer.Ordinal);
            items.Add(itemId);
        }

        // The first item of an id wins, as everywhere in the snapshot.
        private static Dictionary<string, LibraryItem> ItemsById(IReadOnlyList<LibraryItem> items) =>
            items.GroupBy(item => item.SourceItemId, StringComparer.Ordinal).ToDictionary(item => item.Key, item => item.First(), StringComparer.Ordinal);

        // A file's identity as the inspection last read it (R2: the size too, as inodes are reused); null while unknown.
        private static FileIdentity? IdentityOf(PhysicalFileEntity file) =>
            file is { DeviceId: { } deviceId, Inode: { } inode, ReadSizeBytes: { } sizeBytes } ? new FileIdentity(deviceId, inode, sizeBytes) : null;

        private static bool SameValue(string first, string second) =>
            string.Equals(first.Trim(), second.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>MAM-34 — what a link keeps of one of its item's files (R2).</summary>
    private readonly record struct FileIdentity(long DeviceId, long Inode, long SizeBytes);
}
