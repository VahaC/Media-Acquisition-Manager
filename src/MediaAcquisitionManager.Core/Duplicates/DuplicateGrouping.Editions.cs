using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Core.Duplicates;

// MAM-37 — copies of one work whose names say different editions (Vasyl, 2026-10-08: "[05.1] Rambo. Last Blood
// (2019) [Theatrical Cut, US Transfer]… і [05.2] Rambo. Last Blood (2019) [Extended Cut, FRA Transfer]… це НЕ
// ДУБЛІ!!!!! бо різні версії"): another cut is another version, no copy, as another year is another film.
public static partial class DuplicateGrouping
{
    /// <summary>
    /// <paramref name="versionIds"/>, the copies of one part of a component, split by the editions their names say
    /// (<paramref name="editions"/> by version): the part whole, with no <c>Editions</c> text, when every copy says
    /// one set; else one part per set, with the set as its <c>Editions</c> text — its editions comma-joined in
    /// <see cref="MediaEdition"/>'s order, "none" for the copies that say none.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Vasyl, 2026-10-08 — the Theatrical and the Extended Cut of Rambo: Last Blood are different versions, so neither
    /// is a copy of the other, and a work that holds only those two is in no group. This reverses MAM-20's reading of
    /// specification §3.3, where copies of one work in different editions were one group flagged for review: every
    /// copy of a same-work group now says the same editions, so the group carries no
    /// <see cref="DuplicateDistinctionKind.Edition"/>; a possible match still does, as evidence for the owner deciding
    /// whether two works are one.
    /// </para>
    /// <para>
    /// A copy's editions are what all its names say — its files' names and the libraries' names of its version
    /// (<see cref="DuplicateName.Editions"/>), over all its files — and the whole set decides: Terminator 2's "UK
    /// Remastered, Theatrical Cut" is not another copy's "Theatrical Cut", and Widescreen, FullScreen, Open Matte,
    /// IMAX and Remastered are editions too, so Tom &amp; Jerry's cartoon and its Widescreen copy are no copies of
    /// each other. A copy that says none beside one that says one is another edition too, as the Edition distinction
    /// always took it: nothing says it is that cut. A part is still a group only with two copies and two files. One
    /// file's names and byte-identical files are other classes, untouched: their bytes are one, whatever their names
    /// say.
    /// </para>
    /// </remarks>
    private static IEnumerable<(IReadOnlyList<Guid> VersionIds, string? Editions)> EditionPartsOf(
        IReadOnlyList<Guid> versionIds, Dictionary<Guid, SortedSet<MediaEdition>> editions)
    {
        var sets = versionIds.ToLookup(versionId => SetKeyOf(editions[versionId]), StringComparer.Ordinal);
        if (sets.Count <= 1)
        {
            yield return (versionIds, null);
            yield break;
        }

        foreach (var set in sets)
            yield return ([.. set], set.Key.Length > 0 ? set.Key : "none");
    }
}
