namespace MediaAcquisitionManager.Api.Services.LibraryLinks;

/// <summary>
/// MAM-34 — keeps the owner's TMDb links on their library items once a file inspection has read the files' identities
/// (decision 10): the identities a link finds its item by again, and the item a link whose item is gone belongs to now.
/// </summary>
/// <remarks>
/// <para>
/// Why after the inspection and not only at a sync: when the library re-creates an item under a new id, the sync stores
/// the item's files as new names, which carry no identity until the inspection that sync queued has looked at them — so
/// the sync itself can't tell where the link's item went. Run right after that inspection, the link is back on its item
/// before the owner looks, instead of a sync later.
/// </para>
/// <para>
/// Only ever the app's own rows: the library is never called, and no file row is touched (an identity is never
/// inferred onto a name, ADR 0009).
/// </para>
/// </remarks>
public interface ILibraryLinkReattacher
{
    /// <summary>
    /// Copies onto every attached link the identities of its item's files that it doesn't hold yet (a series' through
    /// its episodes, while its series work holds no other series item of the library), then moves each unattached link to the one item of its library and kind whose files hold one of
    /// its identities — decided as a whole (R1): a link whose files lead to several items, or to an item another link
    /// claims or holds, stays unattached. Every change is conditional, so a sync or an owner's change made meanwhile is
    /// never overwritten.
    /// </summary>
    Task ReattachAsync(CancellationToken cancellationToken = default);
}
