using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Library;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Services.Library;

// Which work each group of items lands on (ADR 0008): the work its items were on before when they can,
// so a work keeps its id and first-seen time across syncs, and a new work only for what is new.
public sealed partial class LibrarySnapshotStore
{
    /// <summary>
    /// A work for every group of <paramref name="groups"/>, in their order: an existing one where the
    /// rule below finds one, else a new work, added to the context.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A group's candidates are (a) the works its items' facts sat on before this sync ("item
    /// continuity", counted per item) and (b) the works of this connection that held one of the group's
    /// identifiers and whose identifiers don't contradict the group's — no scheme with another value
    /// ("identifier carry-over"), so a file upgraded or renamed under a new library item id but the same
    /// TMDb id keeps its work. A work holding any other source's fact is never a candidate: a sync only
    /// ever writes works of its own source (ADR 0008).
    /// </para>
    /// <para>
    /// Global and deterministic: every (group, work) pair is scored by (continuity, shared identifiers)
    /// and assigned greedily from the best, ties to the older work (first-seen time, then id), then to
    /// the earlier group — never by the order the library listed its items. Each work goes to one group
    /// at most, so when a work's items split, the group holding most of them keeps it.
    /// </para>
    /// </remarks>
    private async Task<WorkAssignment> AssignWorksAsync(
        Guid integrationId,
        IReadOnlyList<LibraryWorkGroup> groups,
        ExistingSnapshot existing,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var previous = existing.WorkIds.ToList();
        var shared = await SharedWorksAsync(integrationId, previous, cancellationToken);
        var createdUtc = await db.MediaWorks
            .Where(work => EF.Parameter(previous).Contains(work.Id))
            .ToDictionaryAsync(work => work.Id, work => work.CreatedUtc, cancellationToken);

        // Item continuity: the works each item's facts sat on — normally one, but a previous sync's
        // split or bad stored data may have left an item's facts on several.
        var worksByItem = existing.Titles.Select(title => (title.SourceItemId, title.MediaWorkId))
            .Concat(existing.Identifiers.Select(identifier => (identifier.SourceItemId, identifier.MediaWorkId)))
            .Concat(existing.Versions.Select(version => (version.SourceItemId, version.MediaWorkId)))
            .Where(fact => fact.SourceItemId is not null)
            .GroupBy(fact => fact.SourceItemId!, StringComparer.Ordinal)
            .ToDictionary(item => item.Key, item => item.Select(fact => fact.MediaWorkId).ToHashSet(), StringComparer.Ordinal);

        // Identifier carry-over: what each work held from this connection, compared the way the grouping
        // compares (trimmed, any letter case).
        var identifiersByWork = existing.Identifiers
            .GroupBy(identifier => identifier.MediaWorkId)
            .ToDictionary(work => work.Key, work => IdentifierKeys(work.Select(identifier => (identifier.Scheme, identifier.Value))));
        var worksByIdentifier = identifiersByWork
            .SelectMany(work => work.Value.Select(identifier => (Identifier: identifier, WorkId: work.Key)))
            .GroupBy(pair => pair.Identifier)
            .ToDictionary(identifier => identifier.Key, identifier => identifier.Select(pair => pair.WorkId).ToList());

        var candidates = new List<WorkCandidate>();
        for (var index = 0; index < groups.Count; index++)
        {
            var items = groups[index].Items;
            var groupIdentifiers = IdentifierKeys(items.SelectMany(item => item.Identifiers).Select(identifier => (identifier.Scheme, identifier.Value)));

            var continuity = new Dictionary<Guid, int>();
            foreach (var item in items)
            {
                if (!worksByItem.TryGetValue(item.SourceItemId, out var works))
                    continue;
                foreach (var work in works)
                    continuity[work] = continuity.GetValueOrDefault(work) + 1;
            }

            var carriedOver = groupIdentifiers
                .SelectMany(identifier => worksByIdentifier.GetValueOrDefault(identifier) ?? [])
                .Where(work => AreCompatible(groupIdentifiers, identifiersByWork[work]));

            foreach (var work in continuity.Keys.Union(carriedOver))
            {
                if (shared.Contains(work))
                    continue;
                var sharedIdentifiers = identifiersByWork.TryGetValue(work, out var held) ? groupIdentifiers.Count(held.Contains) : 0;
                candidates.Add(new WorkCandidate(index, work, continuity.GetValueOrDefault(work), sharedIdentifiers, createdUtc[work]));
            }
        }

        var workIds = new Guid[groups.Count];
        var assigned = new bool[groups.Count];
        var taken = new HashSet<Guid>();
        foreach (var candidate in candidates
            .OrderByDescending(candidate => candidate.Continuity)
            .ThenByDescending(candidate => candidate.SharedIdentifiers)
            .ThenBy(candidate => candidate.WorkCreatedUtc)
            .ThenBy(candidate => candidate.WorkId)
            .ThenBy(candidate => candidate.GroupIndex))
        {
            if (assigned[candidate.GroupIndex] || !taken.Add(candidate.WorkId))
                continue;
            workIds[candidate.GroupIndex] = candidate.WorkId;
            assigned[candidate.GroupIndex] = true;
        }

        var created = new List<Guid>();
        for (var index = 0; index < groups.Count; index++)
        {
            if (assigned[index])
                continue;
            var work = new MediaWorkEntity { Kind = groups[index].Items[0].Kind, CreatedUtc = now };
            db.MediaWorks.Add(work);
            workIds[index] = work.Id;
            created.Add(work.Id);
        }

        return new WorkAssignment(workIds, created);
    }

    /// <summary>
    /// Those of <paramref name="workIds"/> that also hold a title, an identifier or a version some other
    /// source reported: another integration, or a source of another kind.
    /// </summary>
    /// <remarks>
    /// The ids go as one JSON parameter (<c>EF.Parameter</c>), here and wherever the store passes every
    /// work of a snapshot: EF Core 10's default of one parameter per id would run a large library into
    /// SQLite's limit on the number of parameters.
    /// </remarks>
    private async Task<HashSet<Guid>> SharedWorksAsync(Guid integrationId, List<Guid> workIds, CancellationToken cancellationToken)
    {
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
            .ToListAsync(cancellationToken);
        return [.. shared];
    }

    // Blank values name nothing, as in the grouping.
    private static HashSet<(MediaIdentifierScheme Scheme, string Value)> IdentifierKeys(IEnumerable<(MediaIdentifierScheme Scheme, string Value)> identifiers) =>
    [
        .. identifiers
            .Select(identifier => (identifier.Scheme, Value: identifier.Value.Trim().ToUpperInvariant()))
            .Where(identifier => identifier.Value.Length > 0),
    ];

    // Together they name at most one value per scheme. A group whose own items disagree never is.
    private static bool AreCompatible(
        HashSet<(MediaIdentifierScheme Scheme, string Value)> group,
        HashSet<(MediaIdentifierScheme Scheme, string Value)> work) =>
        group.Concat(work).Distinct().GroupBy(identifier => identifier.Scheme).All(scheme => scheme.Count() == 1);

    /// <summary>One (group, work) pair the assignment may choose, with its score.</summary>
    private readonly record struct WorkCandidate(int GroupIndex, Guid WorkId, int Continuity, int SharedIdentifiers, DateTime WorkCreatedUtc);

    /// <summary>The work of each group, in the groups' order, and the works made for this sync.</summary>
    private sealed record WorkAssignment(IReadOnlyList<Guid> WorkIds, List<Guid> CreatedWorkIds);
}
