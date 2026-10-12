using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Library;
using MediaAcquisitionManager.Core.Matching;

namespace MediaAcquisitionManager.Core.Duplicates;

// MAM-20 — possible matches: one title and year, no id that decides (ADR 0012's second tier).
public static partial class DuplicateGrouping
{
    /// <summary>
    /// Works, and names no library reported, that may be one work (spec §3.3 row 4): two subjects sharing a title's
    /// match key with that title's own year (<see cref="TitleMatchKey"/>) — a work's titles, every source's, and what
    /// an unreported name's file name, then folder name, claims (<c>FileNameClaims</c>). No distance, no year ± 1, a
    /// title alone never (ADR 0012). Each group needs the owner's word, and none is redundant.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Not offered: two works the library or their ids say are different (<see cref="IsApart"/>) — a film and an
    /// episode, ids of one number space none of which is shared (The Continental's three items with three IMDb ids;
    /// two episodes of two shows), two episodes at different places of their shows, even under one episode's ids
    /// (MAM-37); two works already one (<see cref="WorkComponents"/>); and two subjects with one file between them (one
    /// identity, or one hash) — the bytes already say they are one, in the classes above.
    /// </para>
    /// <para>
    /// A group is the subjects joined by pairs, unless that would put two works that are apart in it: then each group
    /// grows from a pair by the subjects paired to it that are apart from none of it — The Continental's three items and
    /// one file named "The Continental (2023)" are three groups, the file with each item, never one that says the three
    /// may be one work (ADR 0028).
    /// </para>
    /// </remarks>
    private static IEnumerable<DuplicateGroup> PossibleGroups(
        IReadOnlyList<DuplicateWork> works,
        IReadOnlyList<DuplicateName> names,
        ILookup<Guid, DuplicateName> namesByWork,
        Dictionary<Guid, int> componentOf)
    {
        List<Subject> subjects =
        [
            .. works
                .Select(work => SubjectOf(work.WorkId, work, [.. namesByWork[work.WorkId].DistinctBy(name => name.FileId)], TitlesOf(work)))
                .Concat(names.Where(name => name.IsUnreported).DistinctBy(name => name.FileId).Select(name => SubjectOf(name.FileId, null, [name], ClaimsOf(name))))
                .Where(subject => subject.Titles.Count > 0),
        ];

        var sharing = new Dictionary<(string Key, int Year), List<int>>();
        for (var index = 0; index < subjects.Count; index++)
        {
            foreach (var title in subjects[index].Titles.DistinctBy(title => (title.Key, title.Year)))
            {
                if (!sharing.TryGetValue((title.Key, title.Year), out var holders))
                    sharing[(title.Key, title.Year)] = holders = [];
                holders.Add(index);
            }
        }

        var parent = Enumerable.Range(0, subjects.Count).ToArray();
        var pairs = new SortedSet<(int First, int Second)>();
        foreach (var holders in sharing.Values.Where(holders => holders.Count >= 2))
        {
            for (var first = 0; first < holders.Count; first++)
            {
                for (var second = first + 1; second < holders.Count; second++)
                {
                    var (one, other) = (subjects[holders[first]], subjects[holders[second]]);
                    if (IsApart(one, other, componentOf) || IsOne(one, other, componentOf))
                        continue;
                    Union(parent, holders[first], holders[second]);
                    pairs.Add((Math.Min(holders[first], holders[second]), Math.Max(holders[first], holders[second])));
                }
            }
        }

        foreach (var component in pairs.GroupBy(pair => Find(parent, pair.First)).OrderBy(component => component.Key))
        {
            foreach (var indices in GroupsOf([.. component], subjects, componentOf))
                yield return PossibleGroupOf(indices, subjects, sharing);
        }
    }

    /// <summary>
    /// The groups of one component of pairs: the whole of it while no two of its works are apart, else a group grown
    /// from each pair no group holds yet — by the subjects paired to a member, lowest first, apart from none of it —
    /// so every pair is offered, and no group holds two works the rules keep apart.
    /// </summary>
    private static IEnumerable<IReadOnlyCollection<int>> GroupsOf(
        IReadOnlyList<(int First, int Second)> pairs, IReadOnlyList<Subject> subjects, Dictionary<Guid, int> componentOf)
    {
        List<int> members = [.. pairs.SelectMany(pair => new[] { pair.First, pair.Second }).Distinct().Order()];
        if (!members.Any(first => members.Any(second => first < second && IsApart(subjects[first], subjects[second], componentOf))))
        {
            yield return members;
            yield break;
        }

        var partners = pairs
            .SelectMany(pair => new[] { (Member: pair.First, Partner: pair.Second), (Member: pair.Second, Partner: pair.First) })
            .ToLookup(pair => pair.Member, pair => pair.Partner);
        List<SortedSet<int>> groups = [];
        foreach (var (first, second) in pairs)
        {
            if (groups.Any(group => group.Contains(first) && group.Contains(second)))
                continue;

            var group = new SortedSet<int> { first, second };
            var grew = true;
            while (grew)
            {
                grew = false;
                foreach (var candidate in group.SelectMany(member => partners[member]).Where(candidate => !group.Contains(candidate)).Distinct().Order().ToList())
                {
                    if (group.Any(member => IsApart(subjects[member], subjects[candidate], componentOf)))
                        continue;
                    group.Add(candidate);
                    grew = true;
                }
            }

            groups.Add(group);
            yield return group;
        }
    }

    // One possible group of the subjects at indices: their files, the titles two of them or more share, and whether
    // the measured durations agree.
    private static DuplicateGroup PossibleGroupOf(
        IReadOnlyCollection<int> indices, IReadOnlyList<Subject> subjects, Dictionary<(string Key, int Year), List<int>> sharing)
    {
        var held = indices.ToHashSet();
        List<Subject> members = [.. indices.Order().Select(index => subjects[index])];
        List<DuplicateName> files = [.. members.SelectMany(subject => subject.Names).DistinctBy(name => name.FileId)];
        List<DuplicateTitle> titles =
        [
            .. members
                .SelectMany(subject => subject.Titles)
                .Where(title => sharing[(title.Key, title.Year)].Count(held.Contains) >= 2)
                .Select(title => new DuplicateTitle(title.Text, title.Year))
                .Distinct()
                .OrderBy(title => title.Text, LibraryTitleOrder.Comparer)
                .ThenBy(title => title.Text, StringComparer.Ordinal)
                .ThenBy(title => title.Year),
        ];
        List<long> durations =
        [
            .. files.DistinctBy(FileKeyOf).Select(name => name.DurationMilliseconds).OfType<long>(),
        ];

        return new DuplicateGroup(
            KeyOf("Possible|" + string.Join('|', members.Select(subject => subject.Id.ToString("D")).Order(StringComparer.Ordinal))),
            DuplicateClass.Possible,
            // The spec's manual confirmation: a title and a year are never proof.
            NeedsReview: true,
            [.. EditionDistinctions(files), .. DynamicRangeDistinctions(files)],
            FileIdsOf(files),
            PathKeysOf(files),
            [.. members.Where(subject => subject.Work is not null).Select(subject => subject.Id)],
            null,
            null,
            [],
            titles,
            DurationAgreement.Of(durations),
            null,
            null);
    }

    /// <summary>
    /// Whether the library or the ids say two works are different, so a title and a year can't make them one: a film
    /// and an episode (the library says what each is); ids of one number space, as works sharing one are in one
    /// component (<see cref="WorkComponents"/>), or kept apart by their places (MAM-37); two episodes whose places are
    /// known and differ — another show's, by its titles, or another season or episode of one ("Episode 1" of two shows,
    /// the name TMDb and Jellyfin give an episode with no title of its own).
    /// </summary>
    private static bool IsApart(Subject first, Subject second, Dictionary<Guid, int> componentOf)
    {
        if (first.Work is not { } firstWork || second.Work is not { } secondWork)
            return false;
        if (firstWork.Kind != secondWork.Kind)
            return true;
        if (InOneComponent(firstWork, secondWork, componentOf))
            return false;
        return first.NumberSpaces.Overlaps(second.NumberSpaces)
            || (first.Places.Count > 0 && second.Places.Count > 0 && !first.Places.Overlaps(second.Places));
    }

    // Whether the two are already one: one work by their ids, or one file between them by its identity or its hash.
    private static bool IsOne(Subject first, Subject second, Dictionary<Guid, int> componentOf) =>
        (first.Work is { } firstWork && second.Work is { } secondWork && InOneComponent(firstWork, secondWork, componentOf))
        || first.Files.Overlaps(second.Files)
        || first.Hashes.Overlaps(second.Hashes);

    private static bool InOneComponent(DuplicateWork first, DuplicateWork second, Dictionary<Guid, int> componentOf) =>
        componentOf.TryGetValue(first.WorkId, out var firstComponent)
        && componentOf.TryGetValue(second.WorkId, out var secondComponent)
        && firstComponent == secondComponent;

    private static Subject SubjectOf(Guid id, DuplicateWork? work, IReadOnlyList<DuplicateName> names, IReadOnlyList<SubjectTitle> titles) =>
        new(
            id,
            work,
            names,
            titles,
            work is null ? [] : NumberSpacesOf(work),
            work is null ? [] : PlacesOf(work),
            [.. names.Select(IdentityOf).OfType<(long, long)>()],
            [.. names.Select(name => name.Sha256?.ToLowerInvariant()).OfType<string>()]);

    // The number spaces a work holds ids in: each registry and kind of its own ids, each registry of its series keys.
    private static HashSet<(MediaIdentifierScheme Scheme, MediaWorkKind? Kind, bool OfSeries)> NumberSpacesOf(DuplicateWork work) =>
        [.. work.Keys.Select(key => key.IsSeriesKey ? (key.Scheme, null, true) : (key.Scheme, key.Kind, false))];

    // Where a work sits in a show by the show's titles: each title's match key with the season and the episode. A place
    // whose show has no title — a link's TMDb show — says nothing.
    private static HashSet<(string SeriesKey, int SeasonNumber, int EpisodeNumber)> PlacesOf(DuplicateWork work) =>
    [
        .. work.Places.SelectMany(place => place.SeriesTitles
            .Select(TitleMatchKey.For)
            .Where(key => key.Length > 0)
            .Select(key => (key, place.SeasonNumber, place.EpisodeNumber))),
    ];

    // A work's titles that carry a year, by match key; a title with no letter or digit has no key.
    private static IReadOnlyList<SubjectTitle> TitlesOf(DuplicateWork work) =>
    [
        .. work.Titles
            .Where(title => title.Year is not null)
            .Select(title => new SubjectTitle(title.Text, TitleMatchKey.For(title.Text), title.Year!.Value))
            .Where(title => title.Key.Length > 0),
    ];

    // What an unreported name's path claims, each title with the one year the claim names.
    private static IReadOnlyList<SubjectTitle> ClaimsOf(DuplicateName name) =>
    [
        .. name.NameClaims
            .Where(claim => claim.Years is { Earliest: { } earliest } years && earliest == years.Latest)
            .SelectMany(claim => claim.Titles.Select(title => new SubjectTitle(title, TitleMatchKey.For(title), claim.Years!.Latest)))
            .Where(title => title.Key.Length > 0),
    ];

    /// <summary>A work or an unreported name a possible match is offered between.</summary>
    /// <param name="Id">The work's id, or the name's.</param>
    /// <param name="Work">The work; <c>null</c> for an unreported name.</param>
    /// <param name="Names">Its names.</param>
    /// <param name="Titles">Its titles with a year, or its name's claims.</param>
    /// <param name="NumberSpaces">The number spaces of its ids (<see cref="NumberSpacesOf"/>); none for a name.</param>
    /// <param name="Places">Where it sits in a show by the show's titles (<see cref="PlacesOf"/>); none for a name.</param>
    /// <param name="Files">The identities of its names' files, where known.</param>
    /// <param name="Hashes">Its names' current SHA-256s, lower-cased.</param>
    private sealed record Subject(
        Guid Id,
        DuplicateWork? Work,
        IReadOnlyList<DuplicateName> Names,
        IReadOnlyList<SubjectTitle> Titles,
        HashSet<(MediaIdentifierScheme Scheme, MediaWorkKind? Kind, bool OfSeries)> NumberSpaces,
        HashSet<(string SeriesKey, int SeasonNumber, int EpisodeNumber)> Places,
        HashSet<(long DeviceId, long Inode)> Files,
        HashSet<string> Hashes);

    /// <summary>A title as written, its match key, and its year.</summary>
    private sealed record SubjectTitle(string Text, string Key, int Year);
}
