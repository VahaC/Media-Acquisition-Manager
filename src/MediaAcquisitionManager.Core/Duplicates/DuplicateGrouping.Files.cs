using System.Globalization;
using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Core.Duplicates;

// MAM-20 — the two classes the bytes decide: one file's names, and files whose bytes are the same.
public static partial class DuplicateGrouping
{
    /// <summary>
    /// The names of one file (spec §3.3 row 1, ADR 0006): grouped by the device and inode the app read, two names or
    /// more. Two rows of one path are one name, so two libraries reporting one path make no group. Removing a name
    /// frees nothing until its file's last name goes, so the room recovered is none, and certain.
    /// </summary>
    private static IEnumerable<DuplicateGroup> SameFileGroups(IReadOnlyList<DuplicateName> names)
    {
        foreach (var file in names.Where(name => IdentityOf(name) is not null).GroupBy(name => IdentityOf(name)!.Value))
        {
            var nameCount = file.Select(name => name.PathKey).Distinct(StringComparer.Ordinal).Count();
            if (nameCount < 2)
                continue;

            var (deviceId, inode) = file.Key;
            yield return new DuplicateGroup(
                KeyOf(string.Create(CultureInfo.InvariantCulture, $"SameFile|{deviceId}|{inode}")),
                DuplicateClass.SameFile,
                NeedsReview: false,
                [],
                FileIdsOf(file),
                PathKeysOf(file),
                WorkIdsOf(file),
                new DuplicateIdentity(deviceId, inode, file.Max(name => name.LinkCount), nameCount),
                null,
                [],
                [],
                null,
                null,
                new DuplicateRecoverableSpace(0, null));
        }
    }

    /// <summary>
    /// Files whose bytes have one SHA-256 (spec §3.3 row 2, ADR 0022): two identities or more, each with every name it
    /// has — a hardlinked copy brings all its names. A hash counts only under the key of the app's last look at the
    /// name, so bytes changed since never make a group.
    /// </summary>
    private static IEnumerable<DuplicateGroup> ByteIdenticalGroups(
        IReadOnlyList<DuplicateName> names, IReadOnlyDictionary<long, string?> fileSystems)
    {
        var byIdentity = names.Where(name => IdentityOf(name) is not null).ToLookup(name => IdentityOf(name)!.Value);
        foreach (var set in ByteIdenticalSets(names))
        {
            List<DuplicateName> members = [.. set.Identities.SelectMany(identity => byIdentity[identity])];
            yield return new DuplicateGroup(
                KeyOf("ByteIdentical|" + set.Sha256),
                DuplicateClass.ByteIdentical,
                NeedsReview: false,
                [],
                FileIdsOf(members),
                PathKeysOf(members),
                WorkIdsOf(members),
                null,
                set.Sha256,
                [],
                [],
                null,
                null,
                RecoverableOf(set, byIdentity, fileSystems));
        }
    }

    /// <summary>
    /// The room removing all but the largest copy would free: every other copy's size — only where it is sure (Vasyl,
    /// 2026-10-07). None, with why, when a copy's file system can share blocks or is unknown (the first that applies:
    /// sharing, then unknown), else when a copy has a name the app never found (its link count is higher than the
    /// names it knows, or unknown), as that name keeps the bytes whatever is removed here.
    /// </summary>
    private static DuplicateRecoverableSpace RecoverableOf(
        CopySet set, ILookup<(long DeviceId, long Inode), DuplicateName> byIdentity, IReadOnlyDictionary<long, string?> fileSystems)
    {
        var gaps = set.Identities.Select(identity => FileSystemSharing.GapOf(fileSystems.GetValueOrDefault(identity.DeviceId))).ToList();
        if (gaps.Contains(RecoverableSpaceGap.SharingFileSystem))
            return new DuplicateRecoverableSpace(null, RecoverableSpaceGap.SharingFileSystem);
        if (gaps.Contains(RecoverableSpaceGap.UnknownFileSystem))
            return new DuplicateRecoverableSpace(null, RecoverableSpaceGap.UnknownFileSystem);

        foreach (var identity in set.Identities)
        {
            var known = byIdentity[identity].Select(name => name.PathKey).Distinct(StringComparer.Ordinal).Count();
            if (byIdentity[identity].Max(name => name.LinkCount) is not { } linkCount || linkCount > known)
                return new DuplicateRecoverableSpace(null, RecoverableSpaceGap.UnlistedNames);
        }

        // Each copy's size as its hashed names read it, the larger of names read a moment apart (as FileSpace counts it).
        // Never unknown: ADR 0022 keeps a hash under the size the app read, so a name with a hash has its size.
        List<long> sizes =
        [
            .. set.Identities.Select(identity => byIdentity[identity]
                .Where(name => name.Sha256 is not null)
                .Select(name => name.SizeBytes)
                .OfType<long>()
                .Max()),
        ];
        return new DuplicateRecoverableSpace(sizes.Sum() - sizes.Max(), null);
    }

    // The hashes two identities or more share, each with its identities in ascending order.
    private static List<CopySet> ByteIdenticalSets(IReadOnlyList<DuplicateName> names) =>
    [
        .. names
            .Where(name => name.Sha256 is not null && IdentityOf(name) is not null)
            .GroupBy(name => name.Sha256!.ToLowerInvariant(), StringComparer.Ordinal)
            .Select(hash => new CopySet(hash.Key, [.. hash.Select(name => IdentityOf(name)!.Value).Distinct().Order()]))
            .Where(set => set.Identities.Count >= 2),
    ];

    /// <summary>One hash and the files whose bytes have it.</summary>
    private sealed record CopySet(string Sha256, IReadOnlyList<(long DeviceId, long Inode)> Identities);
}
