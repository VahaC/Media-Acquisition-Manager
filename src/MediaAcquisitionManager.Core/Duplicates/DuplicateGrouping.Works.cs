using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Core.Duplicates;

// MAM-20 — copies of one work across libraries and integrations, by their registry keys (ADR 0017).
public static partial class DuplicateGrouping
{
    /// <summary>
    /// The component of every work with a registry key: works sharing any key, across every library and integration,
    /// are one (union-find over the keys compared as <see cref="Normalized"/> compares them) — but two places of one show
    /// never meet in a component (MAM-37, <paramref name="showPlaces"/> from <see cref="ShowPlacesOf"/>), and a work the
    /// library itself holds at two places of a show is alone in its own. A work without a key is in none — the 65 id-less
    /// files of one Tom &amp; Jerry Kids item are no copies (ADR 0017).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Vasyl, 2026-10-08 — two episodes of one show at different places are always different works, whatever ids they
    /// share: a Penguins of Madagascar pair under one IMDb id, Mickey Mouse Clubhouse's S03E31 under all of S03E33's ids.
    /// The sync already keeps them two works (<c>LibraryWorkGrouping</c>); a join here must not make them one again.
    /// Films keep the 2026-10-07 rule: one IMDb id and two TMDb ids stay one work, the disagreement shown; a film item the
    /// owner linked as an episode (MAM-34) sits at the link's place, as an episode does.
    /// </para>
    /// <para>
    /// In three passes, the works in id order — with refusals, the order of the joins decides who is with whom, so it
    /// must not be the order the loader read them in. First the placed works, pair by pair in order, each join refused
    /// when the two components sit at different places of a show both hold, or either sits at two places of one show;
    /// so every placed work of a component holds the component's places of each show it holds, and two places of one show
    /// never meet, whatever chain of ids runs between them. Then the unplaced works among themselves, as before. Last each
    /// unplaced component joins every placed component it shares a key with — or none of them, when those sit at
    /// different places of one show, or one of them at two: a film item with the IMDb id of two episodes is neither, as
    /// <c>LibraryWorkGrouping</c>'s item that fits two works joins neither. A work at two places of one show is two
    /// episodes a sync made one (a show without ids), a copy of nothing (Vasyl, 2026-10-08): it joins nothing, so
    /// <see cref="SameWorkGroups"/>, which drops it, drops nothing else. When nothing disagrees and no work sits at two
    /// places of one show, the components are those of a plain union-find.
    /// </para>
    /// </remarks>
    internal static Dictionary<Guid, int> WorkComponents(
        IReadOnlyList<DuplicateWork> works, Dictionary<Guid, Dictionary<int, SortedSet<(int Season, int Episode)>>> showPlaces)
    {
        List<DuplicateWork> keyed = [.. works.Where(work => work.Keys.Count > 0).OrderBy(work => work.WorkId.ToString("D"), StringComparer.Ordinal)];
        var parent = Enumerable.Range(0, keyed.Count).ToArray();
        // A root's places: those of every placed work of its component, show by show; a join adds the other root's.
        List<Dictionary<int, SortedSet<(int Season, int Episode)>>> places =
        [
            .. keyed.Select(work => showPlaces.TryGetValue(work.WorkId, out var held) ? new Dictionary<int, SortedSet<(int Season, int Episode)>>(held) : []),
        ];
        var placed = places.Select(held => held.Count > 0).ToArray();
        // Each key's holders, in index order.
        var holders = new Dictionary<(MediaIdentifierScheme, string, MediaWorkKind?, int?, int?), List<int>>();
        for (var index = 0; index < keyed.Count; index++)
        {
            foreach (var key in keyed[index].Keys.Select(Normalized).Distinct())
            {
                if (!holders.TryGetValue(key, out var indices))
                    holders[key] = indices = [];
                indices.Add(index);
            }
        }

        // Every pair, not each holder with the first: a refused first holder (another episode under the key) must not
        // keep the copies behind it apart.
        var pairs = new SortedSet<(int First, int Second)>();
        foreach (var indices in holders.Values)
        {
            List<int> placedHolders = [.. indices.Where(index => placed[index])];
            for (var first = 0; first < placedHolders.Count; first++)
            {
                for (var second = first + 1; second < placedHolders.Count; second++)
                    pairs.Add((placedHolders[first], placedHolders[second]));
            }
        }

        // A work at two places of one show joins nothing, and nothing joins it: a join never gives a root a second place of
        // a show, so such a work stays alone, the only work of its component.
        foreach (var (first, second) in pairs)
        {
            var (firstRoot, secondRoot) = (Find(parent, first), Find(parent, second));
            if (firstRoot != secondRoot
                && !AtTwoPlaces(places[firstRoot])
                && !AtTwoPlaces(places[secondRoot])
                && !Disagree(places[firstRoot], places[secondRoot]))
            {
                Join(parent, places, firstRoot, secondRoot);
            }
        }

        foreach (var indices in holders.Values)
        {
            List<int> unplacedHolders = [.. indices.Where(index => !placed[index])];
            foreach (var index in unplacedHolders.Skip(1))
                Union(parent, unplacedHolders[0], index);
        }

        var unplacedComponents = Enumerable.Range(0, keyed.Count)
            .Where(index => !placed[index])
            .GroupBy(index => Find(parent, index))
            .OrderBy(component => component.Key)
            .ToList();
        foreach (var component in unplacedComponents)
        {
            List<int> reached =
            [
                .. component
                    .SelectMany(index => keyed[index].Keys.Select(Normalized))
                    .SelectMany(key => holders[key])
                    .Where(index => placed[index])
                    .Select(index => Find(parent, index))
                    .Distinct()
                    .Order(),
            ];
            // A film item with the ids of a work at two places of one show may be either of its episodes: it joins none.
            if (reached.Any(root => AtTwoPlaces(places[root]))
                || reached.Any(first => reached.Any(second => first < second && Disagree(places[first], places[second]))))
                continue;
            foreach (var target in reached)
                Join(parent, places, component.Key, target);
        }

        return keyed.Select((work, index) => (work.WorkId, Root: Find(parent, index))).ToDictionary(work => work.WorkId, work => work.Root);
    }

    // Unions two components and gives the root the other's places; refusing a join is the caller's.
    private static void Join(int[] parent, List<Dictionary<int, SortedSet<(int Season, int Episode)>>> places, int first, int second)
    {
        var (firstRoot, secondRoot) = (Find(parent, first), Find(parent, second));
        if (firstRoot == secondRoot)
            return;
        Union(parent, firstRoot, secondRoot);
        var (root, other) = firstRoot < secondRoot ? (firstRoot, secondRoot) : (secondRoot, firstRoot);
        foreach (var (show, at) in places[other])
            places[root].TryAdd(show, at);
    }

    /// <summary>
    /// Another copy or version of the same work (spec §3.3 row 3): a component whose works hold two copies or more —
    /// two versions with different files, each file counted once by its identity (a hardlink in two libraries is one
    /// file, the <see cref="DuplicateClass.SameFile"/> group's), and a version of several parts one copy. Never
    /// redundant, so no estimate: which copy to keep is the owner's call.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Vasyl, 2026-10-07 — works whose ids disagree stay one group, flagged for review with the disagreement as a
    /// distinction, each file showing its own work's ids: two films with one IMDb id and different TMDb ids, a link
    /// beside another library's TMDb id, copies of one episode under two shows' ids — at one place when the shows share
    /// an id or a title, as MAM-37 keeps one show's places apart, but at any places when they share neither, since
    /// nothing then says they are one show.
    /// </para>
    /// <para>
    /// MAM-37 (Vasyl, 2026-10-08) — but two episodes of one show at different places are never one group, whatever ids
    /// they share (<see cref="WorkComponents"/>), nor is one work the library holds at two places of a show. And an
    /// episode's files whose names carry different segment letters are no copies of each other
    /// (<see cref="EpisodeSegments"/>): the copies split by the letters their versions' names carry, so Mickey Mouse
    /// Clubhouse's S03E09a is a copy of another S03E09a only — never of S03E09b, nor of a whole S03E09. Nor are a film's
    /// copies whose names claim years more than one apart (<see cref="YearPartsOf"/>): Blade Runner 2049, a version of
    /// the 1982 film's item, is no copy of it, while Seksmisja's "(1984)" and its remux, whose name claims no year, stay
    /// one group; an episode's year is its show's, so an episode's copies are never split by years. Nor are copies whose
    /// names say different editions (<see cref="EditionPartsOf"/>): the Theatrical and the Extended Cut of Rambo: Last
    /// Blood are two versions, not two copies, so a same-work group is never flagged for its editions — every copy in it
    /// says the same ones. A file's names say one thing between them, all that any of them says: a hardlink one library
    /// names by its cut and another by none is that cut through both, so its names never land it in two parts.
    /// </para>
    /// <para>
    /// MAM-37 (Vasyl, 2026-10-08) — copies whose measured running times differ widely are flagged too, beside any other
    /// distinction, with the shortest and the longest as evidence (<see cref="RunningTimesOf"/>): a featurette under the
    /// film's ids, another film under the first one's. Running times split nothing.
    /// </para>
    /// </remarks>
    private static IEnumerable<DuplicateGroup> SameWorkGroups(
        IReadOnlyList<DuplicateWork> works,
        ILookup<Guid, DuplicateName> namesByWork,
        Dictionary<Guid, int> componentOf,
        Dictionary<Guid, Dictionary<int, SortedSet<(int Season, int Episode)>>> showPlaces)
    {
        foreach (var component in works.Where(work => componentOf.ContainsKey(work.WorkId)).GroupBy(work => componentOf[work.WorkId]))
        {
            List<DuplicateWork> componentWorks = [.. component.OrderBy(work => work.WorkId.ToString("D"), StringComparer.Ordinal)];
            // MAM-37 — a work the library holds at two places of one show is two episodes a sync made one (a show without
            // ids): never a group, whatever ids they share. WorkComponents leaves such a work alone in its component, so
            // this drops its own files and nobody else's.
            if (componentWorks.Any(work => AtTwoPlaces(showPlaces[work.WorkId])))
                continue;
            var workIds = componentWorks.Select(work => work.WorkId).ToHashSet();
            List<DuplicateName> members = [.. componentWorks.SelectMany(work => namesByWork[work.WorkId]).DistinctBy(name => name.FileId)];

            // MAM-37 — what a file's names say, worked out once per file: the letters, the years and the editions of
            // all its names. The names of one file say one thing between them — a hardlink two libraries hold, one name
            // saying its cut and the other none, is still one file of one cut — so every version holding it reads it
            // alike, and its names never land one file in two parts, a copy of one cut by one name and of another by the
            // other.
            var saidOf = members
                .GroupBy(FileKeyOf, StringComparer.Ordinal)
                .ToDictionary(
                    file => file.Key,
                    file => (
                        Segments: file.Select(name => name.EpisodeSegment).OfType<string>().ToList(),
                        Years: file.Select(name => name.ClaimedYear).OfType<int>().ToList(),
                        Editions: file.SelectMany(name => name.Editions).ToList()),
                    StringComparer.Ordinal);

            // A copy is what one version holds: the set of its files. Two libraries' versions of one path hold one set.
            // MAM-37 — and the segment letters its files' names carry, none for a whole episode or a film, the years
            // they claim, and the editions they say: each file's as all its names say them.
            var copies = new Dictionary<Guid, SortedSet<string>>();
            var segments = new Dictionary<Guid, SortedSet<string>>();
            var years = new Dictionary<Guid, SortedSet<int>>();
            var editions = new Dictionary<Guid, SortedSet<MediaEdition>>();
            foreach (var name in members)
            {
                var fileKey = FileKeyOf(name);
                var said = saidOf[fileKey];
                foreach (var holder in name.Holders.Where(holder => workIds.Contains(holder.WorkId)))
                {
                    if (!copies.TryGetValue(holder.VersionId, out var files))
                    {
                        copies[holder.VersionId] = files = new SortedSet<string>(StringComparer.Ordinal);
                        segments[holder.VersionId] = new SortedSet<string>(StringComparer.Ordinal);
                        years[holder.VersionId] = new SortedSet<int>();
                        editions[holder.VersionId] = new SortedSet<MediaEdition>();
                    }

                    files.Add(fileKey);
                    segments[holder.VersionId].UnionWith(said.Segments);
                    years[holder.VersionId].UnionWith(said.Years);
                    editions[holder.VersionId].UnionWith(said.Editions);
                }
            }

            // MAM-37 — the years part films only. Vasyl's 2026-10-08 rule is about a film: Blade Runner 2049 under the
            // 1982 film's ids. In an episode's name a year alone in brackets is its show's or its season's ("S01E01.
            // MobLand (2025). …", Sonarr's {Series TitleYear}), never the episode's own, and its place and segment
            // letters already decide which episode it is; so a component holding an episode, or a film linked as one,
            // is never split by years.
            var ofFilms = componentWorks.All(work => work.Kind == MediaWorkKind.Movie && showPlaces[work.WorkId].Count == 0);

            // MAM-37 — a copy is a copy only of those whose names carry the same letters, claim one film's years
            // (YearPartsOf, films only) and say the same editions (EditionPartsOf): one group per set of letters,
            // cluster of years and set of editions, split in that order, each split within the parts of the one before
            // — which segment, which film, then which cut of it. A component with no letter, no film's years far apart
            // and one set of editions is one part, the whole of it, so its group, key and all, is what it was before.
            var parts = copies.Keys
                .GroupBy(versionId => string.Join(',', segments[versionId]), StringComparer.Ordinal)
                .SelectMany(lettered => (ofFilms ? YearPartsOf([.. lettered], years) : [([.. lettered], null)])
                    .Select(part => (Letters: lettered.Key, part.VersionIds, part.Years)))
                .SelectMany(dated => EditionPartsOf(dated.VersionIds, editions).Select(part => (dated.Letters, part.VersionIds, dated.Years, part.Editions)));
            foreach (var (letters, part, partYears, partEditions) in parts)
            {
                var versionIds = part.ToHashSet();
                List<DuplicateName> partMembers =
                [
                    .. members.Where(name => name.Holders.Any(holder => workIds.Contains(holder.WorkId) && versionIds.Contains(holder.VersionId))),
                ];
                var partWorkIds = partMembers
                    .SelectMany(name => name.Holders)
                    .Where(holder => workIds.Contains(holder.WorkId) && versionIds.Contains(holder.VersionId))
                    .Select(holder => holder.WorkId)
                    .ToHashSet();
                List<DuplicateWork> partWorks = [.. componentWorks.Where(work => partWorkIds.Contains(work.WorkId))];
                List<SortedSet<string>> partCopies = [.. part.Select(versionId => copies[versionId])];

                var copyCount = partCopies.Select(files => string.Join('\n', files)).Distinct(StringComparer.Ordinal).Count();
                var fileCount = partMembers.Select(FileKeyOf).Distinct(StringComparer.Ordinal).Count();
                if (copyCount < 2 || fileCount < 2)
                    continue;

                var runningTimes = RunningTimesOf(partCopies, partMembers);
                // No Edition: every copy of a part says the same editions (EditionPartsOf).
                List<DuplicateDistinction> distinctions = [.. IdentifierDistinctions(partWorks), .. DynamicRangeDistinctions(partMembers)];
                // The last kind, so the list stays in kind order.
                if (runningTimes is not null && RunningTimeSpread.IsWide(runningTimes.ShortestMilliseconds, runningTimes.LongestMilliseconds))
                    distinctions.Add(new DuplicateDistinction(DuplicateDistinctionKind.RunningTimes, null, false));
                // A part with letters names them, so two libraries' a and b groups of the same works keep keys of their own;
                // so does a part of a cluster of years, or of the copies that claim none, beside another cluster, and a part
                // of one set of editions, or of the copies that say none, beside another set. In the order of the splits:
                // "|Segments:" when the copies carry letters, then "|Years:" and "|Editions:" only when theirs parted
                // something.
                var canonical = "SameWork|" + string.Join('|', partWorks.Select(work => work.WorkId.ToString("D")));
                if (letters.Length > 0)
                    canonical += "|Segments:" + letters;
                if (partYears is not null)
                    canonical += "|Years:" + partYears;
                if (partEditions is not null)
                    canonical += "|Editions:" + partEditions;
                yield return new DuplicateGroup(
                    KeyOf(canonical),
                    DuplicateClass.SameWork,
                    NeedsReview: distinctions.Count > 0,
                    distinctions,
                    FileIdsOf(partMembers),
                    PathKeysOf(partMembers),
                    [.. partWorks.Select(work => work.WorkId)],
                    null,
                    null,
                    SharedKeysOf(partWorks),
                    [],
                    null,
                    runningTimes,
                    null);
            }
        }
    }

    /// <summary>
    /// MAM-37 — the running times of the shortest and the longest measured copy (Vasyl, 2026-10-08): each copy once, as
    /// the copies are counted, its running time the sum of its files' measured durations — a film in two parts runs as
    /// long as both — and none when a file of it has no measured duration, as one part unknown leaves the whole unknown
    /// and an unknown decides nothing. <c>null</c> when fewer than two copies have one.
    /// </summary>
    private static DuplicateRunningTimes? RunningTimesOf(IEnumerable<SortedSet<string>> copies, IReadOnlyList<DuplicateName> members)
    {
        // One duration per file: the names of one file read one file, so any of them the inspector measured tells it —
        // the longest, so the answer doesn't hang on which name comes first.
        var durationOf = members
            .GroupBy(FileKeyOf, StringComparer.Ordinal)
            .ToDictionary(file => file.Key, file => file.Max(name => name.DurationMilliseconds), StringComparer.Ordinal);
        List<long> measured =
        [
            .. copies
                .DistinctBy(files => string.Join('\n', files), StringComparer.Ordinal)
                .Where(files => files.All(file => durationOf[file] is not null))
                .Select(files => files.Sum(file => durationOf[file]!.Value)),
        ];
        return measured.Count < 2 ? null : new DuplicateRunningTimes(measured.Min(), measured.Max());
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
    /// Where the works' ids disagree: a number space of their own ids holding two values (two TMDb film ids), or a
    /// show's registry holding two shows. Each once, by kind, then scheme. One show's ids at two places never meet in a
    /// group (MAM-37, <see cref="WorkComponents"/>).
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
            .Where(space => space.Select(key => Normalized(key).Value).Distinct(StringComparer.Ordinal).Count() >= 2)
            .Select(space => new DuplicateDistinction(DuplicateDistinctionKind.IdentifierConflict, space.Key, true));
        return own.Concat(series).Distinct().OrderBy(distinction => distinction.Kind).ThenBy(distinction => distinction.Scheme).ThenBy(distinction => distinction.OfSeries);
    }
}
