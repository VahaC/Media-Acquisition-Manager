using MediaAcquisitionManager.Core.Abstractions.Library;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Library;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Services.Library;

// Which work each group of items lands on (ADR 0008): the work its items were on before when they can,
// so a work keeps its id and first-seen time across syncs, and a new work only for what is new.
public sealed partial class LibrarySnapshotStore
{
    /// <summary>The key an episode's position in its series is compared under (MAM-32): never a
    /// <see cref="MediaIdentifierScheme"/> name, so it can't meet a registry id.</summary>
    private const string PositionKey = "Position";

    /// <summary>
    /// A work for every group of <paramref name="groups"/>, in their order: an existing one where the
    /// rule below finds one, else a new work, added to the context and to
    /// <see cref="PreviousWorks.Created"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A group's candidates are (a) the works its items' facts sat on before this sync ("item
    /// continuity", counted per item) and (b) the works of this connection that held one of the group's
    /// keys and whose keys don't contradict the group's — no scheme with another value ("identifier
    /// carry-over"), so a file upgraded or renamed under a new library item id but the same TMDb id keeps
    /// its work. The keys are the registry ids, and MAM-32 adds an episode's position: its series work
    /// with its season and episode numbers (<paramref name="positionsOf"/>), the identity ADR 0017 gives
    /// an episode beside its own ids. A work holding any other source's fact is never a candidate: a sync
    /// only ever writes works of its own source (ADR 0008); nor is a work of another kind (MAM-32), so an
    /// item the library now calls an episode never lands on the movie it was.
    /// </para>
    /// <para>
    /// Global and deterministic: every (group, work) pair is scored by (continuity, shared keys) and
    /// assigned greedily from the best, ties to the older work (first-seen time, then id), then to the
    /// earlier group — never by the order the library listed its items. Each work goes to one group at
    /// most, so when a work's items split, the group holding most of them keeps it.
    /// </para>
    /// </remarks>
    private Guid[] AssignWorks(
        IReadOnlyList<LibraryWorkGroup> groups,
        MediaWorkKind kind,
        ExistingSnapshot existing,
        PreviousWorks previous,
        Func<LibraryItem, IEnumerable<(string Scheme, string Value)>> positionsOf,
        DateTime now)
    {
        // Item continuity: the works each item's facts sat on — normally one, but a previous sync's
        // split or bad stored data may have left an item's facts on several. MAM-35 — not its poster, which sits
        // where its other facts do and so adds nothing.
        var worksByItem = existing.Titles.Select(title => (title.SourceItemId, title.MediaWorkId))
            .Concat(existing.Identifiers.Select(identifier => (identifier.SourceItemId, identifier.MediaWorkId)))
            .Concat(existing.Versions.Select(version => (version.SourceItemId, version.MediaWorkId)))
            .Concat(existing.Positions.Select(position => (position.SourceItemId, position.MediaWorkId)))
            .Where(fact => fact.SourceItemId is not null)
            .GroupBy(fact => fact.SourceItemId!, StringComparer.Ordinal)
            .ToDictionary(item => item.Key, item => item.Select(fact => fact.MediaWorkId).ToHashSet(), StringComparer.Ordinal);

        // Key carry-over: what each work held from this connection, compared the way the grouping
        // compares (trimmed, any letter case).
        var keysByWork = existing.Identifiers
            .Select(identifier => (identifier.MediaWorkId, Scheme: identifier.Scheme.ToString(), identifier.Value))
            .Concat(existing.Positions
                .Where(position => position.SeriesWorkId is not null && position.SeasonNumber is not null && position.EpisodeNumber is not null)
                .Select(position => (position.MediaWorkId, Scheme: PositionKey, Value: PositionValue(position.SeriesWorkId!.Value, position.SeasonNumber!.Value, position.EpisodeNumber!.Value))))
            .GroupBy(key => key.MediaWorkId)
            .ToDictionary(work => work.Key, work => Keys(work.Select(key => (key.Scheme, key.Value))));
        var worksByKey = keysByWork
            .SelectMany(work => work.Value.Select(key => (Key: key, WorkId: work.Key)))
            .GroupBy(pair => pair.Key)
            .ToDictionary(key => key.Key, key => key.Select(pair => pair.WorkId).ToList());

        var candidates = new List<WorkCandidate>();
        for (var index = 0; index < groups.Count; index++)
        {
            var items = groups[index].Items;
            var groupKeys = Keys(items
                .SelectMany(item => item.Identifiers.Select(identifier => (identifier.Scheme.ToString(), identifier.Value)).Concat(positionsOf(item))));

            var continuity = new Dictionary<Guid, int>();
            foreach (var item in items)
            {
                if (!worksByItem.TryGetValue(item.SourceItemId, out var works))
                    continue;
                foreach (var work in works)
                    continuity[work] = continuity.GetValueOrDefault(work) + 1;
            }

            var carriedOver = groupKeys
                .SelectMany(key => worksByKey.GetValueOrDefault(key) ?? [])
                .Where(work => AreCompatible(groupKeys, keysByWork[work]));

            foreach (var work in continuity.Keys.Union(carriedOver))
            {
                if (previous.Shared.Contains(work) || !previous.Works.TryGetValue(work, out var stored) || stored.Kind != kind)
                    continue;
                var sharedKeys = keysByWork.TryGetValue(work, out var held) ? groupKeys.Count(held.Contains) : 0;
                candidates.Add(new WorkCandidate(index, work, continuity.GetValueOrDefault(work), sharedKeys, stored.CreatedUtc));
            }
        }

        var workIds = new Guid[groups.Count];
        var assigned = new bool[groups.Count];
        foreach (var candidate in candidates
            .OrderByDescending(candidate => candidate.Continuity)
            .ThenByDescending(candidate => candidate.SharedKeys)
            .ThenBy(candidate => candidate.WorkCreatedUtc)
            .ThenBy(candidate => candidate.WorkId)
            .ThenBy(candidate => candidate.GroupIndex))
        {
            if (assigned[candidate.GroupIndex] || !previous.Taken.Add(candidate.WorkId))
                continue;
            workIds[candidate.GroupIndex] = candidate.WorkId;
            assigned[candidate.GroupIndex] = true;
        }

        for (var index = 0; index < groups.Count; index++)
        {
            if (assigned[index])
                continue;
            var work = new MediaWorkEntity { Kind = kind, CreatedUtc = now };
            db.MediaWorks.Add(work);
            workIds[index] = work.Id;
            previous.Created.Add(work.Id);
        }

        return workIds;
    }

    /// <summary>
    /// The works this connection's facts sat on before the sync, with their kind and first-seen time,
    /// and those of them that also hold another source's fact.
    /// </summary>
    private async Task<PreviousWorks> LoadPreviousWorksAsync(Guid integrationId, ExistingSnapshot existing, CancellationToken cancellationToken)
    {
        var workIds = existing.WorkIds.ToList();
        var works = await db.MediaWorks
            .Where(work => EF.Parameter(workIds).Contains(work.Id))
            .ToDictionaryAsync(work => work.Id, work => (work.CreatedUtc, work.Kind), cancellationToken);
        return new PreviousWorks(works, await SharedWorksAsync(integrationId, workIds, cancellationToken));
    }

    /// <summary>
    /// Those of <paramref name="workIds"/> that also hold a title, an identifier, a version or an episode
    /// position some other source reported: another integration, or a source of another kind.
    /// </summary>
    /// <remarks>
    /// The ids go as one JSON parameter (<c>EF.Parameter</c>), here and wherever the store passes every
    /// work of a snapshot: EF Core 10's default of one parameter per id would run a large library into
    /// SQLite's limit on the number of parameters.
    /// </remarks>
    private async Task<HashSet<Guid>> SharedWorksAsync(Guid integrationId, List<Guid> workIds, CancellationToken cancellationToken)
    {
        // MAM-35 — not the posters: another source's poster sits beside that source's other facts, which are asked here.
        var shared = await db.MediaWorkTitles
            .Where(title => EF.Parameter(workIds).Contains(title.MediaWorkId)
                && (title.SourceConnectionId != integrationId || title.SourceKind != ProvenanceSourceKind.MediaLibrary))
            .Select(title => title.MediaWorkId)
            .Union(db.MediaWorkIdentifiers
                .Where(identifier => EF.Parameter(workIds).Contains(identifier.MediaWorkId)
                    && (identifier.SourceConnectionId != integrationId || identifier.SourceKind != ProvenanceSourceKind.MediaLibrary))
                .Select(identifier => identifier.MediaWorkId))
            .Union(db.MediaVersions
                .Where(version => EF.Parameter(workIds).Contains(version.MediaWorkId)
                    && (version.SourceConnectionId != integrationId || version.SourceKind != ProvenanceSourceKind.MediaLibrary))
                .Select(version => version.MediaWorkId))
            .Union(db.MediaEpisodePositions
                .Where(position => EF.Parameter(workIds).Contains(position.MediaWorkId)
                    && (position.SourceConnectionId != integrationId || position.SourceKind != ProvenanceSourceKind.MediaLibrary))
                .Select(position => position.MediaWorkId))
            .ToListAsync(cancellationToken);
        return [.. shared];
    }

    /// <summary>MAM-32 — an episode's position as a key: its series work and its numbers.</summary>
    private static string PositionValue(Guid seriesWorkId, int seasonNumber, int episodeNumber) =>
        $"{seriesWorkId:N}/{seasonNumber}/{episodeNumber}";

    // Blank values name nothing, as in the grouping.
    private static HashSet<(string Scheme, string Value)> Keys(IEnumerable<(string Scheme, string Value)> keys) =>
    [
        .. keys
            .Select(key => (key.Scheme, Value: key.Value.Trim().ToUpperInvariant()))
            .Where(key => key.Value.Length > 0),
    ];

    // Together they name at most one value per scheme. A group whose own items disagree never is.
    private static bool AreCompatible(HashSet<(string Scheme, string Value)> group, HashSet<(string Scheme, string Value)> work) =>
        group.Concat(work).Distinct().GroupBy(key => key.Scheme, StringComparer.Ordinal).All(scheme => scheme.Count() == 1);

    /// <summary>One (group, work) pair the assignment may choose, with its score.</summary>
    private readonly record struct WorkCandidate(int GroupIndex, Guid WorkId, int Continuity, int SharedKeys, DateTime WorkCreatedUtc);

    /// <summary>
    /// The works before the sync, and what the assignment does with them across the kinds: the ones
    /// already given to a group (<see cref="Taken"/>) and the ones made for this sync
    /// (<see cref="Created"/>).
    /// </summary>
    private sealed record PreviousWorks(Dictionary<Guid, (DateTime CreatedUtc, MediaWorkKind Kind)> Works, HashSet<Guid> Shared)
    {
        public HashSet<Guid> Taken { get; } = [];

        public List<Guid> Created { get; } = [];
    }
}
