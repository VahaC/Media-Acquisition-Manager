namespace MediaAcquisitionManager.Core.Duplicates;

// MAM-37 — copies of one work whose file names claim years far apart (Vasyl, 2026-10-08: "Blade Runner 2049 взагалі не
// має бути в дублікатах"): the names say what the ids hide, as an episode's places and segment letters do.
public static partial class DuplicateGrouping
{
    /// <summary>How many years apart two years claimed by names may be and still be one film's: a festival and a
    /// release, a year's end and the next.</summary>
    private const int YearsOfOneFilm = 1;

    /// <summary>
    /// <paramref name="versionIds"/>, the copies of one part of a component, split by the years their file names claim
    /// (<see cref="FileNameYear"/>, <paramref name="claimedYears"/> by version): the part whole, with no
    /// <c>Years</c> text, when the years claimed fall in one cluster or none; else one part per cluster, its years as its
    /// <c>Years</c> text, and one of the copies that claim no year, "none".
    /// </summary>
    /// <remarks>
    /// <para>
    /// Vasyl, 2026-10-08 — Jellyfin made Blade Runner 2049's file a version of the 1982 film's item, with its ids; the
    /// names, "(1982)" and "(2017)", say they are two films, so neither is a copy of the other and the work is in no
    /// group. Years one apart are one film's (<see cref="YearsOfOneFilm"/>). Seksmisja's "(1984)" and "[1983 DVDRemux]"
    /// stay one group as a name and one that claims no year (<see cref="FileNameYear"/>).
    /// </para>
    /// <para>
    /// A copy's years are those its files' names claim, a multi-part copy's normally one; a copy whose names claim years
    /// further apart than one film's is no single year's, and is taken as claiming none. The years the copies claim are
    /// sorted and cut where two neighbours are more than one film's apart: a cluster is a run of years, each within one
    /// of the next, so 1982, 1983 and 1984 are one cluster though its ends are two apart — a cut anywhere in a run would
    /// part two copies one year apart, which are one film's. With one cluster or none nothing is split — a copy that
    /// claims no year, «Ніж у воді»'s featurette, is a copy of the others as before, and the group, key and all, is what
    /// it was. With two or more, a copy that claims no year can't be placed in any, so those copies are a part of their
    /// own. A part is still a group only with two copies and two files.
    /// </para>
    /// </remarks>
    private static IEnumerable<(IReadOnlyList<Guid> VersionIds, string? Years)> YearPartsOf(
        IReadOnlyList<Guid> versionIds, Dictionary<Guid, SortedSet<int>> claimedYears)
    {
        var yearsOf = versionIds.ToDictionary(
            versionId => versionId,
            versionId => claimedYears[versionId] is { Count: > 0 } years && years.Max - years.Min <= YearsOfOneFilm ? years : null);
        List<List<int>> clusters = [];
        foreach (var year in yearsOf.Values.OfType<SortedSet<int>>().SelectMany(years => years).Distinct().Order())
        {
            if (clusters.Count == 0 || year - clusters[^1][^1] > YearsOfOneFilm)
                clusters.Add([]);
            clusters[^1].Add(year);
        }

        if (clusters.Count <= 1)
        {
            yield return (versionIds, null);
            yield break;
        }

        // A copy's years are never more than one film's apart, so they all fall in one cluster: its first decides.
        foreach (var cluster in clusters)
            yield return ([.. versionIds.Where(versionId => yearsOf[versionId] is { } years && cluster.Contains(years.Min))], string.Join(',', cluster));
        List<Guid> unplaced = [.. versionIds.Where(versionId => yearsOf[versionId] is null)];
        if (unplaced.Count > 0)
            yield return (unplaced, "none");
    }
}
