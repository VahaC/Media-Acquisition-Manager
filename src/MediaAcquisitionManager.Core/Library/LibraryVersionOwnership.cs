using MediaAcquisitionManager.Core.Abstractions.Library;

namespace MediaAcquisitionManager.Core.Library;

/// <summary>
/// Gives each version a library reports to exactly one of its items, before the items are grouped
/// into works (MAM-8, ADR 0008).
/// </summary>
/// <remarks>
/// <para>
/// A version is identified by its <see cref="LibraryVersion.SourceVersionId"/>: Jellyfin's media
/// source id is the id of the item that owns the file, unique per file on the server. Jellyfin 10.9 to
/// 10.11 return versions it merged as separate items that each list <i>every</i> source of the group,
/// and 12.1 does the same for a merge across libraries; read as they come, N items would give N²
/// versions of one work.
/// </para>
/// <para>
/// So a source id belongs to the item whose own <see cref="LibraryItem.SourceItemId"/> equals it, and
/// otherwise to the first item (by <see cref="LibraryItem.SourceItemId"/>, ordinal) that lists it;
/// every other listing of it is dropped, and so is a second listing inside one item. A version without
/// a source id can't be told apart from anything and stays with the item that listed it.
/// </para>
/// </remarks>
public static class LibraryVersionOwnership
{
    /// <summary><paramref name="items"/> in the same order, each with only the versions it owns.</summary>
    public static IReadOnlyList<LibraryItem> Resolve(IReadOnlyList<LibraryItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        // Ordinal: ids are compared byte for byte everywhere else (the snapshot's upsert keys too).
        var owners = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            if (item.Versions.Any(version => string.Equals(version.SourceVersionId, item.SourceItemId, StringComparison.Ordinal)))
                owners.TryAdd(item.SourceItemId, item.SourceItemId);
        }

        foreach (var item in items.OrderBy(item => item.SourceItemId, StringComparer.Ordinal))
        {
            foreach (var version in item.Versions)
            {
                if (version.SourceVersionId is { } sourceId)
                    owners.TryAdd(sourceId, item.SourceItemId);
            }
        }

        return items
            .Select(item =>
            {
                var kept = new HashSet<string>(StringComparer.Ordinal);
                return item with
                {
                    Versions =
                    [
                        .. item.Versions.Where(version =>
                            version.SourceVersionId is not { } sourceId
                            || (string.Equals(owners[sourceId], item.SourceItemId, StringComparison.Ordinal) && kept.Add(sourceId))),
                    ],
                };
            })
            .ToList()
            .AsReadOnly();
    }
}
