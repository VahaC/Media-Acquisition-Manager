using System.Security.Cryptography;
using System.Text;
using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Core.Duplicates;

/// <summary>
/// MAM-20 — the four classes of duplicate of specification §3.3, worked out from what the database holds: the names of
/// one file (<see cref="DuplicateClass.SameFile"/>), byte-identical files (<see cref="DuplicateClass.ByteIdentical"/>),
/// copies of one work across libraries (<see cref="DuplicateClass.SameWork"/>) and possible matches by title and year
/// (<see cref="DuplicateClass.Possible"/>). Pure: the caller loads, this decides (ADR 0028).
/// </summary>
/// <remarks>
/// <para>
/// Worked out on every read and stored nowhere, so a sync, an inspection or a link the owner sets moves the groups at
/// once, with no migration and nothing to keep in step. Each group has a <see cref="DuplicateGroup.Key"/> that names it
/// on every read while what makes it one stays: the Duplicates page (MAM-21) addresses groups by it.
/// </para>
/// <para>
/// The classes are independent: a name can be in a byte-identical group and in a group of one work's copies at once.
/// Nothing here asks a download client (ADR 0018): a file a torrent seeds is classed and counted like any other.
/// </para>
/// </remarks>
public static partial class DuplicateGrouping
{
    /// <summary>How many bytes of the SHA-256 of its canonical text a group's key keeps: 16, 32 hex characters — more
    /// than enough to tell a library's groups apart, short enough for a URL.</summary>
    private const int KeyBytes = 16;

    /// <summary>
    /// The devices of the files in byte-identical groups, each once, in ascending order: the only devices whose file
    /// system decides anything (<see cref="Group"/>'s estimate), so the caller asks the mount table about them alone —
    /// and not at all when there is no such group.
    /// </summary>
    public static IReadOnlyList<long> DevicesOfByteIdenticalCopies(IReadOnlyList<DuplicateName> names)
    {
        ArgumentNullException.ThrowIfNull(names);

        return [.. ByteIdenticalSets(names).SelectMany(set => set.Identities).Select(identity => identity.DeviceId).Distinct().Order()];
    }

    /// <summary>
    /// Every group of <paramref name="names"/>, by class in <see cref="DuplicateClass"/>'s order, then by key.
    /// <paramref name="works"/> are the library works holding them; <paramref name="fileSystems"/> the file system type
    /// of each device of <see cref="DevicesOfByteIdenticalCopies"/> (<c>null</c>, or a device left out, for
    /// unknown).
    /// </summary>
    public static IReadOnlyList<DuplicateGroup> Group(
        IReadOnlyList<DuplicateName> names, IReadOnlyList<DuplicateWork> works, IReadOnlyDictionary<long, string?> fileSystems)
    {
        ArgumentNullException.ThrowIfNull(names);
        ArgumentNullException.ThrowIfNull(works);
        ArgumentNullException.ThrowIfNull(fileSystems);

        var namesByWork = names
            .SelectMany(name => name.Holders.Select(holder => (holder.WorkId, Name: name)))
            .ToLookup(held => held.WorkId, held => held.Name);
        // ADR 0017 — only a film or an episode holds files; a series is known through its episodes' series keys.
        var held = works
            .Where(work => work.Kind is MediaWorkKind.Movie or MediaWorkKind.Episode && namesByWork[work.WorkId].Any())
            .DistinctBy(work => work.WorkId)
            .ToList();
        var componentOf = WorkComponents(held);

        return
        [
            .. SameFileGroups(names)
                .Concat(ByteIdenticalGroups(names, fileSystems))
                .Concat(SameWorkGroups(held, namesByWork, componentOf))
                .Concat(PossibleGroups(held, names, namesByWork, componentOf))
                .OrderBy(group => group.Class)
                .ThenBy(group => group.Key, StringComparer.Ordinal),
        ];
    }

    /// <summary>
    /// A registry key as keys are compared: the value trimmed and upper-cased (the <c>WorkMatching</c> rule), with its
    /// number space and numbers.
    /// </summary>
    internal static (MediaIdentifierScheme Scheme, string Value, MediaWorkKind? Kind, int? SeasonNumber, int? EpisodeNumber) Normalized(
        RegistryKey key) =>
        (key.Scheme, key.Value.Trim().ToUpperInvariant(), key.Kind, key.SeasonNumber, key.EpisodeNumber);

    // ── shared ──────────────────────────────────────────────────────────────────────────────────

    // The first KeyBytes of the SHA-256 of the group's canonical text, in lowercase hex.
    private static string KeyOf(string canonical) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)).AsSpan(0, KeyBytes));

    // The file a name leads to, when the app's last look read its identity (ADR 0006: the only proof two names are one
    // file).
    private static (long DeviceId, long Inode)? IdentityOf(DuplicateName name) =>
        name is { DeviceId: { } deviceId, Inode: { } inode } ? (deviceId, inode) : null;

    // What counts a name's file once: its identity where known, else its path — a name the app hasn't looked at is a
    // file of its own until it has.
    private static string FileKeyOf(DuplicateName name) =>
        IdentityOf(name) is { } identity
            ? FormattableString.Invariant($"file:{identity.DeviceId}:{identity.Inode}")
            : "path:" + name.PathKey;

    private static IReadOnlyList<Guid> FileIdsOf(IEnumerable<DuplicateName> members) => [.. members.Select(name => name.FileId).Distinct()];

    private static IReadOnlyList<Guid> WorkIdsOf(IEnumerable<DuplicateName> members) =>
        [.. members.SelectMany(name => name.Holders).Select(holder => holder.WorkId).Distinct()];

    /// <summary>
    /// What tells the copies of one work, or a possible match's files, apart (specification §3.3): their editions when
    /// the sets differ — a labelled file and an unlabelled one differ too — and their measured dynamic ranges when two
    /// files' known sets differ; an unknown range differs from nothing.
    /// </summary>
    private static List<DuplicateDistinction> CopyDistinctions(IReadOnlyCollection<DuplicateName> members)
    {
        List<DuplicateDistinction> distinctions = [];
        if (members.Select(name => SetKeyOf(name.Editions)).Distinct(StringComparer.Ordinal).Count() > 1)
            distinctions.Add(new DuplicateDistinction(DuplicateDistinctionKind.Edition, null, false));
        if (members.Where(name => name.DynamicRanges.Count > 0).Select(name => SetKeyOf(name.DynamicRanges)).Distinct(StringComparer.Ordinal).Count() > 1)
            distinctions.Add(new DuplicateDistinction(DuplicateDistinctionKind.DynamicRange, null, false));
        return distinctions;
    }

    private static string SetKeyOf<T>(IEnumerable<T> values)
        where T : struct, Enum =>
        string.Join(',', values.Distinct().Order());

    // Union-find, the LibraryWorkGrouping way: the smaller index is the root, so a component's root never depends on
    // the order of the unions.
    private static int Find(int[] parent, int index)
    {
        while (parent[index] != index)
        {
            parent[index] = parent[parent[index]];
            index = parent[index];
        }

        return index;
    }

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
