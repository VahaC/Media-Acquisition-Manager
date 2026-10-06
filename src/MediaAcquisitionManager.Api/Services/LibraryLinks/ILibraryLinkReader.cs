using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Api.Services.LibraryLinks;

/// <summary>
/// MAM-34 — the one read of "a linked id counts like a library id" for every reader of a work's ids: the Library page's
/// cards, its search and its missing-ids filter, the search page's library block and the release matching that follows
/// it, and the recommendations. In memory for the works a reader shows (<see cref="ReadAsync"/>, built on
/// <c>LinkedIdentifiers</c>), and as SQL for the filters that pick them, which apply the same rule — so a card, a filter
/// and a match never count a link differently.
/// </summary>
/// <remarks>
/// <para>
/// Only links attached to a work count (<c>LibraryItemLinkEntity.MediaWorkId</c> set): one whose item the library no
/// longer reports names no work to read.
/// </para>
/// <para>
/// R8 — the IMDb id TMDb gave counts only where the linked item's library holds no IMDb id of its own: the library's
/// word wins, everywhere alike.
/// </para>
/// </remarks>
public interface ILibraryLinkReader
{
    /// <summary>
    /// Each of <paramref name="workIds"/> with its ids as readers count them, its attached links and whether it counts
    /// as linked — every id asked for is in the answer. The name of the user who set a link is on its row only
    /// <paramref name="forOwner"/>: it is the owner's sign-in name, half of the owner's credentials (the sync run's
    /// rule). Batched, no N+1: one query each for the works' identifier rows, the episodes' series and the links, however
    /// many works.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, LinkedWork>> ReadAsync(
        IReadOnlyCollection<Guid> workIds, bool forOwner, CancellationToken cancellationToken = default);

    /// <summary>
    /// The works that count as holding the id <paramref name="scheme"/> <paramref name="key"/>, as SQL;
    /// <paramref name="key"/> is the value trimmed and upper-cased, compared the way <c>WorkMatching</c> compares.
    /// </summary>
    IQueryable<Guid> WorksHolding(MediaIdentifierScheme scheme, string key);

    /// <summary>
    /// The works that count as holding an id containing <paramref name="text"/> (upper-cased; ids are ASCII), as SQL —
    /// the Library page's search.
    /// </summary>
    IQueryable<Guid> WorksWithAnIdContaining(string text);

    /// <summary>
    /// The works that count as linked, as SQL: those an attached link sits on, and (R16) the episodes of a series one
    /// sits on — the Library page's missing-ids filter leaves them out, as their cards show no missing id.
    /// </summary>
    IQueryable<Guid> LinkedWorkIds();
}
