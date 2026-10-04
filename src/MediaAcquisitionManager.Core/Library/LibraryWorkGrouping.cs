using MediaAcquisitionManager.Core.Abstractions.Library;
using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Core.Library;

/// <summary>
/// Decides which items of one library are one work (MAM-8, ADR 0006): only by the reliable identifiers
/// of specification §3.1, never by a title and a year.
/// </summary>
/// <remarks>
/// <para>
/// The rule, in order:
/// </para>
/// <list type="number">
/// <item>An item without identifiers, and an item that names two values of one scheme itself, is a work
/// of its own.</item>
/// <item>Items that share a (scheme, value) are linked, and so are the items linked to those (a
/// connected component). A component that has at most one value per scheme is one work.</item>
/// <item>A component with two values of one scheme is split by exact identifier sets: every
/// <b>maximal</b> set — one that is not a proper subset of another set in the component — is one work
/// with the items that have exactly that set; an item whose set is a proper subset of exactly one
/// maximal set joins that work; any other item is a work of its own.</item>
/// </list>
/// <para>
/// So the two "Чужі" items of the reference library (TMDb 679 and IMDb tt0090605 each) are one work
/// with two versions, as Jellyfin keeps them as two items (VendorSamples README). A conflict is never
/// settled by a guess: nothing joins a work whose identifiers it would contradict, and an item that
/// fits two works joins neither. Values compare trimmed and in any letter case (<c>tt0090605</c> is
/// <c>TT0090605</c>).
/// </para>
/// <para>
/// Deterministic, so a re-sync of an unchanged library gives the same groups: items inside a group and
/// the groups themselves are ordered by <see cref="LibraryItem.SourceItemId"/> (ordinal, the groups by
/// their first item), whatever order the items came in.
/// </para>
/// <para>
/// MAM-32 — the rule runs on the items of one kind at a time (the snapshot store groups movies, series
/// and episodes apart), so a movie and a series sharing a number are never one work (ADR 0017). An
/// episode's series' ids with its numbers are identifiers of it too: the caller adds them as further
/// keys (<c>Group(items, moreKeys)</c>),
/// so the two halves of an episode are one work, and two episodes carrying one episode's ids under
/// different numbers are two.
/// </para>
/// </remarks>
public static class LibraryWorkGrouping
{
    /// <summary>The works <paramref name="items"/> make, each with its items, as the rule above
    /// decides.</summary>
    public static IReadOnlyList<LibraryWorkGroup> Group(IReadOnlyList<LibraryItem> items) => Group(items, _ => []);

    /// <summary>
    /// The works <paramref name="items"/> make, with <paramref name="moreKeys"/> of each item counted as
    /// identifiers beside its own: a key names a scheme of its own, never one of
    /// <see cref="MediaIdentifierScheme"/>'s (MAM-32: an episode's position in its series).
    /// </summary>
    public static IReadOnlyList<LibraryWorkGroup> Group(
        IReadOnlyList<LibraryItem> items, Func<LibraryItem, IEnumerable<(string Key, string Value)>> moreKeys)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(moreKeys);

        // Sorted first, so every index below — and with it the order of the groups and of the items in
        // them — is the same whatever order the library listed the items in.
        var ordered = items.OrderBy(item => item.SourceItemId, StringComparer.Ordinal).ToList();
        var identifiers = ordered.Select(item => IdentifiersOf(item, moreKeys(item))).ToList();

        var groups = new List<List<int>>();
        var parent = Enumerable.Range(0, ordered.Count).ToArray();
        var firstHolder = new Dictionary<(string, string), int>();
        var linked = new List<int>();
        for (var index = 0; index < ordered.Count; index++)
        {
            if (identifiers[index] is not { Count: > 0 } own)
            {
                groups.Add([index]);
                continue;
            }

            linked.Add(index);
            foreach (var identifier in own)
            {
                var key = (identifier.Key, identifier.Value);
                if (firstHolder.TryGetValue(key, out var holder))
                    Union(parent, index, holder);
                else
                    firstHolder[key] = index;
            }
        }

        // Ascending indices, so each component lists its items in SourceItemId order.
        foreach (var component in linked.GroupBy(index => Find(parent, index)).Select(component => component.ToList()))
        {
            if (IsConsistent(component, identifiers))
                groups.Add(component);
            else
                groups.AddRange(Split(component, identifiers));
        }

        return groups
            .OrderBy(group => group[0])
            .Select(group => new LibraryWorkGroup([.. group.Select(index => ordered[index])]))
            .ToList()
            .AsReadOnly();
    }

    /// <summary>
    /// The item's identifiers and further keys by scheme, each value trimmed and upper-cased (so letter
    /// case never makes two values of one id); empty when it has none, <c>null</c> when it names two
    /// values of one scheme itself — an item that contradicts itself can't be matched to anything.
    /// </summary>
    private static Dictionary<string, string>? IdentifiersOf(LibraryItem item, IEnumerable<(string Key, string Value)> moreKeys)
    {
        var identifiers = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (scheme, raw) in item.Identifiers.Select(identifier => (identifier.Scheme.ToString(), identifier.Value)).Concat(moreKeys))
        {
            var value = raw.Trim().ToUpperInvariant();
            // A blank value names nothing, so it links to nothing either.
            if (value.Length == 0)
                continue;
            if (identifiers.TryGetValue(scheme, out var known) && !string.Equals(known, value, StringComparison.Ordinal))
                return null;
            identifiers[scheme] = value;
        }

        return identifiers;
    }

    private static bool IsConsistent(List<int> component, List<Dictionary<string, string>?> identifiers) =>
        component
            .SelectMany(index => identifiers[index]!)
            .GroupBy(identifier => identifier.Key)
            .All(scheme => scheme.Select(identifier => identifier.Value).Distinct(StringComparer.Ordinal).Count() == 1);

    /// <summary>The groups of a component with two values of one scheme: its maximal identifier sets,
    /// the items that fit exactly one of them, and the rest alone.</summary>
    private static List<List<int>> Split(List<int> component, List<Dictionary<string, string>?> identifiers)
    {
        var sets = component
            .Select(index => identifiers[index]!)
            .DistinctBy(SetKey, StringComparer.Ordinal)
            .ToList();
        var maximal = sets.Where(set => !sets.Any(other => IsProperSubset(set, other))).ToList();

        var byMaximalSet = maximal.ToDictionary(SetKey, _ => new List<int>(), StringComparer.Ordinal);
        var alone = new List<List<int>>();
        foreach (var index in component)
        {
            var set = identifiers[index]!;
            if (byMaximalSet.TryGetValue(SetKey(set), out var exact))
            {
                exact.Add(index);
                continue;
            }

            var supersets = maximal.Where(candidate => IsProperSubset(set, candidate)).ToList();
            if (supersets.Count == 1)
                byMaximalSet[SetKey(supersets[0])].Add(index);
            else
                alone.Add([index]);
        }

        return [.. byMaximalSet.Values.Where(group => group.Count > 0), .. alone];
    }

    private static bool IsProperSubset(Dictionary<string, string> set, Dictionary<string, string> other) =>
        set.Count < other.Count
        && set.All(identifier => other.TryGetValue(identifier.Key, out var value) && string.Equals(value, identifier.Value, StringComparison.Ordinal));

    // One string per exact set: the schemes in order, each with its value.
    private static string SetKey(Dictionary<string, string> set) =>
        string.Join('\n', set.OrderBy(identifier => identifier.Key, StringComparer.Ordinal).Select(identifier => $"{identifier.Key}={identifier.Value}"));

    private static int Find(int[] parent, int index)
    {
        while (parent[index] != index)
        {
            parent[index] = parent[parent[index]];
            index = parent[index];
        }

        return index;
    }

    // The smaller index becomes the root, so a component's root never depends on the order of unions.
    private static void Union(int[] parent, int first, int second)
    {
        var firstRoot = Find(parent, first);
        var secondRoot = Find(parent, second);
        if (firstRoot == secondRoot)
            return;
        if (firstRoot < secondRoot)
            parent[secondRoot] = firstRoot;
        else
            parent[firstRoot] = secondRoot;
    }
}
