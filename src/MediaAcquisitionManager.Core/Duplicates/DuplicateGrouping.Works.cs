using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Core.Duplicates;

// MAM-20 — copies of one work across libraries and integrations, by their registry keys (ADR 0017).
public static partial class DuplicateGrouping
{
    /// <summary>
    /// The component of every work with a registry key: works sharing any key, across every library and integration,
    /// are one (union-find over the keys compared as <see cref="Normalized"/> compares them). A work without a key is
    /// in none — the 65 id-less files of one Tom &amp; Jerry Kids item are no copies (ADR 0017).
    /// </summary>
    private static Dictionary<Guid, int> WorkComponents(IReadOnlyList<DuplicateWork> works)
    {
        var keyed = works.Where(work => work.Keys.Count > 0).ToList();
        var parent = Enumerable.Range(0, keyed.Count).ToArray();
        var firstHolder = new Dictionary<(MediaIdentifierScheme, string, MediaWorkKind?, int?, int?), int>();
        for (var index = 0; index < keyed.Count; index++)
        {
            foreach (var key in keyed[index].Keys.Select(Normalized))
            {
                if (firstHolder.TryGetValue(key, out var holder))
                    Union(parent, index, holder);
                else
                    firstHolder[key] = index;
            }
        }

        return keyed.Select((work, index) => (work.WorkId, Root: Find(parent, index))).ToDictionary(work => work.WorkId, work => work.Root);
    }

    /// <summary>
    /// Another copy or version of the same work (spec §3.3 row 3): a component whose works hold two copies or more —
    /// two versions with different files, each file counted once by its identity (a hardlink in two libraries is one
    /// file, the <see cref="DuplicateClass.SameFile"/> group's), and a version of several parts one copy. Never
    /// redundant, so no estimate: which copy to keep is the owner's call.
    /// </summary>
    /// <remarks>
    /// Vasyl, 2026-10-07 — works whose ids disagree stay one group, flagged for review with the disagreement as a
    /// distinction, each file showing its own work's ids: two works with one IMDb id and different TMDb ids, a link
    /// beside another library's TMDb id, two episodes with one episode's ids under different numbers.
    /// </remarks>
    private static IEnumerable<DuplicateGroup> SameWorkGroups(
        IReadOnlyList<DuplicateWork> works, ILookup<Guid, DuplicateName> namesByWork, Dictionary<Guid, int> componentOf)
    {
        foreach (var component in works.Where(work => componentOf.ContainsKey(work.WorkId)).GroupBy(work => componentOf[work.WorkId]))
        {
            List<DuplicateWork> componentWorks = [.. component.OrderBy(work => work.WorkId.ToString("D"), StringComparer.Ordinal)];
            var workIds = componentWorks.Select(work => work.WorkId).ToHashSet();
            List<DuplicateName> members = [.. componentWorks.SelectMany(work => namesByWork[work.WorkId]).DistinctBy(name => name.FileId)];

            // A copy is what one version holds: the set of its files. Two libraries' versions of one path hold one set.
            var copies = new Dictionary<Guid, SortedSet<string>>();
            foreach (var name in members)
            {
                foreach (var holder in name.Holders.Where(holder => workIds.Contains(holder.WorkId)))
                {
                    if (!copies.TryGetValue(holder.VersionId, out var files))
                        copies[holder.VersionId] = files = new SortedSet<string>(StringComparer.Ordinal);
                    files.Add(FileKeyOf(name));
                }
            }

            var copyCount = copies.Values.Select(files => string.Join('\n', files)).Distinct(StringComparer.Ordinal).Count();
            var fileCount = members.Select(FileKeyOf).Distinct(StringComparer.Ordinal).Count();
            if (copyCount < 2 || fileCount < 2)
                continue;

            List<DuplicateDistinction> distinctions = [.. IdentifierDistinctions(componentWorks), .. CopyDistinctions(members)];
            yield return new DuplicateGroup(
                KeyOf("SameWork|" + string.Join('|', componentWorks.Select(work => work.WorkId.ToString("D")))),
                DuplicateClass.SameWork,
                NeedsReview: distinctions.Count > 0,
                distinctions,
                FileIdsOf(members),
                [.. workIds],
                null,
                null,
                SharedKeysOf(componentWorks),
                [],
                null,
                null);
        }
    }

    /// <summary>
    /// The keys that make the works one: those two works or more hold, or a single work's own — one value per
    /// registry, value and numbers, by scheme, own ids before series keys, then value and numbers.
    /// </summary>
    private static IReadOnlyList<RegistryKey> SharedKeysOf(IReadOnlyList<DuplicateWork> works)
    {
        var holders = works
            .SelectMany(work => work.Keys.Select(key => (Key: key, work.WorkId)))
            .GroupBy(held => Normalized(held.Key))
            .Where(key => works.Count == 1 || key.Select(held => held.WorkId).Distinct().Count() >= 2)
            .Select(key => key.First().Key);
        return
        [
            .. holders
                .DistinctBy(key => (key.Scheme, key.Value.ToUpperInvariant(), key.SeasonNumber, key.EpisodeNumber))
                .OrderBy(key => key.Scheme)
                .ThenBy(key => key.IsSeriesKey)
                .ThenBy(key => key.Value, StringComparer.Ordinal)
                .ThenBy(key => key.SeasonNumber)
                .ThenBy(key => key.EpisodeNumber),
        ];
    }

    /// <summary>
    /// Where the works' ids disagree: a number space of their own ids holding two values (two TMDb film ids), a show's
    /// registry holding two shows, or one show with one episode's ids under two numbers (S03E31 and S03E33). Each
    /// once, by kind, then scheme.
    /// </summary>
    private static IEnumerable<DuplicateDistinction> IdentifierDistinctions(IReadOnlyList<DuplicateWork> works)
    {
        var keys = works.SelectMany(work => work.Keys).ToList();
        var own = keys
            .Where(key => !key.IsSeriesKey)
            .GroupBy(key => (key.Scheme, key.Kind))
            .Where(space => space.Select(key => Normalized(key).Value).Distinct(StringComparer.Ordinal).Count() >= 2)
            .Select(space => new DuplicateDistinction(DuplicateDistinctionKind.IdentifierConflict, space.Key.Scheme, false));
        var series = keys
            .Where(key => key.IsSeriesKey)
            .GroupBy(key => key.Scheme)
            .Select(space =>
                space.Select(key => Normalized(key).Value).Distinct(StringComparer.Ordinal).Count() >= 2
                    ? new DuplicateDistinction(DuplicateDistinctionKind.IdentifierConflict, space.Key, true)
                    : space.Select(key => (key.SeasonNumber, key.EpisodeNumber)).Distinct().Count() >= 2
                        ? new DuplicateDistinction(DuplicateDistinctionKind.EpisodeNumbers, null, false)
                        : null)
            .OfType<DuplicateDistinction>();
        return own.Concat(series).Distinct().OrderBy(distinction => distinction.Kind).ThenBy(distinction => distinction.Scheme).ThenBy(distinction => distinction.OfSeries);
    }
}
