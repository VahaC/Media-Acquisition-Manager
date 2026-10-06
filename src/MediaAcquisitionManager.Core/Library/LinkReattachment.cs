namespace MediaAcquisitionManager.Core.Library;

/// <summary>
/// MAM-34 — which TMDb links whose library item is gone move to another item of the same library: the one rule for the
/// sync and for the pass after a file inspection, so the two can never re-attach differently (decision 10, R1).
/// </summary>
/// <remarks>
/// <para>
/// A link keeps the identities (device, inode, size) of its item's files; when the library re-creates the item under a
/// new id, the item whose files hold one of them is the link's item again. Decided as a whole, never one link at a time:
/// a link moves only when its identities lead to exactly one item, no other link's identities lead to that item, and the
/// item holds no link of its own. Anything else — two items holding the files (hardlinks, a copy), two links meeting on
/// one item (the library merged them), an item already linked — is a question only the owner can answer, so every link
/// involved stays where it is: unattached, and listed for the owner. Which link was listed first decides nothing.
/// </para>
/// <para>
/// Item ids are compared byte for byte, as the library gives them (ordinal); the callers pass the items of one library
/// and of the link's own kind.
/// </para>
/// </remarks>
public static class LinkReattachment
{
    /// <summary>
    /// The moves the rule above allows: each link of <paramref name="unattached"/> that moves, with the item it moves to.
    /// </summary>
    /// <param name="unattached">Every link whose item is gone, each with the items whose files hold one of its
    /// identities (empty when none does).</param>
    /// <param name="linkedItems">The items that already hold a link.</param>
    public static IReadOnlyDictionary<Guid, string> Moves(
        IReadOnlyList<(Guid LinkId, IReadOnlySet<string> Holders)> unattached, IReadOnlySet<string> linkedItems)
    {
        ArgumentNullException.ThrowIfNull(unattached);
        ArgumentNullException.ThrowIfNull(linkedItems);

        // Every link that reaches an item claims it, a link reaching several items included: its files are under that
        // item too, so handing the item to another link would be a guess.
        var claims = unattached
            .SelectMany(link => link.Holders)
            .GroupBy(item => item, StringComparer.Ordinal)
            .ToDictionary(item => item.Key, item => item.Count(), StringComparer.Ordinal);

        return unattached
            .Where(link => link.Holders.Count == 1)
            .Select(link => (link.LinkId, Item: link.Holders.Single()))
            .Where(move => claims[move.Item] == 1 && !linkedItems.Contains(move.Item))
            .ToDictionary(move => move.LinkId, move => move.Item);
    }
}
